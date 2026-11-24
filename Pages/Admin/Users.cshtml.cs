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
    public UsersModel(AppDbContext db) => _db = db;

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
