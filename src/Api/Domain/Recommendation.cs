namespace Api.Domain;

public enum RecommendationStatus { Draft = 1, PendingReview = 2, Published = 3, Rejected = 4 }
public enum RecommendationType { Insight = 1, RiskPrediction = 2, Action = 3 }

public class Recommendation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = default!;
    public Guid DocumentId { get; set; }
    public Guid? ScoreSnapshotId { get; set; }
    public RecommendationStatus Status { get; set; } = RecommendationStatus.PendingReview;
    public RecommendationType Type { get; set; } = RecommendationType.Insight;
    public int Priority { get; set; } = 3;

    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;   // keep simple now
    public string? EvidenceJson { get; set; }

    public string? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}