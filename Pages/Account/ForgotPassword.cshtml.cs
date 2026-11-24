using System.Collections.Concurrent;
using Marketplace.Data;
using Marketplace.Models;
using Marketplace.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Account;

/// <summary>
/// Step 1 of a password reset: ask for the email, send a link. The answer is always the same
/// ("if an account exists...") so the page cannot be used to find out who has an account.
/// </summary>
public class ForgotPasswordModel : PageModel
{
    private static readonly ConcurrentDictionary<string, DateTime> LastSent = new();
    private readonly AppDbContext _db;
    private readonly Mailer _mailer;
    private readonly ResetTokens _tokens;
    public ForgotPasswordModel(AppDbContext db, Mailer mailer, ResetTokens tokens) { _db = db; _mailer = mailer; _tokens = tokens; }

    [BindProperty, System.ComponentModel.DataAnnotations.StringLength(256)] public string Email { get; set; } = "";
    public bool Sent { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        var email = (Email ?? "").Trim().ToLowerInvariant();
        Sent = true;
        if (email.Length == 0 || !ModelState.IsValid) { Sent = false; return Page(); }
        if (LastSent.TryGetValue(email, out var t) && DateTime.UtcNow - t < TimeSpan.FromSeconds(60)) return Page();
        LastSent[email] = DateTime.UtcNow;
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
        if (user is not null && user.IsActive && user.AccountStatus == ApprovalStatus.Approved)
        {
            var token = _tokens.Make(user);
            _mailer.Send(user.Email, "Reset your Nudge password", "Choose a new password",
                "<p>Someone (hopefully you) asked to reset the password for this account. The link works for 1 hour and only once.</p><p>If it was not you, ignore this email. Your password stays the same.</p>",
                "Reset password", "/Account/ResetPassword?t=" + Uri.EscapeDataString(token));
        }
        return Page();
    }
}
