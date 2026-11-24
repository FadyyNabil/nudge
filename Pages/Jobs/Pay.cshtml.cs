using Marketplace.Data;
using Marketplace.Models;
using Marketplace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Jobs;

/// <summary>
/// The payment page. After hiring, the client sends the agreed amount to the owner's InstaPay / wallet
/// number with the job's reference code in the note, then uploads a screenshot as proof.
/// The job only starts when the owner confirms the money arrived.
/// </summary>
[Authorize(Roles = Roles.Client)]
public class PayModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly PortfolioStorage _storage;
    private readonly Mailer _mailer;
    public PayModel(AppDbContext db, IConfiguration config, PortfolioStorage storage, Mailer mailer) { _db = db; _config = config; _storage = storage; _mailer = mailer; }

    public Job Job { get; private set; } = null!;
    public string OwnerNumber => JobMoney.OwnerNumber(_config);
    [BindProperty] public IFormFile? Proof { get; set; }
    [TempData] public string? Message { get; set; }
    [TempData] public string? Error { get; set; }

    private async Task<bool> LoadAsync(int id)
    {
        var j = await _db.Jobs.Include(x => x.Client).FirstOrDefaultAsync(x => x.Id == id);
        if (j is null || j.ClientId != User.GetUserId()) return false;
        Job = j;
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Job.Status != JobStatus.AwaitingPayment) { Error = "This job is not waiting for a payment."; return RedirectToPage(new { id }); }
        if (Job.ProofUploadedAtUtc != null) { Error = "Your proof is already with Sho8lana. Please wait for the confirmation."; return RedirectToPage(new { id }); }
        if (Proof is null) { Error = "Choose the screenshot of your payment first."; return RedirectToPage(new { id }); }
        var ext = Path.GetExtension(Proof.FileName).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".pdf")) { Error = "Upload the proof as a PNG, JPG or PDF file."; return RedirectToPage(new { id }); }
        var err = await _storage.ValidateAsync(Proof);
        if (err is not null) { Error = err; return RedirectToPage(new { id }); }

        await using var ms = new MemoryStream();
        await Proof.CopyToAsync(ms);
        // Replace any older proof for this job.
        var old = await _db.PaymentProofs.Where(p => p.JobId == id).ToListAsync();
        _db.PaymentProofs.RemoveRange(old);
        _db.PaymentProofs.Add(new PaymentProof
        {
            JobId = id,
            OriginalFileName = Path.GetFileName(Proof.FileName),
            ContentType = _storage.MimeFor(Proof.FileName) ?? "application/octet-stream",
            Data = ms.ToArray()
        });
        Job.ProofUploadedAtUtc = DateTime.UtcNow;
        Job.PaymentNote = null;
        await _db.SaveChangesAsync();

        _mailer.SendToAdmins("Payment proof to check: " + Job.Title, "A client sent a payment proof",
            $"<p><strong>{Mailer.E(Job.Client?.FullName)}</strong> says they paid <strong>{Job.AgreedPrice:N2} EGP</strong> for <strong>{Mailer.E(Job.Title)}</strong> (reference {Mailer.E(Job.PaymentRef)}). Check your InstaPay, then confirm or reject the proof.</p>",
            "Open payments", "/Admin/Transactions");
        Message = "Thank you. Your proof was sent. The work starts when Sho8lana confirms that the money arrived.";
        return RedirectToPage(new { id });
    }
}
