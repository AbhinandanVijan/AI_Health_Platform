namespace Api.Domain;

public class BiomarkerReading
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = default!;

    public string BiomarkerCode { get; set; } = default!;   // e.g., "GLUCOSE", "HBA1C"
    public string? SourceName { get; set; }                 // raw text label
    public decimal Value { get; set; }
    public string Unit { get; set; } = default!;

    public decimal? NormalizedValue { get; set; }
    public string? NormalizedUnit { get; set; }

    public DateTime ObservedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? DocumentId { get; set; }
}