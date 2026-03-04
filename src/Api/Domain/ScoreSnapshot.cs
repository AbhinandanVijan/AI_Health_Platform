namespace Api.Domain;

public enum RiskBand
{
    Low = 1,
    Moderate = 2,
    High = 3
}

public class ScoreSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = default!;
    public Guid DocumentId { get; set; }
    public Guid? ProcessingJobId { get; set; }

    public int OverallScore { get; set; }                 // 0-100
    public decimal Confidence { get; set; }               // 0.0-1.0
    public RiskBand RiskBand { get; set; } = RiskBand.Low;
    public string ModelVersion { get; set; } = "rules-v1";

    public string BreakdownJson { get; set; } = "{}";     // category scores + factors
    public string? InputsHash { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}