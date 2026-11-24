using Marketplace.Data;
using Marketplace.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Services;

/// <summary>
/// The money rules for finishing a job, in one place so every path pays out the same way.
/// </summary>
public static class JobMoney
{
    /// <summary>Marks the job finished and releases the held money (agreed price minus commission) to the freelancer.</summary>
    public static void Release(Job job)
    {
        job.Status = JobStatus.Completed;
        job.CompletedAtUtc = DateTime.UtcNow;
        job.Escrow = EscrowStatus.Released;
        job.ReleasedAtUtc = DateTime.UtcNow;
    }

    public static decimal FreelancerShare(Job job) => (job.AgreedPrice ?? 0) - (job.CommissionAmount ?? 0);

    /// <summary>Delivered jobs whose client has been silent for 7 days get paid out automatically.</summary>
    public static async Task<int> AutoReleaseDueAsync(AppDbContext db, Mailer mailer)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Job.AutoReleaseDays);
        var due = await db.Jobs.Include(j => j.Client)
            .Where(j => j.Status == JobStatus.InProgress && j.Escrow == EscrowStatus.Held && j.DeliveredAtUtc != null && j.DeliveredAtUtc <= cutoff)
            .ToListAsync();
        foreach (var j in due)
        {
            Release(j);
            var fl = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == j.AcceptedFreelancerId);
            NotifyReleased(mailer, j, fl, auto: true);
        }
        if (due.Count > 0) await db.SaveChangesAsync();
        return due.Count;
    }

    /// <summary>Tells both sides that the payment was released.</summary>
    public static void NotifyReleased(Marketplace.Services.Mailer mailer, Job job, User? freelancer, bool auto = false, string? byAdmin = null)
    {
        var why = auto ? $"The client did not answer within {Job.AutoReleaseDays} days, so the payment was released automatically."
            : byAdmin is not null ? "The site owner reviewed the job and released the payment."
            : "The client approved the work.";
        var path = "/Jobs/Details/" + job.Id;
        if (freelancer is not null)
            mailer.Send(freelancer.Email, "You got paid: " + job.Title, "Payment released to you",
                $"<p>{Mailer.E(why)}</p><p>Job: <strong>{Mailer.E(job.Title)}</strong><br>You receive <strong>{JobMoney.FreelancerShare(job):N2} EGP</strong> (agreed price minus {job.CommissionPercent}% commission).</p>",
                "View the job", path);
        if (job.Client is not null)
            mailer.Send(job.Client.Email, "Payment released: " + job.Title, "Payment released",
                $"<p>{Mailer.E(why)}</p><p>Job: <strong>{Mailer.E(job.Title)}</strong><br>Agreed price: {job.AgreedPrice:N2} EGP.</p>",
                "View the job", path);
    }
}

/// <summary>Checks every 30 minutes for delivered jobs the client has not answered for 7 days.</summary>
public class AutoReleaseService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly Mailer _mailer;
    private readonly ILogger<AutoReleaseService> _log;

    public AutoReleaseService(IServiceScopeFactory scopes, Mailer mailer, ILogger<AutoReleaseService> log)
    { _scopes = scopes; _mailer = mailer; _log = log; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ContinueWith(_ => { });
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var n = await JobMoney.AutoReleaseDueAsync(db, _mailer);
                if (n > 0) _log.LogWarning("Auto-released {Count} delivered job(s) after {Days} days without a client answer.", n, Job.AutoReleaseDays);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Auto-release check failed."); }
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken).ContinueWith(_ => { });
        }
    }
}
