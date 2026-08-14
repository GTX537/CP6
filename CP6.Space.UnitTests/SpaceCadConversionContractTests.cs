using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceCadConversionContractTests
{
    [Fact]
    public void Layer_contract_keeps_visible_default_for_pre_inventory_v1_json()
    {
        var layer = JsonSerializer.Deserialize<SpaceCadIrLayerV1>(
            """{"LayerId":"WALL","Name":"WALL","EntityCount":1}""");

        Assert.True(layer!.IsVisible);
        Assert.Null(layer.Color);
        Assert.Null(layer.LineType);
    }

    [Theory]
    [InlineData(SpaceCadSourceFormat.Dxf)]
    [InlineData(SpaceCadSourceFormat.Dwg)]
    public void Contract_accepts_the_same_versioned_ir_for_dxf_and_dwg(
        SpaceCadSourceFormat format)
    {
        var request = ValidRequest(format);
        var package = ValidPackage(request);

        SpaceCadConversionContract.ValidatePackage(request, package);

        Assert.Equal(SpaceCadIrVersions.SchemaVersion, package.Document.SchemaVersion);
        Assert.Equal(SpaceCadIrVersions.CoordinateSystem, package.Document.CoordinateSystem);
        Assert.Single(package.Entities);
    }

    [Fact]
    public void Request_rejects_empty_scope_and_noncanonical_hash()
    {
        var request = ValidRequest(SpaceCadSourceFormat.Dxf) with
        {
            TenantId = Guid.Empty,
            SourceSha256 = new string('A', 64)
        };

        Assert.Throws<ArgumentException>(
            () => SpaceCadConversionContract.ValidateRequest(request));
    }

    [Fact]
    public void Request_rejects_an_undefined_source_format()
    {
        var request = ValidRequest(SpaceCadSourceFormat.Dxf) with
        {
            SourceFormat = (SpaceCadSourceFormat)999
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => SpaceCadConversionContract.ValidateRequest(request));
    }

    [Fact]
    public void Document_rejects_unknown_units_with_a_guessed_scale()
    {
        var request = ValidRequest(SpaceCadSourceFormat.Dxf);
        var package = ValidPackage(request);
        var document = package.Document with
        {
            Unit = SpaceCadUnit.Unknown,
            ScaleToMillimeters = 1m
        };

        Assert.Throws<InvalidDataException>(
            () => SpaceCadConversionContract.ValidateDocument(request, document));
    }

    [Fact]
    public void Document_rejects_an_undefined_source_unit()
    {
        var request = ValidRequest(SpaceCadSourceFormat.Dxf);
        var document = ValidPackage(request).Document with
        {
            Unit = (SpaceCadUnit)999
        };

        Assert.Throws<InvalidDataException>(
            () => SpaceCadConversionContract.ValidateDocument(request, document));
    }

    [Fact]
    public void Package_rejects_duplicate_source_references()
    {
        var request = ValidRequest(SpaceCadSourceFormat.Dxf);
        var package = ValidPackage(request);
        var duplicate = package.Entities[0] with { RawType = "LINE_COPY" };
        package = package with
        {
            Entities = [package.Entities[0], duplicate],
            Summary = package.Summary with
            {
                EntityCount = 2,
                SupportedEntityCount = 2
            }
        };

        Assert.Throws<InvalidDataException>(
            () => SpaceCadConversionContract.ValidatePackage(request, package));
    }

    [Fact]
    public void Package_rejects_an_entity_that_references_an_unknown_layer()
    {
        var request = ValidRequest(SpaceCadSourceFormat.Dxf);
        var package = ValidPackage(request);
        package = package with
        {
            Entities = [package.Entities[0] with { LayerId = "MISSING" }]
        };

        Assert.Throws<InvalidDataException>(
            () => SpaceCadConversionContract.ValidatePackage(request, package));
    }

    [Fact]
    public void Package_rejects_layer_counts_that_do_not_match_entity_records()
    {
        var request = ValidRequest(SpaceCadSourceFormat.Dxf);
        var package = ValidPackage(request) with
        {
            Layers = [new SpaceCadIrLayerV1("WALL", "WALL", 2)]
        };

        Assert.Throws<InvalidDataException>(
            () => SpaceCadConversionContract.ValidatePackage(request, package));
    }

    [Fact]
    public void Package_rejects_duplicate_block_identifiers()
    {
        var request = ValidRequest(SpaceCadSourceFormat.Dxf);
        var block = new SpaceCadIrBlockV1("H:200", "RACK", false, null, 1);
        var package = ValidPackage(request) with
        {
            Blocks = [block, block],
            Summary = ValidPackage(request).Summary with { BlockCount = 2 }
        };

        Assert.Throws<InvalidDataException>(
            () => SpaceCadConversionContract.ValidatePackage(request, package));
    }

    [Fact]
    public void Unknown_entity_must_remain_explicitly_unsupported()
    {
        var request = ValidRequest(SpaceCadSourceFormat.Dxf);
        var package = ValidPackage(request);
        var invalid = package.Entities[0] with
        {
            Type = SpaceCadIrEntityType.Unknown,
            IsSupported = true
        };

        Assert.Throws<InvalidDataException>(
            () => SpaceCadConversionContract.ValidateEntity(invalid));
    }

    [Fact]
    public void Standalone_records_reject_undefined_enums_and_negative_counts()
    {
        var request = ValidRequest(SpaceCadSourceFormat.Dxf);
        var package = ValidPackage(request);

        Assert.Throws<InvalidDataException>(() =>
            SpaceCadConversionContract.ValidateEntity(
                package.Entities[0] with { Type = (SpaceCadIrEntityType)999 }));
        Assert.Throws<InvalidDataException>(() =>
            SpaceCadConversionContract.ValidateIssue(
                new SpaceCadConversionIssueV1(
                    "SPACE_CAD_TEST",
                    (SpaceCadIssueSeverity)999)));
        Assert.Throws<InvalidDataException>(() =>
            SpaceCadConversionContract.ValidateLayer(
                package.Layers[0] with { EntityCount = -1 }));
        Assert.Throws<InvalidDataException>(() =>
            SpaceCadConversionContract.ValidateSummary(
                package.Summary with { EntityCount = -1 }));
    }

    private static SpaceCadConversionRequest ValidRequest(
        SpaceCadSourceFormat format) =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            new string('a', 64),
            format,
            "contract-test",
            "1.0.0");

    private static SpaceCadIrPackageV1 ValidPackage(
        SpaceCadConversionRequest request)
    {
        var bounds = new SpaceCadBoundsV1(0, 0, 1000, 1000);
        var document = new SpaceCadIrDocumentV1(
            SpaceCadIrVersions.SchemaVersion,
            request.SourceSha256,
            request.SourceFormat,
            "AC1032",
            SpaceCadUnit.Millimeter,
            1m,
            SpaceCadIrVersions.CoordinateSystem,
            bounds,
            request.ConverterId,
            request.ConverterVersion);
        var entity = new SpaceCadIrEntityV1(
            "H:100",
            SpaceCadIrEntityType.Line,
            "LINE",
            "WALL",
            null,
            [new SpaceCadPointV1(0, 0), new SpaceCadPointV1(1000, 1000)],
            null,
            null,
            null,
            SpaceCadAffineTransformV1.Identity,
            bounds,
            false,
            true,
            new Dictionary<string, string>());
        return new SpaceCadIrPackageV1(
            document,
            [new SpaceCadIrLayerV1("WALL", "WALL", 1)],
            [],
            [entity],
            [],
            new SpaceCadIrSummaryV1(1, 0, 1, 1, 0, 0, bounds));
    }
}
