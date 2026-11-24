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
    public decimal HeldForMe { get; private set; }
    public int PortfolioCount { get; private set; }
    public bool NeedsInstaPay { get; private set; }
    public List<(int Id, int JobId, string JobTitle, string From, DateTime At)> Nudges { get; private set; } = new();
    /// <summary>Unread chat messages per job, for the badge on each row.</summary>
    public Dictionary<int, int> UnreadChatByJob { get; private set; } = new();

    // Hide one nudge notice (only your own).
    public async Task<IActionResult> OnPostDismissNudgeAsync(int nudgeId)
    {
        var me = User.GetUserId();
        var n = await _db.NudgeLogs.FirstOrDefaultAsync(x => x.Id == nudgeId && x.ToUserId == me);
        if (n is not null && n.DismissedAtUtc == null) { n.DismissedAtUtc = DateTime.UtcNow; await _db.SaveChangesAsync(); }
        return RedirectToPage();
    }

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
            NeedsInstaPay = string.IsNullOrWhiteSpace(user.InstaPayNumber);
            PortfolioCount = await _db.PortfolioItems.CountAsync(p => p.FreelancerId == user.Id);
            MyApplications = await _db.Applications.Include(a => a.Job)
                .Where(a => a.FreelancerId == user.Id).OrderByDescending(a => a.Id).ToListAsync();

            // Sum on the C# side: SQLite cannot SUM decimal columns in SQL.
            var paid = await _db.Jobs
                .Where(j => j.AcceptedFreelancerId == user.Id && j.Status == JobStatus.Completed)
                .Select(j => new { j.AgreedPrice, j.CommissionAmount }).ToListAsync();
            Earnings = paid.Sum(p => (p.AgreedPrice ?? 0) - (p.CommissionAmount ?? 0));

            // Money held in escrow for this freelancer's jobs still in progress.
            var held = await _db.Jobs
                .Where(j => j.AcceptedFreelancerId == user.Id && j.Escrow == EscrowStatus.Held)
                .Select(j => new { j.AgreedPrice, j.CommissionAmount }).ToListAsync();
            HeldForMe = held.Sum(p => (p.AgreedPrice ?? 0) - (p.CommissionAmount ?? 0));
        }

        // Unread chat messages on jobs where this user is one of the two parties.
        var chatJobIds = user.Role == Roles.Client
            ? MyJobs.Where(j => j.AcceptedFreelancerId != null).Select(j => j.Id).ToList()
            : MyApplications.Where(ap => ap.Job!.AcceptedFreelancerId == user.Id).Select(ap => ap.JobId).ToList();
        if (chatJobIds.Count > 0)
            UnreadChatByJob = await _db.ChatMessages.AsNoTracking()
                .Where(m => m.FromUserId != user.Id && m.ReadAtUtc == null && chatJobIds.Contains(m.JobId))
                .GroupBy(m => m.JobId).Select(g => new { g.Key, C = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.C);

                var logs = await _db.NudgeLogs.AsNoTracking().Where(n => n.ToUserId == user.Id && n.DismissedAtUtc == null)
            .OrderByDescending(n => n.CreatedAtUtc).Take(20).ToListAsync();
        if (logs.Count > 0)
        {
            var jobIds = logs.Select(n => n.JobId).Distinct().ToList();
            var userIds = logs.Select(n => n.FromUserId).Distinct().ToList();
            var jobs = await _db.Jobs.AsNoTracking().Where(j => jobIds.Contains(j.Id)).ToDictionaryAsync(j => j.Id, j => j.Title);
            var names = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);
            Nudges = logs.Select(n => (n.Id, n.JobId, jobs.GetValueOrDefault(n.JobId, "a job"), names.GetValueOrDefault(n.FromUserId, "Someone"), n.CreatedAtUtc)).ToList();
        }
        return Page();
    }
}
