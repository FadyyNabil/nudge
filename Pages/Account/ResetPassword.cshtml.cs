using System.ComponentModel.DataAnnotations;
using Marketplace.Data;
using Marketplace.Models;
using Marketplace.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Marketplace.Pages.Account;

/// <summary>Step 2 of a password reset: the emailed link opens this page.</summary>
public class ResetPasswordModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly ResetTokens _tokens;
    private readonly Mailer _mailer;
    public ResetPasswordModel(AppDbContext db, IPasswordHasher<User> hasher, ResetTokens tokens, Mailer mailer)
    { _db = db; _hasher = hasher; _tokens = tokens; _mailer = mailer; }

    [BindProperty(SupportsGet = true), StringLength(2000)] public string T { get; set; } = "";
    public bool Invalid { get; private set; }

    [BindProperty] public InputModel Input { get; set; } = new();
    public class InputModel
    {
        [Required, StringLength(100, MinimumLength = 8)]
        [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Password must contain at least one letter and one number.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required, DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }

    // The link can carry the token as ?t=...; GET only checks it, it never changes anything.
    public async Task<IActionResult> OnGetAsync(string? t)
    {
        T = t ?? T ?? "";
        Invalid = await _tokens.ReadAsync(_db, T) is null;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _tokens.ReadAsync(_db, T);
        if (user is null) { Invalid = true; return Page(); }
        if (!ModelState.IsValid) return Page();
        user.PasswordHash = _hasher.HashPassword(user, Input.Password);   // also kills this link (it is tied to the old hash)
        await _db.SaveChangesAsync();
        _mailer.Send(user.Email, "Your Sho8lana password was changed", "Password changed",
            "<p>The password for your Sho8lana account was just changed. If this was not you, reset it again right away and tell the site owner.</p>",
            "Log in", "/Account/Login");
        TempData["Info"] = "Password changed. Log in with your new password.";
        return RedirectToPage("/Account/Login");
    }
}
