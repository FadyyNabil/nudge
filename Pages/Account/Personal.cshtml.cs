using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Marketplace.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Marketplace.Pages.Account;

[Authorize]
public class PersonalModel : PageModel
{
    private readonly AppDbContext _db;
    public PersonalModel(AppDbContext db) => _db = db;

    [BindProperty, Required, MaxLength(100)]
    public string FullName { get; set; } = "";
    public string Email { get; private set; } = "";
    public string Role { get; private set; } = "";
    [TempData] public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var u = await _db.Users.FindAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        if (u is null) return RedirectToPage("/Account/Login");
        FullName = u.FullName; Email = u.Email; Role = u.Role;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var u = await _db.Users.FindAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        if (u is null) return RedirectToPage("/Account/Login");
        Email = u.Email; Role = u.Role;
        if (!ModelState.IsValid) return Page();
        u.FullName = FullName.Trim();
        await _db.SaveChangesAsync();
        var claims = User.Claims.Where(c => c.Type != ClaimTypes.Name).Append(new Claim(ClaimTypes.Name, u.FullName));
        await HttpContext.SignInAsync("AuthCookie", new ClaimsPrincipal(new ClaimsIdentity(claims, "AuthCookie")));
        Message = "Saved.";
        return RedirectToPage();
    }
}
