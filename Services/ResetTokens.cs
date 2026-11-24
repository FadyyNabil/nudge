using System.Security.Cryptography;
using System.Text;
using Marketplace.Data;
using Marketplace.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Services;

/// <summary>
/// Password-reset links. A token is encrypted and signed by ASP.NET data protection (keys live in the database),
/// valid for 1 hour, and tied to a fingerprint of the user's CURRENT password hash,
/// so it stops working the moment the password changes (one use only).
/// </summary>
public class ResetTokens
{
    private readonly ITimeLimitedDataProtector _protector;
    public ResetTokens(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("Sho8lana.PasswordReset").ToTimeLimitedDataProtector();

    private static string Fingerprint(User u)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(u.PasswordHash)))[..24];

    public string Make(User u) => _protector.Protect($"{u.Id}|{Fingerprint(u)}", TimeSpan.FromHours(1));

    public async Task<User?> ReadAsync(AppDbContext db, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var parts = _protector.Unprotect(token.Trim()).Split('|');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var id)) return null;
            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id);
            if (u is null || !u.IsActive || u.AccountStatus != ApprovalStatus.Approved) return null;
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(parts[1]), Encoding.UTF8.GetBytes(Fingerprint(u))) ? u : null;
        }
        catch (CryptographicException) { return null; }
    }
}
