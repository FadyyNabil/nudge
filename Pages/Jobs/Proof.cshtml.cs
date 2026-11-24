using Marketplace.Services;
using Marketplace.Data;
using Marketplace.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Jobs;

/// <summary>Shows the payment proof of a job. Only the admin and the client who paid can open it.</summary>
[Authorize]
public class ProofModel : PageModel
{
    private readonly AppDbContext _db;
    public ProofModel(AppDbContext db) => _db = db;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var job = await _db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id);
        if (job is null) return NotFound();
        var me = User.GetUserId();
        if (!(User.IsInRole(Roles.Admin) || (job.ClientId == me && User.IsInRole(Roles.Client)))) return Forbid();
        var proof = await _db.PaymentProofs.AsNoTracking().Where(p => p.JobId == id).OrderByDescending(p => p.Id).FirstOrDefaultAsync();
        if (proof is null) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Disposition"] = "inline";
        return File(proof.Data, proof.ContentType);
    }
}
