using Marketplace.Data;
using Marketplace.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Admin;

[Authorize(Roles = Roles.Admin)]
public class UsersModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly Marketplace.Services.Mailer _mailer;
    public UsersModel(AppDbContext db, Marketplace.Services.Mailer mailer) { _db = db; _mailer = mailer; }

    public List<User> Users { get; private set; } = new();
    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync() =>
        Users = await _db.Users.AsNoTracking().OrderBy(u => u.Role).ThenBy(u => u.Id).ToListAsync();

    public Task<IActionResult> OnPostApproveAsync(int id) => DecideAsync(id, ApprovalStatus.Approved);
    public Task<IActionResult> OnPostDeclineAsync(int id) => DecideAsync(id, ApprovalStatus.Rejected);

    private async Task<IActionResult> DecideAsync(int id, ApprovalStatus decision)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();
        if (user.Role == Roles.Admin) return RedirectToPage();
        user.AccountStatus = decision;
        await _db.SaveChangesAsync();
        if (decision == ApprovalStatus.Approved)
            _mailer.Send(user.Email, "Your Sho8lana account is approved", "Welcome to Sho8lana",
                $"<p>Hi {Marketplace.Services.Mailer.E(user.FullName)}, your account was approved. You can log in now.</p>"
                + (user.Role == Roles.Freelancer ? "<p>Next: browse open jobs and send your first application.</p>" : "<p>Next: post your first job and get applications from freelancers.</p>"),
                "Log in", "/Account/Login");
        else
            _mailer.Send(user.Email, "About your Sho8lana account request", "Your request was declined",
                $"<p>Hi {Marketplace.Services.Mailer.E(user.FullName)}, we could not approve your account request this time. If you think this is a mistake, reply to the site owner.</p>");
        Message = decision == ApprovalStatus.Approved ? $"{user.FullName} approved. They can log in now." : $"{user.FullName} declined.";
        return RedirectToPage();
    }

    // Suspend or reactivate. Admin accounts cannot be suspended (so the owner cannot lock himself out).
    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null) return NotFound();
        if (user.Role == Roles.Admin) return RedirectToPage();

        user.IsActive = !user.IsActive;
        await _db.SaveChangesAsync();
        Message = user.IsActive ? $"{user.FullName} reactivated." : $"{user.FullName} suspended.";
        return RedirectToPage();
    }
}
