namespace Api.Domain;

public class ScoreSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = default!;

    public int OverallScore { get; set; }                 // 0-100
    public decimal Confidence { get; set; }               // 0.0-1.0
    public string RulesetVersion { get; set; } = "v1";

    public string BreakdownJson { get; set; } = "{}";     // category scores + weights
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}