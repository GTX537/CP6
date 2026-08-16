using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class SpaceCadCoordinatePreparation
{
    private const int MatrixPrecision = 12;

    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static SpaceCadCoordinateAnalysisV1 Analyze(
        SpaceCadConversionRequest request,
        SpaceCadIrPackageV1 package,
        SpaceCadCoordinateLimitsV1? limits = null)
    {
        SpaceCadConversionContract.ValidatePackage(request, package);
        var effectiveLimits = limits ?? new SpaceCadCoordinateLimitsV1();
        ValidateLimits(effectiveLimits);
        var issues = package.Issues.ToList();
        var sourceBounds = ToSourceBounds(
            package.Document.Bounds,
            package.Document.ScaleToMillimeters);
        SpaceCadBoundsV1? suggestedBounds = null;
        var plausible = false;

        if (package.Document.Unit == SpaceCadUnit.Unknown
            || package.Document.ScaleToMillimeters is null)
        {
            AddIssue(
                issues,
                new SpaceCadConversionIssueV1(
                    "SPACE_CAD_UNIT_UNKNOWN",
                    SpaceCadIssueSeverity.Blocking,
                    DetailToken: "confirmation-required"));
        }
        else
        {
            suggestedBounds = package.Document.Bounds;
            plausible = AddExtentIssues(suggestedBounds, effectiveLimits, issues);
        }

        return new SpaceCadCoordinateAnalysisV1(
            SpaceCadCoordinateVersions.SchemaVersion,
            package.Document.SourceSha256,
            package.Document.Unit,
            package.Document.ScaleToMillimeters,
            sourceBounds,
            suggestedBounds,
            plausible,
            RequiresUnitConfirmation: true,
            issues.ToArray());
    }

    public static SpaceCadCoordinatePreparationV1 Prepare(
        SpaceCadConversionRequest request,
        SpaceCadIrPackageV1 package,
        SpaceCadCoordinateConfirmationV1 confirmation,
        SpaceCadCoordinateLimitsV1? limits = null)
    {
        SpaceCadConversionContract.ValidatePackage(request, package);
        var effectiveLimits = limits ?? new SpaceCadCoordinateLimitsV1();
        ValidateLimits(effectiveLimits);
        ValidateConfirmation(package.Document, confirmation);

        var confirmedScale = ScaleToMillimeters(confirmation.ConfirmedUnit);
        var detectedScale = package.Document.ScaleToMillimeters ?? 1m;
        var scaleCorrection = confirmedScale / detectedScale;
        var rotation = NormalizeDegrees(confirmation.RotationZDegrees);
        var radians = decimal.ToDouble(rotation) * Math.PI / 180d;
        var cosine = RoundMatrix((decimal)Math.Cos(radians));
        var sine = RoundMatrix((decimal)Math.Sin(radians));
        var sourceOriginX = confirmation.SourceOriginInSourceUnits.X * confirmedScale;
        var sourceOriginY = confirmation.SourceOriginInSourceUnits.Y * confirmedScale;
        var sourceOriginZ = confirmation.SourceOriginInSourceUnits.Z * confirmedScale;
        var floorOrigin = confirmation.FloorOriginMillimeters;
        var globalTransform = new SpaceCadAffineTransformV1(
            RoundMatrix(cosine * scaleCorrection),
            RoundMatrix(-sine * scaleCorrection),
            RoundMatrix(sine * scaleCorrection),
            RoundMatrix(cosine * scaleCorrection),
            RoundMillimeter(
                floorOrigin.X - ((cosine * sourceOriginX) - (sine * sourceOriginY))),
            RoundMillimeter(
                floorOrigin.Y - ((sine * sourceOriginX) + (cosine * sourceOriginY))),
            RoundMillimeter(floorOrigin.Z - sourceOriginZ));

        var entities = package.Entities
            .Select(entity => TransformEntity(entity, globalTransform, scaleCorrection, rotation))
            .ToArray();
        var preparedBounds = UnionBounds(entities.Select(entity => entity.Bounds));
        var issues = package.Issues
            .Where(issue => !issue.Code.Equals(
                "SPACE_CAD_UNIT_UNKNOWN",
                StringComparison.Ordinal))
            .ToList();
        AddExtentIssues(preparedBounds, effectiveLimits, issues);
        AddBoundaryIssues(
            preparedBounds,
            confirmation.TargetFloor.BoundaryBounds,
            effectiveLimits.BoundaryToleranceMillimeters,
            issues);
        AddEntityBoundaryIssues(
            entities,
            confirmation.TargetFloor.BoundaryBounds,
            effectiveLimits.BoundaryToleranceMillimeters,
            issues);

        var document = package.Document with
        {
            Unit = confirmation.ConfirmedUnit,
            ScaleToMillimeters = confirmedScale,
            Bounds = preparedBounds,
        };
        var summary = package.Summary with { Bounds = preparedBounds };
        var preparedPackage = package with
        {
            Document = document,
            Entities = entities,
            Issues = issues.ToArray(),
            Summary = summary,
        };
        SpaceCadConversionContract.ValidatePackage(request, preparedPackage);

        var metadataWithoutHash = new SpaceCadCoordinateMetadataV1(
            SpaceCadCoordinateVersions.SchemaVersion,
            package.Document.SourceSha256,
            UnitConfirmed: true,
            package.Document.Unit,
            package.Document.ScaleToMillimeters,
            confirmation.ConfirmedUnit,
            confirmedScale,
            confirmation.SourceOriginInSourceUnits,
            confirmation.FloorOriginMillimeters,
            rotation,
            confirmation.TargetFloor,
            globalTransform,
            ToSourceBounds(
                package.Document.Bounds,
                package.Document.ScaleToMillimeters),
            preparedBounds,
            TransformSha256: string.Empty);
        var transformHash = ComputeSha256(CanonicalMetadata(metadataWithoutHash));
        var metadata = metadataWithoutHash with { TransformSha256 = transformHash };
        var ready = issues.All(issue => issue.Severity != SpaceCadIssueSeverity.Blocking);
        return new SpaceCadCoordinatePreparationV1(
            metadata,
            preparedPackage,
            issues.ToArray(),
            ready);
    }

    public static string SerializeMetadata(SpaceCadCoordinateMetadataV1 metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.SchemaVersion != SpaceCadCoordinateVersions.SchemaVersion
            || !metadata.UnitConfirmed
            || metadata.ConfirmedUnit == SpaceCadUnit.Unknown
            || metadata.ConfirmedScaleToMillimeters <= 0
            || string.IsNullOrWhiteSpace(metadata.TransformSha256)
            || !metadata.TransformSha256.Equals(
                ComputeSha256(CanonicalMetadata(metadata with { TransformSha256 = string.Empty })),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("CAD coordinate metadata is incomplete.");
        }
        return JsonSerializer.Serialize(metadata, MetadataJsonOptions);
    }

    private static SpaceCadIrEntityV1 TransformEntity(
        SpaceCadIrEntityV1 entity,
        SpaceCadAffineTransformV1 global,
        decimal scaleCorrection,
        decimal rotation)
    {
        var points = entity.Points
            .Select(point => TransformPoint(point, global, scaleCorrection))
            .ToArray();
        decimal? radius = entity.Radius is { } value
            ? RoundMillimeter(Math.Abs(value * scaleCorrection))
            : null;
        var bounds = points.Length == 0
            ? TransformBounds(entity.Bounds, global, scaleCorrection)
            : Bounds(points, radius);
        return entity with
        {
            Points = points,
            Radius = radius,
            StartAngleDegrees = entity.StartAngleDegrees is { } start
                ? NormalizeDegrees(start + rotation)
                : null,
            EndAngleDegrees = entity.EndAngleDegrees is { } end
                ? NormalizeDegrees(end + rotation)
                : null,
            Transform = entity.Type == SpaceCadIrEntityType.BlockReference
                ? Compose(global, entity.Transform, scaleCorrection)
                : SpaceCadAffineTransformV1.Identity,
            Bounds = bounds,
        };
    }

    private static SpaceCadPointV1 TransformPoint(
        SpaceCadPointV1 point,
        SpaceCadAffineTransformV1 transform,
        decimal scaleCorrection) =>
        new(
            RoundMillimeter(
                (transform.M11 * point.X)
                + (transform.M12 * point.Y)
                + transform.OffsetX),
            RoundMillimeter(
                (transform.M21 * point.X)
                + (transform.M22 * point.Y)
                + transform.OffsetY),
            RoundMillimeter((point.Z * scaleCorrection) + transform.OffsetZ));

    private static SpaceCadAffineTransformV1 Compose(
        SpaceCadAffineTransformV1 global,
        SpaceCadAffineTransformV1 entity,
        decimal scaleCorrection) =>
        new(
            RoundMatrix((global.M11 * entity.M11) + (global.M12 * entity.M21)),
            RoundMatrix((global.M11 * entity.M12) + (global.M12 * entity.M22)),
            RoundMatrix((global.M21 * entity.M11) + (global.M22 * entity.M21)),
            RoundMatrix((global.M21 * entity.M12) + (global.M22 * entity.M22)),
            RoundMillimeter(
                (global.M11 * entity.OffsetX)
                + (global.M12 * entity.OffsetY)
                + global.OffsetX),
            RoundMillimeter(
                (global.M21 * entity.OffsetX)
                + (global.M22 * entity.OffsetY)
                + global.OffsetY),
            RoundMillimeter((entity.OffsetZ * scaleCorrection) + global.OffsetZ));

    private static SpaceCadBoundsV1? TransformBounds(
        SpaceCadBoundsV1? bounds,
        SpaceCadAffineTransformV1 transform,
        decimal scaleCorrection)
    {
        if (bounds is null)
            return null;
        var corners = new[]
        {
            new SpaceCadPointV1(bounds.MinX, bounds.MinY),
            new SpaceCadPointV1(bounds.MinX, bounds.MaxY),
            new SpaceCadPointV1(bounds.MaxX, bounds.MinY),
            new SpaceCadPointV1(bounds.MaxX, bounds.MaxY),
        }.Select(point => TransformPoint(point, transform, scaleCorrection)).ToArray();
        return Bounds(corners, radius: null);
    }

    private static SpaceCadBoundsV1? Bounds(
        IReadOnlyList<SpaceCadPointV1> points,
        decimal? radius)
    {
        if (points.Count == 0)
            return null;
        var minX = points.Min(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxX = points.Max(point => point.X);
        var maxY = points.Max(point => point.Y);
        if (radius is { } value && points.Count == 1)
        {
            minX -= value;
            minY -= value;
            maxX += value;
            maxY += value;
        }
        return new SpaceCadBoundsV1(minX, minY, maxX, maxY);
    }

    private static SpaceCadBoundsV1? UnionBounds(IEnumerable<SpaceCadBoundsV1?> values)
    {
        var bounds = values.Where(value => value is not null).Cast<SpaceCadBoundsV1>().ToArray();
        return bounds.Length == 0
            ? null
            : new SpaceCadBoundsV1(
                bounds.Min(value => value.MinX),
                bounds.Min(value => value.MinY),
                bounds.Max(value => value.MaxX),
                bounds.Max(value => value.MaxY));
    }

    private static SpaceCadBoundsV1? ToSourceBounds(
        SpaceCadBoundsV1? normalizedBounds,
        decimal? detectedScale)
    {
        if (normalizedBounds is null)
            return null;
        var scale = detectedScale ?? 1m;
        return new SpaceCadBoundsV1(
            normalizedBounds.MinX / scale,
            normalizedBounds.MinY / scale,
            normalizedBounds.MaxX / scale,
            normalizedBounds.MaxY / scale);
    }

    private static bool AddExtentIssues(
        SpaceCadBoundsV1? bounds,
        SpaceCadCoordinateLimitsV1 limits,
        ICollection<SpaceCadConversionIssueV1> issues)
    {
        if (bounds is null)
        {
            AddIssue(
                issues,
                new SpaceCadConversionIssueV1(
                    "SPACE_CAD_BOUNDS_MISSING",
                    SpaceCadIssueSeverity.Blocking));
            return false;
        }

        var span = decimal.Max(bounds.MaxX - bounds.MinX, bounds.MaxY - bounds.MinY);
        if (span < limits.MinimumFloorSpanMillimeters)
        {
            AddIssue(
                issues,
                new SpaceCadConversionIssueV1(
                    "SPACE_CAD_EXTENT_IMPLAUSIBLE",
                    SpaceCadIssueSeverity.Blocking,
                    DetailToken: "below-minimum"));
            return false;
        }
        if (span > limits.MaximumFloorSpanMillimeters)
        {
            AddIssue(
                issues,
                new SpaceCadConversionIssueV1(
                    "SPACE_CAD_EXTENT_IMPLAUSIBLE",
                    SpaceCadIssueSeverity.Blocking,
                    DetailToken: "above-maximum"));
            return false;
        }
        return true;
    }

    private static void AddBoundaryIssues(
        SpaceCadBoundsV1? prepared,
        SpaceCadBoundsV1 boundary,
        decimal tolerance,
        ICollection<SpaceCadConversionIssueV1> issues)
    {
        if (prepared is null)
            return;
        if (prepared.MinX < boundary.MinX - tolerance
            || prepared.MinY < boundary.MinY - tolerance
            || prepared.MaxX > boundary.MaxX + tolerance
            || prepared.MaxY > boundary.MaxY + tolerance)
        {
            AddIssue(
                issues,
                new SpaceCadConversionIssueV1(
                    "SPACE_CAD_FLOOR_BOUNDARY_EXCEEDED",
                    SpaceCadIssueSeverity.Blocking,
                    DetailToken: "outside-target-floor"));
        }
    }

    private static void AddEntityBoundaryIssues(
        IReadOnlyList<SpaceCadIrEntityV1> entities,
        SpaceCadBoundsV1 boundary,
        decimal tolerance,
        ICollection<SpaceCadConversionIssueV1> issues)
    {
        foreach (var entity in entities
                     .Where(entity => entity.Bounds is { } bounds
                         && IsOutsideBoundary(bounds, boundary, tolerance))
                     .OrderBy(entity => entity.SourceRef, StringComparer.Ordinal))
        {
            AddIssue(
                issues,
                new SpaceCadConversionIssueV1(
                    "SPACE_CAD_ENTITY_FLOOR_BOUNDARY_EXCEEDED",
                    SpaceCadIssueSeverity.Warning,
                    entity.SourceRef,
                    "outside-target-floor"));
        }
    }

    private static bool IsOutsideBoundary(
        SpaceCadBoundsV1 bounds,
        SpaceCadBoundsV1 boundary,
        decimal tolerance) =>
        bounds.MinX < boundary.MinX - tolerance
        || bounds.MinY < boundary.MinY - tolerance
        || bounds.MaxX > boundary.MaxX + tolerance
        || bounds.MaxY > boundary.MaxY + tolerance;

    private static void ValidateLimits(SpaceCadCoordinateLimitsV1 limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.MinimumFloorSpanMillimeters <= 0
            || limits.MaximumFloorSpanMillimeters < limits.MinimumFloorSpanMillimeters
            || limits.BoundaryToleranceMillimeters < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limits),
                "CAD coordinate limits are invalid.");
        }
    }

    private static void ValidateConfirmation(
        SpaceCadIrDocumentV1 document,
        SpaceCadCoordinateConfirmationV1 confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(confirmation.SourceOriginInSourceUnits);
        ArgumentNullException.ThrowIfNull(confirmation.FloorOriginMillimeters);
        ArgumentNullException.ThrowIfNull(confirmation.TargetFloor);
        if (!confirmation.UnitConfirmed)
            throw new InvalidDataException("CAD units must be explicitly confirmed before parsing.");
        if (confirmation.ConfirmedUnit == SpaceCadUnit.Unknown)
            throw new InvalidDataException("A concrete CAD unit must be confirmed before parsing.");
        if (!confirmation.SourceSha256.Equals(document.SourceSha256, StringComparison.Ordinal))
            throw new InvalidDataException("CAD coordinate confirmation source hash does not match.");
        if (confirmation.RotationZDegrees is < -360m or > 360m)
            throw new ArgumentOutOfRangeException(
                nameof(confirmation),
                "CAD rotation must be between -360 and 360 degrees.");
        var floor = confirmation.TargetFloor;
        if (floor.FloorLogicalId == Guid.Empty
            || string.IsNullOrWhiteSpace(floor.FloorCode)
            || floor.FloorCode.Length > SpaceCadConversionContract.MaximumIdentifierLength
            || !floor.FloorCode.Equals(floor.FloorCode.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("A canonical target floor identity is required.", nameof(confirmation));
        }
        if (!floor.CoordinateSystem.Equals(
                SpaceCadCoordinateVersions.TargetCoordinateSystem,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Target floor coordinates must be {SpaceCadCoordinateVersions.TargetCoordinateSystem}.");
        }
        if (floor.BoundaryBounds.MinX > floor.BoundaryBounds.MaxX
            || floor.BoundaryBounds.MinY > floor.BoundaryBounds.MaxY)
        {
            throw new InvalidDataException("Target floor boundary bounds are inverted.");
        }
    }

    private static decimal ScaleToMillimeters(SpaceCadUnit unit) => unit switch
    {
        SpaceCadUnit.Millimeter => 1m,
        SpaceCadUnit.Centimeter => 10m,
        SpaceCadUnit.Meter => 1_000m,
        SpaceCadUnit.Inch => 25.4m,
        SpaceCadUnit.Foot => 304.8m,
        _ => throw new InvalidDataException("A supported confirmed CAD unit is required."),
    };

    private static decimal NormalizeDegrees(decimal value)
    {
        var normalized = value % 360m;
        if (normalized < 0)
            normalized += 360m;
        return decimal.Round(normalized, 6, MidpointRounding.AwayFromZero);
    }

    private static decimal RoundMatrix(decimal value) =>
        decimal.Round(value, MatrixPrecision, MidpointRounding.AwayFromZero);

    private static decimal RoundMillimeter(decimal value) =>
        decimal.Round(value, 0, MidpointRounding.AwayFromZero);

    private static void AddIssue(
        ICollection<SpaceCadConversionIssueV1> issues,
        SpaceCadConversionIssueV1 issue)
    {
        if (!issues.Any(existing =>
                existing.Code.Equals(issue.Code, StringComparison.Ordinal)
                && existing.SourceRef == issue.SourceRef
                && existing.DetailToken == issue.DetailToken))
        {
            issues.Add(issue);
        }
    }

    private static string CanonicalMetadata(SpaceCadCoordinateMetadataV1 metadata)
    {
        var values = new[]
        {
            metadata.SchemaVersion.ToString(CultureInfo.InvariantCulture),
            metadata.SourceSha256,
            metadata.UnitConfirmed ? "true" : "false",
            metadata.DetectedUnit.ToString(),
            Number(metadata.DetectedScaleToMillimeters),
            metadata.ConfirmedUnit.ToString(),
            Number(metadata.ConfirmedScaleToMillimeters),
            Point(metadata.SourceOriginInSourceUnits),
            $"{metadata.FloorOriginMillimeters.X},{metadata.FloorOriginMillimeters.Y},{metadata.FloorOriginMillimeters.Z}",
            Number(metadata.RotationZDegrees),
            metadata.TargetFloor.FloorLogicalId.ToString("D"),
            metadata.TargetFloor.FloorCode,
            metadata.TargetFloor.Level.ToString(CultureInfo.InvariantCulture),
            metadata.TargetFloor.ElevationMillimeters.ToString(CultureInfo.InvariantCulture),
            metadata.TargetFloor.CoordinateSystem,
            Bounds(metadata.TargetFloor.BoundaryBounds),
            Transform(metadata.SourceToFloorTransform),
            Bounds(metadata.SourceBounds),
            Bounds(metadata.PreparedBounds),
        };
        return string.Join('|', values);
    }

    private static string Point(SpaceCadPointV1 point) =>
        $"{Number(point.X)},{Number(point.Y)},{Number(point.Z)}";

    private static string Bounds(SpaceCadBoundsV1? bounds) => bounds is null
        ? "null"
        : $"{Number(bounds.MinX)},{Number(bounds.MinY)},{Number(bounds.MaxX)},{Number(bounds.MaxY)}";

    private static string Transform(SpaceCadAffineTransformV1 transform) =>
        string.Join(
            ',',
            Number(transform.M11),
            Number(transform.M12),
            Number(transform.M21),
            Number(transform.M22),
            Number(transform.OffsetX),
            Number(transform.OffsetY),
            Number(transform.OffsetZ));

    private static string Number(decimal? value) =>
        value?.ToString("G29", CultureInfo.InvariantCulture) ?? "null";

    private static string ComputeSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
