namespace Api.Domain;

public enum JobType { OcrLabPdf = 1, ParseVcf = 2, Normalize = 3, ScoreRecalc = 4, GenerateRecommendations = 5 }
public enum JobStatus { Ready = 1, Processing = 2, Succeeded = 3, Failed = 4, InsufficientData = 5 }

public class ProcessingJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = default!;
    public Guid? DocumentId { get; set; }

    public JobType Type { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Ready;

    public int AttemptCount { get; set; } = 0;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public string? Error { get; set; }
}