using Marketplace.Data;
using Marketplace.Models;
using Marketplace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Account;

/// <summary>
/// Downloads a freelancer's CV. Only the freelancer themself and the admin may open it.
/// The bytes come from the database, so they survive redeploys.
/// </summary>
[Authorize]
public class CvModel : PageModel
{
    private readonly AppDbContext _db;
    public CvModel(AppDbContext db) => _db = db;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user?.CvData is null || user.CvContentType is null) return NotFound();

        var me = User.GetUserId();
        if (!User.IsInRole(Roles.Admin) && me != user.Id) return Forbid();

        // nosniff stops the browser from guessing a different type than the one we send.
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var inline = user.CvContentType.StartsWith("image/") || user.CvContentType == "application/pdf";
        if (inline) Response.Headers["Content-Disposition"] = "inline";
        return File(user.CvData, user.CvContentType, inline ? null : user.CvFileName);
    }
}
