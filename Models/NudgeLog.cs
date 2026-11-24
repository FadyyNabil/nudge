namespace Marketplace.Models;

/// <summary>One "Nudge" sent on a job: who nudged whom, and when. Also drives the dismissible notice on the receiver's dashboard.</summary>
public class NudgeLog
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public int FromUserId { get; set; }
    public int ToUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DismissedAtUtc { get; set; }
}
