namespace Marketplace.Models;

/// <summary>
/// One chat message on a job between the client and the hired freelancer.
/// Admins can read the thread for disputes; only the two parties can write.
/// </summary>
public class ChatMessage
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public int FromUserId { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; set; }
}
