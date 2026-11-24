using Marketplace.Data;
using Marketplace.Models;
using Marketplace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Jobs;

/// <summary>
/// Downloads one delivered file. Allowed viewers:
///   the freelancer who sent it, the admin,
///   and the client who owns the job ONLY after the work was submitted for approval (or approved).
/// A client whose payment was refunded (cancelled job) is locked out again.
/// </summary>
[Authorize]
public class DeliveryFileModel : PageModel
{
    private readonly AppDbContext _db;
    public DeliveryFileModel(AppDbContext db) => _db = db;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var file = await _db.DeliveryFiles.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (file is null) return NotFound();
        var job = await _db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == file.JobId);
        if (job is null) return NotFound();

        var me = User.GetUserId();
        var allowed = User.IsInRole(Roles.Admin)
            || file.FreelancerId == me
            || (job.ClientId == me && User.IsInRole(Roles.Client) && ((job.Escrow == EscrowStatus.Held && job.DeliveredAtUtc != null) || job.Escrow == EscrowStatus.Released));
        if (!allowed) return Forbid();

        // nosniff stops the browser from guessing a different type than the one we send.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var inline = file.ContentType.StartsWith("image/") || file.ContentType == "application/pdf";
        if (inline) Response.Headers["Content-Disposition"] = "inline";
        return File(file.Data, file.ContentType, inline ? null : file.OriginalFileName);
    }
}
