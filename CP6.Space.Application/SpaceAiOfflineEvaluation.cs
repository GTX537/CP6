using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class SpaceAiOfflineEvaluationVersions
{
    public const int SchemaVersion = 1;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceAiEvaluationDatasetPurpose
{
    DevelopmentSeed = 0,
    FormalRelease = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceAiEvaluationSplit
{
    DevelopmentSeed = 0,
    Calibration = 1,
    Validation = 2,
    ReleaseHoldout = 3,
}

public sealed record SpaceAiEvaluationSampleV1(
    string SampleId,
    string LayoutFamily,
    SpaceAiEvaluationSplit Split,
    string SourceFile,
    string SourceSha256,
    int ExpectedTargetCount,
    string? License = null,
    string? DeidentificationEvidence = null);

public sealed record SpaceAiEvaluationManifestV1(
    int SchemaVersion,
    string DatasetVersion,
    SpaceAiEvaluationDatasetPurpose Purpose,
    bool CountsTowardReleaseGate,
    string Unit,
    string CoordinateSystem,
    string MappingProfileVersion,
    string RuleSetVersion,
    string ExpectedAnswerVersion,
    string License,
    IReadOnlyList<SpaceAiEvaluationSampleV1> Samples,
    string? ApplicationCommitSha = null,
    string? ParserVersion = null,
    string? ProviderVersion = null,
    string? ModelVersion = null,
    string? AnnotationReviewEvidence = null,
    string? AcceptanceDate = null,
    bool IsImmutable = false,
    string? IntegrityAuditSha256 = null,
    bool IntegrityAuditPassed = false);

public sealed record SpaceAiEvaluationRelationV1(
    WarehouseRelationType RelationType,
    string TargetMatchKey);

public sealed record SpaceAiExpectedTargetV1(
    string SampleId,
    string ExpectedId,
    string MatchKey,
    WarehouseSpaceType ObjectType,
    IReadOnlyDictionary<string, string> KeyAttributes,
    IReadOnlyList<SpaceAiEvaluationRelationV1> Relations);

public sealed record SpaceAiEvaluationPredictionV1(
    string SampleId,
    string ProposalId,
    string MatchKey,
    WarehouseSpaceType ObjectType,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<SpaceAiEvaluationRelationV1> Relations,
    decimal Confidence);

public sealed record SpaceAiEvaluationEffortV1(
    string SampleId,
    int ManualBaselineOperations,
    int AiAssistedOperations);

public sealed record SpaceAiOfflineEvaluationRequestV1(
    SpaceAiEvaluationManifestV1 Manifest,
    IReadOnlyList<SpaceAiExpectedTargetV1> ExpectedTargets,
    IReadOnlyList<SpaceAiEvaluationPredictionV1> Predictions,
    IReadOnlyList<SpaceAiEvaluationEffortV1> Effort);

public sealed record SpaceAiOfflineEvaluationThresholds(
    decimal TargetCoverage = 0.80m,
    decimal OverallSemanticAccuracy = 0.90m,
    decimal HighConfidencePrecision = 0.95m,
    decimal HighConfidenceWilsonLowerBound = 0.90m,
    decimal ManualOperationReduction = 0.70m,
    decimal DefaultHighConfidenceThreshold = 0.90m,
    decimal MinimumCalibrationThreshold = 0.50m)
{
    public SpaceAiOfflineEvaluationThresholds Validate()
    {
        if (TargetCoverage is < 0 or > 1
            || OverallSemanticAccuracy is < 0 or > 1
            || HighConfidencePrecision is < 0 or > 1
            || HighConfidenceWilsonLowerBound is < 0 or > 1
            || ManualOperationReduction is < 0 or > 1
            || DefaultHighConfidenceThreshold is < 0 or > 1
            || MinimumCalibrationThreshold is < 0 or > 1
            || MinimumCalibrationThreshold > DefaultHighConfidenceThreshold)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetCoverage));
        }

        return this;
    }
}

