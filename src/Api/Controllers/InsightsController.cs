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

    public InsightsController(AppDbContext db)
    {
        _db = db;
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
        DateTime CreatedAtUtc
    );

    public record InsightSnapshotResponse(
        Guid DocumentId,
        Guid SnapshotId,
        int OverallScore,
        decimal Confidence,
        RiskBand RiskBand,
        string ModelVersion,
        string BreakdownJson,
        DateTime CreatedAtUtc,
        IReadOnlyList<InsightRecommendationDto> Recommendations
    );

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
                r.CreatedAtUtc
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
            CreatedAtUtc: snapshot.CreatedAtUtc,
            Recommendations: recommendations
        ));
    }

    [Authorize]
    [HttpPost("generate/{docId:guid}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(InsightSnapshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<InsightSnapshotResponse>> Generate(Guid docId)
    {
        var doc = await _db.RawDocuments
            .FirstOrDefaultAsync(d => d.Id == docId && d.UserId == UserId);

        if (doc is null)
            return NotFound(new { message = "Document not found" });

        var biomarkerCodes = await _db.BiomarkerReadings
            .AsNoTracking()
            .Where(r => r.UserId == UserId && r.DocumentId == docId)
            .Select(r => r.BiomarkerCode)
            .Distinct()
            .ToListAsync();

        if (biomarkerCodes.Count == 0)
            return BadRequest(new { message = "No biomarkers available for this document. Generate insights after OCR/manual entries are saved." });

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
            ModelVersion = "rules-v1",
            BreakdownJson = breakdownJson,
            InputsHash = $"{docId}:{biomarkerCodes.Count}:{evaluation.MissingMandatoryBiomarkers.Count}"
        };

        _db.ScoreSnapshots.Add(snapshot);

        var recommendations = BuildRecommendations(docId, snapshot.Id, evaluation, riskBand, biomarkerCodes);
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
                    r.CreatedAtUtc
                ))
                .ToList()
        );

        return Ok(response);
    }

    private List<Recommendation> BuildRecommendations(
        Guid docId,
        Guid snapshotId,
        MandatoryEvaluationResult evaluation,
        RiskBand riskBand,
        IReadOnlyList<string> biomarkerCodes)
    {
        var items = new List<Recommendation>
        {
            new()
            {
                UserId = UserId,
                DocumentId = docId,
                ScoreSnapshotId = snapshotId,
                Status = RecommendationStatus.Published,
                Type = RecommendationType.Insight,
                Priority = 1,
                Title = "Health insight summary",
                Content = $"{biomarkerCodes.Count} biomarkers were analyzed for this report. Current risk band is {riskBand}.",
                EvidenceJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    extractedCanonicalBiomarkerCount = evaluation.ExtractedCanonicalBiomarkerCount,
                    minimumRequiredCanonicalBiomarkerCount = evaluation.MinimumRequiredCanonicalBiomarkerCount
                })
            }
        };

        if (evaluation.MissingMandatoryBiomarkers.Count > 0)
        {
            items.Add(new Recommendation
            {
                UserId = UserId,
                DocumentId = docId,
                ScoreSnapshotId = snapshotId,
                Status = RecommendationStatus.Published,
                Type = RecommendationType.RiskPrediction,
                Priority = 2,
                Title = "Missing mandatory biomarkers impact confidence",
                Content = "Add the remaining mandatory biomarkers to improve prediction confidence and risk accuracy.",
                EvidenceJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    missingMandatoryBiomarkers = evaluation.MissingMandatoryBiomarkers
                })
            });
        }
        else
        {
            items.Add(new Recommendation
            {
                UserId = UserId,
                DocumentId = docId,
                ScoreSnapshotId = snapshotId,
                Status = RecommendationStatus.Published,
                Type = RecommendationType.RiskPrediction,
                Priority = 2,
                Title = "Mandatory biomarker coverage achieved",
                Content = "All mandatory biomarkers are present; risk estimation is based on complete required coverage.",
                EvidenceJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    presentMandatoryBiomarkers = evaluation.PresentMandatoryBiomarkers
                })
            });
        }

        items.Add(new Recommendation
        {
            UserId = UserId,
            DocumentId = docId,
            ScoreSnapshotId = snapshotId,
            Status = RecommendationStatus.Published,
            Type = RecommendationType.Action,
            Priority = 3,
            Title = "Recommended next step",
            Content = "Track this report against your next lab upload to detect trend direction and risk movement over time.",
            EvidenceJson = null
        });

        return items;
    }
}
