using Api.Auth;
using Api.Domain;
using Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Api.Controllers;

[ApiController]
[Route("api/biomarkers")]
public class BiomarkersController : ControllerBase
{
    private readonly AppDbContext _db;

    public BiomarkersController(AppDbContext db)
    {
        _db = db;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public record ManualBiomarkerInput(
        string BiomarkerName,
        decimal Value,
        string Unit,
        decimal? NormalizedValue,
        string? NormalizedUnit,
        DateTime? ObservedAtUtc
    );

    public record ManualBiomarkersRequest(
        Guid DocumentId,
        IReadOnlyList<ManualBiomarkerInput> Biomarkers
    );

    public record ManualBiomarkersResponse(
        Guid DocumentId,
        int SavedCount,
        Guid? LatestJobId,
        JobStatus? LatestJobStatus,
        string? LatestJobError,
        IReadOnlyList<string>? MissingMandatoryBiomarkers
    );

    [Authorize]
    [HttpPost("manual")]
    [Consumes("application/json")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(ManualBiomarkersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ManualBiomarkersResponse>> UpsertManualBiomarkers([FromBody] ManualBiomarkersRequest request)
    {
        if (request.DocumentId == Guid.Empty)
            return BadRequest(new { message = "documentId is required" });

        if (request.Biomarkers is null || request.Biomarkers.Count == 0)
            return BadRequest(new { message = "biomarkers must contain at least one item" });

        var doc = await _db.RawDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.UserId == UserId);

        if (doc is null)
            return NotFound(new { message = "Document not found" });

        var now = DateTime.UtcNow;
        var savedCount = 0;

        foreach (var item in request.Biomarkers)
        {
            if (string.IsNullOrWhiteSpace(item.BiomarkerName))
                return BadRequest(new { message = "Each biomarker must include biomarkerName" });
            if (string.IsNullOrWhiteSpace(item.Unit))
                return BadRequest(new { message = "Each biomarker must include unit" });

            var code = BiomarkerCatalogPolicy.CanonicalizeBiomarker(item.BiomarkerName);
            var observedAt = item.ObservedAtUtc?.ToUniversalTime() ?? now;

            var existing = await _db.BiomarkerReadings
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefaultAsync(r =>
                    r.UserId == UserId &&
                    r.DocumentId == request.DocumentId &&
                    r.BiomarkerCode == code &&
                    r.SourceType == BiomarkerSourceType.Manual);

            if (existing is null)
            {
                _db.BiomarkerReadings.Add(new BiomarkerReading
                {
                    UserId = UserId,
                    DocumentId = request.DocumentId,
                    BiomarkerCode = code,
                    SourceName = item.BiomarkerName,
                    Value = item.Value,
                    Unit = item.Unit,
                    NormalizedValue = item.NormalizedValue,
                    NormalizedUnit = item.NormalizedUnit,
                    ObservedAtUtc = observedAt,
                    SourceType = BiomarkerSourceType.Manual,
                    EnteredByUserId = UserId,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                });
                savedCount++;
                continue;
            }

            existing.SourceName = item.BiomarkerName;
            existing.Value = item.Value;
            existing.Unit = item.Unit;
            existing.NormalizedValue = item.NormalizedValue;
            existing.NormalizedUnit = item.NormalizedUnit;
            existing.ObservedAtUtc = observedAt;
            existing.SourceType = BiomarkerSourceType.Manual;
            existing.EnteredByUserId = UserId;
            existing.UpdatedAtUtc = now;
            savedCount++;
        }

        await _db.SaveChangesAsync();

        var latestJob = await _db.ProcessingJobs
            .Where(j => j.DocumentId == request.DocumentId && j.UserId == UserId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync();

        var evaluation = await BiomarkerCatalogPolicy.EvaluateDocumentAsync(_db, UserId, request.DocumentId);

        if (latestJob is not null)
        {
            latestJob.CompletedAtUtc = DateTime.UtcNow;
            if (evaluation.IsSufficient)
            {
                latestJob.Status = JobStatus.Succeeded;
                latestJob.Error = null;
            }
            else
            {
                latestJob.Status = JobStatus.InsufficientData;
                latestJob.Error = BiomarkerCatalogPolicy.BuildInsufficientDataError(evaluation);
            }

            doc.Status = DocumentStatus.Processed;
            doc.ProcessedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        return Ok(new ManualBiomarkersResponse(
            DocumentId: request.DocumentId,
            SavedCount: savedCount,
            LatestJobId: latestJob?.Id,
            LatestJobStatus: latestJob?.Status,
            LatestJobError: latestJob?.Error,
            MissingMandatoryBiomarkers: evaluation.IsSufficient ? null : evaluation.MissingMandatoryBiomarkers
        ));
    }
}
