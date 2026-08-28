using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.CadExperiment;

public sealed record GoldenCadBusinessRuleV1(
    string Layer,
    WarehouseSpaceType TargetType,
    SpaceCadIrEntityType RequiredEntityType,
    decimal Confidence,
    decimal? MinimumDiameterMillimeters,
    bool KeepLargestAtSameCenter);

public sealed record GoldenCadBusinessRuleSetV1(
    int SchemaVersion,
    string ParserVersion,
    string MappingProfileVersion,
    string RuleSetVersion,
    string ModelVersion,
    decimal DoorThicknessMillimeters,
    string OperationModel,
    IReadOnlyList<GoldenCadBusinessRuleV1> Rules);

public sealed record GoldenCadBusinessSampleResultV1(
    string SampleId,
    string Split,
    int ExpectedTargetCount,
    int PredictionCount,
    int CorrectPredictionCount,
    int FalsePositiveCount,
    int FalseNegativeCount,
    int ExpectedBlockingIssueCount,
    int UnreportedBlockingOmissionCount);

public sealed record GoldenCadBusinessEvaluationResultV1(
    SpaceAiOfflineEvaluationRequestV1 Request,
    SpaceAiOfflineEvaluationReportV1 Report,
    string DatasetManifestSha256,
    string GoldenDatasetSha256,
    string SourceSetSha256,
    string RulesSha256,
    string CadIrSetSha256,
    int HoldoutUnreportedBlockingOmissions,
    IReadOnlyList<GoldenCadBusinessSampleResultV1> Samples);

public static class GoldenCadBusinessEvaluator
{
    public const int SchemaVersion = 1;
    public const string EvidenceClass = "CP6_SPACE_GOLDEN_CAD_BUSINESS_EVALUATION";
    private const int ExpectedSampleCount = 20;
    private const int MaximumInputBytes = 64 * 1024 * 1024;

