using System.ComponentModel.DataAnnotations;

namespace Marketplace.Models;

/// <summary>
/// The client's proof of payment (a screenshot of the InstaPay or wallet transfer) for one job.
/// Stored in the database (the server disk is wiped on every deploy). Only the client, the freelancer-free admin review, and the owner can open it.
/// </summary>
public class PaymentProof
{
    public int Id { get; set; }
    public int JobId { get; set; }

    [MaxLength(260)] public string OriginalFileName { get; set; } = "";
    [MaxLength(100)] public string ContentType { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}