public sealed record SpaceAiEvaluationMetricsV1(
    int ExpectedTargetCount,
    int PredictionCount,
    int CorrectPredictionCount,
    int FalsePositiveCount,
    int FalseNegativeCount,
    decimal TargetCoverage,
    decimal OverallSemanticAccuracy,
    int HighConfidencePredictionCount,
    int CorrectHighConfidencePredictionCount,
    decimal HighConfidencePrecision,
    decimal HighConfidenceWilsonLowerBound,
    int ManualBaselineOperations,
    int AiAssistedOperations,
    decimal ManualOperationReduction);

public sealed record SpaceAiEvaluationSplitMetricsV1(
    SpaceAiEvaluationSplit Split,
    SpaceAiEvaluationMetricsV1 Metrics);

public sealed record SpaceAiThresholdCandidateV1(
    decimal Threshold,
    int PredictionCount,
    int CorrectPredictionCount,
    decimal Precision,
    decimal WilsonLowerBound,
    bool MeetsPrecisionGate,
    bool MeetsWilsonGate);

public sealed record SpaceAiThresholdCalibrationV1(
    SpaceAiEvaluationSplit SourceSplit,
    decimal? SelectedThreshold,
    bool HighConfidenceShortcutEnabled,
    string DecisionCode,
    IReadOnlyList<SpaceAiThresholdCandidateV1> Candidates);

public sealed record SpaceAiEvaluationGateV1(
    bool EvaluationDataValid,
    bool FormalReleaseEvidenceComplete,
    bool QualityThresholdsMet,
    bool HighConfidenceShortcutEnabled,
    bool ReleaseEligible,
    IReadOnlyList<string> IssueCodes);

public sealed record SpaceAiOfflineEvaluationReportV1(
    int SchemaVersion,
    string DatasetVersion,
    SpaceAiEvaluationDatasetPurpose DatasetPurpose,
    decimal AppliedHighConfidenceThreshold,
    SpaceAiEvaluationMetricsV1 OverallMetrics,
    SpaceAiEvaluationMetricsV1 OutOfSampleMetrics,
    IReadOnlyList<SpaceAiEvaluationSplitMetricsV1> SplitMetrics,
    SpaceAiThresholdCalibrationV1 Calibration,
    SpaceAiEvaluationGateV1 Gate,
    string ReportSha256);

public static class SpaceAiEvaluationProposalAdapter
{
    public static IReadOnlyList<SpaceAiEvaluationPredictionV1> FromDraftProposals(
        string sampleId,
        IReadOnlyList<WarehouseDraftProposalV1> proposals)
    {
        RequireToken(sampleId, nameof(sampleId));
        ArgumentNullException.ThrowIfNull(proposals);
        var matchKeyByLogicalId = proposals.ToDictionary(
            item => item.LogicalId,
            item => item.SourceKey);

        return proposals
            .Select(proposal => new SpaceAiEvaluationPredictionV1(
                sampleId,
                proposal.LogicalId.ToString("D"),
                proposal.SourceKey,
                proposal.ObjectType,
                proposal.Fields
                    .Where(item => !item.FieldPath.Equals(
                        "type",
                        StringComparison.Ordinal))
                    .ToDictionary(
                        item => item.FieldPath,
                        item => item.ValueToken,
                        StringComparer.Ordinal),
                proposal.Relations
                    .Select(relation => new SpaceAiEvaluationRelationV1(
                        relation.RelationType,
                        matchKeyByLogicalId.TryGetValue(
                            relation.TargetLogicalId,
                            out var targetMatchKey)
                                ? targetMatchKey
                                : throw new InvalidDataException(
                                    "A proposal relation target is absent from the proposal set.")))
                    .OrderBy(item => item.RelationType)
                    .ThenBy(item => item.TargetMatchKey, StringComparer.Ordinal)
                    .ToArray(),
                proposal.Confidence))
            .OrderBy(item => item.ProposalId, StringComparer.Ordinal)
            .ToArray();
    }

    private static void RequireToken(string? value, string parameterName)
    {
        if (value is not { Length: > 0 and <= 256 }
            || !value.Equals(value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("A non-empty canonical token is required.", parameterName);
        }
    }
}

public sealed class SpaceAiOfflineEvaluator
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly SpaceAiOfflineEvaluationThresholds _thresholds;

