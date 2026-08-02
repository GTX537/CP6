using CP6.Space.Contracts;

namespace CP6.Space.Application;

public sealed record SpaceExcelTargetFieldDefinition(
    string Sheet,
    string Field,
    string DataType,
    bool Required,
    bool IsBusinessKey,
    string? ReferenceTarget = null);

public static class SpaceExcelTargetCatalog
{
    public const int MappingSchemaVersion = 1;
    public const string TemplateSchemaVersion = "1.0";

    private static readonly SpaceExcelTargetFieldDefinition[] Definitions =
    [
        Text("Racks", "FloorCode", true),
        Text("Racks", "ZoneCode", true),
        Text("Racks", "RackCode", true, key: true),
        Decimal("Racks", "XMm", true),
        Decimal("Racks", "YMm", true),
        Decimal("Racks", "ZMm", false),
        Decimal("Racks", "WidthMm", true),
        Decimal("Racks", "DepthMm", true),
        Decimal("Racks", "HeightMm", true),
        Decimal("Racks", "RotationZDeg", false),
        Text("Racks", "RackTemplateCode", false),
        Text("Racks", "LifecycleStatus", true),

        Text("RackLevels", "RackCode", true, key: true, reference: "Racks.RackCode"),
        Integer("RackLevels", "LevelNo", true, key: true),
        Decimal("RackLevels", "BottomZMm", true),
        Decimal("RackLevels", "ClearHeightMm", true),
        Integer("RackLevels", "BinCount", true),
        Integer("RackLevels", "DepthCount", true),
        Decimal("RackLevels", "LoadCapacityKg", false),
        Text("RackLevels", "LifecycleStatus", true),

        Text("Locations", "LocationCode", true, key: true),
        Text("Locations", "RackCode", true, reference: "Racks.RackCode"),
        Integer("Locations", "ColumnNo", true),
        Integer("Locations", "LevelNo", true, reference: "RackLevels.LevelNo"),
        Integer("Locations", "DepthNo", true),
        Text("Locations", "LifecycleStatus", true),
        Text("Locations", "LocationType", false),

        Text("Bindings", "WmsWarehouseCode", true, key: true),
        Text("Bindings", "ExternalLocationId", true, key: true),
        Text("Bindings", "LocationCode", true, reference: "Locations.LocationCode"),
        Text("Bindings", "BindingMode", false),

        Text("Attributes", "ObjectType", true, key: true),
        Text("Attributes", "BusinessKey", true, key: true),
        Text("Attributes", "Namespace", true, key: true),
        Text("Attributes", "Key", true, key: true),
        Text("Attributes", "Value", true),
        Text("Attributes", "Unit", false),
    ];

    private static readonly IReadOnlyDictionary<string, SpaceExcelTargetFieldDefinition>
        ByKey = Definitions.ToDictionary(
            item => Key(item.Sheet, item.Field),
            StringComparer.Ordinal);

    public static IReadOnlyList<SpaceExcelTargetFieldDefinition> All => Definitions;

    public static IReadOnlyList<string> Sheets => Definitions
        .Select(item => item.Sheet)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public static IReadOnlyList<SpaceExcelTargetFieldDefinition> ForSheet(
        string? sheet) =>
        string.IsNullOrWhiteSpace(sheet)
            ? []
            : Definitions
                .Where(item => string.Equals(
                    item.Sheet,
                    sheet.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

    public static SpaceExcelTargetFieldDefinition? Find(
        string? sheet,
        string? field)
    {
        if (string.IsNullOrWhiteSpace(sheet) || string.IsNullOrWhiteSpace(field))
            return null;
        return ByKey.GetValueOrDefault(Key(sheet.Trim(), field.Trim()));
    }

    private static string Key(string sheet, string field) =>
        $"{sheet.ToUpperInvariant()}:{field.ToUpperInvariant()}";

    private static SpaceExcelTargetFieldDefinition Text(
        string sheet,
        string field,
        bool required,
        bool key = false,
        string? reference = null) =>
        new(sheet, field, "Text", required, key, reference);

    private static SpaceExcelTargetFieldDefinition Integer(
        string sheet,
        string field,
        bool required,
        bool key = false,
        string? reference = null) =>
        new(sheet, field, "Integer", required, key, reference);

    private static SpaceExcelTargetFieldDefinition Decimal(
        string sheet,
        string field,
        bool required,
        bool key = false,
        string? reference = null) =>
        new(sheet, field, "Decimal", required, key, reference);
}

public interface ISpaceExcelMappingService
{
    Task<IReadOnlyList<SpaceExcelMappingProfileDto>> GetProfilesAsync(
        CancellationToken cancellationToken = default);

    Task<SpaceExcelMappingProfileDto> GetProfileAsync(
        Guid profileId,
        int? version = null,
        CancellationToken cancellationToken = default);

    SpaceExcelMappingPreviewDto Preview(
        PreviewSpaceExcelMappingRequest request);

    Task<SaveSpaceExcelMappingProfileResponse> SaveProfileAsync(
        SaveSpaceExcelMappingProfileRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}
