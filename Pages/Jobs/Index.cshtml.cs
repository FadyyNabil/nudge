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

    public List<Job> Jobs { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var query = _db.Jobs.Include(j => j.Client).Where(j => j.Status == JobStatus.Open);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(j => j.Title.Contains(term) || j.Description.Contains(term));
        }
        // LINQ is translated to SQL. Newest first, at most 50 rows.
        Jobs = await query.OrderByDescending(j => j.Id).Take(50).ToListAsync();
    }
}
