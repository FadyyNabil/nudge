using System.ComponentModel.DataAnnotations;
using Marketplace.Data;
using Marketplace.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _hasher;

    public RegisterModel(AppDbContext db, IPasswordHasher<User> hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    // [BindProperty] fills this object from the submitted form.
    [BindProperty]
    public InputModel Input { get; set; } = new();

    // The form's shape and its validation rules (checked on the server).
    public class InputModel
    {
        [Required, StringLength(100, MinimumLength = 2)]
        public string FullName { get; set; } = "";

        // Public sign-up may only create Clients or Freelancers. The Admin role
        // is never accepted here, even if someone edits the form.
        [Required, RegularExpression("^(Client|Freelancer)$", ErrorMessage = "Choose Client or Freelancer.")]
        public string Role { get; set; } = Roles.Freelancer;

        [Required, EmailAddress, StringLength(256)]
        public string Email { get; set; } = "";

        // At least 8 chars with a letter and a digit.
        [Required, StringLength(100, MinimumLength = 8)]
        [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$",
            ErrorMessage = "Password must contain at least one letter and one number.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required, DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        public string ConfirmPassword { get; set; } = "";

        // Link to the person's work (website, GitHub, Behance, LinkedIn...). Required for freelancers.
        [StringLength(500)]
        public string? PortfolioUrl { get; set; }
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var portfolio = Input.PortfolioUrl?.Trim();
        if (string.IsNullOrEmpty(portfolio) && Input.Role == Roles.Freelancer)
            ModelState.AddModelError("Input.PortfolioUrl", "Add a link to your portfolio so the owner can review your work.");
        else if (!string.IsNullOrEmpty(portfolio) &&
                 !(Uri.TryCreate(portfolio, UriKind.Absolute, out var pu) && (pu.Scheme == Uri.UriSchemeHttp || pu.Scheme == Uri.UriSchemeHttps)))
            ModelState.AddModelError("Input.PortfolioUrl", "Enter a full link starting with http:// or https://");
        if (!ModelState.IsValid) return Page();

        var email = Input.Email.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Email == email))
        {
            ModelState.AddModelError(string.Empty, "An account with this email already exists.");
            return Page();
        }

        var user = new User { FullName = Input.FullName.Trim(), Email = email, Role = Input.Role,
            PortfolioUrl = string.IsNullOrEmpty(portfolio) ? null : portfolio, AccountStatus = ApprovalStatus.PendingApproval };
        user.PasswordHash = _hasher.HashPassword(user, Input.Password);

        _db.Users.Add(user);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique index caught a race between two simultaneous sign-ups.
            ModelState.AddModelError(string.Empty, "An account with this email already exists.");
            return Page();
        }

        return RedirectToPage("/Account/Login", new { registered = true });
    }
}
