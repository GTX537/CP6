using System.Text;
using System.Text.Json;

namespace CP6.Space.Domain;

public sealed record SpacePutawayRecommendationData(
    Guid SiteId,
    Guid PublishedVersionId,
    string WarehouseCode,
    DateTime GeneratedAtUtc,
    Guid GeneratedBy,
    string DefinitionVersion,
    string Outcome,
    int ExaminedLocationCount,
    int EligibleCandidateCount,
    int ReturnedCandidateCount,
    bool IsTruncated,
    bool ExclusionSamplesTruncated,
    string RequestJson,
    string SourcesJson,
    string ExclusionsJson,
    string ExclusionSamplesJson,
    string CandidatesJson,
    string LimitationsJson,
    string RequestHash);

public sealed class SpacePutawayRecommendation : SpaceTenantEntity
{
    public const int RequestJsonMaximumBytes = 8 * 1024;
    public const int SourcesJsonMaximumBytes = 16 * 1024;
    public const int ExclusionsJsonMaximumBytes = 8 * 1024;
    public const int ExclusionSamplesJsonMaximumBytes = 128 * 1024;
    public const int CandidatesJsonMaximumBytes = 512 * 1024;
    public const int LimitationsJsonMaximumBytes = 16 * 1024;

    private SpacePutawayRecommendation()
    {
    }

    public Guid SiteId { get; private set; }
    public Guid PublishedVersionId { get; private set; }
    public string WarehouseCode { get; private set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; private set; }
    public Guid GeneratedBy { get; private set; }
    public string DefinitionVersion { get; private set; } = string.Empty;
    public string Outcome { get; private set; } = string.Empty;
    public int ExaminedLocationCount { get; private set; }
    public int EligibleCandidateCount { get; private set; }
    public int ReturnedCandidateCount { get; private set; }
    public bool IsTruncated { get; private set; }
    public bool ExclusionSamplesTruncated { get; private set; }
    public string RequestJson { get; private set; } = "{}";
    public string SourcesJson { get; private set; } = "{}";
    public string ExclusionsJson { get; private set; } = "{}";
    public string ExclusionSamplesJson { get; private set; } = "[]";
    public string CandidatesJson { get; private set; } = "[]";
    public string LimitationsJson { get; private set; } = "[]";
    public string RequestHash { get; private set; } = string.Empty;

    public static SpacePutawayRecommendation Create(
        Guid tenantId,
        Guid recommendationId,
        SpacePutawayRecommendationData value)
    {
        ArgumentNullException.ThrowIfNull(value);
        RequireIdentity(value.SiteId, nameof(value.SiteId));
        RequireIdentity(
            value.PublishedVersionId,
            nameof(value.PublishedVersionId));
        RequireIdentity(value.GeneratedBy, nameof(value.GeneratedBy));
        RequireUtc(value.GeneratedAtUtc, nameof(value.GeneratedAtUtc));
        if (value.ExaminedLocationCount < 0 ||
            value.EligibleCandidateCount < 0 ||
            value.ReturnedCandidateCount < 0 ||
            value.EligibleCandidateCount > value.ExaminedLocationCount ||
            value.ReturnedCandidateCount > value.EligibleCandidateCount ||
            value.IsTruncated !=
            (value.ReturnedCandidateCount < value.EligibleCandidateCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Putaway recommendation counts are inconsistent.");
        }
        var expectedOutcome = value.ReturnedCandidateCount == 0
            ? "NoCandidate"
            : "CandidatesGenerated";
        if (!string.Equals(
                value.Outcome,
                expectedOutcome,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Putaway recommendation outcome is inconsistent.",
                nameof(value));
        }

        Json(
            value.RequestJson,
            JsonValueKind.Object,
            RequestJsonMaximumBytes,
            nameof(value.RequestJson));
        Json(
            value.SourcesJson,
            JsonValueKind.Object,
            SourcesJsonMaximumBytes,
            nameof(value.SourcesJson));
        Json(
            value.ExclusionsJson,
            JsonValueKind.Object,
            ExclusionsJsonMaximumBytes,
            nameof(value.ExclusionsJson));
        Json(
            value.ExclusionSamplesJson,
            JsonValueKind.Array,
            ExclusionSamplesJsonMaximumBytes,
            nameof(value.ExclusionSamplesJson));
        Json(
            value.CandidatesJson,
            JsonValueKind.Array,
            CandidatesJsonMaximumBytes,
            nameof(value.CandidatesJson));
        Json(
            value.LimitationsJson,
            JsonValueKind.Array,
            LimitationsJsonMaximumBytes,
            nameof(value.LimitationsJson));

        var result = new SpacePutawayRecommendation
        {
            SiteId = value.SiteId,
            PublishedVersionId = value.PublishedVersionId,
            WarehouseCode = Text(
                value.WarehouseCode,
                100,
                nameof(value.WarehouseCode)),
            GeneratedAtUtc = value.GeneratedAtUtc,
            GeneratedBy = value.GeneratedBy,
            DefinitionVersion = Text(
                value.DefinitionVersion,
                50,
                nameof(value.DefinitionVersion)),
            Outcome = value.Outcome,
            ExaminedLocationCount = value.ExaminedLocationCount,
            EligibleCandidateCount = value.EligibleCandidateCount,
            ReturnedCandidateCount = value.ReturnedCandidateCount,
            IsTruncated = value.IsTruncated,
            ExclusionSamplesTruncated = value.ExclusionSamplesTruncated,
            RequestJson = value.RequestJson,
            SourcesJson = value.SourcesJson,
            ExclusionsJson = value.ExclusionsJson,
            ExclusionSamplesJson = value.ExclusionSamplesJson,
            CandidatesJson = value.CandidatesJson,
            LimitationsJson = value.LimitationsJson,
            RequestHash = Hash(value.RequestHash),
        };
        result.SetTenant(tenantId);
        result.SetId(recommendationId);
        return result;
    }

    private static void Json(
        string value,
        JsonValueKind expected,
        int maximumBytes,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Encoding.UTF8.GetByteCount(value) > maximumBytes)
        {
            throw new ArgumentException(
                $"JSON exceeds the {maximumBytes}-byte evidence limit.",
                parameterName);
        }
        using var document = JsonDocument.Parse(value);
        if (document.RootElement.ValueKind != expected)
        {
            throw new ArgumentException(
                $"JSON must be a {expected}.",
                parameterName);
        }
    }

    private static void RequireIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Identity is required.", parameterName);
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("UTC is required.", parameterName);
    }

    private static string Text(
        string value,
        int maximumLength,
        string parameterName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                $"A value of at most {maximumLength} characters is required.",
                parameterName);
        }
        return normalized;
    }

    private static string Hash(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is null ||
            normalized.Length != 64 ||
            !normalized.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "A SHA-256 request hash is required.",
                nameof(value));
        }
        return normalized;
    }
}
