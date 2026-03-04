using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Api.Auth;
using Api.Domain;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace Api.Controllers;

[ApiController]
[Route("api/uploads")]
public class UploadsController : ControllerBase
{
    private readonly IAmazonS3 _s3;
    private readonly IAmazonSQS _sqs;
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;

    public UploadsController(IAmazonS3 s3, IAmazonSQS sqs, AppDbContext db, IConfiguration cfg)
    {
        _s3 = s3;
        _sqs = sqs;
        _db = db;
        _cfg = cfg;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public record PresignRequest(string FileName, string ContentType, DocumentType DocType);
    public record PresignResponse(string UploadUrl, string Bucket, string ObjectKey, DateTime ExpiresAtUtc);
    public record ReprocessRequest(Guid DocumentId);
    public record UploadStatusResponse(
        Guid DocumentId,
        DocumentStatus DocumentStatus,
        Guid? LatestJobId,
        JobStatus? LatestJobStatus,
        string? LatestJobError,
        IReadOnlyList<string>? MissingMandatoryBiomarkers
    );

    [Authorize]
    [HttpGet("status/{docId:guid}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(UploadStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UploadStatusResponse>> GetStatus(Guid docId)
    {
        var doc = await _db.RawDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == docId && d.UserId == UserId);

        if (doc is null)
            return NotFound(new { message = "Document not found" });

        var latestJob = await _db.ProcessingJobs
            .AsNoTracking()
            .Where(j => j.DocumentId == docId && j.UserId == UserId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync();

        var evaluation = await BiomarkerCatalogPolicy.EvaluateDocumentAsync(_db, UserId, docId);
        IReadOnlyList<string>? missing = evaluation.IsSufficient ? null : evaluation.MissingMandatoryBiomarkers;

        return Ok(new UploadStatusResponse(
            DocumentId: doc.Id,
            DocumentStatus: doc.Status,
            LatestJobId: latestJob?.Id,
            LatestJobStatus: latestJob?.Status,
            LatestJobError: latestJob?.Error,
            MissingMandatoryBiomarkers: missing
        ));
    }

    [Authorize]
    [HttpPost("presign")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(PresignResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<PresignResponse> Presign([FromBody] PresignRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FileName)) return BadRequest("FileName required");
        if (string.IsNullOrWhiteSpace(req.ContentType)) return BadRequest("ContentType required");

        var bucket = _cfg["S3_BUCKET"] ?? throw new InvalidOperationException("Missing S3_BUCKET");

        // Basic file-name sanitization
        var safeName = req.FileName.Replace("..", "")
                                   .Replace("/", "_")
                                   .Replace("\\", "_");

        var prefix = req.DocType switch
        {
            DocumentType.LabPdf => "labs",
            DocumentType.GenomicsVcf => "genomics",
            _ => "uploads"
        };

        var objectKey = $"{prefix}/{UserId}/{Guid.NewGuid()}_{safeName}";
        var expiresAt = DateTime.UtcNow.AddMinutes(10);

        // NOTE: Including ContentType here requires the client upload to use the same Content-Type header.
        var url = _s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            Expires = expiresAt,
            ContentType = req.ContentType
        });

        return Ok(new PresignResponse(url, bucket, objectKey, expiresAt));
    }

    public record FinalizeRequest(
        string ObjectKey,
        string FileName,
        string ContentType,
        DocumentType DocType,
        long? SizeBytes = null,
        string? Sha256 = null
    );

    [Authorize]
    [HttpPost("finalize")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Finalize([FromBody] FinalizeRequest req)
    {
        var bucket = _cfg["S3_BUCKET"] ?? throw new InvalidOperationException("Missing S3_BUCKET");

        // Idempotency: don't double-insert for same user+bucket+key
        var existing = await _db.RawDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.UserId == UserId && d.Bucket == bucket && d.ObjectKey == req.ObjectKey);

        if (existing != null)
            return Ok(new { id = existing.Id, alreadyExists = true });

        var doc = new RawDocument
        {
            UserId = UserId,
            Bucket = bucket,
            ObjectKey = req.ObjectKey,
            OriginalFileName = req.FileName,
            ContentType = req.ContentType,
            Type = req.DocType,
            Status = DocumentStatus.Uploaded,
            SizeBytes = req.SizeBytes,
            Sha256 = req.Sha256
        };

        _db.RawDocuments.Add(doc);

        // Optional DB job row (audit + idempotency)
        var job = new ProcessingJob
        {
            UserId = UserId,
            DocumentId = doc.Id,
            Type = req.DocType == DocumentType.LabPdf ? JobType.OcrLabPdf : JobType.ParseVcf,
            Status = JobStatus.Ready
        };

        _db.ProcessingJobs.Add(job);

        await _db.SaveChangesAsync();

        // Optional: send SQS message (if configured)
        var queueUrl = _cfg["SQS_QUEUE_URL"];
        if (!string.IsNullOrWhiteSpace(queueUrl))
        {
            var message = new
            {
                jobId = job.Id,
                docId = doc.Id,
                userId = UserId,
                type = job.Type.ToString()
            };

            await _sqs.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = JsonSerializer.Serialize(message)
            });
        }

        return Ok(new { id = doc.Id, jobId = job.Id });
    }

    [Authorize]
    [HttpPost("reprocess")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReprocessFromBody([FromBody] ReprocessRequest req)
    {
        return await Reprocess(req.DocumentId);
    }

    [Authorize]
    [HttpPost("reprocess/{docId:guid}")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Reprocess(Guid docId)
    {
        var doc = await _db.RawDocuments
            .FirstOrDefaultAsync(d => d.Id == docId && d.UserId == UserId);

        if (doc is null)
            return NotFound(new { message = "Document not found" });

        var jobType = doc.Type switch
        {
            DocumentType.LabPdf => JobType.OcrLabPdf,
            DocumentType.GenomicsVcf => JobType.ParseVcf,
            _ => JobType.Normalize
        };

        var job = new ProcessingJob
        {
            UserId = UserId,
            DocumentId = doc.Id,
            Type = jobType,
            Status = JobStatus.Ready
        };

        _db.ProcessingJobs.Add(job);

        doc.Status = DocumentStatus.Uploaded;
        doc.ProcessedAtUtc = null;

        await _db.SaveChangesAsync();

        var queueUrl = _cfg["SQS_QUEUE_URL"];
        if (!string.IsNullOrWhiteSpace(queueUrl))
        {
            var message = new
            {
                jobId = job.Id,
                docId = doc.Id,
                userId = UserId,
                type = job.Type.ToString()
            };

            await _sqs.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = JsonSerializer.Serialize(message)
            });
        }

        return Ok(new { id = doc.Id, jobId = job.Id, requeued = true });
    }
}