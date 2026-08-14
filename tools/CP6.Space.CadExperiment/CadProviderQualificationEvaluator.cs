using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.CadExperiment;

public sealed record CadProviderQualificationRequestV1(
    int SchemaVersion,
    Guid SiteId,
    DateTime EvaluatedAtUtc,
    IReadOnlyList<CadProviderQualificationCandidateV1> Candidates);

public sealed record CadProviderQualificationCandidateV1(
    string ProviderKey,
    string ProviderVersion,
    string DeploymentMode,
    string DataBoundary,
    bool SupportsDwg,
    bool SupportsDxf,
    DateTime ValidFromUtc,
    DateTime ExpiresAtUtc,
    string? ApprovalEvidenceReference,
    string? SecretReference,
    CadProviderHardGateEvidenceV1 HardGates,
    bool TrialPreflightPassed,
    string? TrialPreflightReportSha256,
    string QualificationRubricVersion,
    string GoldenDatasetSha256,
    string FrozenEnvironmentSha256,
    string? QualificationEvidenceReference,
    CadProviderQualificationScoresV1 Scores);

public sealed record CadProviderHardGateEvidenceV1(
    string? LicensingApprovalReference,
    string? SecurityApprovalReference,
    string? DataRegionApprovalReference,
    string? DeletionRetentionApprovalReference);

public sealed record CadProviderQualificationScoresV1(
    int EntityBlockAttributeCoverage,
    int GeometryUnitCoordinateFidelity,
    int PerformanceMemoryStability,
    int SecurityIsolationOperability,
    int SaasLicensingTotalCost,
    int VendorSupportVersionExit);

public sealed record CadProviderQualificationCandidateResultV1(
    string ProviderKey,
    string ProviderVersion,
    string DeploymentMode,
    string DataBoundary,
    bool SupportsDwg,
    bool SupportsDxf,
    DateTime ValidFromUtc,
    DateTime ExpiresAtUtc,
    string? ApprovalEvidenceReference,
    string? SecretReference,
    CadProviderHardGateEvidenceV1 HardGates,
    bool TrialPreflightPassed,
    string? TrialPreflightReportSha256,
    string QualificationRubricVersion,
    string GoldenDatasetSha256,
    string FrozenEnvironmentSha256,
    string? SourceQualificationEvidenceReference,
    CadProviderQualificationScoresV1 Scores,
    int QualificationScore,
    bool Qualified,
    IReadOnlyList<string> BlockingCodes);

public sealed record CadProviderQualificationSelectionSlotV1(
    string ProviderKey,
    string ProviderVersion,
    int QualificationScore);

public sealed record CadProviderQualificationReportV1(
    int SchemaVersion,
    Guid SiteId,
    DateTime EvaluatedAtUtc,
    string? QualificationRubricVersion,
    string? GoldenDatasetSha256,
    string? FrozenEnvironmentSha256,
    int MinimumQualificationScore,
    int QualifiedCandidateCount,
    bool CadGaReady,
    CadProviderQualificationSelectionSlotV1? Primary,
    CadProviderQualificationSelectionSlotV1? Backup,
    IReadOnlyList<CadProviderQualificationCandidateResultV1> Candidates,
    IReadOnlyList<string> BlockingCodes,
    string SelectionSha256,
    IReadOnlyList<SpaceCadProviderCertificationInputDto> CertificationInputs);

public static class CadProviderQualificationEvaluator
{
    public const int SchemaVersion = 1;
    public const string RubricVersion = "cad-provider-adr-0001-v1";
    public const int MinimumScore = 80;
    private const int MaximumInputBytes = 1024 * 1024;
    private const int MaximumCandidates = 16;

    private static readonly JsonSerializerOptions StrictJsonOptions = new(
        CadExperimentJson.Options)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonSerializerOptions HashJsonOptions = new(
        CadExperimentJson.Options)
    {
        WriteIndented = false,
    };

    public static async Task<CadProviderQualificationReportV1> EvaluateAsync(
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        var request = await ReadAsync(inputPath, cancellationToken);
        return Evaluate(request);
    }

