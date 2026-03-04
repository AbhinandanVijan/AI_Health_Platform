using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Auth;
using Microsoft.EntityFrameworkCore;

namespace Api.Services;

public sealed record MandatoryPolicy(
    int MinimumRequiredCanonicalBiomarkerCount,
    IReadOnlyList<string> MandatoryBiomarkers,
    IReadOnlyDictionary<string, string> NameToCode,
    ISet<string> MandatoryCodes
);

public sealed record MandatoryEvaluationResult(
    IReadOnlyList<string> MissingMandatoryBiomarkers,
    IReadOnlyList<string> PresentMandatoryBiomarkers,
    int ExtractedCanonicalBiomarkerCount,
    int MinimumRequiredCanonicalBiomarkerCount,
    bool IsSufficient
);

public static class BiomarkerCatalogPolicy
{
    private static readonly object Sync = new();
    private static Dictionary<string, string>? _aliasIndex;
    private static MandatoryPolicy? _mandatoryPolicy;

    public static string BiomarkerNameToCode(string name)
    {
        var sanitized = Regex.Replace(name ?? string.Empty, "[^A-Za-z0-9]+", "_").Trim('_');
        return sanitized.ToUpperInvariant();
    }

    public static string CanonicalizeBiomarker(string name)
    {
        EnsureLoaded();

        var key = NormalizeText(name);
        var matched = MatchAlias(key);
        if (!string.IsNullOrWhiteSpace(matched))
            return matched;

        var categoryPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hematology",
            "metabolic",
            "lipid",
            "diabetes",
            "thyroid",
            "inflammation",
            "nutrition",
            "vitamins"
        };

