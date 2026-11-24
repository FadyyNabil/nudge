using Marketplace.Data;
using Marketplace.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Services;

/// <summary>
/// The money rules for finishing a job, in one place so every path pays out the same way.
/// </summary>
public static class JobMoney
{
    /// <summary>The owner's InstaPay / wallet number where clients send their payment.</summary>
    public static string OwnerNumber(IConfiguration c) => c["Marketplace:OwnerInstaPay"] ?? "01275021671";

    /// <summary>
    /// The client approved the work (or 7 days passed). The money is still with Sho8lana:
    /// the freelancer's share now waits for the owner to send it, so the job is NOT completed yet.
    /// </summary>
    public static void Approve(Job job)
    {
        job.Status = JobStatus.AwaitingPayout;
        job.ApprovedAtUtc = DateTime.UtcNow;
    }

    /// <summary>The owner sent the freelancer's share: now the job is completed and paid.</summary>
    public static void MarkSent(Job job)
    {
        job.Status = JobStatus.Completed;
        job.CompletedAtUtc = DateTime.UtcNow;
        job.Escrow = EscrowStatus.Released;
        job.ReleasedAtUtc = DateTime.UtcNow;
        job.PayoutSentAtUtc = DateTime.UtcNow;
    }

    public static decimal FreelancerShare(Job job) => (job.AgreedPrice ?? 0) - (job.CommissionAmount ?? 0);

    /// <summary>Delivered jobs whose client has been silent for 7 days are approved automatically.</summary>
    public static async Task<int> AutoReleaseDueAsync(AppDbContext db, Mailer mailer)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Job.AutoReleaseDays);
        var due = await db.Jobs.Include(j => j.Client)
            .Where(j => j.Status == JobStatus.InProgress && j.Escrow == EscrowStatus.Held && j.DeliveredAtUtc != null && j.DeliveredAtUtc <= cutoff)
            .ToListAsync();
        foreach (var j in due)
        {
            Approve(j);
            var fl = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == j.AcceptedFreelancerId);
            NotifyApproved(mailer, j, fl, auto: true);
        }
        if (due.Count > 0) await db.SaveChangesAsync();
        return due.Count;
    }

    /// <summary>Tells both sides the work is approved, and the owner that a payout is waiting.</summary>
    public static void NotifyApproved(Mailer mailer, Job job, User? freelancer, bool auto = false, string? byAdmin = null)
    {
        var why = auto ? $"The client did not answer within {Job.AutoReleaseDays} days, so the work was approved automatically."
            : byAdmin is not null ? "The site owner reviewed the job and approved it."
            : "The client approved the work.";
        var path = "/Jobs/Details/" + job.Id;
        if (freelancer is not null)
            mailer.Send(freelancer.Email, "Work approved: " + job.Title, "Your work was approved",
                $"<p>{Mailer.E(why)}</p><p>Job: <strong>{Mailer.E(job.Title)}</strong><br>Sho8lana will send <strong>{FreelancerShare(job):N2} EGP</strong> (agreed price minus {job.CommissionPercent}% commission) to your InstaPay number {Mailer.E(freelancer.InstaPayNumber)}. You get another email when it is sent.</p>",
                "View the job", path);
        if (job.Client is not null)
            mailer.Send(job.Client.Email, "Work approved: " + job.Title, "Work approved",
                $"<p>{Mailer.E(why)}</p><p>Job: <strong>{Mailer.E(job.Title)}</strong>. Sho8lana will now pay the freelancer.</p>", "View the job", path);
        mailer.SendToAdmins("Payout to send: " + job.Title, "A freelancer payout is waiting",
            $"<p>Job <strong>{Mailer.E(job.Title)}</strong> ({Mailer.E(job.PaymentRef)}) was approved. Send <strong>{FreelancerShare(job):N2} EGP</strong> to {Mailer.E(freelancer?.FullName)} on InstaPay {Mailer.E(freelancer?.InstaPayNumber)}, then press Mark as sent.</p>",
            "Open payouts", "/Admin/Transactions");
    }

    /// <summary>Tells both sides the payout was sent.</summary>
    public static void NotifySent(Mailer mailer, Job job, User? freelancer)
    {
        var path = "/Jobs/Details/" + job.Id;
        if (freelancer is not null)
            mailer.Send(freelancer.Email, "You got paid: " + job.Title, "Your payment was sent",
                $"<p>Sho8lana sent <strong>{FreelancerShare(job):N2} EGP</strong> to your InstaPay number {Mailer.E(freelancer.InstaPayNumber)} for <strong>{Mailer.E(job.Title)}</strong>.</p>", "View the job", path);
        if (job.Client is not null)
            mailer.Send(job.Client.Email, "Job completed: " + job.Title, "Job completed",
                $"<p>The freelancer was paid and <strong>{Mailer.E(job.Title)}</strong> is completed. Thank you for using Sho8lana.</p>", "View the job", path);
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
