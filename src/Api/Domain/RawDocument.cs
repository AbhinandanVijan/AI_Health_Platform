namespace Api.Domain;

public enum DocumentType { LabPdf = 1, GenomicsVcf = 2, WearableJson = 3 }
public enum DocumentStatus { Uploaded = 1, Processing = 2, Processed = 3, Failed = 4 }

public class RawDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = default!;     // Identity User Id (string)
    public DocumentType Type { get; set; }
    public DocumentStatus Status { get; set; } = DocumentStatus.Uploaded;

    public string StorageProvider { get; set; } = "s3";
    public string Bucket { get; set; } = default!;
    public string ObjectKey { get; set; } = default!;
    public string? OriginalFileName { get; set; }
    public string? ContentType { get; set; }

    public string? Sha256 { get; set; }
    public long? SizeBytes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
}