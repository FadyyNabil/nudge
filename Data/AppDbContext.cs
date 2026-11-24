using Marketplace.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Data;

/// <summary>Our session with the database. Each DbSet is a table we can query with LINQ.</summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobApplication> Applications => Set<JobApplication>();
    public DbSet<PortfolioItem> PortfolioItems => Set<PortfolioItem>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // The DATABASE refuses duplicate emails, even if two people register at once.
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();

        // Store enums as readable text ("Open", "InProgress"...) instead of numbers.
        b.Entity<Job>().Property(j => j.Status).HasConversion<string>().HasMaxLength(20);
        b.Entity<Job>().Property(j => j.Escrow).HasConversion<string>().HasMaxLength(20);
        b.Entity<Job>().Property(j => j.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        b.Entity<JobApplication>().Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        b.Entity<JobApplication>().Property(a => a.Approval).HasConversion<string>().HasMaxLength(20);

        b.Entity<User>().Property(u => u.AccountStatus).HasConversion<string>().HasMaxLength(20);

        // Money columns: up to 16 digits before the decimal point, 2 after.
        b.Entity<Job>().Property(j => j.Budget).HasPrecision(18, 2);
        b.Entity<Job>().Property(j => j.AgreedPrice).HasPrecision(18, 2);
        b.Entity<Job>().Property(j => j.CommissionPercent).HasPrecision(5, 2);
        b.Entity<Job>().Property(j => j.CommissionAmount).HasPrecision(18, 2);
        b.Entity<JobApplication>().Property(a => a.ProposedPrice).HasPrecision(18, 2);

        // Restrict = you cannot delete a user who still has jobs/applications.
        // (It also avoids SQL Server's "multiple cascade paths" error.)
        b.Entity<Job>().HasOne(j => j.Client).WithMany().HasForeignKey(j => j.ClientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<JobApplication>().HasOne(a => a.Freelancer).WithMany().HasForeignKey(a => a.FreelancerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<JobApplication>().HasOne(a => a.Job).WithMany(j => j.Applications).HasForeignKey(a => a.JobId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<PortfolioItem>().HasOne(p => p.Freelancer).WithMany().HasForeignKey(p => p.FreelancerId).OnDelete(DeleteBehavior.Restrict);

        // One application per freelancer per job.
        b.Entity<JobApplication>().HasIndex(a => new { a.JobId, a.FreelancerId }).IsUnique();
    }
}
