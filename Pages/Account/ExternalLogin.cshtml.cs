using System.Security.Claims;
using Marketplace.Data;
using Marketplace.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Account;

/// <summary>
/// Starts and finishes "Continue with Google/LinkedIn".
/// /Account/ExternalLogin?provider=Google sends the person to the provider.
/// The provider returns to /Account/ExternalLogin?handler=Callback with a
/// temporary ExternalCookie; we read the email from it and either log the
/// person in or send them to finish signing up.
/// </summary>
public class ExternalLoginModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public ExternalLoginModel(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [TempData]
    public string? LoginError { get; set; }

    public IActionResult OnGet(string provider)
    {
        if (!ProviderEnabled(provider)) return RedirectToPage("/Account/Login");
        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Page("/Account/ExternalLogin", pageHandler: "Callback")
        };
        return Challenge(properties, provider);
    }

    public async Task<IActionResult> OnGetCallbackAsync()
    {
        var result = await HttpContext.AuthenticateAsync("ExternalCookie");
        if (!result.Succeeded || result.Principal is null)
        {
            await HttpContext.SignOutAsync("ExternalCookie");
            LoginError = "Sign-in did not complete. Try again.";
            return RedirectToPage("/Account/Login");
        }

        var email = result.Principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        var name = result.Principal.FindFirstValue(ClaimTypes.Name) ?? "";
        var provider = result.Principal.Identity?.AuthenticationType ?? "external";
        await HttpContext.SignOutAsync("ExternalCookie");

        if (string.IsNullOrEmpty(email))
        {
            LoginError = $"Your {provider} account did not share an email address, so we cannot use it here.";
            return RedirectToPage("/Account/Login");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            // No account yet: finish on the normal sign-up form, prefilled.
            return RedirectToPage("/Account/Register", new { email, name, from = provider });
        }

        // Same gates as password login.
        if (!user.IsActive)
        {
            LoginError = "This account has been suspended. Contact the site owner.";
            return RedirectToPage("/Account/Login");
        }
        if (user.AccountStatus == ApprovalStatus.PendingApproval)
        {
            LoginError = "Your account is under review. You can log in as soon as the owner approves it.";
            return RedirectToPage("/Account/Login");
        }
        if (user.AccountStatus == ApprovalStatus.Rejected)
        {
            LoginError = "Your account request was declined. Contact the site owner.";
            return RedirectToPage("/Account/Login");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };
        var identity = new ClaimsIdentity(claims, "AuthCookie");
        await HttpContext.SignInAsync("AuthCookie", new ClaimsPrincipal(identity));

        return RedirectToPage("/Dashboard");
    }

    private bool ProviderEnabled(string provider) => provider switch
    {
        "Google" or "LinkedIn" =>
            !string.IsNullOrWhiteSpace(_config[$"Authentication:{provider}:ClientId"]) &&
            !string.IsNullOrWhiteSpace(_config[$"Authentication:{provider}:ClientSecret"]),
        _ => false
    };
}
