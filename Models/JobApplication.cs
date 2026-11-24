using System.ComponentModel.DataAnnotations;

namespace Marketplace.Models;

public enum ApplicationStatus { Pending, Accepted, Rejected }

/// <summary>The owner's (admin's) review of an application. Clients only see Approved ones.</summary>
public enum ApprovalStatus { PendingApproval, Approved, Rejected, EmailUnverified }

/// <summary>One freelancer's application to one job.</summary>
public class JobApplication
{
    public int Id { get; set; }

    public int JobId { get; set; }
    public Job? Job { get; set; }

    public int FreelancerId { get; set; }
    public User? Freelancer { get; set; }

    [Required, MaxLength(2000)]
    public string CoverLetter { get; set; } = "";

    public decimal ProposedPrice { get; set; }
    // Step 1: the admin approves or rejects the application (every application starts as PendingApproval).
    public ApprovalStatus Approval { get; set; } = ApprovalStatus.PendingApproval;
    public DateTime? ReviewedAtUtc { get; set; }

    // Step 2: the client's decision, which only exists for approved applications.
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
