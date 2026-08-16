using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class SpaceBuiltInWarehouseTemplates
{
    public const int SchemaVersion = 1;

    private static readonly Lazy<CatalogEntry> StandardWarehouse =
        new(CreateStandardWarehouse, LazyThreadSafetyMode.ExecutionAndPublication);

    public static Guid StandardWarehouseTemplateId =>
        StandardWarehouse.Value.Template.Id;

    public static Guid StandardWarehouseVersionId =>
        StandardWarehouse.Value.Template.LatestVersion.Id;

    public static IReadOnlyList<SpaceWarehouseTemplateDto> List() =>
        [StandardWarehouse.Value.Template];

    public static bool TryPreview(
        Guid templateId,
        Guid templateVersionId,
        out SpaceWarehouseTemplateInstantiationPreviewDto? preview)
    {
        var entry = StandardWarehouse.Value;
        if (entry.Template.Id != templateId ||
            entry.Template.LatestVersion.Id != templateVersionId)
        {
            preview = null;
            return false;
        }

        preview = entry.Preview;
        return true;
    }

    private static CatalogEntry CreateStandardWarehouse()
    {
        var dataset = SpaceStandardWarehouseDatasetGenerator.Generate();
        var templateId = SpaceStandardWarehouseDatasetGenerator.CreateDeterministicId(
            "warehouse-template:system:standard-warehouse");
        var templateVersionId =
            SpaceStandardWarehouseDatasetGenerator.CreateDeterministicId(
                "warehouse-template-version:system:standard-warehouse:v1");

        var floors = dataset.Floors.Select(value =>
            new SpaceWarehouseTemplateFloorPlanDto(
                value.ExpectedId,
                value.Code,
                value.Name,
                value.Level,
                CheckedInt(value.OriginZmm, nameof(value.OriginZmm)),
                CheckedInt(value.WidthMm, nameof(value.WidthMm)),
                CheckedInt(value.DepthMm, nameof(value.DepthMm)),
                CheckedInt(value.HeightMm, nameof(value.HeightMm))))
            .ToArray();
        var zones = dataset.Zones.Select(value =>
            new SpaceWarehouseTemplateZonePlanDto(
                value.ExpectedId,
                value.FloorExpectedId,
                value.Code,
                value.ZoneType,
                CheckedInt(value.MinXmm, nameof(value.MinXmm)),
                CheckedInt(value.MinYmm, nameof(value.MinYmm)),
                CheckedInt(value.MaxXmm, nameof(value.MaxXmm)),
                CheckedInt(value.MaxYmm, nameof(value.MaxYmm))))
            .ToArray();
        var aisles = dataset.Aisles.Select(value =>
            new SpaceWarehouseTemplateAislePlanDto(
                value.ExpectedId,
                value.FloorExpectedId,
                value.ZoneExpectedId,
                value.Code,
                CheckedInt(value.StartXmm, nameof(value.StartXmm)),
                CheckedInt(value.StartYmm, nameof(value.StartYmm)),
                CheckedInt(value.EndXmm, nameof(value.EndXmm)),
                CheckedInt(value.EndYmm, nameof(value.EndYmm))))
            .ToArray();
        var racks = dataset.Racks.Select(value =>
            new SpaceWarehouseTemplateRackPlanDto(
                value.ExpectedId,
                value.FloorExpectedId,
                value.ZoneExpectedId,
                value.AisleExpectedId,
                value.Code,
                CheckedInt(value.Xmm, nameof(value.Xmm)),
                CheckedInt(value.Ymm, nameof(value.Ymm)),
                CheckedInt(value.Zmm, nameof(value.Zmm)),
                value.RotationDegrees,
                CheckedInt(value.WidthMm, nameof(value.WidthMm)),
                CheckedInt(value.DepthMm, nameof(value.DepthMm)),
                CheckedInt(value.HeightMm, nameof(value.HeightMm)),
                value.Columns,
                value.Levels,
                value.Depths))
            .ToArray();
        var counts = new SpaceWarehouseTemplateCountsDto(
            floors.Length,
            zones.Length,
            aisles.Length,
            racks.Length,
            dataset.Locations.Count);
        var canonical = JsonSerializer.Serialize(
            new
            {
                schemaVersion = SchemaVersion,
                floors,
                zones,
                aisles,
                racks,
                locationCount = dataset.Locations.Count,
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var contentHash = Sha256(canonical);
        var proposalHash = Sha256(
            $"space-template-preview-v1\n{templateId:D}\n" +
            $"{templateVersionId:D}\n{contentHash}");
        var version = new SpaceWarehouseTemplateVersionDto(
            templateVersionId,
            VersionNo: 1,
            SchemaVersion,
            contentHash,
            counts,
            Status: "Ready");
        var template = new SpaceWarehouseTemplateDto(
            templateId,
            Scope: "System",
            SpaceStandardWarehouseDatasetContract.WarehouseCode,
            Name: "CP6 标准货架仓",
            Description:
                "平台内置的双层标准货架仓模板；500 个货架、10,000 个库位。",
            Status: "Active",
            version);
        var preview = new SpaceWarehouseTemplateInstantiationPreviewDto(
            SchemaVersion,
            templateId,
            templateVersionId,
            contentHash,
            proposalHash,
            counts,
            floors,
            zones,
            aisles,
            racks,
            WritesDraft: false);
        return new CatalogEntry(template, preview);
    }

    private static int CheckedInt(decimal value, string parameterName)
    {
        if (value != decimal.Truncate(value) ||
            value is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"{parameterName} is not an integer millimeter value.");
        }
        return decimal.ToInt32(value);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record CatalogEntry(
        SpaceWarehouseTemplateDto Template,
        SpaceWarehouseTemplateInstantiationPreviewDto Preview);
}
