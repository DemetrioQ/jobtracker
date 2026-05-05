using jobtracker.Data;
using jobtracker.Services.JobSources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace jobtracker.Services;

public sealed class JobIngestionService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<JobIngestionOptions> options,
    ILogger<JobIngestionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small delay so the host (DB migrations, identity init) is fully ready.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await IngestOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Job ingestion cycle failed");
            }

            var interval = TimeSpan.FromMinutes(Math.Max(5, options.CurrentValue.IntervalMinutes));
            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task IngestOnceAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sources = scope.ServiceProvider.GetServices<IJobSource>().ToList();

        var totalFetched = 0;
        var totalUpserted = 0;

        foreach (var source in sources)
        {
            var fetched = 0;
            var upserted = 0;
            try
            {
                await foreach (var listing in source.FetchAsync(ct))
                {
                    fetched++;
                    if (await UpsertAsync(db, listing, ct)) upserted++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Source {Source} threw during fetch", source.Name);
            }

            totalFetched += fetched;
            totalUpserted += upserted;
            logger.LogInformation("Ingested {Source}: fetched={Fetched} upserted={Upserted}",
                source.Name, fetched, upserted);
        }

        await db.SaveChangesAsync(ct);

        var cutoff = DateTime.UtcNow.AddDays(-options.CurrentValue.RetentionDays);
        var pruned = await db.JobListings
            .Where(l => l.PostedAt < cutoff && l.FetchedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation("Ingestion complete: fetched={Fetched} upserted={Upserted} pruned={Pruned}",
            totalFetched, totalUpserted, pruned);
    }

    private static async Task<bool> UpsertAsync(ApplicationDbContext db, JobListing listing, CancellationToken ct)
    {
        var existing = await db.JobListings
            .FirstOrDefaultAsync(l => l.Source == listing.Source && l.ExternalId == listing.ExternalId, ct);

        if (existing is null)
        {
            db.JobListings.Add(listing);
            return true;
        }

        // Refresh mutable fields in case the source updated them.
        existing.Title = listing.Title;
        existing.Company = listing.Company;
        existing.Location = listing.Location;
        existing.Remote = listing.Remote;
        existing.Url = listing.Url;
        existing.PostedAt = listing.PostedAt;
        existing.FetchedAt = listing.FetchedAt;
        existing.Description = listing.Description;
        existing.Tags = listing.Tags;
        existing.Salary = listing.Salary;
        return false;
    }
}
