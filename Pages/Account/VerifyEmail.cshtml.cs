using System.Collections.Concurrent;
using Marketplace.Data;
using Marketplace.Models;
using Marketplace.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Account;

public class VerifyEmailModel : PageModel
{
    // Wrong-code counter and last-send time per email (in memory, resets on restart).
    private static readonly ConcurrentDictionary<string, (int Fails, DateTime Since)> Attempts = new();
    private static readonly ConcurrentDictionary<string, DateTime> LastSent = new();

    private readonly AppDbContext _db;
    private readonly EmailVerifier _verifier;
    private readonly Mailer _mailer;

    public VerifyEmailModel(AppDbContext db, EmailVerifier verifier, Mailer mailer)
    {
        _db = db;
        _verifier = verifier;
        _mailer = mailer;
    }

    [BindProperty(SupportsGet = true)]
    public string Email { get; set; } = "";

    [BindProperty, System.ComponentModel.DataAnnotations.StringLength(12)]
    public string Code { get; set; } = "";

    [TempData]
    public string? Info { get; set; }

    public string? Error { get; set; }

    public void OnGet() { }

    private async Task<User?> FindUnverifiedAsync()
    {
        var email = (Email ?? "").Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        return user is not null && user.AccountStatus == ApprovalStatus.EmailUnverified ? user : null;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await FindUnverifiedAsync();
        if (user is null)
        {
            Error = "This email is not waiting for verification. Try logging in.";
            return Page();
        }
        var key = user.Email;
        var a = Attempts.GetOrAdd(key, _ => (0, DateTime.UtcNow));
        if (a.Fails >= 5 && DateTime.UtcNow - a.Since < TimeSpan.FromMinutes(15))
        {
            Error = "Too many wrong codes. Wait 15 minutes, then try again.";
            return Page();
        }
        if (!_verifier.Check(user, Code))
        {
            var fresh = a.Fails == 0 || a.Fails >= 5;
            Attempts[key] = (a.Fails >= 5 ? 1 : a.Fails + 1, fresh ? DateTime.UtcNow : a.Since);
            Error = "That code is wrong or has expired.";
            return Page();
        }
        Attempts.TryRemove(key, out _);
        user.AccountStatus = ApprovalStatus.PendingApproval;
        await _db.SaveChangesAsync();
        _mailer.NotifyNewSignup(user);
        return RedirectToPage("/Account/Login", new { registered = true });
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        var user = await FindUnverifiedAsync();
        if (user is null)
        {
            Error = "This email is not waiting for verification. Try logging in.";
            return Page();
        }
        if (LastSent.TryGetValue(user.Email, out var t) && DateTime.UtcNow - t < TimeSpan.FromSeconds(60))
        {
            Error = "Please wait a minute before asking for another code.";
            return Page();
        }
        LastSent[user.Email] = DateTime.UtcNow;
        var sent = await _verifier.SendCodeAsync(user);
        Info = sent ? "We sent a new code. Check your inbox and spam folder." : "We could not send the email right now. Try again in a minute.";
        return RedirectToPage(new { email = user.Email });
    }
}