    public SpaceAiOfflineEvaluator()
        : this(new SpaceAiOfflineEvaluationThresholds())
    {
    }

    public SpaceAiOfflineEvaluator(SpaceAiOfflineEvaluationThresholds thresholds)
    {
        _thresholds = (thresholds
            ?? throw new ArgumentNullException(nameof(thresholds))).Validate();
    }

    public SpaceAiOfflineEvaluationReportV1 Evaluate(
        SpaceAiOfflineEvaluationRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = ValidateAndBind(request);
        var matchedCorrectProposalIds = MatchCorrectPredictions(context);
        var calibration = Calibrate(context, matchedCorrectProposalIds);
        var appliedThreshold = calibration.SelectedThreshold
            ?? _thresholds.DefaultHighConfidenceThreshold;
        var overall = Metrics(
            context,
            matchedCorrectProposalIds,
            appliedThreshold,
            Enum.GetValues<SpaceAiEvaluationSplit>());
        var outOfSample = Metrics(
            context,
            matchedCorrectProposalIds,
            appliedThreshold,
            [
                SpaceAiEvaluationSplit.Validation,
                SpaceAiEvaluationSplit.ReleaseHoldout,
            ]);
        var splitMetrics = Enum.GetValues<SpaceAiEvaluationSplit>()
            .Where(split => context.SamplesById.Values.Any(
                sample => sample.Split == split))
            .Select(split => new SpaceAiEvaluationSplitMetricsV1(
                split,
                Metrics(
                    context,
                    matchedCorrectProposalIds,
                    appliedThreshold,
                    [split])))
            .ToArray();

        var gate = BuildGate(context, calibration, overall, outOfSample);
        var unsigned = new SpaceAiOfflineEvaluationReportV1(
            SpaceAiOfflineEvaluationVersions.SchemaVersion,
            context.Request.Manifest.DatasetVersion,
            context.Request.Manifest.Purpose,
            appliedThreshold,
            overall,
            outOfSample,
            splitMetrics,
            calibration,
            gate,
            string.Empty);
        return unsigned with { ReportSha256 = ComputeHash(unsigned) };
    }

    public static string Serialize(SpaceAiOfflineEvaluationReportV1 report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!IsSha256(report.ReportSha256)
            || !ComputeHash(report with { ReportSha256 = string.Empty })
                .Equals(report.ReportSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The offline evaluation report hash is invalid.");
        }

        return JsonSerializer.Serialize(report, CanonicalJsonOptions);
    }

    public static decimal WilsonLowerBound(int successes, int total)
    {
        if (successes < 0 || total < 0 || successes > total)
            throw new ArgumentOutOfRangeException(nameof(successes));
        if (total == 0)
            return 0;

        const double z = 1.959963984540054;
        var n = (double)total;
        var p = successes / n;
        var zSquared = z * z;
        var numerator = p + (zSquared / (2 * n))
            - (z * Math.Sqrt(((p * (1 - p)) / n)
                + (zSquared / (4 * n * n))));
        var denominator = 1 + (zSquared / n);
        return Round((decimal)(numerator / denominator));
    }