    public static CadProviderQualificationReportV1 Evaluate(
        CadProviderQualificationRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SchemaVersion != SchemaVersion)
            throw new InvalidDataException(
                $"Provider qualification schemaVersion must be {SchemaVersion}.");
        if (request.SiteId == Guid.Empty)
            throw new InvalidDataException("A non-empty SiteId is required.");
        if (request.EvaluatedAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidDataException("EvaluatedAtUtc must be UTC.");
        var candidates = request.Candidates ?? throw new InvalidDataException(
            "Provider qualification candidates are required.");
        if (candidates.Count is < 1 or > MaximumCandidates)
            throw new InvalidDataException(
                $"Provider qualification requires 1 to {MaximumCandidates} candidates.");

        var normalized = candidates.Select(candidate => Normalize(
                candidate,
                request.EvaluatedAtUtc))
            .ToArray();
        if (normalized.Select(item => item.ProviderKey)
                .Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            throw new InvalidDataException("Provider keys must be unique.");
        }

        var globalBlockers = new List<string>();
        var rubrics = normalized.Select(item => item.QualificationRubricVersion)
            .Distinct(StringComparer.Ordinal).ToArray();
        var datasets = normalized.Select(item => item.GoldenDatasetSha256)
            .Distinct(StringComparer.Ordinal).ToArray();
        var environments = normalized.Select(item => item.FrozenEnvironmentSha256)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (rubrics.Length != 1 || datasets.Length != 1 || environments.Length != 1)
            globalBlockers.Add("CAD_PROVIDER_QUALIFICATION_BASELINE_MISMATCH");
        if (rubrics.Length != 1 || !rubrics[0].Equals(RubricVersion, StringComparison.Ordinal))
            globalBlockers.Add("CAD_PROVIDER_QUALIFICATION_RUBRIC_UNSUPPORTED");

        var results = normalized
            .Select(ToResult)
            .OrderByDescending(item => item.QualificationScore)
            .ThenBy(item => item.ProviderKey, StringComparer.Ordinal)
            .ToArray();
        var qualified = results.Where(item => item.Qualified).ToArray();
        CadProviderQualificationCandidateResultV1? primary = null;
        CadProviderQualificationCandidateResultV1? backup = null;
        if (qualified.Length < 2)
        {
            globalBlockers.Add("CAD_PROVIDER_QUALIFIED_CANDIDATES_INSUFFICIENT");
        }
        else
        {
            var topScore = qualified[0].QualificationScore;
            if (qualified.Count(item => item.QualificationScore == topScore) != 1)
            {
                globalBlockers.Add("CAD_PROVIDER_PRIMARY_SCORE_TIE");
            }
            else
            {
                primary = qualified[0];
                var secondScore = qualified[1].QualificationScore;
                if (qualified.Count(item => item.QualificationScore == secondScore) != 1)
                    globalBlockers.Add("CAD_PROVIDER_BACKUP_SCORE_TIE");
                else
                    backup = qualified[1];
            }
        }

        var blockers = globalBlockers.Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var gaReady = blockers.Length == 0 && primary is not null && backup is not null;
        if (!gaReady)
        {
            primary = null;
            backup = null;
        }

        var primarySlot = primary is null ? null : ToSlot(primary);
        var backupSlot = backup is null ? null : ToSlot(backup);
        var hashPayload = new QualificationHashPayload(
            SchemaVersion,
            request.SiteId,
            request.EvaluatedAtUtc,
            rubrics.Length == 1 ? rubrics[0] : null,
            datasets.Length == 1 ? datasets[0] : null,
            environments.Length == 1 ? environments[0] : null,
            MinimumScore,
            qualified.Length,
            gaReady,
            primarySlot,
            backupSlot,
            results,
            blockers);
        var selectionSha256 = Hash(hashPayload);
        var certifications = gaReady
            ? new[]
            {
                ToCertification(primary!, SpaceCadProviderRole.Primary, selectionSha256),
                ToCertification(backup!, SpaceCadProviderRole.Backup, selectionSha256),
            }
            : [];

        return new CadProviderQualificationReportV1(
            SchemaVersion,
            request.SiteId,
            request.EvaluatedAtUtc,
            rubrics.Length == 1 ? rubrics[0] : null,
            datasets.Length == 1 ? datasets[0] : null,
            environments.Length == 1 ? environments[0] : null,
            MinimumScore,
            qualified.Length,
            gaReady,
            primarySlot,
            backupSlot,
            results,
            blockers,
            selectionSha256,
            certifications);
    }

