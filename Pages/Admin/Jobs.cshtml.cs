using Marketplace.Data;
using Marketplace.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Admin;

[Authorize(Roles = Roles.Admin)]
public class JobsModel : PageModel
{
    private readonly AppDbContext _db;
    public JobsModel(AppDbContext db) => _db = db;

    public List<Job> Jobs { get; private set; } = new();
    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync() =>
        Jobs = await _db.Jobs.Include(j => j.Client).AsNoTracking().OrderByDescending(j => j.Id).ToListAsync();

    // Moderation: the owner can cancel any open or in-progress job.
    public async Task<IActionResult> OnPostCancelAsync(int id)
    {
        var job = await _db.Jobs.FindAsync(id);
        if (job is null) return NotFound();
        if (job.Status is JobStatus.Open or JobStatus.InProgress)
        {
            job.Status = JobStatus.Cancelled;
            job.CommissionAmount = null;
            job.CommissionPercent = null;
            await _db.SaveChangesAsync();
            Message = "Job cancelled.";
        }
        return RedirectToPage();
    }
}
