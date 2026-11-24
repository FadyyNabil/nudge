using System.Security.Claims;

namespace Marketplace.Services;

public static class ClaimsExtensions
{
    /// <summary>The logged-in user's database id (stored in the auth cookie), or 0 if anonymous.</summary>
    public static int GetUserId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
}
