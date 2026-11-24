using Marketplace.Data;
using Marketplace.Models;
using Marketplace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Portfolio;

/// <summary>
/// Downloads an uploaded portfolio file. Allowed viewers:
///   the freelancer who owns it, the admin, and a client whose job that freelancer applied to.
/// </summary>
[Authorize]
public class FileModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly PortfolioStorage _storage;
    public FileModel(AppDbContext db, PortfolioStorage storage) { _db = db; _storage = storage; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var item = await _db.PortfolioItems.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (item?.StoredFileName is null) return NotFound();

        var me = User.GetUserId();
        var allowed = User.IsInRole(Roles.Admin)
            || item.FreelancerId == me
            || (User.IsInRole(Roles.Client) && await _db.Applications.AnyAsync(a =>
                    a.FreelancerId == item.FreelancerId && a.Job!.ClientId == me));
        if (!allowed) return Forbid();

        var path = _storage.GetPath(item.StoredFileName);
        if (path is null) return NotFound();

        // nosniff stops the browser from guessing a different type than the one we send.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Images open in the browser; PDFs are downloaded.
        var inline = item.ContentType!.StartsWith("image/");
        if (inline) Response.Headers["Content-Disposition"] = "inline";
        return PhysicalFile(path, item.ContentType!, inline ? null : item.OriginalFileName);
    }
}
