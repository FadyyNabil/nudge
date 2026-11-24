namespace Marketplace.Models;

/// <summary>The three kinds of user. Stored as text in Users.Role.</summary>
public static class Roles
{
    public const string Admin = "Admin";           // the site owner
    public const string Client = "Client";         // posts jobs
    public const string Freelancer = "Freelancer"; // applies to jobs
}
