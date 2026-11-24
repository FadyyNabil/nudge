using System.ComponentModel.DataAnnotations;

namespace Marketplace.Models;

/// <summary>One piece of a freelancer's portfolio: a link, an uploaded file, or both.</summary>
public class PortfolioItem
{
    public int Id { get; set; }

    public int FreelancerId { get; set; }
    public User? Freelancer { get; set; }

    [Required, MaxLength(120)]
    public string Title { get; set; } = "";

    [MaxLength(1000)]
    public string? Description { get; set; }

    // Optional external link (GitHub, Behance, personal site...). Always http or https.
    [MaxLength(500)]
    public string? Url { get; set; }

    // Optional uploaded file. StoredFileName is a random name on disk (never the user's name),
    // OriginalFileName is only used for display and the download name.
    [MaxLength(100)] public string? StoredFileName { get; set; }
    [MaxLength(200)] public string? OriginalFileName { get; set; }
    [MaxLength(100)] public string? ContentType { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