    private static async Task<CadProviderQualificationRequestV1> ReadAsync(
        string inputPath,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(inputPath);
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("Provider qualification input was not found.", path);
        if (info.Length is <= 0 or > MaximumInputBytes)
            throw new InvalidDataException(
                $"Provider qualification input must be 1 to {MaximumInputBytes} bytes.");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        using var document = JsonDocument.Parse(
            bytes,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
        RejectDuplicateProperties(document.RootElement, "$", 0);
        return document.RootElement.Deserialize<CadProviderQualificationRequestV1>(
                   StrictJsonOptions)
               ?? throw new InvalidDataException(
                   "Provider qualification input is empty.");
    }

    private static NormalizedCandidate Normalize(
        CadProviderQualificationCandidateV1 value,
        DateTime evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(value);
        var key = SpaceCadProviderKey.Normalize(value.ProviderKey);
        var version = RequireReference(value.ProviderVersion, 100, "ProviderVersion");
        if (!Enum.TryParse<SpaceCadProviderDeploymentMode>(
                value.DeploymentMode,
                ignoreCase: true,
                out var deployment) || !Enum.IsDefined(deployment))
        {
            throw new InvalidDataException(
                $"Provider '{key}' has an invalid DeploymentMode.");
        }
        if (!Enum.TryParse<SpaceCadProviderDataBoundary>(
                value.DataBoundary,
                ignoreCase: true,
                out var boundary) || !Enum.IsDefined(boundary))
        {
            throw new InvalidDataException(
                $"Provider '{key}' has an invalid DataBoundary.");
        }
        if (value.ValidFromUtc.Kind != DateTimeKind.Utc ||
            value.ExpiresAtUtc.Kind != DateTimeKind.Utc ||
            value.ExpiresAtUtc <= value.ValidFromUtc)
        {
            throw new InvalidDataException(
                $"Provider '{key}' has an invalid UTC approval window.");
        }
        var hardGates = value.HardGates ?? throw new InvalidDataException(
            $"Provider '{key}' hard-gate evidence is required.");
        var normalizedGates = new CadProviderHardGateEvidenceV1(
            OptionalReference(hardGates.LicensingApprovalReference, 500),
            OptionalReference(hardGates.SecurityApprovalReference, 500),
            OptionalReference(hardGates.DataRegionApprovalReference, 500),
            OptionalReference(hardGates.DeletionRetentionApprovalReference, 500));
        var approvalEvidence = OptionalReference(
            value.ApprovalEvidenceReference,
            500);
        var secretReference = OptionalReference(value.SecretReference, 256);
        var sourceQualificationEvidence = OptionalReference(
            value.QualificationEvidenceReference,
            500);
        var trialHash = string.IsNullOrWhiteSpace(value.TrialPreflightReportSha256)
            ? null
            : RequireSha256(
                value.TrialPreflightReportSha256,
                $"Provider '{key}' TrialPreflightReportSha256");
        var rubric = RequireReference(
            value.QualificationRubricVersion,
            100,
            $"Provider '{key}' QualificationRubricVersion");
        var dataset = RequireSha256(
            value.GoldenDatasetSha256,
            $"Provider '{key}' GoldenDatasetSha256");
        var environment = RequireSha256(
            value.FrozenEnvironmentSha256,
            $"Provider '{key}' FrozenEnvironmentSha256");
        var scores = value.Scores ?? throw new InvalidDataException(
            $"Provider '{key}' scores are required.");
        ValidateScore(
            scores.EntityBlockAttributeCoverage,
            25,
            key,
            nameof(scores.EntityBlockAttributeCoverage));
        ValidateScore(
            scores.GeometryUnitCoordinateFidelity,
            20,
            key,
            nameof(scores.GeometryUnitCoordinateFidelity));
        ValidateScore(
            scores.PerformanceMemoryStability,
            15,
            key,
            nameof(scores.PerformanceMemoryStability));
        ValidateScore(
            scores.SecurityIsolationOperability,
            15,
            key,
            nameof(scores.SecurityIsolationOperability));
        ValidateScore(
            scores.SaasLicensingTotalCost,
            15,
            key,
            nameof(scores.SaasLicensingTotalCost));
        ValidateScore(
            scores.VendorSupportVersionExit,
            10,
            key,
            nameof(scores.VendorSupportVersionExit));
        var total = scores.EntityBlockAttributeCoverage +
            scores.GeometryUnitCoordinateFidelity +
            scores.PerformanceMemoryStability +
            scores.SecurityIsolationOperability +
            scores.SaasLicensingTotalCost +
            scores.VendorSupportVersionExit;

        var blockers = new List<string>();
        if (!value.SupportsDwg || !value.SupportsDxf)
            blockers.Add("CAD_PROVIDER_FORMAT_COVERAGE_INCOMPLETE");
        if (approvalEvidence is null)
            blockers.Add("CAD_PROVIDER_APPROVAL_EVIDENCE_MISSING");
        if (normalizedGates.LicensingApprovalReference is null)
            blockers.Add("CAD_PROVIDER_LICENSING_APPROVAL_MISSING");
        if (normalizedGates.SecurityApprovalReference is null)
            blockers.Add("CAD_PROVIDER_SECURITY_APPROVAL_MISSING");
        if (normalizedGates.DataRegionApprovalReference is null)
            blockers.Add("CAD_PROVIDER_DATA_REGION_APPROVAL_MISSING");
        if (normalizedGates.DeletionRetentionApprovalReference is null)
            blockers.Add("CAD_PROVIDER_DELETION_RETENTION_APPROVAL_MISSING");
        if (!value.TrialPreflightPassed || trialHash is null)
            blockers.Add("CAD_PROVIDER_TRIAL_PREFLIGHT_NOT_PASSED");
        if (sourceQualificationEvidence is null)
            blockers.Add("CAD_PROVIDER_QUALIFICATION_EVIDENCE_MISSING");
        if (deployment == SpaceCadProviderDeploymentMode.ApprovedCloudService &&
            secretReference is null)
            blockers.Add("CAD_PROVIDER_SECRET_REFERENCE_MISSING");
        if (evaluatedAtUtc < value.ValidFromUtc || evaluatedAtUtc >= value.ExpiresAtUtc)
            blockers.Add("CAD_PROVIDER_APPROVAL_NOT_CURRENT");
        if (total < MinimumScore)
            blockers.Add("CAD_PROVIDER_QUALIFICATION_SCORE_BELOW_THRESHOLD");

        return new NormalizedCandidate(
            key,
            version,
            deployment,
            boundary,
            value.SupportsDwg,
            value.SupportsDxf,
            value.ValidFromUtc,
            value.ExpiresAtUtc,
            approvalEvidence,
            secretReference,
            normalizedGates,
            value.TrialPreflightPassed,
            trialHash,
            rubric,
            dataset,
            environment,
            sourceQualificationEvidence,
            scores,
            total,
            blockers.Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }

    private static CadProviderQualificationCandidateResultV1 ToResult(
        NormalizedCandidate value) =>
        new(
            value.ProviderKey,
            value.ProviderVersion,
            value.DeploymentMode.ToString(),
            value.DataBoundary.ToString(),
            value.SupportsDwg,
            value.SupportsDxf,
            value.ValidFromUtc,
            value.ExpiresAtUtc,
            value.ApprovalEvidenceReference,
            value.SecretReference,
            value.HardGates,
            value.TrialPreflightPassed,
            value.TrialPreflightReportSha256,
            value.QualificationRubricVersion,
            value.GoldenDatasetSha256,
            value.FrozenEnvironmentSha256,
            value.SourceQualificationEvidenceReference,
            value.Scores,
            value.QualificationScore,
            value.BlockingCodes.Count == 0,
            value.BlockingCodes);

    private static CadProviderQualificationSelectionSlotV1 ToSlot(
        CadProviderQualificationCandidateResultV1 value) =>
        new(value.ProviderKey, value.ProviderVersion, value.QualificationScore);

    private static SpaceCadProviderCertificationInputDto ToCertification(
        CadProviderQualificationCandidateResultV1 value,
        SpaceCadProviderRole role,
        string selectionSha256) =>
        new(
            value.ProviderKey,
            role.ToString(),
            value.DeploymentMode,
            value.DataBoundary,
            value.ApprovalEvidenceReference!,
            value.SecretReference,
            value.ValidFromUtc,
            value.ExpiresAtUtc,
            value.SupportsDwg,
            value.SupportsDxf,
            LicensingApproved: true,
            SecurityApproved: true,
            DataRegionApproved: true,
            DeletionRetentionApproved: true,
            value.QualificationScore,
            value.QualificationRubricVersion,
            value.GoldenDatasetSha256,
            value.FrozenEnvironmentSha256,
            $"sha256:{selectionSha256}");

    private static string Hash(QualificationHashPayload value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, HashJsonOptions);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static void ValidateScore(
        int value,
        int maximum,
        string providerKey,
        string dimension)
    {
        if (value is < 0 || value > maximum)
        {
            throw new InvalidDataException(
                $"Provider '{providerKey}' score '{dimension}' must be between 0 and {maximum}.");
        }
    }

    private static string RequireReference(string? value, int maximum, string field)
    {
        var normalized = OptionalReference(value, maximum);
        return normalized ?? throw new InvalidDataException(
            $"{field} requires a bounded opaque reference.");
    }

    private static string? OptionalReference(string? value, int maximum)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Equals("<pending>", StringComparison.OrdinalIgnoreCase))
            return null;
        if (normalized.Length > maximum || normalized.Any(char.IsControl) ||
            normalized.Any(char.IsWhiteSpace))
            throw new InvalidDataException("Evidence references must be bounded opaque values.");
        return normalized;
    }