        var parts = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && categoryPrefixes.Contains(parts[0]))
        {
            var withoutCategory = string.Join(' ', parts.Skip(1));
            matched = MatchAlias(withoutCategory);
            if (!string.IsNullOrWhiteSpace(matched))
                return matched;
        }

        return BiomarkerNameToCode(name);
    }

    public static MandatoryPolicy GetMandatoryPolicy()
    {
        EnsureLoaded();
        return _mandatoryPolicy!;
    }

    public static async Task<MandatoryEvaluationResult> EvaluateDocumentAsync(AppDbContext db, string userId, Guid docId)
    {
        var policy = GetMandatoryPolicy();

        var extractedCodes = await db.BiomarkerReadings
            .AsNoTracking()
            .Where(r => r.UserId == userId && r.DocumentId == docId)
            .Select(r => r.BiomarkerCode)
            .Distinct()
            .ToListAsync();

        var extractedSet = new HashSet<string>(extractedCodes, StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var present = new List<string>();

        foreach (var biomarker in policy.MandatoryBiomarkers)
        {
            var code = policy.NameToCode[biomarker];
            if (extractedSet.Contains(code))
                present.Add(biomarker);
            else
                missing.Add(biomarker);
        }

        var count = extractedSet.Count;
        var sufficient = missing.Count == 0 && count >= policy.MinimumRequiredCanonicalBiomarkerCount;

        return new MandatoryEvaluationResult(
            MissingMandatoryBiomarkers: missing,
            PresentMandatoryBiomarkers: present,
            ExtractedCanonicalBiomarkerCount: count,
            MinimumRequiredCanonicalBiomarkerCount: policy.MinimumRequiredCanonicalBiomarkerCount,
            IsSufficient: sufficient
        );
    }

    public static string BuildInsufficientDataError(MandatoryEvaluationResult evaluation)
    {
        var payload = new
        {
            code = "LAB_REPORT_VALIDATION_INSUFFICIENT_DATA",
            message = "Uploaded report is missing mandatory blood biomarkers.",
            missingMandatoryBiomarkers = evaluation.MissingMandatoryBiomarkers,
            presentMandatoryBiomarkers = evaluation.PresentMandatoryBiomarkers,
            extractedCanonicalBiomarkerCount = evaluation.ExtractedCanonicalBiomarkerCount,
            minimumRequiredCanonicalBiomarkerCount = evaluation.MinimumRequiredCanonicalBiomarkerCount,
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string? MatchAlias(string candidate)
    {
        var aliases = _aliasIndex!;
        if (aliases.TryGetValue(candidate, out var exact))
            return exact;

        var tokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var padded = $" {candidate} ";

        foreach (var alias in aliases.Keys.OrderByDescending(k => k.Length))
        {
            var code = aliases[alias];
            if (string.IsNullOrWhiteSpace(alias))
                continue;

            if (alias.Contains(' '))
            {
                if (padded.Contains($" {alias} ", StringComparison.OrdinalIgnoreCase))
                    return code;
                continue;
            }

            if (alias.Length <= 2)
            {
                if (tokens.Contains(alias))
                    return code;
                continue;
            }

            if (tokens.Contains(alias))
                return code;
        }

        return null;
    }

    private static void EnsureLoaded()
    {
        if (_aliasIndex is not null && _mandatoryPolicy is not null)
            return;

        lock (Sync)
        {
            if (_aliasIndex is not null && _mandatoryPolicy is not null)
                return;

            var baseDir = AppContext.BaseDirectory;
            var dataDir = Path.Combine(baseDir, "data");
            var biomarkerPath = Path.Combine(dataDir, "biomarker.json");
            var mandatoryPath = Path.Combine(dataDir, "mandatory_biomarkers.json");

            if (!File.Exists(biomarkerPath))
                throw new FileNotFoundException($"Missing biomarker catalog file: {biomarkerPath}");
            if (!File.Exists(mandatoryPath))
                throw new FileNotFoundException($"Missing mandatory biomarker policy file: {mandatoryPath}");

            var biomarkerDoc = JsonDocument.Parse(File.ReadAllText(biomarkerPath));
            if (biomarkerDoc.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Invalid biomarker.json format: root must be an object");

            var aliasIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var property in biomarkerDoc.RootElement.EnumerateObject())
            {
                var canonicalName = property.Name;
                var code = BiomarkerNameToCode(canonicalName);
                aliasIndex[NormalizeText(canonicalName)] = code;

                var humanized = HumanizeCatalogKey(canonicalName);
                if (!string.IsNullOrWhiteSpace(humanized))
                    aliasIndex[humanized] = code;

                if (property.Value.ValueKind != JsonValueKind.Object)
                    continue;

                if (!property.Value.TryGetProperty("aliases", out var aliasesElement) || aliasesElement.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var alias in aliasesElement.EnumerateArray())
                {
                    if (alias.ValueKind != JsonValueKind.String)
                        continue;

                    var value = alias.GetString();
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    aliasIndex[NormalizeText(value)] = code;
                }
            }

            var mandatoryDoc = JsonDocument.Parse(File.ReadAllText(mandatoryPath));
            if (mandatoryDoc.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Invalid mandatory_biomarkers.json format: root must be an object");

            var minimum = mandatoryDoc.RootElement.TryGetProperty("minimumRequiredCanonicalBiomarkerCount", out var minElement)
                ? minElement.GetInt32()
                : 0;

            if (!mandatoryDoc.RootElement.TryGetProperty("mandatoryBiomarkers", out var mandatoryElement)
                || mandatoryElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Invalid mandatory_biomarkers.json: mandatoryBiomarkers must be an array");
            }

            var mandatoryNames = mandatoryElement
                .EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var nameToCode = mandatoryNames
                .ToDictionary(name => name, CanonicalizeBiomarker, StringComparer.OrdinalIgnoreCase);

            var mandatoryCodes = new HashSet<string>(nameToCode.Values, StringComparer.OrdinalIgnoreCase);

            _aliasIndex = aliasIndex;
            _mandatoryPolicy = new MandatoryPolicy(
                MinimumRequiredCanonicalBiomarkerCount: minimum,
                MandatoryBiomarkers: mandatoryNames,
                NameToCode: nameToCode,
                MandatoryCodes: mandatoryCodes
            );
        }
    }

    private static string NormalizeText(string value)
    {
        return string.Join(' ', (value ?? string.Empty).Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string HumanizeCatalogKey(string value)
    {
        var spaced = Regex.Replace(value ?? string.Empty, "[-_]+", " ");
        spaced = Regex.Replace(spaced, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return NormalizeText(spaced);
    }
}
