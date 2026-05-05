namespace jobtracker.Services;

public sealed class JobIngestionOptions
{
    public const string SectionName = "JobIngestion";

    /// <summary>How often the background ingester runs.</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Listings older than this are pruned.</summary>
    public int RetentionDays { get; set; } = 7;

    public IndeedOptions Indeed { get; set; } = new();
    public RemotiveOptions Remotive { get; set; } = new();
    public AdzunaOptions Adzuna { get; set; } = new();

    public sealed class IndeedOptions
    {
        public string[] Queries { get; set; } = Array.Empty<string>();
        public string[] Locations { get; set; } = Array.Empty<string>();
    }

    public sealed class RemotiveOptions
    {
        public string Category { get; set; } = "software-dev";
        public string[] SearchTerms { get; set; } = Array.Empty<string>();
    }

    public sealed class AdzunaOptions
    {
        public string AppId { get; set; } = "";
        public string AppKey { get; set; } = "";
        public string[] Countries { get; set; } = Array.Empty<string>();
        public string[] Queries { get; set; } = Array.Empty<string>();
        public int MaxDaysOld { get; set; } = 1;
    }
}