    private EvaluationContext ValidateAndBind(
        SpaceAiOfflineEvaluationRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(request.Manifest);
        ArgumentNullException.ThrowIfNull(request.Manifest.Samples);
        ArgumentNullException.ThrowIfNull(request.ExpectedTargets);
        ArgumentNullException.ThrowIfNull(request.Predictions);
        ArgumentNullException.ThrowIfNull(request.Effort);
        if (request.Manifest.SchemaVersion != SpaceAiOfflineEvaluationVersions.SchemaVersion
            || !Enum.IsDefined(request.Manifest.Purpose))
        {
            throw new InvalidDataException("The evaluation manifest schema is unsupported.");
        }

        RequireToken(request.Manifest.DatasetVersion, "datasetVersion");
        var samplesById = UniqueBy(
            request.Manifest.Samples,
            item => item.SampleId,
            "Dataset sample IDs must be unique.");
        foreach (var sample in request.Manifest.Samples)
        {
            RequireToken(sample.SampleId, "sampleId");
            RequireToken(sample.LayoutFamily, "layoutFamily");
            if (!Enum.IsDefined(sample.Split) || sample.ExpectedTargetCount < 0)
                throw new InvalidDataException("A dataset sample is invalid.");
        }

        var expectedById = UniqueBy(
            request.ExpectedTargets,
            item => item.ExpectedId,
            "Expected target IDs must be unique.");
        var expectedMatchKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expected in request.ExpectedTargets)
        {
            ValidateTarget(
                expected.SampleId,
                expected.ExpectedId,
                expected.MatchKey,
                expected.ObjectType,
                expected.KeyAttributes,
                expected.Relations,
                samplesById);
            if (!expectedMatchKeys.Add($"{expected.SampleId}\n{expected.MatchKey}"))
            {
                throw new InvalidDataException(
                    "Expected match keys must be unique within each sample.");
            }
        }
        if (request.ExpectedTargets.Any(expected => expected.Relations.Any(relation =>
                relation.TargetMatchKey.Equals(expected.MatchKey, StringComparison.Ordinal)
                || !expectedMatchKeys.Contains(
                    $"{expected.SampleId}\n{relation.TargetMatchKey}"))))
        {
            throw new InvalidDataException(
                "Expected relations must reference another target in the same sample.");
        }

        _ = UniqueBy(
            request.Predictions,
            item => item.ProposalId,
            "Prediction proposal IDs must be unique.");
        var predictionMatchKeys = request.Predictions
            .Select(item => $"{item.SampleId}\n{item.MatchKey}")
            .ToHashSet(StringComparer.Ordinal);
        foreach (var prediction in request.Predictions)
        {
            ValidateTarget(
                prediction.SampleId,
                prediction.ProposalId,
                prediction.MatchKey,
                prediction.ObjectType,
                prediction.Attributes,
                prediction.Relations,
                samplesById);
            if (prediction.Confidence is < 0 or > 1)
                throw new InvalidDataException("Prediction confidence must be between zero and one.");
        }
        if (request.Predictions.Any(prediction => prediction.Relations.Any(relation =>
                relation.TargetMatchKey.Equals(prediction.MatchKey, StringComparison.Ordinal)
                || !predictionMatchKeys.Contains(
                    $"{prediction.SampleId}\n{relation.TargetMatchKey}"))))
        {
            throw new InvalidDataException(
                "Prediction relations must reference another proposal in the same sample.");
        }

        var effortBySample = UniqueBy(
            request.Effort,
            item => item.SampleId,
            "Effort rows must be unique by sample.");
        foreach (var effort in request.Effort)
        {
            if (!samplesById.ContainsKey(effort.SampleId)
                || effort.ManualBaselineOperations <= 0
                || effort.AiAssistedOperations < 0)
            {
                throw new InvalidDataException("An evaluation effort row is invalid.");
            }
        }

