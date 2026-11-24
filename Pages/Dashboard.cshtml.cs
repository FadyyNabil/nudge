using Marketplace.Data;
using Marketplace.Models;
using Marketplace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages;

// Any logged-in user. The content depends on the role; admins are sent to /Admin.
[Authorize]
public class DashboardModel : PageModel
{
    private readonly AppDbContext _db;
    public DashboardModel(AppDbContext db) => _db = db;

    public string FullName { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string Role { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }
    public List<Job> MyJobs { get; private set; } = new();
    public List<JobApplication> MyApplications { get; private set; } = new();
    public decimal Earnings { get; private set; }
    public int PortfolioCount { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _db.Users.FindAsync(User.GetUserId());
        if (user is null) return RedirectToPage("/Account/Login");

        FullName = user.FullName; Email = user.Email; Role = user.Role; CreatedAt = user.CreatedAtUtc;

        if (user.Role == Roles.Admin) return RedirectToPage("/Admin/Index");

        if (user.Role == Roles.Client)
        {
            // Include only approved applications: the client must not even see a count of unapproved ones.
            MyJobs = await _db.Jobs.Include(j => j.Applications.Where(a => a.Approval == ApprovalStatus.Approved))
                .Where(j => j.ClientId == user.Id).OrderByDescending(j => j.Id).ToListAsync();
        }
        else
        {
            PortfolioCount = await _db.PortfolioItems.CountAsync(p => p.FreelancerId == user.Id);
            MyApplications = await _db.Applications.Include(a => a.Job)
                .Where(a => a.FreelancerId == user.Id).OrderByDescending(a => a.Id).ToListAsync();

            // Sum on the C# side: SQLite cannot SUM decimal columns in SQL.
            var paid = await _db.Jobs
                .Where(j => j.AcceptedFreelancerId == user.Id && j.Status == JobStatus.Completed)
                .Select(j => new { j.AgreedPrice, j.CommissionAmount }).ToListAsync();
            Earnings = paid.Sum(p => (p.AgreedPrice ?? 0) - (p.CommissionAmount ?? 0));
        }
        return Page();
    }
}
