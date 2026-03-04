namespace Api.Domain;

public enum RecommendationStatus { Draft = 1, PendingReview = 2, Published = 3, Rejected = 4 }

public class Recommendation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = default!;
    public RecommendationStatus Status { get; set; } = RecommendationStatus.PendingReview;

    public string Title { get; set; } = default!;
    public string Content { get; set; } = default!;   // keep simple now

    public string? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}