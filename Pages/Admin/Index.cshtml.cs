using Marketplace.Data;
using Marketplace.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Admin;

// Only the owner. Everyone else gets "access denied" (or the login page if anonymous).
[Authorize(Roles = Roles.Admin)]
public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public decimal CommissionEarned { get; private set; }
    public decimal CommissionPending { get; private set; }
    public decimal CompletedValue { get; private set; }
    public int ClientCount { get; private set; }
    public int FreelancerCount { get; private set; }
    public int Open { get; private set; }
    public int InProgress { get; private set; }
    public int Completed { get; private set; }
    public int Cancelled { get; private set; }
    public int AwaitingApproval { get; private set; }

    public List<Job> RecentJobs { get; private set; } = new();

    public async Task OnGetAsync()
    {
        RecentJobs = await _db.Jobs.Include(j => j.Client).AsNoTracking().OrderByDescending(j => j.Id).Take(10).ToListAsync();
        ClientCount = await _db.Users.CountAsync(u => u.Role == Roles.Client);
        FreelancerCount = await _db.Users.CountAsync(u => u.Role == Roles.Freelancer);
        AwaitingApproval = await _db.Applications.CountAsync(a => a.Approval == ApprovalStatus.PendingApproval && a.Job!.Status == JobStatus.Open);
        Open = await _db.Jobs.CountAsync(j => j.Status == JobStatus.Open);
        InProgress = await _db.Jobs.CountAsync(j => j.Status == JobStatus.InProgress || j.Status == JobStatus.AwaitingPayment || j.Status == JobStatus.AwaitingPayout);
        Completed = await _db.Jobs.CountAsync(j => j.Status == JobStatus.Completed);
        Cancelled = await _db.Jobs.CountAsync(j => j.Status == JobStatus.Cancelled);

        // Money is summed in C# (not SQL) because SQLite cannot SUM decimal columns.
        var money = await _db.Jobs
            .Where(j => j.Status == JobStatus.Completed || j.Status == JobStatus.InProgress || j.Status == JobStatus.AwaitingPayout)
            .Select(j => new { j.Status, j.AgreedPrice, j.CommissionAmount }).ToListAsync();

        CommissionEarned = money.Where(m => m.Status == JobStatus.Completed).Sum(m => m.CommissionAmount ?? 0);
        CommissionPending = money.Where(m => m.Status != JobStatus.Completed).Sum(m => m.CommissionAmount ?? 0);
        CompletedValue = money.Where(m => m.Status == JobStatus.Completed).Sum(m => m.AgreedPrice ?? 0);
    }
}
