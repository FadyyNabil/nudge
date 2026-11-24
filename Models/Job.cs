using System.ComponentModel.DataAnnotations;

namespace Marketplace.Models;

public enum JobStatus { Open, InProgress, Completed, Cancelled }

/// <summary>A job posted by a client. Freelancers apply to it.</summary>
public class Job
{
    public int Id { get; set; }

    public int ClientId { get; set; }
    public User? Client { get; set; }

    [Required, MaxLength(120)]
    public string Title { get; set; } = "";

    [Required, MaxLength(4000)]
    public string Description { get; set; } = "";

    // What the client is willing to pay (in the site's demo currency).
    public decimal Budget { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Open;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // ---- Filled in when the client accepts an application ----
    public int? AcceptedFreelancerId { get; set; }
    public decimal? AgreedPrice { get; set; }

    // The commission rate is copied onto the job when it is accepted, so changing
    // the rate in settings later does not change the money on old jobs.
    public decimal? CommissionPercent { get; set; }
    public decimal? CommissionAmount { get; set; }   // owner's cut
    public DateTime? CompletedAtUtc { get; set; }

    public List<JobApplication> Applications { get; set; } = new();
}
