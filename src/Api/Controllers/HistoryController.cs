using Api.Auth;
using Api.Domain;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Api.Controllers;

[ApiController]
[Route("api/history")]
[Authorize]
public class HistoryController : ControllerBase
{
    private readonly AppDbContext _db;

    public HistoryController(AppDbContext db)
    {
        _db = db;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public record DocumentHistoryItem(
        Guid DocumentId,
        string? FileName,
        string? ContentType,
        DocumentType DocumentType,
        DocumentStatus DocumentStatus,
        DateTime CreatedAtUtc,
        DateTime? ProcessedAtUtc,
        Guid? LatestJobId,
        JobStatus? LatestJobStatus,
        string? LatestJobError,
        int BiomarkerCount,
        int ManualBiomarkerCount,
        IReadOnlyList<string>? MissingMandatoryBiomarkers
    );

    public record BiomarkerHistoryItem(
        Guid Id,
        Guid? DocumentId,
        string BiomarkerCode,
        string? SourceName,
        decimal Value,
        string Unit,
        BiomarkerSourceType SourceType,
        DateTime ObservedAtUtc,
        DateTime CreatedAtUtc
    );

    public record JobHistoryItem(
        Guid Id,
        Guid? DocumentId,
        JobType Type,
        JobStatus Status,
        int AttemptCount,
        DateTime CreatedAtUtc,
        DateTime? StartedAtUtc,
        DateTime? CompletedAtUtc,
        string? Error
    );

    public record InsightHistoryItem(
        Guid SnapshotId,
        Guid DocumentId,
        int OverallScore,
        decimal Confidence,
        RiskBand RiskBand,
        string ModelVersion,
        DateTime CreatedAtUtc,
        int RecommendationCount,
        int ApprovedRecommendationCount,
        int ReviewRequestedRecommendationCount
    );

    [HttpGet("documents")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentHistoryItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DocumentHistoryItem>>> GetDocuments([FromQuery] int skip = 0, [FromQuery] int take = 20)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);

        var docs = await _db.RawDocuments
            .AsNoTracking()
            .Where(d => d.UserId == UserId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(d => new
            {
                d.Id,
                d.OriginalFileName,
                d.ContentType,
                d.Type,
                d.Status,
                d.CreatedAtUtc,
                d.ProcessedAtUtc,
                BiomarkerCount = _db.BiomarkerReadings.Count(b => b.UserId == UserId && b.DocumentId == d.Id),
                ManualBiomarkerCount = _db.BiomarkerReadings.Count(b => b.UserId == UserId && b.DocumentId == d.Id && b.SourceType == BiomarkerSourceType.Manual),
                LatestJob = _db.ProcessingJobs
                    .Where(j => j.UserId == UserId && j.DocumentId == d.Id)
                    .OrderByDescending(j => j.CreatedAtUtc)
                    .Select(j => new { j.Id, j.Status, j.Error })
                    .FirstOrDefault()
            })
            .ToListAsync();

        var result = new List<DocumentHistoryItem>(docs.Count);
        foreach (var doc in docs)
        {
            var evaluation = await BiomarkerCatalogPolicy.EvaluateDocumentAsync(_db, UserId, doc.Id);
            result.Add(new DocumentHistoryItem(
                DocumentId: doc.Id,
                FileName: doc.OriginalFileName,
                ContentType: doc.ContentType,
                DocumentType: doc.Type,
                DocumentStatus: doc.Status,
                CreatedAtUtc: doc.CreatedAtUtc,
                ProcessedAtUtc: doc.ProcessedAtUtc,
                LatestJobId: doc.LatestJob?.Id,
                LatestJobStatus: doc.LatestJob?.Status,
                LatestJobError: doc.LatestJob?.Error,
                BiomarkerCount: doc.BiomarkerCount,
                ManualBiomarkerCount: doc.ManualBiomarkerCount,
                MissingMandatoryBiomarkers: evaluation.IsSufficient ? null : evaluation.MissingMandatoryBiomarkers
            ));
        }

        return Ok(result);
    }

    [HttpGet("documents/{docId:guid}/biomarkers")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<BiomarkerHistoryItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<BiomarkerHistoryItem>>> GetDocumentBiomarkers(Guid docId)
    {
        var exists = await _db.RawDocuments.AsNoTracking().AnyAsync(d => d.Id == docId && d.UserId == UserId);
        if (!exists)
            return NotFound(new { message = "Document not found" });

        var rows = await _db.BiomarkerReadings
            .AsNoTracking()
            .Where(r => r.UserId == UserId && r.DocumentId == docId)
            .OrderByDescending(r => r.ObservedAtUtc)
            .ThenByDescending(r => r.CreatedAtUtc)
            .Select(r => new BiomarkerHistoryItem(
                r.Id,
                r.DocumentId,
                r.BiomarkerCode,
                r.SourceName,
                r.Value,
                r.Unit,
                r.SourceType,
                r.ObservedAtUtc,
                r.CreatedAtUtc
            ))
            .ToListAsync();

        return Ok(rows);
    }

    [HttpGet("biomarkers")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<BiomarkerHistoryItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<BiomarkerHistoryItem>>> GetBiomarkerFeed(
        [FromQuery] string? code = null,
        [FromQuery] Guid? documentId = null,
        [FromQuery] BiomarkerSourceType? sourceType = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);

        var query = _db.BiomarkerReadings
            .AsNoTracking()
            .Where(r => r.UserId == UserId);

        if (!string.IsNullOrWhiteSpace(code))
            query = query.Where(r => r.BiomarkerCode == code);

        if (documentId.HasValue)
            query = query.Where(r => r.DocumentId == documentId.Value);

        if (sourceType.HasValue)
            query = query.Where(r => r.SourceType == sourceType.Value);

        var rows = await query
            .OrderByDescending(r => r.ObservedAtUtc)
            .ThenByDescending(r => r.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(r => new BiomarkerHistoryItem(
                r.Id,
                r.DocumentId,
                r.BiomarkerCode,
                r.SourceName,
                r.Value,
                r.Unit,
                r.SourceType,
                r.ObservedAtUtc,
                r.CreatedAtUtc
            ))
            .ToListAsync();

        return Ok(rows);
    }

    [HttpGet("documents/{docId:guid}/jobs")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<JobHistoryItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<JobHistoryItem>>> GetDocumentJobs(Guid docId)
    {
        var exists = await _db.RawDocuments.AsNoTracking().AnyAsync(d => d.Id == docId && d.UserId == UserId);
        if (!exists)
            return NotFound(new { message = "Document not found" });

        var jobs = await _db.ProcessingJobs
            .AsNoTracking()
            .Where(j => j.UserId == UserId && j.DocumentId == docId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .Select(j => new JobHistoryItem(
                j.Id,
                j.DocumentId,
                j.Type,
                j.Status,
                j.AttemptCount,
                j.CreatedAtUtc,
                j.StartedAtUtc,
                j.CompletedAtUtc,
                j.Error
            ))
            .ToListAsync();

        return Ok(jobs);
    }

    [HttpGet("insights")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<InsightHistoryItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<InsightHistoryItem>>> GetInsights([FromQuery] Guid? documentId = null, [FromQuery] int skip = 0, [FromQuery] int take = 20)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);

        var query = _db.ScoreSnapshots.AsNoTracking().Where(s => s.UserId == UserId);
        if (documentId.HasValue)
            query = query.Where(s => s.DocumentId == documentId.Value);

        var snapshots = await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(s => new InsightHistoryItem(
                s.Id,
                s.DocumentId,
                s.OverallScore,
                s.Confidence,
                s.RiskBand,
                s.ModelVersion,
                s.CreatedAtUtc,
                _db.Recommendations.Count(r => r.UserId == UserId && r.ScoreSnapshotId == s.Id),
                _db.Recommendations.Count(r => r.UserId == UserId && r.ScoreSnapshotId == s.Id && r.Status == RecommendationStatus.Published && r.ApprovedAtUtc != null),
                _db.Recommendations.Count(r => r.UserId == UserId && r.ScoreSnapshotId == s.Id && r.Status == RecommendationStatus.PendingReview)
            ))
            .ToListAsync();

        return Ok(snapshots);
    }
}
