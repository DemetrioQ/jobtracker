using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace jobtracker.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobListing> JobListings => Set<JobListing>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Job>(b =>
        {
            b.HasIndex(j => j.UserId);
            b.HasIndex(j => new { j.UserId, j.Status });
            b.HasIndex(j => new { j.UserId, j.FollowUpDate });
            b.HasOne(j => j.User)
                .WithMany()
                .HasForeignKey(j => j.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<JobListing>(b =>
        {
            b.HasIndex(l => new { l.Source, l.ExternalId }).IsUnique();
            b.HasIndex(l => l.PostedAt);
        });
    }
}
