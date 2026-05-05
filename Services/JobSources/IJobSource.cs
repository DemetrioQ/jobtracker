using jobtracker.Data;

namespace jobtracker.Services.JobSources;

public interface IJobSource
{
    /// <summary>Stable identifier matching <see cref="JobListing.Source"/>.</summary>
    string Name { get; }

    /// <summary>
    /// Pulls listings from this source. Implementations may ignore or partially
    /// honour the parameters (some APIs filter server-side, others don't).
    /// Returns whatever the source emits — the caller upserts/dedupes.
    /// </summary>
    IAsyncEnumerable<JobListing> FetchAsync(CancellationToken ct);
}
