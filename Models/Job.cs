using System.ComponentModel.DataAnnotations;

namespace Marketplace.Models;

public enum JobStatus { Open, InProgress, Completed, Cancelled, AwaitingPayment, AwaitingPayout }

/// <summary>
/// Where the demo payment sits. None: no payment yet (or refunded after a cancel).
/// Held: the client paid when hiring and the site owner is holding the money (demo escrow).
/// Released: the freelancer finished, and the owner paid out the agreed price minus commission.
/// </summary>
public enum EscrowStatus { None, Held, Released }

/// <summary>How the client paid the freelancer (bookkeeping label only, no real gateway).</summary>
public enum PaymentMethod { InstaPay, VodafoneCash, OrangeCash, EtisalatCash, WePay }

public static class PaymentMethodNames
{
    public static string Label(PaymentMethod m) => m switch
    {
        PaymentMethod.InstaPay => "InstaPay",
        PaymentMethod.VodafoneCash => "Vodafone Cash",
        PaymentMethod.OrangeCash => "Orange Cash",
        PaymentMethod.EtisalatCash => "Etisalat Cash",
        PaymentMethod.WePay => "WE Pay",
        _ => m.ToString()
    };
}

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

    // Chosen by the client when they accept an offer and pay (demo escrow starts here).
    public PaymentMethod? PaymentMethod { get; set; }

    // Demo escrow bookkeeping. No real money moves: these fields only record the story.
    public EscrowStatus Escrow { get; set; } = EscrowStatus.None;
    public DateTime? PaidAtUtc { get; set; }       // client paid, money held by the owner
    public DateTime? ReleasedAtUtc { get; set; }   // owner paid the freelancer, minus commission

    // ---- Client approval step ----
    // Set when the freelancer submits the work for the client to check. Null means "not submitted".
    public DateTime? DeliveredAtUtc { get; set; }
    // The client's note when they ask for changes (cleared when the work is submitted again).
    [MaxLength(1000)]
    public string? ChangeRequestNote { get; set; }

    /// <summary>True while the work waits for the client to approve it or ask for changes.</summary>
    // ---- Manual payment (InstaPay or wallet) ----
    // A short code the client writes in the transfer note, e.g. NUD-0004.
    [MaxLength(20)]
    public string? PaymentRef { get; set; }
    // When the client uploaded the payment proof (null = no proof waiting for the owner).
    public DateTime? ProofUploadedAtUtc { get; set; }
    // The owner's note when a proof was rejected.
    [MaxLength(1000)]
    public string? PaymentNote { get; set; }
    // When the client approved the work (the freelancer's payout is then waiting for the owner).
    public DateTime? ApprovedAtUtc { get; set; }
    // When the owner sent the freelancer's share.
    public DateTime? PayoutSentAtUtc { get; set; }

    public static string MakeRef(int jobId) => "NUD-" + jobId.ToString("D4");

    public bool AwaitingApproval => Status == JobStatus.InProgress && DeliveredAtUtc != null;

    /// <summary>Last time the client got a "new chat message" email for this job (30-minute batching).</summary>
    public DateTime? LastChatEmailToClientAtUtc { get; set; }

    /// <summary>Last time the hired freelancer got a "new chat message" email for this job (30-minute batching).</summary>
    public DateTime? LastChatEmailToFreelancerAtUtc { get; set; }

    /// <summary>Days the client has to answer before the money is released automatically.</summary>
    public const int AutoReleaseDays = 7;

    /// <summary>The status as a person would say it.</summary>
    public string StatusLabel => AwaitingApproval ? "Delivered, waiting for client"
        : Status == JobStatus.AwaitingPayment ? (ProofUploadedAtUtc != null ? "Awaiting Nudge's payment confirmation" : "Waiting for the client's payment")
        : Status == JobStatus.AwaitingPayout ? "Approved, payout pending"
        : Status == JobStatus.InProgress ? "In progress"
        : Status.ToString();

    public List<JobApplication> Applications { get; set; } = new();
}