    private static string RequireSha256(string? value, string field)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized is not { Length: 64 } || normalized.Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException($"{field} must be a 64-character SHA-256.");
        return normalized;
    }

    private static void RejectDuplicateProperties(
        JsonElement element,
        string path,
        int depth)
    {
        if (depth > 32)
            throw new InvalidDataException("Provider qualification JSON is too deeply nested.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException(
                        $"Duplicate JSON property '{property.Name}' at {path}.");
                RejectDuplicateProperties(
                    property.Value,
                    $"{path}.{property.Name}",
                    depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                RejectDuplicateProperties(item, $"{path}[{index}]", depth + 1);
                index++;
            }
        }
    }

    private sealed record NormalizedCandidate(
        string ProviderKey,
        string ProviderVersion,
        SpaceCadProviderDeploymentMode DeploymentMode,
        SpaceCadProviderDataBoundary DataBoundary,
        bool SupportsDwg,
        bool SupportsDxf,
        DateTime ValidFromUtc,
        DateTime ExpiresAtUtc,
        string? ApprovalEvidenceReference,
        string? SecretReference,
        CadProviderHardGateEvidenceV1 HardGates,
        bool TrialPreflightPassed,
        string? TrialPreflightReportSha256,
        string QualificationRubricVersion,
        string GoldenDatasetSha256,
        string FrozenEnvironmentSha256,
        string? SourceQualificationEvidenceReference,
        CadProviderQualificationScoresV1 Scores,
        int QualificationScore,
        IReadOnlyList<string> BlockingCodes);

    private sealed record QualificationHashPayload(
        int SchemaVersion,
        Guid SiteId,
        DateTime EvaluatedAtUtc,
        string? QualificationRubricVersion,
        string? GoldenDatasetSha256,
        string? FrozenEnvironmentSha256,
        int MinimumQualificationScore,
        int QualifiedCandidateCount,
        bool CadGaReady,
        CadProviderQualificationSelectionSlotV1? Primary,
        CadProviderQualificationSelectionSlotV1? Backup,
        IReadOnlyList<CadProviderQualificationCandidateResultV1> Candidates,
        IReadOnlyList<string> BlockingCodes);
}
