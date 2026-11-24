using System.ComponentModel.DataAnnotations;
using Marketplace.Data;
using Marketplace.Models;
using Marketplace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Portfolio;

// Freelancers manage ONLY their own items (the owner id always comes from the login cookie).
[Authorize(Roles = Roles.Freelancer)]
[RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
[RequestSizeLimit(6 * 1024 * 1024)]
public class ManageModel : PageModel
{
    public const int MaxItems = 10;

    private readonly AppDbContext _db;
    private readonly PortfolioStorage _storage;
    public ManageModel(AppDbContext db, PortfolioStorage storage) { _db = db; _storage = storage; }

    public List<PortfolioItem> Items { get; private set; } = new();
    [TempData] public string? Message { get; set; }

    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, StringLength(120, MinimumLength = 3)]
        public string Title { get; set; } = "";

        [StringLength(1000)]
        public string? Description { get; set; }

        [StringLength(500)]
        public string? Url { get; set; }

        public IFormFile? File { get; set; }
    }

    private async Task LoadAsync() =>
        Items = await _db.PortfolioItems.AsNoTracking()
            .Where(p => p.FreelancerId == User.GetUserId()).OrderByDescending(p => p.Id).ToListAsync();

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (Items.Count >= MaxItems) { ModelState.AddModelError(string.Empty, $"You can have at most {MaxItems} items."); return Page(); }

        var url = Input.Url?.Trim();
        if (string.IsNullOrEmpty(url) && Input.File is null)
            ModelState.AddModelError(string.Empty, "Add a link, a file, or both.");

        // Only absolute http/https links. This blocks "javascript:" and similar tricks.
        if (!string.IsNullOrEmpty(url) &&
            !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
            ModelState.AddModelError("Input.Url", "Enter a full link starting with http:// or https://");

        if (Input.File is not null)
        {
            var problem = await _storage.ValidateAsync(Input.File);
            if (problem is not null) ModelState.AddModelError("Input.File", problem);
        }
        if (!ModelState.IsValid) return Page();

        var item = new PortfolioItem
        {
            FreelancerId = User.GetUserId(),
            Title = Input.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim(),
            Url = string.IsNullOrEmpty(url) ? null : url
        };
        if (Input.File is not null)
        {
            var (stored, mime) = await _storage.SaveAsync(Input.File);
            item.StoredFileName = stored;
            item.ContentType = mime;
            item.OriginalFileName = Path.GetFileName(Input.File.FileName);
        }
        _db.PortfolioItems.Add(item);
        await _db.SaveChangesAsync();
        Message = "Portfolio item added.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        // Match on BOTH id and owner, so nobody can delete someone else's item.
        var item = await _db.PortfolioItems.FirstOrDefaultAsync(p => p.Id == id && p.FreelancerId == User.GetUserId());
        if (item is null) return NotFound();
        _storage.Delete(item.StoredFileName);
        _db.PortfolioItems.Remove(item);
        await _db.SaveChangesAsync();
        Message = "Item deleted.";
        return RedirectToPage();
    }
}
