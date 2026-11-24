using Marketplace.Data;
using Marketplace.Models;
using Microsoft.AspNetCore.Authorization;
using Marketplace.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Admin;

[Authorize(Roles = Roles.Admin)]
public class TransactionsModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly Mailer _mailer;
    public TransactionsModel(AppDbContext db, Mailer mailer) { _db = db; _mailer = mailer; }

    public List<Job> ToConfirm { get; private set; } = new();
    public List<Job> ToPay { get; private set; } = new();
    public Dictionary<int, User> Freelancers { get; private set; } = new();
    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    public List<Job> Rows { get; private set; } = new();
    public decimal Earned { get; private set; }
    public decimal Pending { get; private set; }
    public decimal HeldNow { get; private set; }

    public async Task OnGetAsync()
    {
        ToConfirm = await _db.Jobs.Include(j => j.Client).AsNoTracking()
            .Where(j => j.Status == JobStatus.AwaitingPayment && j.ProofUploadedAtUtc != null).OrderBy(j => j.Id).ToListAsync();
        ToPay = await _db.Jobs.Include(j => j.Client).AsNoTracking()
            .Where(j => j.Status == JobStatus.AwaitingPayout).OrderBy(j => j.Id).ToListAsync();
        var ids = ToPay.Select(j => j.AcceptedFreelancerId).Distinct().ToList();
        Freelancers = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        Rows = await _db.Jobs.Include(j => j.Client).AsNoTracking()
            .Where(j => j.Status == JobStatus.InProgress || j.Status == JobStatus.Completed || j.Status == JobStatus.AwaitingPayout)
            .OrderByDescending(j => j.Id).ToListAsync();
        Earned = Rows.Where(j => j.Status == JobStatus.Completed).Sum(j => j.CommissionAmount ?? 0);
        Pending = Rows.Where(j => j.Status != JobStatus.Completed).Sum(j => j.CommissionAmount ?? 0);
        // Client payments currently sitting in escrow, waiting for the work to finish.
        HeldNow = Rows.Where(j => j.Escrow == EscrowStatus.Held).Sum(j => j.AgreedPrice ?? 0);
    }

    // Owner saw the money arrive: the job starts.
    public async Task<IActionResult> OnPostConfirmAsync(int id)
    {
        var j = await _db.Jobs.Include(x => x.Client).FirstOrDefaultAsync(x => x.Id == id);
        if (j is null || j.Status != JobStatus.AwaitingPayment || j.ProofUploadedAtUtc == null) { Error = "That payment is not waiting for confirmation."; return RedirectToPage(); }
        j.Status = JobStatus.InProgress;
        j.Escrow = EscrowStatus.Held;
        j.PaidAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        var fl = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == j.AcceptedFreelancerId);
        var path = "/Jobs/Details/" + id;
        _mailer.Send(j.Client?.Email, "Payment confirmed: " + j.Title, "Your payment is confirmed",
            $"<p>Sho8lana confirmed your payment of <strong>{j.AgreedPrice:N2} EGP</strong> for <strong>{Mailer.E(j.Title)}</strong>. The freelancer can start now. The money is held by Sho8lana until you approve the work.</p>", "View the job", path);
        _mailer.Send(fl?.Email, "You can start: " + j.Title, "The client's payment is confirmed",
            $"<p>Sho8lana confirmed the client's payment for <strong>{Mailer.E(j.Title)}</strong>. You can start now. Upload your files on the job page, then submit the work for approval.</p>", "Open the job", path);
        Message = $"Payment confirmed for {j.PaymentRef}. The job is now in progress and both sides were emailed.";
        return RedirectToPage();
    }

    // The proof is wrong (no money, wrong amount, fake): tell the client to send a new one.
    public async Task<IActionResult> OnPostRejectProofAsync(int id, string? note)
    {
        var j = await _db.Jobs.Include(x => x.Client).FirstOrDefaultAsync(x => x.Id == id);
        if (j is null || j.Status != JobStatus.AwaitingPayment || j.ProofUploadedAtUtc == null) { Error = "That payment is not waiting for confirmation."; return RedirectToPage(); }
        note = (note ?? "").Trim();
        if (note.Length < 3) { Error = "Write a short note so the client knows what is wrong."; return RedirectToPage(); }
        if (note.Length > 1000) note = note[..1000];
        j.ProofUploadedAtUtc = null;
        j.PaymentNote = note;
        _db.PaymentProofs.RemoveRange(_db.PaymentProofs.Where(p => p.JobId == id));
        await _db.SaveChangesAsync();
        _mailer.Send(j.Client?.Email, "Payment proof rejected: " + j.Title, "We could not confirm your payment",
            $"<p>Sho8lana could not confirm your payment for <strong>{Mailer.E(j.Title)}</strong>.</p><p style='background:#f3f5fb;padding:12px;border-radius:8px'>{Mailer.E(note)}</p><p>Please check, and upload a new proof on the payment page.</p>", "Open the payment page", "/Jobs/Pay/" + id);
        Message = $"Proof rejected for {j.PaymentRef}. The client was emailed.";
        return RedirectToPage();
    }

    // The owner sent the freelancer's share: now the job is completed and paid.
    public async Task<IActionResult> OnPostMarkSentAsync(int id)
    {
        var j = await _db.Jobs.Include(x => x.Client).FirstOrDefaultAsync(x => x.Id == id);
        if (j is null || j.Status != JobStatus.AwaitingPayout) { Error = "That payout is not waiting."; return RedirectToPage(); }
        JobMoney.MarkSent(j);
        await _db.SaveChangesAsync();
        var fl = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == j.AcceptedFreelancerId);
        JobMoney.NotifySent(_mailer, j, fl);
        Message = $"Payout marked as sent for {j.PaymentRef}. The job is completed and both sides were emailed.";
        return RedirectToPage();
    }
}
