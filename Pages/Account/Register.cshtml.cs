using System.ComponentModel.DataAnnotations;
using Marketplace.Data;
using Marketplace.Models;
using Marketplace.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly PortfolioStorage _storage;
 private readonly EmailVerifier _verifier;

    public RegisterModel(AppDbContext db, IPasswordHasher<User> hasher, PortfolioStorage storage, EmailVerifier verifier)
    {
        _db = db;
        _hasher = hasher;
        _storage = storage;
 _verifier = verifier;
    }

    // [BindProperty] fills this object from the submitted form.
    [BindProperty]
    public InputModel Input { get; set; } = new();

    // The freelancer's CV file. Kept outside InputModel because it is a file, not text.
    [BindProperty]
    public IFormFile? Cv { get; set; }

    // Set when the person arrives from "Continue with Google/LinkedIn".
    public string? FromProvider { get; private set; }

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

        // The freelancer's InstaPay number or handle, so clients can pay them. Required for freelancers.
        [StringLength(100)]
        public string? InstaPayNumber { get; set; }

        // NOTE: the card fields shown to clients are a visual demo only. They are
        // intentionally NOT bound here and are never read or stored by the server.
    }

    public void OnGet(string? email, string? name, string? from)
    {
        if (!string.IsNullOrWhiteSpace(email)) Input.Email = email.Trim();
        if (!string.IsNullOrWhiteSpace(name)) Input.FullName = name.Trim();
        FromProvider = string.IsNullOrWhiteSpace(from) ? null : from.Trim();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var portfolio = Input.PortfolioUrl?.Trim();
        if (string.IsNullOrEmpty(portfolio) && Input.Role == Roles.Freelancer)
            ModelState.AddModelError("Input.PortfolioUrl", "Add a link to your portfolio so the owner can review your work.");
        else if (!string.IsNullOrEmpty(portfolio) &&
                 !(Uri.TryCreate(portfolio, UriKind.Absolute, out var pu) && (pu.Scheme == Uri.UriSchemeHttp || pu.Scheme == Uri.UriSchemeHttps)))
            ModelState.AddModelError("Input.PortfolioUrl", "Enter a full link starting with http:// or https://");

        var instaPay = Input.InstaPayNumber?.Trim();
        if (Input.Role == Roles.Freelancer && string.IsNullOrEmpty(instaPay))
            ModelState.AddModelError("Input.InstaPayNumber", "Add your InstaPay number so clients can pay you.");

        // The CV is required for freelancers and is stored in the database (the
        // container disk is wiped on every deploy). Same safety rules as portfolio files.
        byte[]? cvBytes = null;
        string? cvMime = null;
        if (Input.Role == Roles.Freelancer)
        {
            if (Cv is null || Cv.Length == 0)
            {
                ModelState.AddModelError(nameof(Cv), "Upload your CV (PDF, PNG or JPG, up to 5 MB).");
            }
            else
            {
                var problem = await _storage.ValidateAsync(Cv);
                if (problem is not null)
                {
                    ModelState.AddModelError(nameof(Cv), problem);
                }
                else
                {
                    await using var ms = new MemoryStream();
                    await Cv.CopyToAsync(ms);
                    cvBytes = ms.ToArray();
                    cvMime = _storage.MimeFor(Cv.FileName);
                }
            }
        }
        if (!ModelState.IsValid) return Page();

        var email = Input.Email.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Email == email))
        {
            ModelState.AddModelError(string.Empty, "An account with this email already exists.");
            return Page();
        }

        var user = new User
        {
            FullName = Input.FullName.Trim(),
            Email = email,
            Role = Input.Role,
            PortfolioUrl = string.IsNullOrEmpty(portfolio) ? null : portfolio,
            InstaPayNumber = string.IsNullOrEmpty(instaPay) ? null : instaPay,
            CvFileName = cvBytes is null ? null : Path.GetFileName(Cv!.FileName),
            CvContentType = cvMime,
            CvData = cvBytes,
            AccountStatus = ApprovalStatus.EmailUnverified
        };
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

        await _verifier.SendCodeAsync(user);
 return RedirectToPage("/Account/VerifyEmail", new { email = user.Email });
    }
}
