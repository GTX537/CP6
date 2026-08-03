using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceCadCoordinatePreparationTests
{
    [Fact]
    public void Analysis_exposes_detected_unit_bounds_and_requires_confirmation()
    {
        var request = Request();
        var package = Package(request, startX: 0, endX: 2_000);

        var result = SpaceCadCoordinatePreparation.Analyze(request, package);

        Assert.Equal(SpaceCadUnit.Millimeter, result.SuggestedUnit);
        Assert.Equal(1m, result.SuggestedScaleToMillimeters);
        Assert.Equal(package.Document.Bounds, result.SuggestedBoundsMillimeters);
        Assert.True(result.IsSuggestedExtentPlausible);
        Assert.True(result.RequiresUnitConfirmation);
        Assert.DoesNotContain(
            result.Issues,
            issue => issue.Severity == SpaceCadIssueSeverity.Blocking);
    }

    [Fact]
    public void Analysis_blocks_an_unknown_unit_without_guessing_a_scale()
    {
        var request = Request();
        var package = Package(
            request,
            unit: SpaceCadUnit.Unknown,
            scaleToMillimeters: null,
            startX: 0,
            endX: 2_000);

        var result = SpaceCadCoordinatePreparation.Analyze(request, package);

        Assert.Null(result.SuggestedScaleToMillimeters);
        Assert.Null(result.SuggestedBoundsMillimeters);
        Assert.False(result.IsSuggestedExtentPlausible);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "SPACE_CAD_UNIT_UNKNOWN"
                     && issue.Severity == SpaceCadIssueSeverity.Blocking);
    }

    [Fact]
    public void Analysis_reports_raw_source_bounds_separately_from_suggested_millimeters()
    {
        var request = Request();
        var package = Package(
            request,
            unit: SpaceCadUnit.Inch,
            scaleToMillimeters: 25.4m,
            startX: 0,
            endX: 25_400);

        var result = SpaceCadCoordinatePreparation.Analyze(request, package);

        Assert.Equal(new SpaceCadBoundsV1(0, 0, 1_000, 0), result.SourceBounds);
        Assert.Equal(new SpaceCadBoundsV1(0, 0, 25_400, 0), result.SuggestedBoundsMillimeters);
    }

    [Fact]
    public void Preparation_rejects_an_unconfirmed_unit()
    {
        var request = Request();
        var package = Package(request);
        var confirmation = Confirmation(request.SourceSha256) with { UnitConfirmed = false };

        Assert.Throws<InvalidDataException>(
            () => SpaceCadCoordinatePreparation.Prepare(request, package, confirmation));
    }

    [Fact]
    public void Preparation_corrects_scale_then_applies_source_origin_and_counterclockwise_rotation()
    {
        var request = Request();
        var package = Package(request, startX: 100, endX: 300);
        var confirmation = Confirmation(request.SourceSha256) with
        {
            ConfirmedUnit = SpaceCadUnit.Centimeter,
            SourceOriginInSourceUnits = new SpaceCadPointV1(100, 0),
            FloorOriginMillimeters = new SpaceCadMillimeterPointV1(1_000, 2_000),
            RotationZDegrees = 90,
        };

        var result = SpaceCadCoordinatePreparation.Prepare(request, package, confirmation);

        Assert.True(result.ReadyForParsing);
        Assert.Equal(SpaceCadUnit.Millimeter, result.Metadata.DetectedUnit);
        Assert.Equal(SpaceCadUnit.Centimeter, result.Metadata.ConfirmedUnit);
        Assert.Equal(10m, result.Metadata.ConfirmedScaleToMillimeters);
        Assert.Equal(new SpaceCadPointV1(1_000, 2_000), result.Package.Entities[0].Points[0]);
        Assert.Equal(new SpaceCadPointV1(1_000, 4_000), result.Package.Entities[0].Points[1]);
        Assert.Equal(new SpaceCadBoundsV1(1_000, 2_000, 1_000, 4_000), result.Package.Document.Bounds);
        Assert.Equal(SpaceCadUnit.Centimeter, result.Package.Document.Unit);
        Assert.Equal(10m, result.Package.Document.ScaleToMillimeters);
        Assert.Equal(SpaceCadAffineTransformV1.Identity, result.Package.Entities[0].Transform);
        Assert.Matches("^[0-9a-f]{64}$", result.Metadata.TransformSha256);
    }

    [Fact]
    public void Preparation_composes_floor_transform_for_block_references()
    {
        var request = Request();
        var package = Package(request, startX: 100, endX: 300);
        var sourceEntity = package.Entities[0] with
        {
            Type = SpaceCadIrEntityType.BlockReference,
            RawType = "INSERT",
            BlockName = "RACK",
            Points = [new SpaceCadPointV1(100, 0)],
            Transform = new SpaceCadAffineTransformV1(1, 0, 0, 1, 100, 0, 0),
            Bounds = new SpaceCadBoundsV1(100, 0, 100, 0),
        };
        package = package with
        {
            Entities = [sourceEntity],
            Document = package.Document with { Bounds = sourceEntity.Bounds },
            Summary = package.Summary with { Bounds = sourceEntity.Bounds },
        };
        var confirmation = Confirmation(request.SourceSha256) with
        {
            ConfirmedUnit = SpaceCadUnit.Centimeter,
            SourceOriginInSourceUnits = new SpaceCadPointV1(100, 0),
            FloorOriginMillimeters = new SpaceCadMillimeterPointV1(1_000, 2_000),
            RotationZDegrees = 90,
        };

        var result = SpaceCadCoordinatePreparation.Prepare(
            request,
            package,
            confirmation,
            new SpaceCadCoordinateLimitsV1(MinimumFloorSpanMillimeters: 1));

        Assert.Equal(new SpaceCadPointV1(1_000, 2_000), result.Package.Entities[0].Points[0]);
        Assert.Equal(
            new SpaceCadAffineTransformV1(0, -10, 10, 0, 1_000, 2_000, 0),
            result.Package.Entities[0].Transform);
    }

    [Fact]
    public void Preparation_blocks_geometry_outside_the_assigned_floor_boundary()
    {
        var request = Request();
        var package = Package(request, startX: 0, endX: 2_000);
        var target = Confirmation(request.SourceSha256).TargetFloor with
        {
            BoundaryBounds = new SpaceCadBoundsV1(0, -100, 1_500, 100),
        };

        var result = SpaceCadCoordinatePreparation.Prepare(
            request,
            package,
            Confirmation(request.SourceSha256) with { TargetFloor = target });

        Assert.False(result.ReadyForParsing);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "SPACE_CAD_FLOOR_BOUNDARY_EXCEEDED"
                     && issue.Severity == SpaceCadIssueSeverity.Blocking);
    }

    [Fact]
    public void Preparation_is_deterministic_for_the_same_confirmation()
    {
        var request = Request();
        var package = Package(request, startX: 0, endX: 2_000);
        var confirmation = Confirmation(request.SourceSha256) with
        {
            RotationZDegrees = -90,
            FloorOriginMillimeters = new SpaceCadMillimeterPointV1(5_000, 5_000),
        };

        var first = SpaceCadCoordinatePreparation.Prepare(request, package, confirmation);
        var second = SpaceCadCoordinatePreparation.Prepare(request, package, confirmation);

        Assert.Equal(first.Metadata.TransformSha256, second.Metadata.TransformSha256);
        Assert.Equal(first.Metadata, second.Metadata);
        Assert.Equal(first.Package.Document.Bounds, second.Package.Document.Bounds);
        Assert.Equal(first.Package.Entities[0].Points, second.Package.Entities[0].Points);
    }

    [Fact]
    public void Preparation_rejects_a_noncanonical_target_floor_coordinate_system()
    {
        var request = Request();
        var package = Package(request);
        var target = Confirmation(request.SourceSha256).TargetFloor with
        {
            CoordinateSystem = "Y_UP",
        };

        Assert.Throws<InvalidDataException>(
            () => SpaceCadCoordinatePreparation.Prepare(
                request,
                package,
                Confirmation(request.SourceSha256) with { TargetFloor = target }));
    }

    [Fact]
    public void Cad_source_cannot_begin_parsing_before_coordinate_confirmation()
    {
        var source = CadSource(Request().SourceSha256);

        Assert.Throws<SpaceFileStateException>(() => source.BeginParsing());
    }

    [Fact]
    public void Cad_source_accepts_canonical_coordinate_metadata_and_can_begin_parsing()
    {
        var request = Request();
        var package = Package(request, startX: 0, endX: 2_000);
        var prepared = SpaceCadCoordinatePreparation.Prepare(
            request,
            package,
            Confirmation(request.SourceSha256));
        var metadataJson = SpaceCadCoordinatePreparation.SerializeMetadata(prepared.Metadata);
        var source = CadSource(request.SourceSha256);

        source.ConfigureImport(
            "cp6-coordinate-development/1.0.0",
            mappingProfileId: null,
            mappingProfileVersion: null,
            prepared.Metadata.ConfirmedUnit.ToString(),
            prepared.Metadata.ConfirmedScaleToMillimeters,
            metadataJson);
        source.BeginParsing();

        Assert.Equal(SpaceSourceState.Parsing, source.State);
        Assert.Equal("Millimeter", source.Unit);
        Assert.Equal(1m, source.ScaleToMillimeters);
        Assert.Equal(metadataJson, source.TransformJson);
    }

    [Fact]
    public void Cad_source_rejects_coordinate_metadata_from_another_source_hash()
    {
        var request = Request();
        var package = Package(request, startX: 0, endX: 2_000);
        var prepared = SpaceCadCoordinatePreparation.Prepare(
            request,
            package,
            Confirmation(request.SourceSha256));
        var metadataJson = SpaceCadCoordinatePreparation.SerializeMetadata(prepared.Metadata)
            .Replace(request.SourceSha256, new string('b', 64), StringComparison.Ordinal);
        var source = CadSource(request.SourceSha256);

        Assert.Throws<ArgumentException>(
            () => source.ConfigureImport(
                "cp6-coordinate-development/1.0.0",
                null,
                null,
                "Millimeter",
                1m,
                metadataJson));
    }

    [Fact]
    public void Metadata_serializer_rejects_a_tampered_transform_hash()
    {
        var request = Request();
        var prepared = SpaceCadCoordinatePreparation.Prepare(
            request,
            Package(request),
            Confirmation(request.SourceSha256));

        Assert.Throws<InvalidDataException>(
            () => SpaceCadCoordinatePreparation.SerializeMetadata(
                prepared.Metadata with { TransformSha256 = new string('b', 64) }));
    }

    private static SpaceCadConversionRequest Request() =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            new string('a', 64),
            SpaceCadSourceFormat.Dxf,
            "coordinate-test",
            "1.0.0");

    private static SpaceCadIrPackageV1 Package(
        SpaceCadConversionRequest request,
        SpaceCadUnit unit = SpaceCadUnit.Millimeter,
        decimal? scaleToMillimeters = 1m,
        decimal startX = 0,
        decimal endX = 2_000)
    {
        var bounds = new SpaceCadBoundsV1(startX, 0, endX, 0);
        var entity = new SpaceCadIrEntityV1(
            "H:100",
            SpaceCadIrEntityType.Line,
            "LINE",
            "WALL",
            null,
            [new SpaceCadPointV1(startX, 0), new SpaceCadPointV1(endX, 0)],
            null,
            null,
            null,
            SpaceCadAffineTransformV1.Identity,
            bounds,
            false,
            true,
            new Dictionary<string, string>());
        return new SpaceCadIrPackageV1(
            new SpaceCadIrDocumentV1(
                SpaceCadIrVersions.SchemaVersion,
                request.SourceSha256,
                request.SourceFormat,
                "AC1032",
                unit,
                scaleToMillimeters,
                SpaceCadIrVersions.CoordinateSystem,
                bounds,
                request.ConverterId,
                request.ConverterVersion),
            [new SpaceCadIrLayerV1("WALL", "WALL", 1)],
            [],
            [entity],
            [],
            new SpaceCadIrSummaryV1(1, 0, 1, 1, 0, 0, bounds));
    }

    private static SpaceCadCoordinateConfirmationV1 Confirmation(string sourceSha256) =>
        new(
            sourceSha256,
            UnitConfirmed: true,
            SpaceCadUnit.Millimeter,
            new SpaceCadPointV1(0, 0),
            new SpaceCadMillimeterPointV1(0, 0),
            RotationZDegrees: 0,
            new SpaceCadFloorAssignmentV1(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                "F01",
                1,
                0,
                SpaceCadCoordinateVersions.TargetCoordinateSystem,
                new SpaceCadBoundsV1(-100_000, -100_000, 100_000, 100_000)));

    private static SpaceModelSource CadSource(string sha256)
    {
        var tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var file = SpaceFile.CreateUploading(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            tenantId,
            "space/source/coordinate-test.dxf",
            "coordinate-test.dxf",
            "application/dxf",
            SpaceFileRetentionClass.Source);
        file.CompleteQuarantine("application/dxf", ".dxf", 100, sha256);
        file.BeginScanning();
        file.MarkClean("unit-test", "1");
        return SpaceModelSource.CreateFileSource(
            tenantId,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            SpaceSourceType.Dxf,
            file,
            "coordinate-test.dxf");
    }
}
