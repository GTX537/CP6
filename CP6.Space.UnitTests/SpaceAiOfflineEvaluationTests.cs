using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceAiOfflineEvaluationTests
{
    [Fact]
    public void Formal_release_requires_complete_evidence_and_out_of_sample_quality()
    {
        var report = new SpaceAiOfflineEvaluator().Evaluate(CreateRequest());

        Assert.True(report.Gate.EvaluationDataValid);
        Assert.True(report.Gate.FormalReleaseEvidenceComplete);
        Assert.True(report.Gate.QualityThresholdsMet);
        Assert.True(report.Gate.HighConfidenceShortcutEnabled);
        Assert.True(report.Gate.ReleaseEligible);
        Assert.Equal(0.90m, report.AppliedHighConfidenceThreshold);
        Assert.Equal(80, report.OverallMetrics.CorrectPredictionCount);
        Assert.Equal(1m, report.OverallMetrics.TargetCoverage);
        Assert.Equal(1m, report.OverallMetrics.OverallSemanticAccuracy);
        Assert.Equal(0.8m, report.OverallMetrics.ManualOperationReduction);
        Assert.True(report.OutOfSampleMetrics.HighConfidenceWilsonLowerBound >= 0.90m);
        Assert.Equal(64, report.ReportSha256.Length);
    }

    [Fact]
    public void Development_seed_can_be_measured_but_never_release()
    {
        var request = CreateRequest(development: true);

        var report = new SpaceAiOfflineEvaluator().Evaluate(request);

        Assert.True(report.Gate.EvaluationDataValid);
        Assert.False(report.Gate.FormalReleaseEvidenceComplete);
        Assert.False(report.Gate.ReleaseEligible);
        Assert.Equal(1m, report.OverallMetrics.TargetCoverage);
        Assert.Contains(
            "DATASET_DEVELOPMENT_CANNOT_RELEASE",
            report.Gate.IssueCodes);
        Assert.Contains("QUALITY_OUT_OF_SAMPLE_MISSING", report.Gate.IssueCodes);
    }

    [Fact]
    public void Duplicate_prediction_is_a_false_positive_not_a_second_match()
    {
        var request = CreateRequest(development: true);
        var expected = request.ExpectedTargets[0];
        request = request with
        {
            Predictions = request.Predictions.Append(
                new SpaceAiEvaluationPredictionV1(
                    expected.SampleId,
                    "duplicate-proposal",
                    expected.MatchKey,
                    WarehouseSpaceType.Rack,
                    expected.KeyAttributes,
                    [],
                    0.99m)).ToArray(),
        };

        var metrics = new SpaceAiOfflineEvaluator().Evaluate(request).OverallMetrics;

        Assert.Equal(80, metrics.CorrectPredictionCount);
        Assert.Equal(81, metrics.PredictionCount);
        Assert.Equal(1, metrics.FalsePositiveCount);
        Assert.Equal(0, metrics.FalseNegativeCount);
        Assert.Equal(0.987654m, metrics.OverallSemanticAccuracy);
    }

    [Fact]
    public void Wrong_type_or_key_attribute_is_not_semantically_correct()
    {
        var request = CreateRequest(development: true);
        var predictions = request.Predictions.ToArray();
        predictions[0] = predictions[0] with { ObjectType = WarehouseSpaceType.Rack };
        predictions[1] = predictions[1] with
        {
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["attributes.semanticLabel"] = "wrong",
            },
        };

        var metrics = new SpaceAiOfflineEvaluator().Evaluate(
            request with { Predictions = predictions }).OverallMetrics;

        Assert.Equal(78, metrics.CorrectPredictionCount);
        Assert.Equal(2, metrics.FalsePositiveCount);
        Assert.Equal(2, metrics.FalseNegativeCount);
        Assert.Equal(0.975m, metrics.TargetCoverage);
    }

    [Fact]
    public void Calibration_selects_lowest_threshold_that_meets_both_precision_gates()
    {
        var request = CreateRequest();
        var calibrationIds = request.Manifest.Samples
            .Where(item => item.Split == SpaceAiEvaluationSplit.Calibration)
            .Select(item => item.SampleId)
            .ToHashSet(StringComparer.Ordinal);
        var predictions = request.Predictions
            .Select(item => calibrationIds.Contains(item.SampleId)
                ? item with { Confidence = 0.96m }
                : item)
            .ToList();
        var firstCalibration = calibrationIds.Order().First();
        for (var index = 0; index < 5; index++)
        {
            predictions.Add(new SpaceAiEvaluationPredictionV1(
                firstCalibration,
                $"calibration-false-positive-{index}",
                $"unmatched-{index}",
                WarehouseSpaceType.Floor,
                new Dictionary<string, string>(),
                [],
                0.91m));
        }

        var report = new SpaceAiOfflineEvaluator().Evaluate(
            request with { Predictions = predictions });

        Assert.True(report.Calibration.HighConfidenceShortcutEnabled);
        Assert.Equal(0.96m, report.Calibration.SelectedThreshold);
        Assert.False(report.Gate.ReleaseEligible);
        Assert.Contains("QUALITY_OUT_OF_SAMPLE_MISSING", report.Gate.IssueCodes);
    }

    [Fact]
    public void Release_holdout_cannot_influence_threshold_calibration()
    {
        var request = CreateRequest();
        var evaluator = new SpaceAiOfflineEvaluator();
        var baseline = evaluator.Evaluate(request);
        var holdoutId = request.Manifest.Samples.First(item =>
            item.Split == SpaceAiEvaluationSplit.ReleaseHoldout).SampleId;
        var contaminated = request with
        {
            Predictions = request.Predictions.Append(
                new SpaceAiEvaluationPredictionV1(
                    holdoutId,
                    "holdout-false-positive",
                    "holdout-unmatched",
                    WarehouseSpaceType.Floor,
                    new Dictionary<string, string>(),
                    [],
                    0.99m)).ToArray(),
        };

        var changed = evaluator.Evaluate(contaminated);

        Assert.Equal(
            baseline.Calibration.SelectedThreshold,
            changed.Calibration.SelectedThreshold);
        Assert.Equal(
            baseline.Calibration.Candidates,
            changed.Calibration.Candidates);
        Assert.True(changed.OutOfSampleMetrics.HighConfidencePrecision
            < baseline.OutOfSampleMetrics.HighConfidencePrecision);
    }

    [Fact]
    public void Small_perfect_sample_closes_high_confidence_shortcut_by_wilson_bound()
    {
        var request = CreateRequest(targetsPerSample: 1);

        var report = new SpaceAiOfflineEvaluator().Evaluate(request);

        Assert.False(report.Calibration.HighConfidenceShortcutEnabled);
        Assert.Null(report.Calibration.SelectedThreshold);
        Assert.Equal("CALIBRATION_THRESHOLD_UNAVAILABLE", report.Calibration.DecisionCode);
        Assert.All(
            report.Calibration.Candidates,
            candidate => Assert.False(candidate.MeetsWilsonGate));
        Assert.False(report.Gate.ReleaseEligible);
    }

    [Fact]
    public void Missing_formal_evidence_fails_closed_even_with_perfect_metrics()
    {
        var request = CreateRequest();
        request = request with
        {
            Manifest = request.Manifest with
            {
                ApplicationCommitSha = null,
                AnnotationReviewEvidence = null,
                IsImmutable = false,
            },
        };

        var report = new SpaceAiOfflineEvaluator().Evaluate(request);

        Assert.True(report.Gate.EvaluationDataValid);
        Assert.False(report.Gate.FormalReleaseEvidenceComplete);
        Assert.False(report.Gate.ReleaseEligible);
        Assert.Contains("DATASET_VERSION_EVIDENCE_MISSING", report.Gate.IssueCodes);
        Assert.Contains("DATASET_ANNOTATION_REVIEW_MISSING", report.Gate.IssueCodes);
        Assert.Contains("DATASET_IMMUTABILITY_EVIDENCE_MISSING", report.Gate.IssueCodes);
    }

    [Fact]
    public void Report_hash_is_independent_of_input_order_and_tampering_is_rejected()
    {
        var request = CreateRequest();
        var evaluator = new SpaceAiOfflineEvaluator();
        var first = evaluator.Evaluate(request);
        var reordered = request with
        {
            ExpectedTargets = request.ExpectedTargets.Reverse().ToArray(),
            Predictions = request.Predictions.Reverse().ToArray(),
            Effort = request.Effort.Reverse().ToArray(),
        };

        var second = evaluator.Evaluate(reordered);

        Assert.Equal(first.ReportSha256, second.ReportSha256);
        Assert.NotEmpty(SpaceAiOfflineEvaluator.Serialize(first));
        Assert.Throws<InvalidDataException>(() =>
            SpaceAiOfflineEvaluator.Serialize(first with
            {
                AppliedHighConfidenceThreshold = 0.99m,
            }));
    }

    [Fact]
    public void Relation_target_must_exist_in_the_same_sample()
    {
        var request = CreateRequest(development: true);
        var expected = request.ExpectedTargets.ToArray();
        expected[0] = expected[0] with
        {
            Relations =
            [
                new SpaceAiEvaluationRelationV1(
                    WarehouseRelationType.ContainedBy,
                    "missing-source-key"),
            ],
        };

        var exception = Assert.Throws<InvalidDataException>(() =>
            new SpaceAiOfflineEvaluator().Evaluate(
                request with { ExpectedTargets = expected }));

        Assert.Contains("same sample", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Draft_adapter_preserves_source_keys_fields_and_relation_targets()
    {
        var floorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var rackId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var geometry = new SpaceCadSemanticGeometryV1(
            SpaceCadSemanticGeometryKind.Point,
            [new SpaceCadMillimeterPointV1(10, 20, 0)],
            null,
            null,
            null,
            false,
            null,
            new SpaceCadMillimeterBoundsV1(10, 20, 10, 20));
        var proposals = new[]
        {
            Proposal(
                floorId,
                "floor-source",
                WarehouseSpaceType.Floor,
                geometry,
                []),
            Proposal(
                rackId,
                "rack-source",
                WarehouseSpaceType.Rack,
                geometry,
                [new WarehouseProposalRelationV1(
                    WarehouseRelationType.ContainedBy,
                    floorId,
                    0.95m,
                    ["rule"])]),
        };

        var adapted = SpaceAiEvaluationProposalAdapter.FromDraftProposals(
            "L1-001",
            proposals);

        var rack = Assert.Single(adapted, item => item.MatchKey == "rack-source");
        Assert.Equal("target", rack.Attributes["attributes.semanticLabel"]);
        Assert.Equal("floor-source", Assert.Single(rack.Relations).TargetMatchKey);
    }

    private static SpaceAiOfflineEvaluationRequestV1 CreateRequest(
        bool development = false,
        int targetsPerSample = 4)
    {
        var samples = Enumerable.Range(0, 20)
            .Select(index => new SpaceAiEvaluationSampleV1(
                $"L{(index / 4) + 1}-{index + 1:D3}",
                $"L{(index / 4) + 1}-Family",
                development
                    ? SpaceAiEvaluationSplit.DevelopmentSeed
                    : index < 10
                        ? SpaceAiEvaluationSplit.Calibration
                        : index < 15
                            ? SpaceAiEvaluationSplit.Validation
                            : SpaceAiEvaluationSplit.ReleaseHoldout,
                $"seeds/{index + 1:D2}.dxf",
                string.Concat(Enumerable.Repeat($"{index + 1:x2}", 32)),
                targetsPerSample,
                development ? null : "Synthetic",
                development ? null : $"deidentification-{index + 1:D2}"))
            .ToArray();
        var expected = samples.SelectMany(sample =>
                Enumerable.Range(0, targetsPerSample).Select(target =>
                    new SpaceAiExpectedTargetV1(
                        sample.SampleId,
                        $"{sample.SampleId}-EXPECTED-{target + 1:D2}",
                        $"{sample.SampleId}-SOURCE-{target + 1:D2}",
                        WarehouseSpaceType.Floor,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["attributes.semanticLabel"] = "target",
                        },
                        [])))
            .ToArray();
        var predictions = expected.Select(item =>
                new SpaceAiEvaluationPredictionV1(
                    item.SampleId,
                    item.ExpectedId.Replace("EXPECTED", "PROPOSAL", StringComparison.Ordinal),
                    item.MatchKey,
                    item.ObjectType,
                    item.KeyAttributes,
                    item.Relations,
                    0.95m))
            .ToArray();
        var effort = samples.Select(sample => new SpaceAiEvaluationEffortV1(
                sample.SampleId,
                10,
                2))
            .ToArray();
        var manifest = new SpaceAiEvaluationManifestV1(
            SpaceAiOfflineEvaluationVersions.SchemaVersion,
            "1.0.0",
            development
                ? SpaceAiEvaluationDatasetPurpose.DevelopmentSeed
                : SpaceAiEvaluationDatasetPurpose.FormalRelease,
            !development,
            "Millimeter",
            "FloorLocal-ZUp",
            "space-cad-mapping-v1",
            "space-v1",
            "1.0.0",
            development ? "CP6-Synthetic-Development-Only" : "Synthetic",
            samples,
            new string('a', 40),
            "parser-v1",
            "provider-v1",
            "model-v1",
            "two-independent-annotators-and-qa-arbitration",
            "2026-08-08",
            true,
            new string('b', 64),
            true);
        return new SpaceAiOfflineEvaluationRequestV1(
            manifest,
            expected,
            predictions,
            effort);
    }

    private static WarehouseDraftProposalV1 Proposal(
        Guid logicalId,
        string sourceKey,
        WarehouseSpaceType objectType,
        SpaceCadSemanticGeometryV1 geometry,
        IReadOnlyList<WarehouseProposalRelationV1> relations) =>
        new(
            logicalId,
            sourceKey,
            $"source-ref:{sourceKey}",
            objectType,
            geometry,
            WarehouseProposalGeometrySource.CadIrDeterministicRule,
            WarehouseProposalCodeState.NotApplicable,
            [
                new WarehouseResolvedFieldV1(
                    "type",
                    objectType.ToString(),
                    WarehouseFusionSource.DeterministicRule,
                    0.95m,
                    []),
                new WarehouseResolvedFieldV1(
                    "attributes.semanticLabel",
                    "target",
                    WarehouseFusionSource.Ai,
                    0.95m,
                    []),
            ],
            relations,
            0.95m,
            WarehouseFusionConfidenceBand.High,
            false,
            true);
}