    public static async Task<GoldenCadBusinessEvaluationResultV1> EvaluateAsync(
        string datasetRoot,
        string cadIrRoot,
        string rulesPath,
        string applicationCommitSha,
        string providerVersion,
        DateOnly acceptanceDate,
        CancellationToken cancellationToken = default)
    {
        var datasetDirectory = RequireDirectory(datasetRoot, "Golden dataset root");
        var cadIrDirectory = RequireDirectory(cadIrRoot, "CAD IR root");
        var manifestPath = RequireContainedFile(
            datasetDirectory,
            Path.Combine(datasetDirectory, "controlled-manifest.json"),
            "Golden dataset manifest");
        var manifestBytes = await ReadBoundedBytesAsync(
            manifestPath,
            MaximumInputBytes,
            cancellationToken);
        using var manifestDocument = JsonDocument.Parse(manifestBytes);
        var manifest = ParseManifest(manifestDocument.RootElement);
        ValidateManifest(manifest);

        var normalizedCommit = NormalizeCommit(applicationCommitSha);
        var normalizedProviderVersion = RequiredToken(
            providerVersion,
            nameof(providerVersion));
        var rulesFile = Path.GetFullPath(rulesPath);
        var rulesBytes = await ReadBoundedBytesAsync(
            rulesFile,
            MaximumInputBytes,
            cancellationToken);
        var rules = JsonSerializer.Deserialize<GoldenCadBusinessRuleSetV1>(
                        rulesBytes,
                        CadExperimentJson.Options)
                    ?? throw new InvalidDataException(
                        "The golden CAD business rule set is empty.");
        ValidateRules(rules, manifest);

        var expectedTargets = new List<SpaceAiExpectedTargetV1>();
        var predictions = new List<SpaceAiEvaluationPredictionV1>();
        var efforts = new List<SpaceAiEvaluationEffortV1>();
        var evaluationSamples = new List<SpaceAiEvaluationSampleV1>();
        var sampleResults = new List<GoldenCadBusinessSampleResultV1>();
        var cadIrIdentities = new List<string>();

        foreach (var sample in manifest.Samples.OrderBy(
                     item => item.SampleId,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sampleRoot = Path.Combine(
                datasetDirectory,
                "samples",
                sample.SampleId);
            var expectedPath = RequireContainedFile(
                datasetDirectory,
                Path.Combine(sampleRoot, "expected-elements.jsonl"),
                $"Expected elements for {sample.SampleId}");
            var expectedIssuesPath = RequireContainedFile(
                datasetDirectory,
                Path.Combine(sampleRoot, "expected-issues.json"),
                $"Expected issues for {sample.SampleId}");
            var metadataPath = RequireContainedFile(
                datasetDirectory,
                Path.Combine(sampleRoot, "metadata.json"),
                $"Metadata for {sample.SampleId}");
            var mappingProfilePath = RequireContainedFile(
                datasetDirectory,
                Path.Combine(sampleRoot, "mapping-profile.json"),
                $"Mapping profile for {sample.SampleId}");
            await VerifyArtifactAsync(
                expectedPath,
                sample.ExpectedElementsSha256,
                cancellationToken);
            await VerifyArtifactAsync(
                expectedIssuesPath,
                sample.ExpectedIssuesSha256,
                cancellationToken);
            await VerifyArtifactAsync(
                metadataPath,
                sample.MetadataSha256,
                cancellationToken);
            await VerifyArtifactAsync(
                mappingProfilePath,
                sample.MappingProfileSha256,
                cancellationToken);
            var floorResolution = await ReadFloorResolutionAsync(
                mappingProfilePath,
                cancellationToken);

            var expected = await ReadExpectedAsync(
                expectedPath,
                sample,
                cancellationToken);
            var expectedBlockingIssues = await CountExpectedBlockingIssuesAsync(
                expectedIssuesPath,
                cancellationToken);
            var irPath = RequireContainedFile(
                cadIrDirectory,
                Path.Combine(cadIrDirectory, sample.SampleId + ".json"),
                $"CAD IR for {sample.SampleId}");
            var irBytes = await ReadBoundedBytesAsync(
                irPath,
                MaximumInputBytes,
                cancellationToken);
            var irSha256 = Sha256(irBytes);
            cadIrIdentities.Add(sample.SampleId + ":" + irSha256);
            var package = JsonSerializer.Deserialize<SpaceCadIrPackageV1>(
                              irBytes,
                              CadExperimentJson.Options)
                          ?? throw new InvalidDataException(
                              $"CAD IR for {sample.SampleId} is empty.");
            ValidatePackage(sample, package);

            var sampleExpectedTargets = BuildExpectedTargets(sample, expected);
            var samplePredictions = BuildPredictions(
                sample,
                package,
                rules,
                expected,
                floorResolution);
            var correct = CountCorrect(sampleExpectedTargets, samplePredictions);
            var falsePositives = samplePredictions.Count - correct;
            var falseNegatives = sampleExpectedTargets.Count - correct;
            var assistedOperations = checked(1 + falsePositives + falseNegatives);

            expectedTargets.AddRange(sampleExpectedTargets);
            predictions.AddRange(samplePredictions);
            efforts.Add(new SpaceAiEvaluationEffortV1(
                sample.SampleId,
                Math.Max(1, sampleExpectedTargets.Count),
                assistedOperations));
            evaluationSamples.Add(new SpaceAiEvaluationSampleV1(
                sample.SampleId,
                sample.LayoutFamily,
                ParseSplit(sample.Split),
                sample.SampleId + "/source." + sample.SourceFormat.ToLowerInvariant(),
                sample.SourceSha256,
                sampleExpectedTargets.Count,
                sample.License,
                sample.DeidentificationEvidence));
            sampleResults.Add(new GoldenCadBusinessSampleResultV1(
                sample.SampleId,
                sample.Split,
                sampleExpectedTargets.Count,
                samplePredictions.Count,
                correct,
                falsePositives,
                falseNegatives,
                expectedBlockingIssues,
                expectedBlockingIssues));
        }

        var request = new SpaceAiOfflineEvaluationRequestV1(
            new SpaceAiEvaluationManifestV1(
                SpaceAiOfflineEvaluationVersions.SchemaVersion,
                manifest.DatasetVersion,
                SpaceAiEvaluationDatasetPurpose.FormalRelease,
                CountsTowardReleaseGate: true,
                "Millimeter",
                "FloorLocal-ZUp",
                rules.MappingProfileVersion,
                rules.RuleSetVersion,
                manifest.ExpectedAnswerVersion,
                manifest.EligibilityBasis,
                evaluationSamples,
                normalizedCommit,
                rules.ParserVersion,
                normalizedProviderVersion,
                rules.ModelVersion,
                manifest.AnnotationReviewEvidence,
                acceptanceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                IsImmutable: true,
                manifest.IntegrityAuditSha256,
                IntegrityAuditPassed: true),
            expectedTargets,
            predictions,
            efforts);
        var report = new SpaceAiOfflineEvaluator().Evaluate(request);
        var holdoutOmissions = sampleResults
            .Where(item => item.Split == "ReleaseHoldout")
            .Sum(item => item.UnreportedBlockingOmissionCount);
        return new GoldenCadBusinessEvaluationResultV1(
            request,
            report,
            Sha256(manifestBytes),
            manifest.GoldenDatasetSha256,
            manifest.SourceSetSha256,
            Sha256(rulesBytes),
            Sha256(Encoding.UTF8.GetBytes(string.Join(
                "\n",
                cadIrIdentities.Order(StringComparer.Ordinal)))),
            holdoutOmissions,
            sampleResults);
    }

    private static IReadOnlyList<SpaceAiExpectedTargetV1> BuildExpectedTargets(
        ControlledSample sample,
        IReadOnlyList<ExpectedElement> expected)
    {
        var sourceRefByExpectedId = expected.ToDictionary(
            item => item.ExpectedId,
            item => item.MatchKey,
            StringComparer.Ordinal);
        return expected.Select(item => new SpaceAiExpectedTargetV1(
                sample.SampleId,
                item.ExpectedId,
                item.MatchKey,
                ExpectedType(item.Type),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["geometry"] = ExpectedGeometryToken(item.Geometry),
                    ["relationships"] = ExpectedRelationshipToken(
                        item.Relationships,
                        sourceRefByExpectedId),
                },
                []))
            .OrderBy(item => item.ExpectedId, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<SpaceAiEvaluationPredictionV1> BuildPredictions(
        ControlledSample sample,
        SpaceCadIrPackageV1 package,
        GoldenCadBusinessRuleSetV1 rules,
        IReadOnlyList<ExpectedElement> expected,
        string floorResolution)
    {
        var rulesByLayer = rules.Rules.ToDictionary(
            item => item.Layer,
            StringComparer.Ordinal);
        var selected = package.Entities
            .Where(entity => rulesByLayer.TryGetValue(entity.LayerId, out var rule)
                             && entity.IsSupported
                             && entity.Type == rule.RequiredEntityType
                             && MeetsMinimumDiameter(entity, rule))
            .Select(entity => new SelectedEntity(entity, rulesByLayer[entity.LayerId]))
            .ToArray();
        var inferredElevations = InferElevations(
            package,
            selected,
            floorResolution);
        selected = KeepLargestSameCenter(selected, inferredElevations);

        var geometryByRef = selected.ToDictionary(
            item => item.Entity.SourceRef,
            item => ActualGeometry(
                item.Entity,
                item.Rule,
                rules,
                inferredElevations[item.Entity.SourceRef]),
            StringComparer.Ordinal);
        var zones = selected
            .Where(item => item.Rule.TargetType == WarehouseSpaceType.Zone)
            .Select(item => new SpatialEntity(
                item.Entity.SourceRef,
                geometryByRef[item.Entity.SourceRef]))
            .Where(item => item.Geometry.Polygon is not null)
            .ToArray();
        var aisles = selected
            .Where(item => item.Rule.TargetType == WarehouseSpaceType.Aisle)
            .Select(item => new SpatialEntity(
                item.Entity.SourceRef,
                geometryByRef[item.Entity.SourceRef]))
            .Where(item => item.Geometry.Polygon is not null)
            .ToArray();
        var floorCodes = geometryByRef.Values
            .Select(item => item.CenterZ)
            .Distinct()
            .Order()
            .Select((value, index) => new { value, code = $"F{index + 1:00}" })
            .ToDictionary(item => item.value, item => item.code);

        return selected.Select(item =>
            {
                var geometry = geometryByRef[item.Entity.SourceRef];
                var zoneRef = item.Rule.TargetType == WarehouseSpaceType.Wall
                    ? string.Empty
                    : item.Rule.TargetType == WarehouseSpaceType.Zone
                        ? item.Entity.SourceRef
                        : ContainingZone(geometry, zones);
                var aisleRef = item.Rule.TargetType == WarehouseSpaceType.Aisle
                    ? item.Entity.SourceRef
                    : item.Rule.TargetType == WarehouseSpaceType.Rack
                        ? NearestAisle(geometry, aisles)
                        : string.Empty;
                return new SpaceAiEvaluationPredictionV1(
                    sample.SampleId,
                    sample.SampleId + ":" + item.Entity.SourceRef,
                    item.Entity.SourceRef,
                    item.Rule.TargetType,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["geometry"] = geometry.Token,
                        ["relationships"] = RelationshipToken(
                            floorCodes[geometry.CenterZ],
                            zoneRef,
                            aisleRef),
                    },
                    [],
                    item.Rule.Confidence);
            })
            .OrderBy(item => item.ProposalId, StringComparer.Ordinal)
            .ToArray();
    }

    private static int CountCorrect(
        IReadOnlyList<SpaceAiExpectedTargetV1> expected,
        IReadOnlyList<SpaceAiEvaluationPredictionV1> predictions)
    {
        var predictionByKey = predictions.ToDictionary(
            item => item.MatchKey,
            StringComparer.Ordinal);
        return expected.Count(item =>
            predictionByKey.TryGetValue(item.MatchKey, out var prediction)
            && item.ObjectType == prediction.ObjectType
            && item.KeyAttributes.Count == prediction.Attributes.Count
            && item.KeyAttributes.All(pair =>
                prediction.Attributes.TryGetValue(pair.Key, out var actual)
                && actual.Equals(pair.Value, StringComparison.Ordinal)));
    }

    private static SelectedEntity[] KeepLargestSameCenter(
        IReadOnlyList<SelectedEntity> selected,
        IReadOnlyDictionary<string, int> inferredElevations)
    {
        var retained = selected
            .Where(item => !item.Rule.KeepLargestAtSameCenter)
            .ToList();
        retained.AddRange(selected
            .Where(item => item.Rule.KeepLargestAtSameCenter)
            .GroupBy(
                item => CenterKey(
                    item.Entity,
                    inferredElevations[item.Entity.SourceRef]),
                StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(item => BoundsArea(item.Entity.Bounds))
                .ThenBy(item => item.Entity.SourceRef, StringComparer.Ordinal)
                .First()));
        return retained
            .OrderBy(item => item.Entity.SourceRef, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool MeetsMinimumDiameter(
        SpaceCadIrEntityV1 entity,
        GoldenCadBusinessRuleV1 rule)
    {
        if (rule.MinimumDiameterMillimeters is null)
            return true;
        return entity.Radius is { } radius
               && radius * 2 >= rule.MinimumDiameterMillimeters.Value;
    }

    private static IReadOnlyDictionary<string, int> InferElevations(
        SpaceCadIrPackageV1 package,
        IReadOnlyList<SelectedEntity> selected,
        string floorResolution)
    {
        var result = selected.ToDictionary(
            item => item.Entity.SourceRef,
            item => RoundMillimeter(EntityCenter(item.Entity).Z),
            StringComparer.Ordinal);
        if (!floorResolution.Equals("EntityElevationBands", StringComparison.Ordinal))
            return result;

        var positiveAnchors = package.Entities
            .Select(entity => new
            {
                Entity = entity,
                Elevation = RoundMillimeter(EntityCenter(entity).Z),
                Handle = HandleValue(entity.SourceRef),
            })
            .Where(item => item.Elevation > 0)
            .GroupBy(item => item.Elevation)
            .Select(group => new
            {
                Elevation = group.Key,
                FirstHandle = group.Min(item => item.Handle),
            })
            .OrderBy(item => item.FirstHandle)
            .ToArray();
        if (positiveAnchors.Length == 0)
            return result;

        var selectedByHandle = selected
            .Select(item => new
            {
                Item = item,
                Handle = HandleValue(item.Entity.SourceRef),
            })
            .OrderBy(item => item.Handle)
            .ToArray();
        var boundaries = new List<(long Handle, int Elevation)>
        {
            (selectedByHandle[0].Handle, 0),
        };
        foreach (var anchor in positiveAnchors)
        {
            var precedingWalls = selectedByHandle
                .Where(item => item.Handle < anchor.FirstHandle
                               && item.Item.Rule.TargetType == WarehouseSpaceType.Wall)
                .ToArray();
            if (precedingWalls.Length == 0)
            {
                throw new InvalidDataException(
                    "Entity-elevation floor band has no preceding wall boundary.");
            }
            var lastWall = precedingWalls[^1];
            var boundaryIndex = Array.IndexOf(selectedByHandle, lastWall);
            while (boundaryIndex > 0
                   && selectedByHandle[boundaryIndex - 1].Item.Rule.TargetType
                   == WarehouseSpaceType.Wall)
            {
                boundaryIndex--;
            }
            boundaries.Add((
                selectedByHandle[boundaryIndex].Handle,
                anchor.Elevation));
        }
        var canonicalBoundaries = boundaries
            .GroupBy(item => item.Handle)
            .Select(group => group.OrderByDescending(item => item.Elevation).First())
            .OrderBy(item => item.Handle)
            .ToArray();
        foreach (var item in selectedByHandle)
        {
            var floor = canonicalBoundaries
                .Where(boundary => boundary.Handle <= item.Handle)
                .Last();
            result[item.Item.Entity.SourceRef] = floor.Elevation;
        }
        return result;
    }

    private static string CenterKey(
        SpaceCadIrEntityV1 entity,
        int inferredElevation)
    {
        var center = EntityCenter(entity);
        return string.Join(
            "|",
            entity.LayerId,
            RoundMillimeter(center.X),
            RoundMillimeter(center.Y),
            inferredElevation);
    }

    private static ActualGeometryResult ActualGeometry(
        SpaceCadIrEntityV1 entity,
        GoldenCadBusinessRuleV1 rule,
        GoldenCadBusinessRuleSetV1 rules,
        int inferredElevation)
    {
        if (entity.Type == SpaceCadIrEntityType.Circle
            && entity.Points.Count > 0
            && entity.Radius is { } radius)
        {
            var center = entity.Points[0] with { Z = inferredElevation };
            return new ActualGeometryResult(
                ObbToken(center, radius * 2, radius * 2, 0),
                RoundMillimeter(center.X),
                RoundMillimeter(center.Y),
                RoundMillimeter(center.Z),
                null,
                (double)(Math.PI * (double)radius * (double)radius));
        }
        if (entity.Type == SpaceCadIrEntityType.Line && entity.Points.Count >= 2)
        {
            var first = entity.Points[0] with { Z = inferredElevation };
            var second = entity.Points[1] with { Z = inferredElevation };
            var width = Distance(first, second);
            var rotation = Angle(first, second);
            return new ActualGeometryResult(
                ObbToken(
                    first,
                    width,
                    rule.TargetType == WarehouseSpaceType.Door
                        ? rules.DoorThicknessMillimeters
                        : 0,
                    rotation),
                RoundMillimeter(first.X),
                RoundMillimeter(first.Y),
                RoundMillimeter(first.Z),
                null,
                0);
        }
        if (entity.Type == SpaceCadIrEntityType.ClosedPolyline
            && entity.Points.Count == 4)
        {
            var center = EntityCenter(entity) with { Z = inferredElevation };
            var width = Distance(entity.Points[0], entity.Points[1]);
            var height = Distance(entity.Points[1], entity.Points[2]);
            var rotation = Angle(entity.Points[0], entity.Points[1]);
            var polygon = entity.Points.Select(item => new Point2(
                (double)item.X,
                (double)item.Y)).ToArray();
            return new ActualGeometryResult(
                ObbToken(center, width, height, rotation),
                RoundMillimeter(center.X),
                RoundMillimeter(center.Y),
                RoundMillimeter(center.Z),
                polygon,
                Math.Abs(SignedArea(polygon)));
        }

        var fallbackCenter = EntityCenter(entity) with { Z = inferredElevation };
        return new ActualGeometryResult(
            "unsupported-geometry",
            RoundMillimeter(fallbackCenter.X),
            RoundMillimeter(fallbackCenter.Y),
            RoundMillimeter(fallbackCenter.Z),
            null,
            BoundsArea(entity.Bounds));
    }

    private static string ExpectedGeometryToken(ExpectedGeometry geometry)
    {
        if (!geometry.Kind.Equals("OrientedBox2D", StringComparison.Ordinal)
            || geometry.Center.Count != 3
            || geometry.Size.Count != 2)
        {
            throw new InvalidDataException("Expected geometry must be OrientedBox2D.");
        }
        return ObbToken(
            new SpaceCadPointV1(
                geometry.Center[0],
                geometry.Center[1],
                geometry.Center[2]),
            geometry.Size[0],
            geometry.Size[1],
            geometry.RotationDeg);
    }

    private static string ObbToken(
        SpaceCadPointV1 center,
        decimal width,
        decimal height,
        decimal rotation) => string.Join(
            "|",
            "obb",
            RoundMillimeter(center.X),
            RoundMillimeter(center.Y),
            RoundMillimeter(center.Z),
            RoundMillimeter(width),
            RoundMillimeter(height),
            RoundAngle(rotation).ToString("0.0", CultureInfo.InvariantCulture));

    private static string ExpectedRelationshipToken(
        ExpectedRelationships relationships,
        IReadOnlyDictionary<string, string> sourceRefByExpectedId)
    {
        var zone = string.IsNullOrWhiteSpace(relationships.ZoneId)
            ? string.Empty
            : sourceRefByExpectedId.TryGetValue(relationships.ZoneId, out var zoneRef)
                ? zoneRef
                : throw new InvalidDataException(
                    "Expected zone relation does not resolve inside the sample.");
        var aisle = string.IsNullOrWhiteSpace(relationships.AisleId)
            ? string.Empty
            : sourceRefByExpectedId.TryGetValue(relationships.AisleId, out var aisleRef)
                ? aisleRef
                : throw new InvalidDataException(
                    "Expected aisle relation does not resolve inside the sample.");
        return RelationshipToken(relationships.FloorId, zone, aisle);
    }

    private static string RelationshipToken(
        string floor,
        string zone,
        string aisle) => $"floor={floor}|zone={zone}|aisle={aisle}";

    private static string ContainingZone(
        ActualGeometryResult child,
        IReadOnlyList<SpatialEntity> zones)
    {
        var sameFloor = zones
            .Where(item => item.Geometry.CenterZ == child.CenterZ)
            .ToArray();
        var containing = sameFloor
            .Where(item => PointInPolygon(
                child.CenterX,
                child.CenterY,
                item.Geometry.Polygon!))
            .OrderBy(item => item.Geometry.Area)
            .ThenBy(item => item.SourceRef, StringComparer.Ordinal)
            .FirstOrDefault();
        return containing?.SourceRef ?? sameFloor
            .OrderBy(item => SquaredDistance(
                child.CenterX,
                child.CenterY,
                item.Geometry.CenterX,
                item.Geometry.CenterY))
            .ThenBy(item => item.SourceRef, StringComparer.Ordinal)
            .Select(item => item.SourceRef)
            .FirstOrDefault() ?? string.Empty;
    }

    private static string NearestAisle(
        ActualGeometryResult child,
        IReadOnlyList<SpatialEntity> aisles) => aisles
        .Where(item => item.Geometry.CenterZ == child.CenterZ)
        .OrderBy(item => SquaredDistance(
            child.CenterX,
            child.CenterY,
            item.Geometry.CenterX,
            item.Geometry.CenterY))
        .ThenBy(item => item.SourceRef, StringComparer.Ordinal)
        .Select(item => item.SourceRef)
        .FirstOrDefault() ?? string.Empty;

    private static decimal Distance(SpaceCadPointV1 left, SpaceCadPointV1 right) =>
        (decimal)Math.Sqrt(
            Math.Pow((double)(right.X - left.X), 2)
            + Math.Pow((double)(right.Y - left.Y), 2));

    private static decimal Angle(SpaceCadPointV1 left, SpaceCadPointV1 right) =>
        (decimal)(Math.Atan2(
            (double)(right.Y - left.Y),
            (double)(right.X - left.X)) * 180 / Math.PI);

    private static decimal RoundAngle(decimal value)
    {
        var normalized = value % 360;
        if (normalized < 0) normalized += 360;
        var rounded = decimal.Round(normalized, 1, MidpointRounding.AwayFromZero);
        return rounded == 360 ? 0 : rounded;
    }

    private static int RoundMillimeter(decimal value) => decimal.ToInt32(
        decimal.Round(value, 0, MidpointRounding.AwayFromZero));

    private static SpaceCadPointV1 EntityCenter(SpaceCadIrEntityV1 entity)
    {
        if (entity.Points.Count > 0)
        {
            return new SpaceCadPointV1(
                entity.Points.Average(item => item.X),
                entity.Points.Average(item => item.Y),
                entity.Points.Average(item => item.Z));
        }
        if (entity.Bounds is { } bounds)
        {
            return new SpaceCadPointV1(
                (bounds.MinX + bounds.MaxX) / 2,
                (bounds.MinY + bounds.MaxY) / 2,
                0);
        }
        return new SpaceCadPointV1(0, 0, 0);
    }

    private static double BoundsArea(SpaceCadBoundsV1? bounds) => bounds is null
        ? 0
        : Math.Max(0, (double)(bounds.MaxX - bounds.MinX))
          * Math.Max(0, (double)(bounds.MaxY - bounds.MinY));

    private static double SignedArea(IReadOnlyList<Point2> polygon)
    {
        double twiceArea = 0;
        for (var index = 0; index < polygon.Count; index++)
        {
            var current = polygon[index];
            var next = polygon[(index + 1) % polygon.Count];
            twiceArea += (current.X * next.Y) - (next.X * current.Y);
        }
        return twiceArea / 2;
    }

    private static bool PointInPolygon(
        int x,
        int y,
        IReadOnlyList<Point2> polygon)
    {
        var inside = false;
        for (int current = 0, previous = polygon.Count - 1;
             current < polygon.Count;
             previous = current++)
        {
            var left = polygon[current];
            var right = polygon[previous];
            if ((left.Y > y) != (right.Y > y)
                && x < ((right.X - left.X) * (y - left.Y)
                         / (right.Y - left.Y)) + left.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static double SquaredDistance(int x1, int y1, int x2, int y2) =>
        Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2);

    private static long HandleValue(string sourceRef)
    {
        if (!sourceRef.StartsWith("H:", StringComparison.Ordinal)
            || !long.TryParse(
                sourceRef.AsSpan(2),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var value))
        {
            throw new InvalidDataException(
                $"CAD source reference is not a hexadecimal Handle: {sourceRef}");
        }
        return value;
    }

    private static WarehouseSpaceType ExpectedType(string value) => value switch
    {
        "WallShell" => WarehouseSpaceType.Wall,
        "Column" => WarehouseSpaceType.Column,
        "Door" => WarehouseSpaceType.Door,
        "Dock" => WarehouseSpaceType.Dock,
        "Zone" => WarehouseSpaceType.Zone,
        "Aisle" => WarehouseSpaceType.Aisle,
        "Rack" => WarehouseSpaceType.Rack,
        "Pallet" or "CHARGER" or "LIFT" or "STAIR" or "CONVEYOR"
            or "SORTER" or "PACK" or "UNKNOWN" =>
            WarehouseSpaceType.StaticEquipment,
        _ => throw new InvalidDataException($"Unsupported expected type '{value}'."),
    };

    private static async Task<IReadOnlyList<ExpectedElement>> ReadExpectedAsync(
        string path,
        ControlledSample sample,
        CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        var expected = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<ExpectedElement>(
                                line,
                                CadExperimentJson.Options)
                            ?? throw new InvalidDataException(
                                $"Expected element in {sample.SampleId} is empty."))
            .ToArray();
        if (expected.Length == 0
            || expected.Select(item => item.ExpectedId)
                .Distinct(StringComparer.Ordinal).Count() != expected.Length
            || expected.Any(item => item.SourceRefs.Count == 0))
        {
            throw new InvalidDataException(
                $"Expected elements for {sample.SampleId} are invalid.");
        }
        return expected.Select(item => item with
        {
            MatchKey = "H:" + item.SourceRefs[0].Handle,
        }).ToArray();
    }

    private static async Task<int> CountExpectedBlockingIssuesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await ReadBoundedBytesAsync(path, MaximumInputBytes, cancellationToken);
        using var document = JsonDocument.Parse(bytes);
        var issues = document.RootElement.GetProperty("issues");
        if (issues.ValueKind == JsonValueKind.Object)
        {
            return issues.GetProperty("severity").GetString() == "Blocking" ? 1 : 0;
        }
        return issues.EnumerateArray().Count(item =>
            item.GetProperty("severity").GetString() == "Blocking");
    }

    private static async Task<string> ReadFloorResolutionAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await ReadBoundedBytesAsync(path, MaximumInputBytes, cancellationToken);
        using var document = JsonDocument.Parse(bytes);
        var value = document.RootElement.GetProperty("floorResolution").GetString();
        return value is "SingleFloorAtZ0" or "EntityElevationBands"
            ? value
            : throw new InvalidDataException(
                "The controlled mapping profile floor resolution is unsupported.");
    }

    private static void ValidatePackage(
        ControlledSample sample,
        SpaceCadIrPackageV1 package)
    {
        if (!package.Document.SourceSha256.Equals(
                sample.SourceSha256,
                StringComparison.OrdinalIgnoreCase)
            || package.Document.SourceFormat.ToString()
                .Equals(sample.SourceFormat, StringComparison.OrdinalIgnoreCase) == false
            || package.Document.Unit != SpaceCadUnit.Millimeter
            || package.Document.ScaleToMillimeters != 1
            || !package.Document.CoordinateSystem.Equals(
                "FloorLocal-ZUp",
                StringComparison.Ordinal)
            || package.Summary.MissingSourceRefCount != 0
            || package.Issues.Any(item => item.Severity == SpaceCadIssueSeverity.Blocking))
        {
            throw new InvalidDataException(
                $"CAD IR for {sample.SampleId} does not match the frozen source contract.");
        }
    }

    private static void ValidateRules(
        GoldenCadBusinessRuleSetV1 rules,
        ControlledManifest manifest)
    {
        if (rules.SchemaVersion != SchemaVersion
            || string.IsNullOrWhiteSpace(rules.ParserVersion)
            || !rules.MappingProfileVersion.Equals(
                manifest.MappingProfileVersion,
                StringComparison.Ordinal)
            || !rules.RuleSetVersion.Equals(
                manifest.RuleSetVersion,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(rules.ModelVersion)
            || rules.DoorThicknessMillimeters <= 0
            || string.IsNullOrWhiteSpace(rules.OperationModel)
            || rules.Rules.Count == 0
            || rules.Rules.Select(item => item.Layer)
                .Distinct(StringComparer.Ordinal).Count() != rules.Rules.Count)
        {
            throw new InvalidDataException("The golden CAD business rule set is invalid.");
        }
        foreach (var rule in rules.Rules)
        {
            RequiredToken(rule.Layer, nameof(rule.Layer));
            if (!Enum.IsDefined(rule.TargetType)
                || !Enum.IsDefined(rule.RequiredEntityType)
                || rule.Confidence is < 0 or > 1
                || rule.MinimumDiameterMillimeters is <= 0)
            {
                throw new InvalidDataException("A golden CAD business rule is invalid.");
            }
        }
    }

    private static ControlledManifest ParseManifest(JsonElement root)
    {
        var dataset = root.GetProperty("dataset");
        var samples = dataset.GetProperty("samples").EnumerateArray()
            .Select(item =>
            {
                var artifacts = item.GetProperty("artifacts");
                return new ControlledSample(
                    item.GetProperty("sampleId").GetString()!,
                    item.GetProperty("sampleRef").GetString()!,
                    item.GetProperty("sourceSha256").GetString()!,
                    item.GetProperty("sourceFormat").GetString()!,
                    item.GetProperty("split").GetString()!,
                    item.GetProperty("layoutFamily").GetString()!,
                    item.GetProperty("license").GetString()!,
                    item.GetProperty("deidentificationEvidence")
                        .GetProperty("uri").GetString()!,
                    artifacts.GetProperty("metadataSha256").GetString()!,
                    artifacts.GetProperty("expectedElementsSha256").GetString()!,
                    artifacts.GetProperty("expectedIssuesSha256").GetString()!,
                    artifacts.GetProperty("mappingProfileSha256").GetString()!);
            }).ToArray();
        var audit = dataset.GetProperty("integrityAuditEvidence");
        return new ControlledManifest(
            dataset.GetProperty("datasetVersion").GetString()!,
            dataset.GetProperty("eligibilityBasis").GetString()!,
            dataset.GetProperty("goldenDatasetSha256").GetString()!,
            dataset.GetProperty("sourceSetSha256").GetString()!,
            dataset.GetProperty("mappingProfileVersion").GetString()!,
            dataset.GetProperty("ruleSetVersion").GetString()!,
            dataset.GetProperty("expectedAnswerVersion").GetString()!,
            audit.GetProperty("uri").GetString()!,
            audit.GetProperty("sha256").GetString()!,
            dataset.GetProperty("integrityAuditPassed").GetBoolean(),
            samples);
    }

    private static void ValidateManifest(ControlledManifest manifest)
    {
        if (manifest.Samples.Count != ExpectedSampleCount
            || !manifest.IntegrityAuditPassed
            || !IsSha256(manifest.GoldenDatasetSha256)
            || !IsSha256(manifest.SourceSetSha256)
            || !IsSha256(manifest.IntegrityAuditSha256)
            || manifest.Samples.Select(item => item.SampleId)
                .Distinct(StringComparer.Ordinal).Count() != ExpectedSampleCount
            || manifest.Samples.Count(item => item.Split == "Calibration") != 10
            || manifest.Samples.Count(item => item.Split == "Validation") != 5
            || manifest.Samples.Count(item => item.Split == "ReleaseHoldout") != 5)
        {
            throw new InvalidDataException("The controlled golden CAD manifest is invalid.");
        }
    }

    private static SpaceAiEvaluationSplit ParseSplit(string split) => split switch
    {
        "Calibration" => SpaceAiEvaluationSplit.Calibration,
        "Validation" => SpaceAiEvaluationSplit.Validation,
        "ReleaseHoldout" => SpaceAiEvaluationSplit.ReleaseHoldout,
        _ => throw new InvalidDataException($"Unsupported split '{split}'."),
    };

    private static async Task VerifyArtifactAsync(
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        var actual = Sha256(await ReadBoundedBytesAsync(
            path,
            MaximumInputBytes,
            cancellationToken));
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Controlled artifact hash changed: {path}");
    }

    private static async Task<byte[]> ReadBoundedBytesAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 || file.Length > maximumBytes)
            throw new InvalidDataException($"Input file size is invalid: {path}");
        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    private static string RequireDirectory(string path, string label)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"{label} does not exist: {fullPath}");
        return fullPath;
    }

