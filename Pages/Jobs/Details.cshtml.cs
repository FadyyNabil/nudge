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
    public DetailsModel(AppDbContext db, IConfiguration config, PortfolioStorage storage) { _db = db; _config = config; _storage = storage; }

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

        return true;
    }

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    /// <summary>May this visitor download that delivered file right now?</summary>
    public bool CanDownload(DeliveryFile d)
    {
        if (IsAdmin) return true;
        var me = User.GetUserId();
        if (d.FreelancerId == me) return true;
        // The client's downloads work only while the demo payment is held or released.
        return IsOwner && Job!.Escrow != EscrowStatus.None;
    }

    // ---------------- Hired freelancer sends the final result ----------------
    public async Task<IActionResult> OnPostDeliverAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!IsHiredFreelancer) return Forbid();
        if (Job!.Status is not (JobStatus.InProgress or JobStatus.Completed))
        { Error = "Files can only be sent for a job that is in progress or finished."; return RedirectToPage(new { id }); }
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
        Message = "File sent. The client sees the name now and can download it while the payment is held by Sho8lana (demo).";
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

        Message = "Application sent. It will be visible to the client after the site owner approves it.";
        return RedirectToPage(new { id });
    }

    // ---------------- Client hires one applicant AND pays ----------------
    // Accepting an offer and paying are one step: the client chooses how they pay
    // (InstaPay or a mobile wallet) and the money is then HELD by the site owner
    // (demo escrow, bookkeeping only) until the freelancer finishes the work.
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

        Job.Status = JobStatus.InProgress;
        Job.AcceptedFreelancerId = app.FreelancerId;
        Job.AgreedPrice = agreed;
        Job.CommissionPercent = percent;
        Job.CommissionAmount = CommissionCalculator.Calculate(agreed, percent);

        // Demo escrow: the client's payment to the site owner is recorded as held.
        Job.PaymentMethod = Method;
        Job.Escrow = EscrowStatus.Held;
        Job.PaidAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        Message = $"Freelancer hired and paid via {PaymentMethodNames.Label(Method.Value)}. "
            + "The money is held by Sho8lana (demo) and goes to the freelancer, minus the commission, when they finish. Other applications were declined.";
        return RedirectToPage(new { id });
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

    // ---------------- Freelancer marks the work as finished ----------------
    // The payout is AUTOMATIC (demo): the held money goes to the freelancer minus
    // the owner's commission, which counts as "earned" from this moment.
    public async Task<IActionResult> OnPostFinishAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!IsHiredFreelancer) return Forbid();   // only the freelancer hired for THIS job
        if (Job!.Status != JobStatus.InProgress) { Error = "Only a job in progress can be finished."; return RedirectToPage(new { id }); }
        if (Job.Escrow != EscrowStatus.Held) { Error = "The client's payment is not held for this job."; return RedirectToPage(new { id }); }

        Job.Status = JobStatus.Completed;
        Job.CompletedAtUtc = DateTime.UtcNow;
        Job.Escrow = EscrowStatus.Released;
        Job.ReleasedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        Message = $"Work marked as finished. Sho8lana released {(Job.AgreedPrice - Job.CommissionAmount)?.ToString("N2")} to you "
            + $"(agreed price minus {Job.CommissionPercent}% commission). Demo only: no real money moved.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (!IsOwner) return Forbid();
        if (Job!.Status is JobStatus.Open or JobStatus.InProgress)
        {
            Job.Status = JobStatus.Cancelled;
            // No commission is earned on a cancelled job.
            Job.CommissionAmount = null;
            Job.CommissionPercent = null;
            // A held demo payment goes back to the client.
            if (Job.Escrow == EscrowStatus.Held)
            {
                Job.Escrow = EscrowStatus.None;
                Message = "Job cancelled. The held payment was refunded to you (demo).";
            }
            else
            {
                Message = "Job cancelled.";
            }
            await _db.SaveChangesAsync();
        }
        return RedirectToPage(new { id });
    }
}
