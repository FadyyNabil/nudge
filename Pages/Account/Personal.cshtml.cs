using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Marketplace.Data;
using Marketplace.Models;
using Marketplace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Marketplace.Pages.Account;

[Authorize]
[RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
[RequestSizeLimit(6 * 1024 * 1024)]
public class PersonalModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly PortfolioStorage _storage;
    public PersonalModel(AppDbContext db, PortfolioStorage storage) { _db = db; _storage = storage; }

    [BindProperty, Required, StringLength(100, MinimumLength = 2)]
    public string FullName { get; set; } = "";

    // Freelancer-only settings.
    [BindProperty, StringLength(500)] public string? PortfolioUrl { get; set; }
    [BindProperty, StringLength(100)] public string? InstaPayNumber { get; set; }
    [BindProperty] public IFormFile? Cv { get; set; }

    public string Email { get; private set; } = "";
    public string Role { get; private set; } = "";
    public int UserId { get; private set; }
    public string? CvFileName { get; private set; }
    public bool IsFreelancer => Role == Roles.Freelancer;
    [TempData] public string? Message { get; set; }

    private int Me => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public async Task<IActionResult> OnGetAsync()
    {
        var u = await _db.Users.FindAsync(Me);
        if (u is null) return RedirectToPage("/Account/Login");
        FullName = u.FullName; Email = u.Email; Role = u.Role; UserId = u.Id;
        PortfolioUrl = u.PortfolioUrl; InstaPayNumber = u.InstaPayNumber; CvFileName = u.CvFileName;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var u = await _db.Users.FindAsync(Me);
        if (u is null) return RedirectToPage("/Account/Login");
        Email = u.Email; Role = u.Role; UserId = u.Id; CvFileName = u.CvFileName;

        var portfolio = PortfolioUrl?.Trim();
        var instaPay = InstaPayNumber?.Trim();
        byte[]? cvBytes = null; string? cvMime = null;
        if (u.Role == Roles.Freelancer)
        {
            if (string.IsNullOrEmpty(portfolio))
                ModelState.AddModelError(nameof(PortfolioUrl), "Add a link to your portfolio.");
            else if (!(Uri.TryCreate(portfolio, UriKind.Absolute, out var pu) && (pu.Scheme == Uri.UriSchemeHttp || pu.Scheme == Uri.UriSchemeHttps)))
                ModelState.AddModelError(nameof(PortfolioUrl), "Enter a full link starting with http:// or https://");
            if (string.IsNullOrEmpty(instaPay))
                ModelState.AddModelError(nameof(InstaPayNumber), "Add your InstaPay number so you can be paid.");
            if (Cv is not null && Cv.Length > 0)
            {
                var problem = await _storage.ValidateAsync(Cv);
                if (problem is not null) ModelState.AddModelError(nameof(Cv), problem);
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

        u.FullName = FullName.Trim();
        if (u.Role == Roles.Freelancer)
        {
            u.PortfolioUrl = portfolio;
            u.InstaPayNumber = instaPay;
            if (cvBytes is not null)
            {
                u.CvFileName = Path.GetFileName(Cv!.FileName);
                u.CvContentType = cvMime;
                u.CvData = cvBytes;
            }
        }
        await _db.SaveChangesAsync();
        var claims = User.Claims.Where(c => c.Type != ClaimTypes.Name).Append(new Claim(ClaimTypes.Name, u.FullName));
        await HttpContext.SignInAsync("AuthCookie", new ClaimsPrincipal(new ClaimsIdentity(claims, "AuthCookie")));
        Message = "Saved.";
        return RedirectToPage();
    }
}