        var structuralIssues = DatasetStructuralIssues(
            request,
            samplesById,
            expectedById,
            effortBySample);
        var releaseEvidenceIssues = ReleaseEvidenceIssues(request.Manifest);
        return new EvaluationContext(
            request,
            samplesById,
            structuralIssues,
            releaseEvidenceIssues);
    }

    private static void ValidateTarget(
        string sampleId,
        string identity,
        string matchKey,
        WarehouseSpaceType objectType,
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyList<SpaceAiEvaluationRelationV1> relations,
        IReadOnlyDictionary<string, SpaceAiEvaluationSampleV1> samplesById)
    {
        RequireToken(sampleId, "sampleId");
        RequireToken(identity, "identity");
        RequireToken(matchKey, "matchKey");
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(relations);
        if (!samplesById.ContainsKey(sampleId) || !Enum.IsDefined(objectType))
            throw new InvalidDataException("An evaluation target references invalid data.");
        foreach (var (key, value) in attributes)
        {
            RequireToken(key, "attributeKey");
            RequireToken(value, "attributeValue");
        }

        var relationKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relation in relations)
        {
            if (relation is null || !Enum.IsDefined(relation.RelationType))
                throw new InvalidDataException("An evaluation relation is invalid.");
            RequireToken(relation.TargetMatchKey, "targetMatchKey");
            if (!relationKeys.Add($"{relation.RelationType}\n{relation.TargetMatchKey}"))
                throw new InvalidDataException("Evaluation relations must be unique.");
        }
    }

    private static IReadOnlyList<string> DatasetStructuralIssues(
        SpaceAiOfflineEvaluationRequestV1 request,
        IReadOnlyDictionary<string, SpaceAiEvaluationSampleV1> samplesById,
        IReadOnlyDictionary<string, SpaceAiExpectedTargetV1> expectedById,
        IReadOnlyDictionary<string, SpaceAiEvaluationEffortV1> effortBySample)
    {
        var issues = new List<string>();
        if (samplesById.Count != 20)
            issues.Add("DATASET_SAMPLE_COUNT_INVALID");
        var families = samplesById.Values
            .GroupBy(item => FamilyCode(item.LayoutFamily), StringComparer.Ordinal)
            .ToArray();
        var requiredFamilies = new[] { "L1", "L2", "L3", "L4", "L5" };
        if (families.Length != requiredFamilies.Length
            || requiredFamilies.Any(required =>
                families.SingleOrDefault(group => group.Key == required)?.Count() < 4))
            issues.Add("DATASET_LAYOUT_FAMILY_COVERAGE_INVALID");
        if (!request.Manifest.Unit.Equals("Millimeter", StringComparison.Ordinal)
            || !request.Manifest.CoordinateSystem.Equals(
                "FloorLocal-ZUp",
                StringComparison.Ordinal))
        {
            issues.Add("DATASET_COORDINATE_CONTRACT_INVALID");
        }
        if (samplesById.Values.Any(sample => !IsSha256(sample.SourceSha256)))
            issues.Add("DATASET_SOURCE_HASH_INVALID");
        if (samplesById.Values.Any(sample =>
                string.IsNullOrWhiteSpace(sample.SourceFile)
                || (!Path.GetExtension(sample.SourceFile).Equals(
                    ".dwg",
                    StringComparison.OrdinalIgnoreCase)
                && !Path.GetExtension(sample.SourceFile).Equals(
                    ".dxf",
                    StringComparison.OrdinalIgnoreCase))))
        {
            issues.Add("DATASET_SOURCE_FORMAT_INVALID");
        }
        if (samplesById.Values.Select(sample => sample.SourceSha256)
                .Distinct(StringComparer.Ordinal).Count() != samplesById.Count
            || samplesById.Values.Select(sample => sample.SourceFile)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != samplesById.Count)
        {
            issues.Add("DATASET_SOURCE_DUPLICATE");
        }
        if (samplesById.Values.Any(sample =>
                request.ExpectedTargets.Count(target =>
                    target.SampleId.Equals(sample.SampleId, StringComparison.Ordinal))
                != sample.ExpectedTargetCount)
            || expectedById.Count != samplesById.Values.Sum(
                sample => sample.ExpectedTargetCount))
        {
            issues.Add("DATASET_EXPECTED_COUNT_MISMATCH");
        }
        if (effortBySample.Count != samplesById.Count)
            issues.Add("DATASET_EFFORT_COVERAGE_INCOMPLETE");
        return issues.Distinct(StringComparer.Ordinal).Order().ToArray();
    }

    private static IReadOnlyList<string> ReleaseEvidenceIssues(
        SpaceAiEvaluationManifestV1 manifest)
    {
        var issues = new List<string>();
        if (manifest.Purpose == SpaceAiEvaluationDatasetPurpose.DevelopmentSeed)
        {
            issues.Add("DATASET_DEVELOPMENT_CANNOT_RELEASE");
            if (manifest.CountsTowardReleaseGate
                || manifest.Samples.Any(sample =>
                    sample.Split != SpaceAiEvaluationSplit.DevelopmentSeed))
            {
                issues.Add("DATASET_PURPOSE_FLAG_CONFLICT");
            }
            return issues.Order().ToArray();
        }

        if (!manifest.CountsTowardReleaseGate)
            issues.Add("DATASET_RELEASE_FLAG_INVALID");
        if (manifest.Samples.Count(sample =>
                sample.Split == SpaceAiEvaluationSplit.Calibration) != 10
            || manifest.Samples.Count(sample =>
                sample.Split == SpaceAiEvaluationSplit.Validation) != 5
            || manifest.Samples.Count(sample =>
                sample.Split == SpaceAiEvaluationSplit.ReleaseHoldout) != 5)
        {
            issues.Add("DATASET_RELEASE_SPLIT_INVALID");
        }
        if (manifest.Samples.Any(sample =>
                string.IsNullOrWhiteSpace(sample.License)))
            issues.Add("DATASET_SAMPLE_LICENSE_MISSING");
        if (IsBlank(manifest.License))
            issues.Add("DATASET_PACKAGE_LICENSE_MISSING");
        if (manifest.Samples.Any(sample =>
                string.IsNullOrWhiteSpace(sample.DeidentificationEvidence)))
            issues.Add("DATASET_DEIDENTIFICATION_EVIDENCE_MISSING");
        if (!IsCommitSha(manifest.ApplicationCommitSha)
            || IsBlank(manifest.ParserVersion)
            || IsBlank(manifest.ProviderVersion)
            || IsBlank(manifest.ModelVersion)
            || IsBlank(manifest.MappingProfileVersion)
            || IsBlank(manifest.RuleSetVersion)
            || IsBlank(manifest.ExpectedAnswerVersion))
        {
            issues.Add("DATASET_VERSION_EVIDENCE_MISSING");
        }
        if (IsBlank(manifest.AnnotationReviewEvidence))
            issues.Add("DATASET_ANNOTATION_REVIEW_MISSING");
        if (!DateOnly.TryParseExact(
                manifest.AcceptanceDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
            issues.Add("DATASET_ACCEPTANCE_DATE_MISSING");
        if (!manifest.IsImmutable)
            issues.Add("DATASET_IMMUTABILITY_EVIDENCE_MISSING");
        if (!manifest.IntegrityAuditPassed
            || !IsSha256(manifest.IntegrityAuditSha256))
        {
            issues.Add("DATASET_INTEGRITY_AUDIT_MISSING");
        }
        return issues.Distinct(StringComparer.Ordinal).Order().ToArray();
    }

    private static HashSet<string> MatchCorrectPredictions(EvaluationContext context)
    {
        var correct = new HashSet<string>(StringComparer.Ordinal);
        var predictionsByMatchKey = context.Request.Predictions
            .GroupBy(
                item => $"{item.SampleId}\n{item.MatchKey}",
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var expected in context.Request.ExpectedTargets)
        {
            if (!predictionsByMatchKey.TryGetValue(
                    $"{expected.SampleId}\n{expected.MatchKey}",
                    out var candidates))
            {
                continue;
            }
            var match = candidates
                .Where(candidate => IsCorrect(expected, candidate))
                .OrderByDescending(candidate => candidate.Confidence)
                .ThenBy(candidate => candidate.ProposalId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (match is not null)
                correct.Add(match.ProposalId);
        }
        return correct;
    }

    private static bool IsCorrect(
        SpaceAiExpectedTargetV1 expected,
        SpaceAiEvaluationPredictionV1 prediction)
    {
        if (expected.ObjectType != prediction.ObjectType
            || expected.KeyAttributes.Any(pair =>
                !prediction.Attributes.TryGetValue(pair.Key, out var actual)
                || !actual.Equals(pair.Value, StringComparison.Ordinal)))
        {
            return false;
        }

        var expectedRelations = expected.Relations
            .Select(RelationIdentity)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var predictedRelations = prediction.Relations
            .Select(RelationIdentity)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return expectedRelations.SequenceEqual(predictedRelations, StringComparer.Ordinal);
    }

    private SpaceAiThresholdCalibrationV1 Calibrate(
        EvaluationContext context,
        IReadOnlySet<string> correctProposalIds)
    {
        var calibrationSampleIds = context.SamplesById.Values
            .Where(sample => sample.Split == SpaceAiEvaluationSplit.Calibration)
            .Select(sample => sample.SampleId)
            .ToHashSet(StringComparer.Ordinal);
        var predictions = context.Request.Predictions
            .Where(item => calibrationSampleIds.Contains(item.SampleId))
            .ToArray();
        var thresholds = predictions
            .Select(item => item.Confidence)
            .Append(_thresholds.DefaultHighConfidenceThreshold)
            .Where(value => value >= _thresholds.MinimumCalibrationThreshold)
            .Distinct()
            .Order()
            .ToArray();
        var candidates = thresholds.Select(threshold =>
        {
            var selected = predictions.Where(item => item.Confidence >= threshold).ToArray();
            var selectedCorrect = selected.Count(item =>
                correctProposalIds.Contains(item.ProposalId));
            var precision = Ratio(selectedCorrect, selected.Length);
            var wilson = WilsonLowerBound(selectedCorrect, selected.Length);
            return new SpaceAiThresholdCandidateV1(
                threshold,
                selected.Length,
                selectedCorrect,
                precision,
                wilson,
                selected.Length > 0
                    && precision >= _thresholds.HighConfidencePrecision,
                selected.Length > 0
                    && wilson >= _thresholds.HighConfidenceWilsonLowerBound);
        }).ToArray();
        var selected = candidates.FirstOrDefault(candidate =>
            candidate.MeetsPrecisionGate && candidate.MeetsWilsonGate);
        return selected is null
            ? new SpaceAiThresholdCalibrationV1(
                SpaceAiEvaluationSplit.Calibration,
                null,
                false,
                predictions.Length == 0
                    ? "CALIBRATION_SPLIT_EMPTY"
                    : "CALIBRATION_THRESHOLD_UNAVAILABLE",
                candidates)
            : new SpaceAiThresholdCalibrationV1(
                SpaceAiEvaluationSplit.Calibration,
                selected.Threshold,
                true,
                "CALIBRATION_THRESHOLD_SELECTED",
                candidates);
    }

    private static SpaceAiEvaluationMetricsV1 Metrics(
        EvaluationContext context,
        IReadOnlySet<string> correctProposalIds,
        decimal highConfidenceThreshold,
        IReadOnlyCollection<SpaceAiEvaluationSplit> splits)
    {
        var selectedSplits = splits.ToHashSet();
        var sampleIds = context.SamplesById.Values
            .Where(sample => selectedSplits.Contains(sample.Split))
            .Select(sample => sample.SampleId)
            .ToHashSet(StringComparer.Ordinal);
        var expectedCount = context.Request.ExpectedTargets.Count(item =>
            sampleIds.Contains(item.SampleId));
        var predictions = context.Request.Predictions
            .Where(item => sampleIds.Contains(item.SampleId))
            .ToArray();
        var correctCount = predictions.Count(item =>
            correctProposalIds.Contains(item.ProposalId));
        var highConfidence = predictions
            .Where(item => item.Confidence >= highConfidenceThreshold)
            .ToArray();
        var correctHighConfidence = highConfidence.Count(item =>
            correctProposalIds.Contains(item.ProposalId));
        var effort = context.Request.Effort
            .Where(item => sampleIds.Contains(item.SampleId))
            .ToArray();
        var baselineOperations = effort.Sum(item => item.ManualBaselineOperations);
        var aiOperations = effort.Sum(item => item.AiAssistedOperations);
        return new SpaceAiEvaluationMetricsV1(
            expectedCount,
            predictions.Length,
            correctCount,
            predictions.Length - correctCount,
            expectedCount - correctCount,
            Ratio(correctCount, expectedCount),
            Ratio(correctCount, predictions.Length),
            highConfidence.Length,
            correctHighConfidence,
            Ratio(correctHighConfidence, highConfidence.Length),
            WilsonLowerBound(correctHighConfidence, highConfidence.Length),
            baselineOperations,
            aiOperations,
            baselineOperations == 0
                ? 0
                : Round(1m - ((decimal)aiOperations / baselineOperations)));
    }

    private SpaceAiEvaluationGateV1 BuildGate(
        EvaluationContext context,
        SpaceAiThresholdCalibrationV1 calibration,
        SpaceAiEvaluationMetricsV1 overall,
        SpaceAiEvaluationMetricsV1 outOfSample)
    {
        var issues = context.StructuralIssues
            .Concat(context.ReleaseEvidenceIssues)
            .ToList();
        if (!calibration.HighConfidenceShortcutEnabled)
            issues.Add(calibration.DecisionCode);
        if (overall.TargetCoverage < _thresholds.TargetCoverage)
            issues.Add("QUALITY_COVERAGE_BELOW_THRESHOLD");
        if (overall.OverallSemanticAccuracy < _thresholds.OverallSemanticAccuracy)
            issues.Add("QUALITY_SEMANTIC_ACCURACY_BELOW_THRESHOLD");
        if (overall.ManualOperationReduction < _thresholds.ManualOperationReduction)
            issues.Add("QUALITY_MANUAL_REDUCTION_BELOW_THRESHOLD");
        if (outOfSample.ExpectedTargetCount == 0
            || outOfSample.HighConfidencePredictionCount == 0)
        {
            issues.Add("QUALITY_OUT_OF_SAMPLE_MISSING");
        }
        else
        {
            if (outOfSample.HighConfidencePrecision
                < _thresholds.HighConfidencePrecision)
                issues.Add("QUALITY_HIGH_CONFIDENCE_PRECISION_BELOW_THRESHOLD");
            if (outOfSample.HighConfidenceWilsonLowerBound
                < _thresholds.HighConfidenceWilsonLowerBound)
                issues.Add("QUALITY_HIGH_CONFIDENCE_WILSON_BELOW_THRESHOLD");
        }

        var structuralValid = context.StructuralIssues.Count == 0;
        var evidenceComplete = context.Request.Manifest.Purpose
            == SpaceAiEvaluationDatasetPurpose.FormalRelease
            && context.ReleaseEvidenceIssues.Count == 0;
        var qualityMet = !issues.Any(issue => issue.StartsWith(
            "QUALITY_",
            StringComparison.Ordinal))
            && calibration.HighConfidenceShortcutEnabled;
        var releaseEligible = structuralValid && evidenceComplete && qualityMet;
        return new SpaceAiEvaluationGateV1(
            structuralValid,
            evidenceComplete,
            qualityMet,
            releaseEligible,
            releaseEligible,
            issues.Distinct(StringComparer.Ordinal).Order().ToArray());
    }

    private static string ComputeHash(SpaceAiOfflineEvaluationReportV1 report)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, CanonicalJsonOptions);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static Dictionary<string, T> UniqueBy<T>(
        IReadOnlyList<T> values,
        Func<T, string> keySelector,
        string error)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null || !result.TryAdd(keySelector(value), value))
                throw new InvalidDataException(error);
        }
        return result;
    }

    private static string RelationIdentity(SpaceAiEvaluationRelationV1 relation) =>
        $"{relation.RelationType}\n{relation.TargetMatchKey}";

    private static string FamilyCode(string layoutFamily)
    {
        var separator = layoutFamily.IndexOf('-');
        return separator > 0 ? layoutFamily[..separator] : layoutFamily;
    }

    private static decimal Ratio(int numerator, int denominator) =>
        denominator == 0 ? 0 : Round((decimal)numerator / denominator);

    private static decimal Round(decimal value) =>
        decimal.Round(value, 6, MidpointRounding.AwayFromZero);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(IsLowerHex);

    private static bool IsCommitSha(string? value) =>
        value is { Length: 40 or 64 } && value.All(IsLowerHex);

    private static bool IsLowerHex(char character) =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    private static void RequireToken(string? value, string field)
    {
        if (value is not { Length: > 0 and <= 256 }
            || !value.Equals(value.Trim(), StringComparison.Ordinal)
            || value.Any(character => character < ' ' || character == '\u007f'))
        {
            throw new InvalidDataException($"Evaluation field '{field}' is invalid.");
        }
    }

    private sealed record EvaluationContext(
        SpaceAiOfflineEvaluationRequestV1 Request,
        IReadOnlyDictionary<string, SpaceAiEvaluationSampleV1> SamplesById,
        IReadOnlyList<string> StructuralIssues,
        IReadOnlyList<string> ReleaseEvidenceIssues);
}