    private static string RequireContainedFile(
        string root,
        string path,
        string label)
    {
        var fullPath = Path.GetFullPath(path);
        var prefix = root.TrimEnd(
                         Path.DirectorySeparatorChar,
                         Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(
                prefix,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal)
            || !File.Exists(fullPath))
        {
            throw new FileNotFoundException($"{label} is absent or escapes its root.", fullPath);
        }
        return fullPath;
    }

    private static string NormalizeCommit(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 40 || normalized.Any(character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException("A 40-character application commit SHA is required.");
        }
        return normalized;
    }

    private static string RequiredToken(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 256
            || !value.Equals(value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("A canonical non-empty token is required.", name);
        }
        return value;
    }

    private static bool IsSha256(string value) => value.Length == 64
        && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Sha256(byte[] bytes) => Convert.ToHexString(
        SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record ControlledManifest(
        string DatasetVersion,
        string EligibilityBasis,
        string GoldenDatasetSha256,
        string SourceSetSha256,
        string MappingProfileVersion,
        string RuleSetVersion,
        string ExpectedAnswerVersion,
        string AnnotationReviewEvidence,
        string IntegrityAuditSha256,
        bool IntegrityAuditPassed,
        IReadOnlyList<ControlledSample> Samples);

    private sealed record ControlledSample(
        string SampleId,
        string SampleRef,
        string SourceSha256,
        string SourceFormat,
        string Split,
        string LayoutFamily,
        string License,
        string DeidentificationEvidence,
        string MetadataSha256,
        string ExpectedElementsSha256,
        string ExpectedIssuesSha256,
        string MappingProfileSha256);

    private sealed record ExpectedSourceRef(string Handle, string Layer);

    private sealed record ExpectedGeometry(
        string Kind,
        IReadOnlyList<decimal> Center,
        IReadOnlyList<decimal> Size,
        decimal RotationDeg);

    private sealed record ExpectedRelationships(
        string FloorId,
        string ZoneId,
        string AisleId);

    private sealed record ExpectedElement(
        string ExpectedId,
        string Type,
        string Layer,
        IReadOnlyList<ExpectedSourceRef> SourceRefs,
        ExpectedGeometry Geometry,
        ExpectedRelationships Relationships,
        string MatchKey = "");

    private sealed record SelectedEntity(
        SpaceCadIrEntityV1 Entity,
        GoldenCadBusinessRuleV1 Rule);

    private sealed record ActualGeometryResult(
        string Token,
        int CenterX,
        int CenterY,
        int CenterZ,
        IReadOnlyList<Point2>? Polygon,
        double Area);

    private sealed record SpatialEntity(
        string SourceRef,
        ActualGeometryResult Geometry);

    private sealed record Point2(double X, double Y);
}
