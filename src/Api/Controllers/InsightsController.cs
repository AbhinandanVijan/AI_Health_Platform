using Api.Auth;
using Api.Domain;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Api.Controllers;

[ApiController]
[Route("api/insights")]
public class InsightsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IInsightsGenerationService _insightsGenerationService;

    public InsightsController(AppDbContext db, IInsightsGenerationService insightsGenerationService)
    {
        _db = db;
        _insightsGenerationService = insightsGenerationService;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public record InsightRecommendationDto(
        Guid Id,
        RecommendationType Type,
        RecommendationStatus Status,
        int Priority,
        string Title,
        string Content,
        string? EvidenceJson,
        DateTime CreatedAtUtc,
        bool IsClinicianApproved,
        DateTime? ApprovedAtUtc
    );

    public record InsightSnapshotResponse(
        Guid DocumentId,
        Guid SnapshotId,
        int OverallScore,
        decimal Confidence,
        RiskBand RiskBand,
        string ModelVersion,
        string BreakdownJson,
        string Scope,
        string RecencyStrategy,
        IReadOnlyList<Guid> ContributingDocumentIds,
        DateTime CreatedAtUtc,
        IReadOnlyList<InsightRecommendationDto> Recommendations
    );

    public record ClinicianRecommendationQueueItemDto(
        Guid Id,
        string UserId,
        string? UserEmail,
        Guid DocumentId,
        Guid? ScoreSnapshotId,
        RecommendationType Type,
        RecommendationStatus Status,
        int Priority,
        string Title,
        string Content,
        DateTime CreatedAtUtc
    );

    private const string AggregateModelVersion = "rules-v1-aggregate";
    private const string DocumentModelVersion = "rules-v1";

    /// <summary>
    /// Gets pending clinician review requests for recommendations.
    /// </summary>
    [Authorize(Roles = "Clinician")]
    [HttpGet("recommendations/pending")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(IReadOnlyList<ClinicianRecommendationQueueItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ClinicianRecommendationQueueItemDto>>> GetPendingRecommendationReviews([FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);

        var rows = await _db.Recommendations
            .AsNoTracking()
            .Where(r => r.Status == RecommendationStatus.PendingReview)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(r => new ClinicianRecommendationQueueItemDto(
                r.Id,
                r.UserId,
                _db.Users.Where(u => u.Id == r.UserId).Select(u => u.Email).FirstOrDefault(),
                r.DocumentId,
                r.ScoreSnapshotId,
                r.Type,
                r.Status,
                r.Priority,
                r.Title,
                r.Content,
                r.CreatedAtUtc
            ))
            .ToListAsync();

        return Ok(rows);
    }

    /// <summary>
    /// Gets the latest generated insight snapshot and recommendations for a document.
    /// </summary>
    /// <param name="docId">Document identifier.</param>
    /// <returns>Latest insight snapshot with recommendations.</returns>
    [Authorize]
    [HttpGet("{docId:guid}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(InsightSnapshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<InsightSnapshotResponse>> GetLatest(Guid docId)
    {
        var doc = await _db.RawDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == docId && d.UserId == UserId);

        if (doc is null)
            return NotFound(new { message = "Document not found" });

        var snapshot = await _db.ScoreSnapshots
            .AsNoTracking()
            .Where(s => s.UserId == UserId && s.DocumentId == docId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (snapshot is null)
            return NotFound(new { message = "No insights generated for this document" });

        var recommendations = await _db.Recommendations
            .AsNoTracking()
            .Where(r => r.UserId == UserId && r.DocumentId == docId && r.ScoreSnapshotId == snapshot.Id)
            .OrderBy(r => r.Priority)
            .ThenByDescending(r => r.CreatedAtUtc)
            .Select(r => new InsightRecommendationDto(
                r.Id,
                r.Type,
                r.Status,
                r.Priority,
                r.Title,
                r.Content,
                r.EvidenceJson,
                r.CreatedAtUtc,
                r.ApprovedAtUtc.HasValue,
                r.ApprovedAtUtc
            ))
            .ToListAsync();

        return Ok(new InsightSnapshotResponse(
            DocumentId: docId,
            SnapshotId: snapshot.Id,
            OverallScore: snapshot.OverallScore,
            Confidence: snapshot.Confidence,
            RiskBand: snapshot.RiskBand,
            ModelVersion: snapshot.ModelVersion,
            BreakdownJson: snapshot.BreakdownJson,
            Scope: "document",
            RecencyStrategy: "document-only",
            ContributingDocumentIds: new[] { docId },
            CreatedAtUtc: snapshot.CreatedAtUtc,
            Recommendations: recommendations
        ));
    }

    /// <summary>
    /// Gets the latest generated aggregate insight snapshot for the user using latest-per-biomarker values across all documents.
    /// </summary>
    /// <returns>Latest aggregate insight snapshot with recommendations.</returns>
    [Authorize]
    [HttpGet("latest")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(InsightSnapshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<InsightSnapshotResponse>> GetLatestAggregate()
    {
        var snapshot = await _db.ScoreSnapshots
            .AsNoTracking()
            .Where(s => s.UserId == UserId && s.ModelVersion == AggregateModelVersion)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (snapshot is null)
            return NotFound(new { message = "No aggregate insights generated for this user" });

        var recommendations = await _db.Recommendations
            .AsNoTracking()
            .Where(r => r.UserId == UserId && r.ScoreSnapshotId == snapshot.Id)
            .OrderBy(r => r.Priority)
            .ThenByDescending(r => r.CreatedAtUtc)
            .Select(r => new InsightRecommendationDto(
                r.Id,
                r.Type,
                r.Status,
                r.Priority,
                r.Title,
                r.Content,
                r.EvidenceJson,
                r.CreatedAtUtc,
                r.ApprovedAtUtc.HasValue,
                r.ApprovedAtUtc
            ))
            .ToListAsync();

        var contributingDocIds = ExtractContributingDocumentIds(snapshot.BreakdownJson);

        return Ok(new InsightSnapshotResponse(
            DocumentId: snapshot.DocumentId,
            SnapshotId: snapshot.Id,
            OverallScore: snapshot.OverallScore,
            Confidence: snapshot.Confidence,
            RiskBand: snapshot.RiskBand,
            ModelVersion: snapshot.ModelVersion,
            BreakdownJson: snapshot.BreakdownJson,
            Scope: "aggregate",
            RecencyStrategy: "latest-observedAt-else-createdAt",
            ContributingDocumentIds: contributingDocIds,
            CreatedAtUtc: snapshot.CreatedAtUtc,
            Recommendations: recommendations
        ));
    }

    /// <summary>
    /// Generates a new insight snapshot and recommendations using deterministic scoring plus LLM + RAG narrative generation.
    /// </summary>
    /// <param name="docId">Document identifier.</param>
    /// <returns>Generated insight snapshot and recommendations.</returns>
    [Authorize]
    [HttpPost("generate/{docId:guid}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(InsightSnapshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<InsightSnapshotResponse>> Generate(Guid docId)
    {
        var doc = await _db.RawDocuments
            .FirstOrDefaultAsync(d => d.Id == docId && d.UserId == UserId);

        if (doc is null)
            return NotFound(new { message = "Document not found" });

        var biomarkerReadings = await _db.BiomarkerReadings
            .AsNoTracking()
            .Where(r => r.UserId == UserId && r.DocumentId == docId)
            .OrderByDescending(r => r.ObservedAtUtc)
            .ToListAsync();

        if (biomarkerReadings.Count == 0)
            return BadRequest(new { message = "No biomarkers available for this document. Generate insights after OCR/manual entries are saved." });

        var biomarkerCodes = biomarkerReadings
            .Select(r => r.BiomarkerCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var evaluation = await BiomarkerCatalogPolicy.EvaluateDocumentAsync(_db, UserId, docId);

        var coverageFactor = Math.Min(1.0m, biomarkerCodes.Count / 20.0m);
        var missingPenalty = evaluation.MissingMandatoryBiomarkers.Count * 8;
        var rawScore = (int)Math.Round(55 + (coverageFactor * 35) - missingPenalty);
        var score = Math.Max(0, Math.Min(100, rawScore));

        var confidence = Math.Max(0.40m, Math.Min(0.95m, 0.50m + (coverageFactor * 0.35m)));

        var riskBand = score >= 80
            ? RiskBand.Low
            : score >= 60
                ? RiskBand.Moderate
                : RiskBand.High;

        var breakdownJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            totalBiomarkers = biomarkerCodes.Count,
            mandatoryMissing = evaluation.MissingMandatoryBiomarkers,
            mandatoryPresent = evaluation.PresentMandatoryBiomarkers,
            minimumRequired = evaluation.MinimumRequiredCanonicalBiomarkerCount,
            extractedCanonicalBiomarkerCount = evaluation.ExtractedCanonicalBiomarkerCount
        });

        var latestJob = await _db.ProcessingJobs
            .Where(j => j.UserId == UserId && j.DocumentId == docId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync();

        var snapshot = new ScoreSnapshot
        {
            UserId = UserId,
            DocumentId = docId,
            ProcessingJobId = latestJob?.Id,
            OverallScore = score,
            Confidence = confidence,
            RiskBand = riskBand,
            ModelVersion = DocumentModelVersion,
            BreakdownJson = breakdownJson,
            InputsHash = $"{docId}:{biomarkerCodes.Count}:{evaluation.MissingMandatoryBiomarkers.Count}"
        };

        _db.ScoreSnapshots.Add(snapshot);

        IReadOnlyList<GeneratedInsightItem> generatedItems;
        try
        {
            generatedItems = await _insightsGenerationService.GenerateAsync(
                new InsightGenerationContext(
                    DocumentId: docId,
                    UserId: UserId,
                    OverallScore: score,
                    Confidence: confidence,
                    RiskBand: riskBand,
                    MandatoryEvaluation: evaluation,
                    Biomarkers: biomarkerReadings
                ),
                HttpContext.RequestAborted
            );
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Insights generation service is currently unavailable." });
        }

        var recommendations = generatedItems
            .Select(item => new Recommendation
            {
                UserId = UserId,
                DocumentId = docId,
                ScoreSnapshotId = snapshot.Id,
                Status = RecommendationStatus.Draft,
                Type = item.Type,
                Priority = item.Priority,
                Title = item.Title,
                Content = item.Content,
                EvidenceJson = item.EvidenceJson
            })
            .ToList();

        _db.Recommendations.AddRange(recommendations);

        await _db.SaveChangesAsync();

        var response = new InsightSnapshotResponse(
            DocumentId: docId,
            SnapshotId: snapshot.Id,
            OverallScore: snapshot.OverallScore,
            Confidence: snapshot.Confidence,
            RiskBand: snapshot.RiskBand,
            ModelVersion: snapshot.ModelVersion,
            BreakdownJson: snapshot.BreakdownJson,
            Scope: "document",
            RecencyStrategy: "document-only",
            ContributingDocumentIds: new[] { docId },
            CreatedAtUtc: snapshot.CreatedAtUtc,
            Recommendations: recommendations
                .OrderBy(r => r.Priority)
                .Select(r => new InsightRecommendationDto(
                    r.Id,
                    r.Type,
                    r.Status,
                    r.Priority,
                    r.Title,
                    r.Content,
                    r.EvidenceJson,
                    r.CreatedAtUtc,
                    r.ApprovedAtUtc.HasValue,
                    r.ApprovedAtUtc
                ))
                .ToList()
        );

        return Ok(response);
    }

    /// <summary>
    /// Generates aggregate insights using latest biomarker values per biomarker code across all documents.
    /// Uses ObservedAtUtc recency first and falls back to CreatedAtUtc.
    /// </summary>
    [Authorize]
    [HttpPost("generate")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(InsightSnapshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<InsightSnapshotResponse>> GenerateAggregate()
    {
        var allReadings = await _db.BiomarkerReadings
            .AsNoTracking()
            .Where(r => r.UserId == UserId)
            .ToListAsync();

        if (allReadings.Count == 0)
            return BadRequest(new { message = "No biomarkers available for this user. Generate insights after OCR/manual entries are saved." });

        var selectedReadings = allReadings
            .GroupBy(r => r.BiomarkerCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(GetClinicalRecency)
                .ThenByDescending(r => r.CreatedAtUtc)
                .First())
            .ToList();

        var biomarkerCodes = selectedReadings
            .Select(r => r.BiomarkerCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var evaluation = BiomarkerCatalogPolicy.EvaluateCodes(biomarkerCodes);

        var coverageFactor = Math.Min(1.0m, biomarkerCodes.Count / 20.0m);
        var missingPenalty = evaluation.MissingMandatoryBiomarkers.Count * 8;
        var rawScore = (int)Math.Round(55 + (coverageFactor * 35) - missingPenalty);
        var score = Math.Max(0, Math.Min(100, rawScore));

        var confidence = Math.Max(0.40m, Math.Min(0.95m, 0.50m + (coverageFactor * 0.35m)));

        var riskBand = score >= 80
            ? RiskBand.Low
            : score >= 60
                ? RiskBand.Moderate
                : RiskBand.High;

        var contributingDocumentIds = selectedReadings
            .Where(r => r.DocumentId.HasValue)
            .Select(r => r.DocumentId!.Value)
            .Distinct()
            .ToList();

        var anchorDocumentId = selectedReadings
            .Where(r => r.DocumentId.HasValue)
            .OrderByDescending(GetClinicalRecency)
            .ThenByDescending(r => r.CreatedAtUtc)
            .Select(r => r.DocumentId!.Value)
            .FirstOrDefault();

        if (anchorDocumentId == Guid.Empty)
        {
            anchorDocumentId = await _db.RawDocuments
                .AsNoTracking()
                .Where(d => d.UserId == UserId)
                .OrderByDescending(d => d.CreatedAtUtc)
                .Select(d => d.Id)
                .FirstOrDefaultAsync();
        }

        if (anchorDocumentId == Guid.Empty)
            return BadRequest(new { message = "No source documents available for this user." });

        var breakdownJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            scope = "aggregate",
            recencyStrategy = "latest-observedAt-else-createdAt",
            totalBiomarkers = biomarkerCodes.Count,
            mandatoryMissing = evaluation.MissingMandatoryBiomarkers,
            mandatoryPresent = evaluation.PresentMandatoryBiomarkers,
            minimumRequired = evaluation.MinimumRequiredCanonicalBiomarkerCount,
            extractedCanonicalBiomarkerCount = evaluation.ExtractedCanonicalBiomarkerCount,
            contributingDocumentIds
        });

        var latestJob = await _db.ProcessingJobs
            .Where(j => j.UserId == UserId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync();

        var snapshot = new ScoreSnapshot
        {
            UserId = UserId,
            DocumentId = anchorDocumentId,
            ProcessingJobId = latestJob?.Id,
            OverallScore = score,
            Confidence = confidence,
            RiskBand = riskBand,
            ModelVersion = AggregateModelVersion,
            BreakdownJson = breakdownJson,
            InputsHash = $"aggregate:{biomarkerCodes.Count}:{evaluation.MissingMandatoryBiomarkers.Count}"
        };

        _db.ScoreSnapshots.Add(snapshot);

        IReadOnlyList<GeneratedInsightItem> generatedItems;
        try
        {
            generatedItems = await _insightsGenerationService.GenerateAsync(
                new InsightGenerationContext(
                    DocumentId: anchorDocumentId,
                    UserId: UserId,
                    OverallScore: score,
                    Confidence: confidence,
                    RiskBand: riskBand,
                    MandatoryEvaluation: evaluation,
                    Biomarkers: selectedReadings
                ),
                HttpContext.RequestAborted
            );
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Insights generation service is currently unavailable." });
        }

        var recommendations = generatedItems
            .Select(item => new Recommendation
            {
                UserId = UserId,
                DocumentId = anchorDocumentId,
                ScoreSnapshotId = snapshot.Id,
                Status = RecommendationStatus.Draft,
                Type = item.Type,
                Priority = item.Priority,
                Title = item.Title,
                Content = item.Content,
                EvidenceJson = item.EvidenceJson
            })
            .ToList();

        _db.Recommendations.AddRange(recommendations);

        await _db.SaveChangesAsync();

        return Ok(new InsightSnapshotResponse(
            DocumentId: anchorDocumentId,
            SnapshotId: snapshot.Id,
            OverallScore: snapshot.OverallScore,
            Confidence: snapshot.Confidence,
            RiskBand: snapshot.RiskBand,
            ModelVersion: snapshot.ModelVersion,
            BreakdownJson: snapshot.BreakdownJson,
            Scope: "aggregate",
            RecencyStrategy: "latest-observedAt-else-createdAt",
            ContributingDocumentIds: contributingDocumentIds,
            CreatedAtUtc: snapshot.CreatedAtUtc,
            Recommendations: recommendations
                .OrderBy(r => r.Priority)
                .Select(r => new InsightRecommendationDto(
                    r.Id,
                    r.Type,
                    r.Status,
                    r.Priority,
                    r.Title,
                    r.Content,
                    r.EvidenceJson,
                    r.CreatedAtUtc,
                    r.ApprovedAtUtc.HasValue,
                    r.ApprovedAtUtc
                ))
                .ToList()
        ));
    }

    /// <summary>
    /// Requests clinician review for a recommendation.
    /// </summary>
    [Authorize]
    [HttpPost("recommendations/{recommendationId:guid}/request-review")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(InsightRecommendationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<InsightRecommendationDto>> RequestRecommendationReview(Guid recommendationId)
    {
        var recommendation = await _db.Recommendations
            .FirstOrDefaultAsync(r => r.Id == recommendationId && r.UserId == UserId);

        if (recommendation is null)
            return NotFound(new { message = "Recommendation not found" });

        if (recommendation.ApprovedAtUtc.HasValue && recommendation.Status == RecommendationStatus.Published)
        {
            return Ok(new InsightRecommendationDto(
                recommendation.Id,
                recommendation.Type,
                recommendation.Status,
                recommendation.Priority,
                recommendation.Title,
                recommendation.Content,
                recommendation.EvidenceJson,
                recommendation.CreatedAtUtc,
                true,
                recommendation.ApprovedAtUtc
            ));
        }

        recommendation.Status = RecommendationStatus.PendingReview;
        recommendation.ApprovedAtUtc = null;
        recommendation.ApprovedByUserId = null;

        await _db.SaveChangesAsync();

        return Ok(new InsightRecommendationDto(
            recommendation.Id,
            recommendation.Type,
            recommendation.Status,
            recommendation.Priority,
            recommendation.Title,
            recommendation.Content,
            recommendation.EvidenceJson,
            recommendation.CreatedAtUtc,
            false,
            null
        ));
    }

    /// <summary>
    /// Approves a recommendation as a clinician.
    /// </summary>
    [Authorize(Roles = "Clinician")]
    [HttpPost("recommendations/{recommendationId:guid}/approve")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(InsightRecommendationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InsightRecommendationDto>> ApproveRecommendation(Guid recommendationId)
    {
        var recommendation = await _db.Recommendations
            .FirstOrDefaultAsync(r => r.Id == recommendationId);

        if (recommendation is null)
            return NotFound(new { message = "Recommendation not found" });

        recommendation.Status = RecommendationStatus.Published;
        recommendation.ApprovedAtUtc = DateTime.UtcNow;
        recommendation.ApprovedByUserId = UserId;

        await _db.SaveChangesAsync();

        return Ok(new InsightRecommendationDto(
            recommendation.Id,
            recommendation.Type,
            recommendation.Status,
            recommendation.Priority,
            recommendation.Title,
            recommendation.Content,
            recommendation.EvidenceJson,
            recommendation.CreatedAtUtc,
            true,
            recommendation.ApprovedAtUtc
        ));
    }

    private static DateTime GetClinicalRecency(BiomarkerReading reading)
        => reading.ObservedAtUtc == default ? reading.CreatedAtUtc : reading.ObservedAtUtc;

    private static IReadOnlyList<Guid> ExtractContributingDocumentIds(string breakdownJson)
    {
        if (string.IsNullOrWhiteSpace(breakdownJson))
            return Array.Empty<Guid>();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(breakdownJson);
            if (!doc.RootElement.TryGetProperty("contributingDocumentIds", out var idsElement)
                || idsElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return Array.Empty<Guid>();
            }

            var ids = new List<Guid>();
            foreach (var item in idsElement.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.String
                    && Guid.TryParse(item.GetString(), out var parsed))
                {
                    ids.Add(parsed);
                }
            }

            return ids.Distinct().ToList();
        }
        catch
        {
            return Array.Empty<Guid>();
        }
    }
}
