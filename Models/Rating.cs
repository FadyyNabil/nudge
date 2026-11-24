using System.ComponentModel.DataAnnotations;

namespace Marketplace.Models;

/// <summary>
/// One rating left after a job is completed: the client rates the freelancer and the
/// freelancer rates the client, once each (1-5 stars + optional review).
/// Admins can hide a review; hidden ones disappear from pages and averages.
/// </summary>
public class Rating
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public int FromUserId { get; set; }
    public int ToUserId { get; set; }
    [Range(1, 5)] public int Stars { get; set; }
    public string? Review { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Set when an admin hides the review.</summary>
    public DateTime? HiddenAtUtc { get; set; }
}
