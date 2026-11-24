using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Marketplace.Data;
using Marketplace.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Marketplace.Pages.Account;

[Authorize]
public class SecurityModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    public SecurityModel(AppDbContext db, IPasswordHasher<User> hasher) { _db = db; _hasher = hasher; }

    [BindProperty] public InputModel Input { get; set; } = new();
    [TempData] public string? Message { get; set; }

    public class InputModel
    {
        [Required, DataType(DataType.Password)] public string Current { get; set; } = "";
        [Required, MinLength(8), DataType(DataType.Password)] public string New { get; set; } = "";
        [Required, Compare(nameof(New), ErrorMessage = "Passwords do not match."), DataType(DataType.Password)] public string Confirm { get; set; } = "";
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var u = await _db.Users.FindAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        if (u is null) return RedirectToPage("/Account/Login");
        if (_hasher.VerifyHashedPassword(u, u.PasswordHash, Input.Current) == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError("Input.Current", "Current password is wrong.");
            return Page();
        }
        u.PasswordHash = _hasher.HashPassword(u, Input.New);
        await _db.SaveChangesAsync();
        Message = "Password changed.";
        return RedirectToPage();
    }
}
