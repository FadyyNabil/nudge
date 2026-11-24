using System.ComponentModel.DataAnnotations;
using Marketplace.Data;
using Marketplace.Models;
using Marketplace.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Jobs;

/// <summary>
/// One page, several actions (handlers): view, apply, accept, reject, complete, cancel.
/// Every action re-checks WHO is asking and WHAT STATE the job is in on the server,
/// because hiding a button in HTML is not security.
/// </summary>
public class DetailsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly PortfolioStorage _storage;
    private readonly Mailer _mailer;
    public DetailsModel(AppDbContext db, IConfiguration config, PortfolioStorage storage, Mailer mailer) { _db = db; _config = config; _storage = storage; _mailer = mailer; }

    public Job? Job { get; private set; }
    public List<JobApplication> Applications { get; private set; } = new();
    public JobApplication? MyApplication { get; private set; }
    public Dictionary<int, List<PortfolioItem>> Portfolios { get; private set; } = new();
    public int MyPortfolioCount { get; private set; }
    public List<DeliveryFile> Deliveries { get; private set; } = new();
    public bool IsOwner { get; private set; }
    public bool IsHiredFreelancer { get; private set; }
    public bool IsAdmin => User.IsInRole(Roles.Admin);
    public bool IsFreelancer => User.IsInRole(Roles.Freelancer);

    // ---- Nudge button ----
    /// <summary>Who this visitor may nudge on this job right now (null = no nudge button).</summary>
    public int? NudgeTargetId { get; private set; }
    public string NudgeButtonText { get; private set; } = "";
    /// <summary>When the visitor last nudged on this job + 24h, if still in the future.</summary>
    public DateTime? NudgeAgainAtUtc { get; private set; }
    public List<(NudgeLog Log, string From, string To)> NudgeHistory { get; private set; } = new();
    public const int NudgeCooldownHours = 24;

    // ---- Job chat ----
    /// <summary>One rendered chat line for the first paint (polls refresh it after).</summary>
    public record ChatLine(string Body, string Name, string RoleLabel, DateTime CreatedAtUtc, bool Mine);
    public List<ChatLine> Chat { get; private set; } = new();
    /// <summary>Client and hired freelancer see the chat once someone is hired; admin reads it for disputes.</summary>
    public bool ShowChat => Job?.AcceptedFreelancerId != null && (IsOwner || IsHiredFreelancer || IsAdmin);
    /// <summary>Only the two parties can write.</summary>
    public bool CanChat => Job?.AcceptedFreelancerId != null && (IsOwner || IsHiredFreelancer);
    [BindProperty, StringLength(2000)] public string? ChatText { get; set; }
    public const int ChatEmailBatchMinutes = 30;

    // ---- Ratings (after completion, both sides rate once) ----
    public record RatingLine(int Id, int Stars, string? Review, string FromName, string RoleLabel, DateTime At, bool Hidden);
    /// <summary>Visible ratings on this job (admin also sees hidden ones, marked).</summary>
    public List<RatingLine> Ratings { get; private set; } = new();
    public bool AlreadyRated { get; private set; }
    /// <summary>Completed job + one of the two parties + has not rated yet.</summary>
    public bool CanRate => Job?.Status == JobStatus.Completed && (IsOwner || IsHiredFreelancer) && !AlreadyRated;
    public bool ShowRatings => Job?.Status == JobStatus.Completed && (IsOwner || IsHiredFreelancer || IsAdmin);
    /// <summary>Client's average across all their jobs: shown to freelancers before they commit.</summary>
    public double ClientAvg { get; private set; }
    public int ClientRatingCount { get; private set; }
    /// <summary>Per-freelancer average shown on their application: freelancer id -> (avg, count).</summary>
    public Dictionary<int, (double Avg, int Count)> FreelancerAvg { get; private set; } = new();
    [BindProperty, Range(1, 5)] public int RateStars { get; set; }
    [BindProperty, StringLength(1000)] public string? RateReview { get; set; }

    public static string StarsText(int stars) => new string('\u2605', stars) + new string('\u2606', 5 - stars);

    private async Task LoadNudgeAsync()
    {
        var me = User.GetUserId();
        var j = Job!;
        if (IsOwner && j.Status == JobStatus.InProgress && j.DeliveredAtUtc == null && j.AcceptedFreelancerId != null)
        { NudgeTargetId = j.AcceptedFreelancerId; NudgeButtonText = "Nudge freelancer"; }
        else if (IsHiredFreelancer && ((j.Status == JobStatus.InProgress && j.DeliveredAtUtc != null)
                 || (j.Status == JobStatus.AwaitingPayment && j.ProofUploadedAtUtc == null)))
        { NudgeTargetId = j.ClientId; NudgeButtonText = "Nudge client"; }

        if (NudgeTargetId != null)
        {
            var last = await _db.NudgeLogs.Where(n => n.JobId == j.Id && n.FromUserId == me)
                .OrderByDescending(n => n.CreatedAtUtc).Select(n => (DateTime?)n.CreatedAtUtc).FirstOrDefaultAsync();
            if (last != null && last.Value.AddHours(NudgeCooldownHours) > DateTime.UtcNow)
                NudgeAgainAtUtc = last.Value.AddHours(NudgeCooldownHours);
        }

        if (IsAdmin)
        {
            var logs = await _db.NudgeLogs.AsNoTracking().Where(n => n.JobId == j.Id).OrderByDescending(n => n.CreatedAtUtc).ToListAsync();
            var ids = logs.Select(n => n.FromUserId).Concat(logs.Select(n => n.ToUserId)).Distinct().ToList();
            var names = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
            NudgeHistory = logs.Select(n => (n, names.GetValueOrDefault(n.FromUserId, "?"), names.GetValueOrDefault(n.ToUserId, "?"))).ToList();
        }
    }

    /// <summary>One readable sentence for the two-step status (admin approval, then client decision).</summary>
    public static string Describe(JobApplication a) => a.Approval switch
    {
        ApprovalStatus.PendingApproval => "waiting for the site owner's approval",
        ApprovalStatus.Rejected => "not approved by the site owner",
        _ => a.Status switch
        {
            ApplicationStatus.Accepted => "accepted by the client",
            ApplicationStatus.Rejected => "declined by the client",
            _ => "approved, waiting for the client"
        }
    };

    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    [BindProperty] public ApplyInput Apply { get; set; } = new();
    [BindProperty] public IFormFile? Delivery { get; set; }
    [BindProperty] public PaymentMethod? Method { get; set; }
    [BindProperty, System.ComponentModel.DataAnnotations.Range(1, 1000000)] public decimal? Price { get; set; }

    public class ApplyInput
    {
        [Required, StringLength(2000, MinimumLength = 20)]
        public string CoverLetter { get; set; } = "";

        [Range(1, 1000000)]
        public decimal ProposedPrice { get; set; }
    }

    // Loads the job and works out what this visitor is allowed to see/do.
    private async Task<bool> LoadAsync(int id)
    {
        // Delivered jobs the client ignored for 7 days are paid out automatically (checked on every visit too).
        await JobMoney.AutoReleaseDueAsync(_db, _mailer);
        Job = await _db.Jobs.Include(j => j.Client).FirstOrDefaultAsync(j => j.Id == id);
        if (Job is null) return false;

        var me = User.GetUserId();
        IsOwner = me != 0 && Job.ClientId == me && User.IsInRole(Roles.Client);
        IsHiredFreelancer = me != 0 && Job.AcceptedFreelancerId == me && User.IsInRole(Roles.Freelancer);

        if (IsOwner || IsAdmin)
        {
            // The admin sees every application. A client only sees the ones the admin APPROVED,
            // so every client action below (accept/reject) can only touch approved applications.
            var query = _db.Applications.Include(a => a.Freelancer).Where(a => a.JobId == id);
            if (!IsAdmin) query = query.Where(a => a.Approval == ApprovalStatus.Approved);
            Applications = await query.OrderBy(a => a.Id).ToListAsync();

            // Load the applicants' portfolios so the client can judge them before hiring.
            var ids = Applications.Select(a => a.FreelancerId).Distinct().ToList();
            var items = await _db.PortfolioItems.AsNoTracking().Where(p => ids.Contains(p.FreelancerId)).OrderBy(p => p.Id).ToListAsync();
            Portfolios = items.GroupBy(p => p.FreelancerId).ToDictionary(g => g.Key, g => g.ToList());
        }

        if (IsFreelancer)
            MyPortfolioCount = await _db.PortfolioItems.CountAsync(p => p.FreelancerId == me);

        if (IsFreelancer)
            MyApplication = await _db.Applications.FirstOrDefaultAsync(a => a.JobId == id && a.FreelancerId == me);

        // Delivery files are only shown to the people involved in this job.
        if (IsOwner || IsAdmin || IsHiredFreelancer)
            Deliveries = await _db.DeliveryFiles.AsNoTracking().Where(d => d.JobId == id).OrderBy(d => d.Id).ToListAsync();

        await LoadNudgeAsync();
                if (ShowChat)
            await LoadChatAsync();
        if (Job.Status == JobStatus.Completed)
            await LoadRatingsAsync();

        return true;
    }

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    /// <summary>May this visitor download that delivered file right now?</summary>
    public bool CanDownload(DeliveryFile d)
    {
        if (IsAdmin) return true;
        var me = User.GetUserId();
        if (d.FreelancerId == me) return true;
        // The client can download only after the freelancer submitted the work for approval
        // (or after it was approved), so cancelling cannot be used to grab unpaid work.
        return IsOwner && ((Job!.Escrow == EscrowStatus.Held && Job.DeliveredAtUtc != null) || Job.Escrow == EscrowStatus.Released);
    }

    // ---------------- Nudge: a polite reminder to the other side ----------------
    // The server decides who may nudge whom and when (not the HTML), and allows one nudge per job per 24 hours per sender.
    public async Task<IActionResult> OnPostNudgeAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        var me = User.GetUserId();
        if (me == 0 || NudgeTargetId is null) { Error = "There is nothing to nudge right now."; return RedirectToPage(new { id }); }
        if (NudgeAgainAtUtc != null) { Error = "You already nudged on this job. You can nudge again after " + NudgeAgainAtUtc.Value.ToString("yyyy-MM-dd HH:mm") + " UTC."; return RedirectToPage(new { id }); }

        var sender = await _db.Users.FindAsync(me);
        var target = await _db.Users.FindAsync(NudgeTargetId.Value);
        if (sender is null || target is null) { Error = "Could not send the nudge."; return RedirectToPage(new { id }); }

        _db.NudgeLogs.Add(new NudgeLog { JobId = id, FromUserId = me, ToUserId = target.Id, CreatedAtUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var why = target.Id == Job!.ClientId
            ? (Job.Status == JobStatus.AwaitingPayment ? "They are waiting for your payment so the work can start." : "They delivered the work and are waiting for you to review and approve it.")
            : "The client is waiting for your work on this job.";
        _mailer.Send(target.Email, $"{sender.FullName} nudged you about {Job.Title} - check in on it", $"{sender.FullName} nudged you",
            $"<p><strong>{Mailer.E(sender.FullName)}</strong> nudged you about <strong>{Mailer.E(Job.Title)}</strong>.</p><p>{why}</p>",
            "Open the job", "/Jobs/Details/" + id);
        Message = "Nudge sent to " + target.FullName + ". You can nudge again in " + NudgeCooldownHours + " hours.";
        return RedirectToPage(new { id });
    }

    // ---------------- Hired freelancer sends the final result ----------------
    public async Task<IActionResult> OnPostDeliverAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!IsHiredFreelancer) return Forbid();
        if (Job!.Status != JobStatus.InProgress || Job.DeliveredAtUtc != null)
        { Error = "Files can only be added while the job is in progress and not yet submitted for approval."; return RedirectToPage(new { id }); }
        if (Delivery is null || Delivery.Length == 0)
        { Error = "Choose a file to send."; return RedirectToPage(new { id }); }

        // Same safety rules as portfolio uploads (type, magic bytes, size).
        var problem = await _storage.ValidateAsync(Delivery);
        if (problem is not null) { Error = problem; return RedirectToPage(new { id }); }

        await using var ms = new MemoryStream();
        await Delivery.CopyToAsync(ms);
        _db.DeliveryFiles.Add(new DeliveryFile
        {
            JobId = id,
            FreelancerId = User.GetUserId(),
            OriginalFileName = Path.GetFileName(Delivery.FileName),
            ContentType = _storage.MimeFor(Delivery.FileName) ?? "application/octet-stream",
            Data = ms.ToArray()
        });
        await _db.SaveChangesAsync();
        Message = "File added. When everything is ready, press \"Submit work for approval\" so the client can review it.";
        return RedirectToPage(new { id });
    }

    // ---------------- Freelancer applies ----------------
    public async Task<IActionResult> OnPostApplyAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!User.Identity!.IsAuthenticated) return Challenge();
        if (!IsFreelancer) return Forbid();
        if (Job!.Status != JobStatus.Open) { Error = "This job is no longer open."; return RedirectToPage(new { id }); }
        if (MyApplication is not null) { Error = "You already applied to this job."; return RedirectToPage(new { id }); }
        // A freelancer must have a portfolio before applying, so clients always have something to look at.
        if (await _db.PortfolioItems.CountAsync(p => p.FreelancerId == User.GetUserId()) == 0)
        { Error = "Add at least one portfolio item before applying."; return RedirectToPage(new { id }); }
        if (!ModelState.IsValid) return Page();

        _db.Applications.Add(new JobApplication
        {
            JobId = id,
            FreelancerId = User.GetUserId(),
            CoverLetter = Apply.CoverLetter.Trim(),
            ProposedPrice = Math.Round(Apply.ProposedPrice, 2)
        });
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateException) { Error = "You already applied to this job."; return RedirectToPage(new { id }); }

        _mailer.SendToAdmins("New application to review: " + Job.Title, "A new application needs your review",
            $"<p><strong>{Mailer.E(User.Identity!.Name)}</strong> applied to <strong>{Mailer.E(Job.Title)}</strong> for {Math.Round(Apply.ProposedPrice, 2):N2} EGP.</p>",
            "Review applications", "/Admin/Applications");
        Message = "Application sent. It will be visible to the client after the site owner approves it.";
        return RedirectToPage(new { id });
    }

    // ---------------- Client hires one applicant AND pays ----------------
    // Accepting an offer and paying are one step: the client chooses how they pay
    // (InstaPay or a mobile wallet) and the money is then HELD by the site owner
    // (manual payment, confirmed by Nudge) until the client approves the work.
    public async Task<IActionResult> OnPostAcceptAsync(int id, int appId)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!IsOwner) return Forbid();   // only the client who posted THIS job
        if (Job!.Status != JobStatus.Open) { Error = "This job is not open."; return RedirectToPage(new { id }); }

        var app = Applications.FirstOrDefault(a => a.Id == appId && a.Status == ApplicationStatus.Pending);
        if (app is null) { Error = "Application not found."; return RedirectToPage(new { id }); }

        if (Method is null || !Enum.IsDefined(typeof(PaymentMethod), Method.Value))
        { Error = "Choose how you will pay (InstaPay or a mobile wallet)."; return RedirectToPage(new { id }); }

        // The client chooses the final price at hiring time; the freelancer's offer is the default.
        if (Price is null || Price < 1 || Price > 1000000)
        { Error = "Enter the agreed price the freelancer will be paid."; return RedirectToPage(new { id }); }
        var agreed = Math.Round(Price.Value, 2);

        // The commission rate is copied onto the job now, so later rate changes do not rewrite history.
        var percent = _config.GetValue<decimal>("Marketplace:CommissionPercent", 10m);

        app.Status = ApplicationStatus.Accepted;
        // Every other application for this job is closed, including ones still waiting for the admin.
        var others = await _db.Applications.Where(a => a.JobId == id && a.Id != appId && a.Status == ApplicationStatus.Pending).ToListAsync();
        foreach (var other in others) other.Status = ApplicationStatus.Rejected;

        // The job waits for the client's payment; work starts only after Nudge confirms the money arrived.
        Job.Status = JobStatus.AwaitingPayment;
        Job.PaymentRef = Job.MakeRef(Job.Id);
        Job.AcceptedFreelancerId = app.FreelancerId;
        Job.AgreedPrice = agreed;
        Job.CommissionPercent = percent;
        Job.CommissionAmount = CommissionCalculator.Calculate(agreed, percent);

        // Nothing is "paid" yet. The client pays by InstaPay or wallet and uploads a proof; the owner confirms it.
        Job.PaymentMethod = Method;
        Job.Escrow = EscrowStatus.None;

        await _db.SaveChangesAsync();
        _mailer.Send(app.Freelancer?.Email, "You were hired: " + Job.Title, "You got the job",
            $"<p>The client chose you for <strong>{Mailer.E(Job.Title)}</strong>.</p><p>Agreed price: <strong>{agreed:N2} EGP</strong>. You receive {agreed - Job.CommissionAmount:N2} EGP after the {percent}% commission, once the client approves your work.</p><p>Work starts when Nudge confirms the client's payment. We will email you then.</p>",
            "Open the job", "/Jobs/Details/" + id);
        TempData["Message"] = "Freelancer chosen. Now send the payment and upload your proof below. The work starts when Nudge confirms it.";
        return RedirectToPage("/Jobs/Pay", new { id });
    }

    public async Task<IActionResult> OnPostRejectAsync(int id, int appId)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!IsOwner) return Forbid();
        var app = Applications.FirstOrDefault(a => a.Id == appId && a.Status == ApplicationStatus.Pending);
        if (app is not null && Job!.Status == JobStatus.Open)
        {
            app.Status = ApplicationStatus.Rejected;
            await _db.SaveChangesAsync();
        }
        return RedirectToPage(new { id });
    }

    // ---------------- Freelancer submits the work for the client to check ----------------
    // The money is NOT released here. The client must approve (or ask for changes), or wait 7 days.
    public async Task<IActionResult> OnPostSubmitAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!IsHiredFreelancer) return Forbid();   // only the freelancer hired for THIS job
        if (Job!.Status != JobStatus.InProgress || Job.DeliveredAtUtc != null)
        { Error = "This job is not waiting for your work right now."; return RedirectToPage(new { id }); }
        if (Job.Escrow != EscrowStatus.Held) { Error = "The client's payment is not held for this job."; return RedirectToPage(new { id }); }
        if (!Deliveries.Any(d => d.FreelancerId == User.GetUserId()))
        { Error = "Add at least one file first, then submit your work."; return RedirectToPage(new { id }); }

        Job.DeliveredAtUtc = DateTime.UtcNow;
        Job.ChangeRequestNote = null;
        await _db.SaveChangesAsync();
        _mailer.Send(Job.Client?.Email, "Work delivered: " + Job.Title, "Your work is ready to review",
            $"<p>The freelancer submitted the work for <strong>{Mailer.E(Job.Title)}</strong>.</p><p>Please download the files and either <strong>approve</strong> (Nudge then sends the freelancer’s share) or <strong>ask for changes</strong>. If you do not answer within {Job.AutoReleaseDays} days, the work is approved automatically.</p>",
            "Review the work", "/Jobs/Details/" + id);
        Message = $"Work submitted. The client can now approve it or ask for changes. If they do not answer within {Job.AutoReleaseDays} days, the work is approved automatically and you are paid.";
        return RedirectToPage(new { id });
    }

    // ---------------- Client approves: this releases the payment ----------------
    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!IsOwner) return Forbid();
        if (!Job!.AwaitingApproval || Job.Escrow != EscrowStatus.Held)
        { Error = "There is no submitted work to approve."; return RedirectToPage(new { id }); }

        JobMoney.Approve(Job);
        await _db.SaveChangesAsync();
        var fl = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == Job.AcceptedFreelancerId);
        JobMoney.NotifyApproved(_mailer, Job, fl);
        Message = $"Approved. Nudge will now send {JobMoney.FreelancerShare(Job):N2} EGP to the freelancer (agreed price minus {Job.CommissionPercent}% commission). You get an email when it is sent.";
        return RedirectToPage(new { id });
    }

    // ---------------- Client asks for changes: back to In progress ----------------
    [BindProperty, StringLength(1000)] public string? ChangeNote { get; set; }

    public async Task<IActionResult> OnPostRequestChangesAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!IsOwner) return Forbid();
        if (!Job!.AwaitingApproval)
        { Error = "There is no submitted work to review."; return RedirectToPage(new { id }); }
        var note = (ChangeNote ?? "").Trim();
        if (note.Length < 5) { Error = "Tell the freelancer what to change (at least a few words)."; return RedirectToPage(new { id }); }
        if (note.Length > 1000) note = note[..1000];

        Job.DeliveredAtUtc = null;
        Job.ChangeRequestNote = note;
        await _db.SaveChangesAsync();
        var fl = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == Job.AcceptedFreelancerId);
        _mailer.Send(fl?.Email, "Changes requested: " + Job.Title, "The client asked for changes",
            $"<p>For <strong>{Mailer.E(Job.Title)}</strong> the client wrote:</p><p style='background:#f3f5fb;padding:12px;border-radius:8px'>{Mailer.E(note)}</p><p>Upload the updated file and submit again.</p>",
            "Open the job", "/Jobs/Details/" + id);
        Message = "Your request was sent. The job is back In progress and the freelancer can send a new version.";
        return RedirectToPage(new { id });
    }

    // ---------------- Admin dispute override ----------------
    // The site owner can settle a stuck job: pay the freelancer, or refund the client.
    public async Task<IActionResult> OnPostAdminResolveAsync(int id, string decision)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!IsAdmin) return Forbid();
        if (Job!.Status != JobStatus.InProgress || Job.Escrow != EscrowStatus.Held)
        { Error = "Only a job in progress with held money can be settled."; return RedirectToPage(new { id }); }
        var fl = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == Job.AcceptedFreelancerId);
        if (decision == "release")
        {
            JobMoney.Approve(Job);
            await _db.SaveChangesAsync();
            JobMoney.NotifyApproved(_mailer, Job, fl, byAdmin: "admin");
            Message = "Settled: the work is approved. The freelancer's payout is now waiting under Transactions.";
        }
        else if (decision == "refund")
        {
            Job.Status = JobStatus.Cancelled;
            Job.CommissionAmount = null;
            Job.CommissionPercent = null;
            Job.Escrow = EscrowStatus.None;
            await _db.SaveChangesAsync();
            var path = "/Jobs/Details/" + id;
            _mailer.Send(Job.Client?.Email, "Job cancelled and refunded: " + Job.Title, "Your payment is refunded",
                $"<p>The site owner reviewed <strong>{Mailer.E(Job.Title)}</strong>, cancelled the job and refunded your payment.</p>", "View the job", path);
            _mailer.Send(fl?.Email, "Job cancelled by the site owner: " + Job.Title, "The job was cancelled",
                $"<p>The site owner reviewed <strong>{Mailer.E(Job.Title)}</strong> and cancelled it. The payment went back to the client.</p>", "View the job", path);
            Message = "Settled: job cancelled. Send the client's money back by InstaPay yourself (see the proof under Transactions).";
        }
        else { Error = "Unknown decision."; }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!IsOwner) return Forbid();
        if (Job!.AwaitingApproval)
        { Error = "The work was already delivered. Approve it or ask for changes instead. If something is wrong, contact the site owner."; return RedirectToPage(new { id }); }
        if (Job.Status == JobStatus.AwaitingPayment && Job.ProofUploadedAtUtc != null)
        { Error = "You already sent a payment proof. Contact the site owner to cancel and get a refund."; return RedirectToPage(new { id }); }
        if (Job.Status == JobStatus.AwaitingPayout)
        { Error = "The work was approved. It cannot be cancelled now."; return RedirectToPage(new { id }); }
        if (Job.Status is JobStatus.Open or JobStatus.InProgress or JobStatus.AwaitingPayment)
        {
            Job.Status = JobStatus.Cancelled;
            // No commission is earned on a cancelled job.
            Job.CommissionAmount = null;
            Job.CommissionPercent = null;
            // A held payment goes back to the client.
            if (Job.Escrow == EscrowStatus.Held)
            {
                Job.Escrow = EscrowStatus.None;
                Message = "Job cancelled. The held payment will be returned to you by Nudge.";
            }
            else
            {
                Message = "Job cancelled.";
            }
            await _db.SaveChangesAsync();
        }
        return RedirectToPage(new { id });
    }

    private async Task LoadChatAsync()
    {
        var me = User.GetUserId();
        var msgs = await _db.ChatMessages.Where(m => m.JobId == Job!.Id).OrderBy(m => m.CreatedAtUtc).ToListAsync();
        if (IsOwner || IsHiredFreelancer)
        {
            // Everything the visitor can see counts as read.
            var changed = false;
            foreach (var m in msgs)
                if (m.FromUserId != me && m.ReadAtUtc == null) { m.ReadAtUtc = DateTime.UtcNow; changed = true; }
            if (changed) await _db.SaveChangesAsync();
        }
        var ids = msgs.Select(m => m.FromUserId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        Chat = msgs.Select(m => new ChatLine(m.Body, names.GetValueOrDefault(m.FromUserId, "?"),
            m.FromUserId == Job!.ClientId ? "Client" : "Freelancer", m.CreatedAtUtc, m.FromUserId == me)).ToList();
    }

    /// <summary>Post a chat message. Only the client and the hired freelancer. The other party gets
    /// one email at most every 30 minutes per job, so a lively chat stays cheap.</summary>
    public async Task<IActionResult> OnPostSendMessageAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!CanChat) return Forbid();
        var text = (ChatText ?? "").Trim();
        if (text.Length == 0) { Error = "Write a message first."; return RedirectToPage(new { id }); }
        if (text.Length > 2000) text = text[..2000];

        var me = User.GetUserId();
        _db.ChatMessages.Add(new ChatMessage { JobId = id, FromUserId = me, Body = text });

        var toClient = IsHiredFreelancer;
        var lastSent = toClient ? Job!.LastChatEmailToClientAtUtc : Job!.LastChatEmailToFreelancerAtUtc;
        if (lastSent == null || lastSent.Value.AddMinutes(ChatEmailBatchMinutes) <= DateTime.UtcNow)
        {
            var recipientId = toClient ? Job!.ClientId : Job!.AcceptedFreelancerId!.Value;
            var recipient = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == recipientId);
            var sender = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == me);
            _mailer.Send(recipient?.Email, "New message on " + Job!.Title,
                "New message on your job",
                "<p><strong>" + Mailer.E(sender?.FullName ?? "The other party") + "</strong> wrote on <strong>" + Mailer.E(Job!.Title) + "</strong>:</p>"
                + "<blockquote style='margin:12px 0;padding:10px 14px;background:#f3f5fb;border-radius:10px'>" + Mailer.E(text) + "</blockquote>"
                + "<p class='muted'>Reply on the job page. Further messages in the next " + ChatEmailBatchMinutes + " minutes won't trigger more emails.</p>",
                "Open the chat", "/Jobs/Details/" + id);
            if (toClient) Job!.LastChatEmailToClientAtUtc = DateTime.UtcNow; else Job!.LastChatEmailToFreelancerAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return RedirectToPage(new { id });
    }

    /// <summary>Lightweight JSON the page polls every 15 seconds. Only the two parties and admins.</summary>
    public async Task<IActionResult> OnGetMessagesAsync(int id)
    {
        var job = await _db.Jobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (job is null || job.AcceptedFreelancerId == null) return NotFound();
        var me = User.GetUserId();
        var isParty = job.ClientId == me || job.AcceptedFreelancerId == me;
        if (!isParty && !IsAdmin) return Forbid();

        var msgs = await _db.ChatMessages.Where(m => m.JobId == id).OrderBy(m => m.CreatedAtUtc).ToListAsync();
        if (isParty)
        {
            var changed = false;
            foreach (var m in msgs)
                if (m.FromUserId != me && m.ReadAtUtc == null) { m.ReadAtUtc = DateTime.UtcNow; changed = true; }
            if (changed) await _db.SaveChangesAsync();
        }
        var ids = msgs.Select(m => m.FromUserId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        var payload = msgs.Select(m => new
        {
            mine = m.FromUserId == me,
            n = names.GetValueOrDefault(m.FromUserId, "?"),
            r = m.FromUserId == job.ClientId ? "Client" : "Freelancer",
            b = m.Body,
            t = m.CreatedAtUtc.ToString("o")
        }).ToList();
        return new JsonResult(payload);
    }

    private async Task LoadRatingsAsync()
    {
        var me = User.GetUserId();
        var ratings = await _db.Ratings.Where(r => r.JobId == Job!.Id).OrderBy(r => r.CreatedAtUtc).ToListAsync();
        AlreadyRated = ratings.Any(r => r.FromUserId == me);
        var ids = ratings.Select(r => r.FromUserId).Distinct().ToList();
        var names = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
        Ratings = ratings.Where(r => r.HiddenAtUtc == null || IsAdmin)
            .Select(r => new RatingLine(r.Id, r.Stars, r.Review, names.GetValueOrDefault(r.FromUserId, "?"),
                r.FromUserId == Job!.ClientId ? "Client" : "Freelancer", r.CreatedAtUtc, r.HiddenAtUtc != null)).ToList();

        // Averages come from every visible rating that person received, not only this job.
        var clientStars = await _db.Ratings.AsNoTracking().Where(r => r.ToUserId == Job!.ClientId && r.HiddenAtUtc == null).Select(r => r.Stars).ToListAsync();
        ClientAvg = clientStars.Count > 0 ? clientStars.Average() : 0; ClientRatingCount = clientStars.Count;
        var freelancerIds = Applications.Select(a => a.FreelancerId).Distinct().ToList();
        if (freelancerIds.Count > 0)
        {
            var rows = await _db.Ratings.AsNoTracking().Where(r => freelancerIds.Contains(r.ToUserId) && r.HiddenAtUtc == null)
                .Select(r => new { r.ToUserId, r.Stars }).ToListAsync();
            FreelancerAvg = rows.GroupBy(r => r.ToUserId).ToDictionary(g => g.Key, g => (g.Average(r => r.Stars), g.Count()));
        }
    }

    /// <summary>Leave a rating after the job is completed. Each party rates the other once.</summary>
    public async Task<IActionResult> OnPostRateAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!CanRate) { Error = AlreadyRated ? "You already rated this job." : "Ratings open once the job is completed."; return RedirectToPage(new { id }); }
        if (RateStars < 1 || RateStars > 5) { Error = "Pick 1 to 5 stars."; return RedirectToPage(new { id }); }
        var me = User.GetUserId();
        var toId = IsOwner ? Job!.AcceptedFreelancerId!.Value : Job!.ClientId;
        var review = string.IsNullOrWhiteSpace(RateReview) ? null : RateReview.Trim();
        if (review?.Length > 1000) review = review[..1000];
        _db.Ratings.Add(new Rating { JobId = id, FromUserId = me, ToUserId = toId, Stars = RateStars, Review = review });
        await _db.SaveChangesAsync();
        Message = "Thanks for rating.";
        return RedirectToPage(new { id });
    }

    /// <summary>Admin: hide a review (out of pages and averages) or bring it back.</summary>
    public async Task<IActionResult> OnPostHideRatingAsync(int id, int ratingId)
    {
        if (!IsAdmin) return Forbid();
        var r = await _db.Ratings.FirstOrDefaultAsync(x => x.Id == ratingId && x.JobId == id);
        if (r is not null) { r.HiddenAtUtc = r.HiddenAtUtc == null ? DateTime.UtcNow : null; await _db.SaveChangesAsync(); }
        return RedirectToPage(new { id });
    }
}
