using System.ComponentModel.DataAnnotations;

namespace jobtracker.Data;

public enum JobStatus
{
    Saved = 0,
    Applied = 1,
    RecruiterMessaged = 2,
    Interview = 3,
    Offer = 4,
    Rejected = 5,
    Withdrawn = 6,
}

public class Job
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [Required, MaxLength(200)]
    public string Company { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Location { get; set; }

    [MaxLength(100)]
    public string? Salary { get; set; }

    [MaxLength(500)]
    public string? Url { get; set; }

    public DateOnly? DateApplied { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Saved;

    [MaxLength(200)]
    public string? RecruiterName { get; set; }

    [MaxLength(200)]
    public string? RecruiterEmail { get; set; }

    public DateOnly? FollowUpDate { get; set; }

    public DateOnly? InterviewDate { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
