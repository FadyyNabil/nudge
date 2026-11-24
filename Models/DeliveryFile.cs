using System.ComponentModel.DataAnnotations;

namespace Marketplace.Models;

/// <summary>
/// The final result a hired freelancer sends to the client. The bytes live in the
/// database (the container disk is wiped on every deploy). The client sees the file
/// name right away, but can only download it while the demo payment is held
/// (or released): a cancelled job refunds the client and locks the files again.
/// </summary>
public class DeliveryFile
{
    public int Id { get; set; }

    public int JobId { get; set; }

    public int FreelancerId { get; set; }

    [MaxLength(260)]
    public string OriginalFileName { get; set; } = "";

    [MaxLength(100)]
    public string ContentType { get; set; } = "";

    public byte[] Data { get; set; } = Array.Empty<byte>();

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}
