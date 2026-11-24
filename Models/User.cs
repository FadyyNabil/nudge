using System.ComponentModel.DataAnnotations;

namespace Marketplace.Models;

/// <summary>One row in the Users table. We never store the real password, only PasswordHash.</summary>
public class User
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    // Stored lowercase so "A@x.com" and "a@x.com" are the same account.
    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    // Admin, Client or Freelancer (see Roles). Public sign-up can only pick Client or Freelancer.
    [Required, MaxLength(20)]
    public string Role { get; set; } = Roles.Freelancer;

    // The admin can suspend an account. Suspended users cannot log in.
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
