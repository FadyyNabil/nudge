using Marketplace.Data;
using Marketplace.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Admin;

/// <summary>Owner-only: approve or reject applications before clients can see them.</summary>
[Authorize(Roles = Roles.Admin)]
public class ApplicationsModel : PageModel
{
    private readonly AppDbContext _db;
    public ApplicationsModel(AppDbContext db) => _db = db;

    public List<JobApplication> Pending { get; private set; } = new();
    public List<JobApplication> Reviewed { get; private set; } = new();
    public Dictionary<int, List<PortfolioItem>> Portfolios { get; private set; } = new();
    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        // Only applications for jobs that are still open are worth reviewing.
        Pending = await _db.Applications.Include(a => a.Freelancer).Include(a => a.Job)
            .Where(a => a.Approval == ApprovalStatus.PendingApproval && a.Job!.Status == JobStatus.Open)
            .OrderBy(a => a.Id).ToListAsync();
        Reviewed = await _db.Applications.AsNoTracking().Include(a => a.Freelancer).Include(a => a.Job)
            .Where(a => a.Approval != ApprovalStatus.PendingApproval)
            .OrderByDescending(a => a.ReviewedAtUtc).Take(20).ToListAsync();

        var ids = Pending.Select(a => a.FreelancerId).Distinct().ToList();
        var items = await _db.PortfolioItems.AsNoTracking().Where(p => ids.Contains(p.FreelancerId)).OrderBy(p => p.Id).ToListAsync();
        Portfolios = items.GroupBy(p => p.FreelancerId).ToDictionary(g => g.Key, g => g.ToList());
    }

    public Task<IActionResult> OnPostApproveAsync(int id) => ReviewAsync(id, ApprovalStatus.Approved);
    public Task<IActionResult> OnPostRejectAsync(int id) => ReviewAsync(id, ApprovalStatus.Rejected);

    private async Task<IActionResult> ReviewAsync(int id, ApprovalStatus decision)
    {
        var app = await _db.Applications.Include(a => a.Job).FirstOrDefaultAsync(a => a.Id == id);
        if (app is null) return NotFound();

        // A decision can only be made once, and only while the job is still open.
        if (app.Approval != ApprovalStatus.PendingApproval || app.Job!.Status != JobStatus.Open)
        {
            Message = "That application can no longer be reviewed.";
            return RedirectToPage();
        }

        app.Approval = decision;
        app.ReviewedAtUtc = DateTime.UtcNow;
        if (decision == ApprovalStatus.Rejected) app.Status = ApplicationStatus.Rejected;
        await _db.SaveChangesAsync();
        Message = decision == ApprovalStatus.Approved ? "Application approved. The client can now see it." : "Application rejected.";
        return RedirectToPage();
    }
}
