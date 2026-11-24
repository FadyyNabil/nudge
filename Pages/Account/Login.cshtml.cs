using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Marketplace.Data;
using Marketplace.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Account;

public class LoginModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IConfiguration _config;

    public LoginModel(AppDbContext db, IPasswordHasher<User> hasher, IConfiguration config)
    {
        _db = db;
        _hasher = hasher;
        _config = config;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Registered { get; set; }

    // Errors handed over from the social-login callback page.
    [TempData]
    public string? LoginError { get; set; }

    [TempData]
    public string? Info { get; set; }

    // The social buttons only appear once the owner adds that provider's keys.
    public bool GoogleEnabled => ProviderEnabled("Google");
    public bool LinkedInEnabled => ProviderEnabled("LinkedIn");

    private bool ProviderEnabled(string provider) =>
        !string.IsNullOrWhiteSpace(_config[$"Authentication:{provider}:ClientId"]) &&
        !string.IsNullOrWhiteSpace(_config[$"Authentication:{provider}:ClientSecret"]);

    public class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var email = Input.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        // Same message for "no such email" and "wrong password", so attackers
        // cannot use the form to find out which emails are registered.
        var result = user is null
            ? PasswordVerificationResult.Failed
            : _hasher.VerifyHashedPassword(user, user.PasswordHash, Input.Password);

        if (user is null || result == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return Page();
        }

        // Correct password, but the owner has suspended this account.
        if (!user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "This account has been suspended. Contact the site owner.");
            return Page();
        }

        // Email not verified yet: send the person to the code page.
 if (user.AccountStatus == ApprovalStatus.EmailUnverified)
 {
 return RedirectToPage("/Account/VerifyEmail", new { email = user.Email });
 }

 // New accounts wait for the owner's decision.
        if (user.AccountStatus == ApprovalStatus.PendingApproval)
        {
            ModelState.AddModelError(string.Empty, "Your account is under review. You can log in as soon as the owner approves it.");
            return Page();
        }
        if (user.AccountStatus == ApprovalStatus.Rejected)
        {
            ModelState.AddModelError(string.Empty, "Your account request was declined. Contact the site owner.");
            return Page();
        }

        // Claims = facts about the user stored inside the cookie.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)   // lets [Authorize(Roles = "...")] work
        };
        var identity = new ClaimsIdentity(claims, "AuthCookie");
        await HttpContext.SignInAsync("AuthCookie", new ClaimsPrincipal(identity));

        // Only follow local return URLs (prevents open-redirect attacks).
        if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            return LocalRedirect(ReturnUrl);

        return RedirectToPage("/Dashboard");
    }
}
