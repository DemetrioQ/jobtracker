using System.ComponentModel.DataAnnotations;

namespace jobtracker.Data;

/// <summary>
/// A job posting fetched from an external source. Not user-scoped — these are
/// shared across users. The user creates a personal <see cref="Job"/> when
/// they "Save" a listing into their own tracker.
/// </summary>
public class JobListing
{
    public int Id { get; set; }

    [Required, MaxLength(40)]
    public string Source { get; set; } = string.Empty;        // "indeed" | "remotive" | "remoteok"

    [Required, MaxLength(200)]
    public string ExternalId { get; set; } = string.Empty;    // dedup key within a source

    [Required, MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Company { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Location { get; set; }

    public bool Remote { get; set; }

    [Required, MaxLength(800)]
    public string Url { get; set; } = string.Empty;

    public DateTime PostedAt { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(300)]
    public string? Tags { get; set; }   // comma-separated

    [MaxLength(100)]
    public string? Salary { get; set; }
}
