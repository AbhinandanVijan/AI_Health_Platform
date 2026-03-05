using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public sealed record InsightGenerationContext(
    Guid DocumentId,
    string UserId,
    int OverallScore,
    decimal Confidence,
    RiskBand RiskBand,
    MandatoryEvaluationResult MandatoryEvaluation,
    IReadOnlyList<BiomarkerReading> Biomarkers
);

public sealed record GeneratedInsightItem(
    RecommendationType Type,
    int Priority,
    string Title,
    string Content,
    string? EvidenceJson
);

public interface IInsightsGenerationService
{
    Task<IReadOnlyList<GeneratedInsightItem>> GenerateAsync(InsightGenerationContext context, CancellationToken cancellationToken);
}

public sealed class OpenAiRagInsightsGenerationService : IInsightsGenerationService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAiRagInsightsGenerationService> _logger;

    public OpenAiRagInsightsGenerationService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OpenAiRagInsightsGenerationService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GeneratedInsightItem>> GenerateAsync(InsightGenerationContext context, CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["LLM_BASE_URL"]?.TrimEnd('/');
        var apiKey = _configuration["LLM_API_KEY"];
        var model = _configuration["LLM_MODEL"];

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("LLM configuration missing. Set LLM_BASE_URL, LLM_API_KEY, and LLM_MODEL.");
        }

        var ragChunks = BuildRagChunks(context);
        var ragJson = JsonSerializer.Serialize(ragChunks);

        var biomarkerSummary = context.Biomarkers
            .OrderBy(b => b.BiomarkerCode)
            .Select(b => new
            {
                biomarkerCode = b.BiomarkerCode,
                sourceName = b.SourceName,
                value = b.Value,
                unit = b.Unit,
                normalizedValue = b.NormalizedValue,
                normalizedUnit = b.NormalizedUnit,
                sourceType = b.SourceType.ToString()
            })
            .ToList();

        var userPayload = new
        {
            documentId = context.DocumentId,
            overallScore = context.OverallScore,
            confidence = context.Confidence,
            riskBand = context.RiskBand.ToString(),
            mandatoryEvaluation = new
            {
                context.MandatoryEvaluation.IsSufficient,
                context.MandatoryEvaluation.MissingMandatoryBiomarkers,
                context.MandatoryEvaluation.PresentMandatoryBiomarkers,
                context.MandatoryEvaluation.ExtractedCanonicalBiomarkerCount,
                context.MandatoryEvaluation.MinimumRequiredCanonicalBiomarkerCount
            },
            biomarkers = biomarkerSummary,
            ragContext = ragChunks
        };

        var systemPrompt = """
You are a clinical wellness insights assistant.
Produce user-friendly health insights from provided biomarkers and retrieval context.
Rules:
1) Do not diagnose diseases.
2) Do not claim certainty. Use cautious language.
3) Keep recommendations actionable and specific.
4) Ground statements in provided ragContext only.
5) Return strict JSON object with key `recommendations`.
6) `recommendations` must contain 3-6 items across types: Insight, RiskPrediction, Action.
7) Each item requires: type, priority (1-5), title, content, evidence (array of sourceId strings).
""";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var completionRequest = new
        {
            model,
            temperature = 0.2,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = JsonSerializer.Serialize(userPayload) }
            }
        };

        request.Content = new StringContent(JsonSerializer.Serialize(completionRequest), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Insights LLM call failed: {StatusCode} {Body}", response.StatusCode, body);
            throw new HttpRequestException($"LLM request failed with status {(int)response.StatusCode}");
        }

        using var root = JsonDocument.Parse(body);
        var content = root.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("LLM returned empty insight content.");

        using var generated = JsonDocument.Parse(content);
        if (!generated.RootElement.TryGetProperty("recommendations", out var recsElement) || recsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("LLM insight payload is missing recommendations array.");

        var items = new List<GeneratedInsightItem>();
        foreach (var rec in recsElement.EnumerateArray())
        {
            var typeRaw = rec.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            var type = ParseType(typeRaw);

            var priority = rec.TryGetProperty("priority", out var priEl) && priEl.TryGetInt32(out var p)
                ? Math.Clamp(p, 1, 5)
                : 3;

            var title = rec.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            var contentText = rec.TryGetProperty("content", out var contentEl) ? contentEl.GetString() : null;

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(contentText))
                continue;

            string? evidenceJson = null;
            if (rec.TryGetProperty("evidence", out var evidenceEl) && evidenceEl.ValueKind == JsonValueKind.Array)
            {
                var evidenceIds = evidenceEl
                    .EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (evidenceIds.Count > 0)
                    evidenceJson = JsonSerializer.Serialize(new { sourceIds = evidenceIds });
            }

            items.Add(new GeneratedInsightItem(type, priority, title!, contentText!, evidenceJson));
        }

        if (items.Count == 0)
            throw new InvalidOperationException("LLM returned no valid recommendations.");

        return items;
    }

    private static RecommendationType ParseType(string? raw)
    {
        if (string.Equals(raw, "RiskPrediction", StringComparison.OrdinalIgnoreCase))
            return RecommendationType.RiskPrediction;
        if (string.Equals(raw, "Action", StringComparison.OrdinalIgnoreCase))
            return RecommendationType.Action;
        return RecommendationType.Insight;
    }

    private List<object> BuildRagChunks(InsightGenerationContext context)
    {
        var maxChunks = int.TryParse(_configuration["RAG_MAX_CHUNKS"], out var configuredMax)
            ? Math.Clamp(configuredMax, 3, 30)
            : 12;

        var chunks = new List<object>();

        chunks.Add(new
        {
            sourceId = "mandatory-policy",
            content = $"Minimum required canonical biomarkers: {context.MandatoryEvaluation.MinimumRequiredCanonicalBiomarkerCount}. " +
                      $"Missing mandatory biomarkers: {string.Join(", ", context.MandatoryEvaluation.MissingMandatoryBiomarkers)}."
        });

        var presentCodes = context.Biomarkers
            .Select(b => b.BiomarkerCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var catalogPath = Path.Combine(AppContext.BaseDirectory, "data", "biomarker.json");
        if (!File.Exists(catalogPath))
            return chunks;

        using var catalogDoc = JsonDocument.Parse(File.ReadAllText(catalogPath));
        if (catalogDoc.RootElement.ValueKind != JsonValueKind.Object)
            return chunks;

        foreach (var property in catalogDoc.RootElement.EnumerateObject())
        {
            var canonicalName = property.Name;
            var code = BiomarkerCatalogPolicy.BiomarkerNameToCode(canonicalName);
            if (!presentCodes.Contains(code))
                continue;

            if (property.Value.ValueKind != JsonValueKind.Object)
                continue;

            var description = property.Value.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String
                ? descEl.GetString()
                : null;

            var unit = property.Value.TryGetProperty("unit", out var unitEl) && unitEl.ValueKind == JsonValueKind.String
                ? unitEl.GetString()
                : null;

            var referenceRange = property.Value.TryGetProperty("referenceRange", out var rangeEl) && rangeEl.ValueKind == JsonValueKind.String
                ? rangeEl.GetString()
                : null;

            chunks.Add(new
            {
                sourceId = $"catalog-{code}",
                content = $"{canonicalName}: {description}. Typical unit: {unit}. Reference range: {referenceRange}."
            });

            if (chunks.Count >= maxChunks)
                break;
        }

        return chunks;
    }
}
