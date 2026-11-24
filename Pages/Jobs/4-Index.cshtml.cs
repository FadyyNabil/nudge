using Marketplace.Data;
using Marketplace.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Jobs;

/// <summary>Public list of open jobs with a simple search box.</summary>
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? B { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Sort { get; set; }

    public List<Job> Jobs { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var query = _db.Jobs.Include(j => j.Client).Include(j => j.Applications).Where(j => j.Status == JobStatus.Open);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(j => j.Title.Contains(term) || j.Description.Contains(term));
        }
        if (B == "s") query = query.Where(j => j.Budget < 1000);
        else if (B == "m") query = query.Where(j => j.Budget >= 1000 && j.Budget <= 5000);
        else if (B == "l") query = query.Where(j => j.Budget > 5000);
        // LINQ is translated to SQL. Newest first, at most 50 rows.
        var ordered = Sort == "high" ? query.OrderByDescending(j => j.Budget)
            : Sort == "few" ? query.OrderBy(j => j.Applications.Count).ThenByDescending(j => j.Id)
            : query.OrderByDescending(j => j.Id);
        Jobs = await ordered.Take(50).ToListAsync();
    }
}
