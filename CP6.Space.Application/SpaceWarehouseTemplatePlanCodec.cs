using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public static class SpaceWarehouseTemplatePlanCodec
{
    public const int MaximumFloors = 20;
    public const int MaximumZones = 200;
    public const int MaximumAisles = 1_000;
    public const int MaximumRacks = 5_000;
    public const int MaximumLocations = 100_000;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public static SpaceWarehouseTemplateInstantiationPreviewDto Seal(
        Guid templateId,
        Guid templateVersionId,
        int schemaVersion,
        IReadOnlyList<SpaceWarehouseTemplateFloorPlanDto>? floors,
        IReadOnlyList<SpaceWarehouseTemplateZonePlanDto>? zones,
        IReadOnlyList<SpaceWarehouseTemplateAislePlanDto>? aisles,
        IReadOnlyList<SpaceWarehouseTemplateRackPlanDto>? racks)
    {
        if (templateId == Guid.Empty || templateVersionId == Guid.Empty)
            throw new ArgumentException("Template identities are required.");
        if (schemaVersion != SpaceWarehouseTemplateContract.SchemaVersion)
        {
            throw new ArgumentException(
                $"Template schemaVersion must be {SpaceWarehouseTemplateContract.SchemaVersion}.",
                nameof(schemaVersion));
        }

        var normalizedFloors = NormalizeFloors(floors);
        var normalizedZones = NormalizeZones(zones);
        var normalizedAisles = NormalizeAisles(aisles);
        var normalizedRacks = NormalizeRacks(racks);
        ValidateGraph(
            normalizedFloors,
            normalizedZones,
            normalizedAisles,
            normalizedRacks);

        int locationCount;
        try
        {
            locationCount = normalizedRacks.Sum(rack => checked(
                rack.Columns * rack.Levels * rack.Depths));
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException(
                "Template location count exceeds the supported range.",
                nameof(racks),
                exception);
        }
        if (locationCount > MaximumLocations)
        {
            throw new ArgumentException(
                $"Template cannot exceed {MaximumLocations} locations.",
                nameof(racks));
        }

        var counts = new SpaceWarehouseTemplateCountsDto(
            normalizedFloors.Length,
            normalizedZones.Length,
            normalizedAisles.Length,
            normalizedRacks.Length,
            locationCount);
        var contentJson = SerializeContent(
            schemaVersion,
            normalizedFloors,
            normalizedZones,
            normalizedAisles,
            normalizedRacks,
            locationCount);
        var contentHash = Sha256(contentJson);
        var proposalHash = Sha256(
            $"space-template-preview-v1\n{templateId:D}\n" +
            $"{templateVersionId:D}\n{contentHash}");
        return new SpaceWarehouseTemplateInstantiationPreviewDto(
            schemaVersion,
            templateId,
            templateVersionId,
            contentHash,
            proposalHash,
            counts,
            normalizedFloors,
            normalizedZones,
            normalizedAisles,
            normalizedRacks,
            WritesDraft: false);
    }

    public static SpaceWarehouseTemplateInstantiationPreviewDto ReadAndSeal(
        Guid templateId,
        Guid templateVersionId,
        string contentJson,
        string expectedContentHash)
    {
        PersistedPlan? plan;
        try
        {
            plan = JsonSerializer.Deserialize<PersistedPlan>(contentJson, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Tenant warehouse template content is invalid.",
                exception);
        }
        if (plan is null)
            throw new InvalidDataException("Tenant warehouse template content is missing.");

        SpaceWarehouseTemplateInstantiationPreviewDto preview;
        try
        {
            preview = Seal(
                templateId,
                templateVersionId,
                plan.SchemaVersion,
                plan.Floors,
                plan.Zones,
                plan.Aisles,
                plan.Racks);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Tenant warehouse template content failed validation.",
                exception);
        }

        if (plan.LocationCount != preview.Counts.Locations ||
            !string.Equals(
                preview.TemplateContentHash,
                expectedContentHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Tenant warehouse template content hash or counts do not match its immutable version.");
        }
        return preview;
    }

    public static string SerializeContent(
        SpaceWarehouseTemplateInstantiationPreviewDto preview) =>
        SerializeContent(
            preview.SchemaVersion,
            preview.Floors,
            preview.Zones,
            preview.Aisles,
            preview.Racks,
            preview.Counts.Locations);

    private static string SerializeContent(
        int schemaVersion,
        IReadOnlyList<SpaceWarehouseTemplateFloorPlanDto> floors,
        IReadOnlyList<SpaceWarehouseTemplateZonePlanDto> zones,
        IReadOnlyList<SpaceWarehouseTemplateAislePlanDto> aisles,
        IReadOnlyList<SpaceWarehouseTemplateRackPlanDto> racks,
        int locationCount) =>
        JsonSerializer.Serialize(
            new PersistedPlan(
                schemaVersion,
                floors,
                zones,
                aisles,
                racks,
                locationCount),
            JsonOptions);

    private static SpaceWarehouseTemplateFloorPlanDto[] NormalizeFloors(
        IReadOnlyList<SpaceWarehouseTemplateFloorPlanDto>? values)
    {
        if (values is null || values.Count is < 1 or > MaximumFloors)
        {
            throw new ArgumentException(
                $"Template must contain 1 to {MaximumFloors} floors.",
                nameof(values));
        }
        return values.Select(value =>
            new SpaceWarehouseTemplateFloorPlanDto(
                Required(value.Key, 200, "floor.key"),
                Required(value.FloorCode, 100, "floor.floorCode"),
                Required(value.Name, 200, "floor.name"),
                value.Level,
                value.Elevation,
                Positive(value.Width, "floor.width"),
                Positive(value.Depth, "floor.depth"),
                Positive(value.Height, "floor.height")))
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static SpaceWarehouseTemplateZonePlanDto[] NormalizeZones(
        IReadOnlyList<SpaceWarehouseTemplateZonePlanDto>? values)
    {
        if (values is null || values.Count > MaximumZones)
            throw new ArgumentException($"Template cannot exceed {MaximumZones} zones.");
        return values.Select(value =>
            new SpaceWarehouseTemplateZonePlanDto(
                Required(value.Key, 200, "zone.key"),
                Required(value.FloorKey, 200, "zone.floorKey"),
                Required(value.ZoneCode, 100, "zone.zoneCode"),
                NormalizeZoneType(value.ZoneType),
                value.MinX,
                value.MinY,
                value.MaxX,
                value.MaxY))
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static SpaceWarehouseTemplateAislePlanDto[] NormalizeAisles(
        IReadOnlyList<SpaceWarehouseTemplateAislePlanDto>? values)
    {
        if (values is null || values.Count > MaximumAisles)
            throw new ArgumentException($"Template cannot exceed {MaximumAisles} aisles.");
        return values.Select(value =>
            new SpaceWarehouseTemplateAislePlanDto(
                Required(value.Key, 200, "aisle.key"),
                Required(value.FloorKey, 200, "aisle.floorKey"),
                Required(value.ZoneKey, 200, "aisle.zoneKey"),
                Required(value.AisleCode, 100, "aisle.aisleCode"),
                value.StartX,
                value.StartY,
                value.EndX,
                value.EndY))
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static SpaceWarehouseTemplateRackPlanDto[] NormalizeRacks(
        IReadOnlyList<SpaceWarehouseTemplateRackPlanDto>? values)
    {
        if (values is null || values.Count > MaximumRacks)
            throw new ArgumentException($"Template cannot exceed {MaximumRacks} racks.");
        return values.Select(value =>
            new SpaceWarehouseTemplateRackPlanDto(
                Required(value.Key, 200, "rack.key"),
                Required(value.FloorKey, 200, "rack.floorKey"),
                Required(value.ZoneKey, 200, "rack.zoneKey"),
                Required(value.AisleKey, 200, "rack.aisleKey"),
                Required(value.RackCode, 100, "rack.rackCode"),
                value.X,
                value.Y,
                NonNegative(value.Z, "rack.z"),
                value.RotationZ,
                Positive(value.Width, "rack.width"),
                Positive(value.Depth, "rack.depth"),
                Positive(value.Height, "rack.height"),
                Positive(value.Columns, "rack.columns"),
                Positive(value.Levels, "rack.levels"),
                Positive(value.Depths, "rack.depths")))
            .OrderBy(value => value.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateGraph(
        IReadOnlyList<SpaceWarehouseTemplateFloorPlanDto> floors,
        IReadOnlyList<SpaceWarehouseTemplateZonePlanDto> zones,
        IReadOnlyList<SpaceWarehouseTemplateAislePlanDto> aisles,
        IReadOnlyList<SpaceWarehouseTemplateRackPlanDto> racks)
    {
        RequireUnique(floors.Select(value => value.Key), "floor keys");
        RequireUnique(floors.Select(value => value.FloorCode), "floor codes", true);
        RequireUnique(zones.Select(value => value.Key), "zone keys");
        RequireUnique(aisles.Select(value => value.Key), "aisle keys");
        RequireUnique(racks.Select(value => value.Key), "rack keys");
        RequireUnique(
            zones.Select(value => $"{value.FloorKey}\n{value.ZoneCode}"),
            "zone codes within a floor",
            true);
        RequireUnique(
            aisles.Select(value => $"{value.FloorKey}\n{value.AisleCode}"),
            "aisle codes within a floor",
            true);
        RequireUnique(
            racks.Select(value => $"{value.FloorKey}\n{value.RackCode}"),
            "rack codes within a floor",
            true);

        var layoutKeys = zones.Select(value => value.Key)
            .Concat(aisles.Select(value => value.Key))
            .Concat(racks.Select(value => value.Key));
        RequireUnique(layoutKeys, "layout object keys");

        var floorKeys = floors.Select(value => value.Key).ToHashSet(StringComparer.Ordinal);
        var zoneByKey = zones.ToDictionary(value => value.Key, StringComparer.Ordinal);
        var aisleByKey = aisles.ToDictionary(value => value.Key, StringComparer.Ordinal);
        foreach (var zone in zones)
        {
            if (!floorKeys.Contains(zone.FloorKey))
                throw new ArgumentException($"Zone {zone.Key} references an unknown floor.");
            if (zone.MinX >= zone.MaxX || zone.MinY >= zone.MaxY)
                throw new ArgumentException($"Zone {zone.Key} has invalid bounds.");
        }
        foreach (var aisle in aisles)
        {
            if (!zoneByKey.TryGetValue(aisle.ZoneKey, out var zone) ||
                zone.FloorKey != aisle.FloorKey)
            {
                throw new ArgumentException($"Aisle {aisle.Key} has an invalid parent chain.");
            }
            if (aisle.StartX == aisle.EndX && aisle.StartY == aisle.EndY)
                throw new ArgumentException($"Aisle {aisle.Key} must have a non-zero centerline.");
            try
            {
                const int halfWidth = 1_500;
                _ = checked(Math.Min(aisle.StartX, aisle.EndX) - halfWidth);
                _ = checked(Math.Min(aisle.StartY, aisle.EndY) - halfWidth);
                _ = checked(Math.Max(aisle.StartX, aisle.EndX) + halfWidth);
                _ = checked(Math.Max(aisle.StartY, aisle.EndY) + halfWidth);
            }
            catch (OverflowException exception)
            {
                throw new ArgumentException(
                    $"Aisle {aisle.Key} coordinates exceed the supported range.",
                    exception);
            }
        }
        foreach (var rack in racks)
        {
            if (!zoneByKey.TryGetValue(rack.ZoneKey, out var zone) ||
                !aisleByKey.TryGetValue(rack.AisleKey, out var aisle) ||
                zone.FloorKey != rack.FloorKey ||
                aisle.FloorKey != rack.FloorKey ||
                aisle.ZoneKey != rack.ZoneKey)
            {
                throw new ArgumentException($"Rack {rack.Key} has an invalid parent chain.");
            }
            if (rack.Height % rack.Levels != 0 ||
                rack.Width % rack.Columns != 0 ||
                rack.Depth % rack.Depths != 0 ||
                rack.Height / rack.Levels <= 100)
            {
                throw new ArgumentException(
                    $"Rack {rack.Key} dimensions cannot form its declared cells.");
            }
        }
        foreach (var floor in floors)
        {
            var commandCount = zones.Count(value => value.FloorKey == floor.Key) +
                aisles.Count(value => value.FloorKey == floor.Key) +
                racks.Count(value => value.FloorKey == floor.Key);
            if (zones.All(value => value.FloorKey != floor.Key) ||
                aisles.All(value => value.FloorKey != floor.Key) ||
                racks.All(value => value.FloorKey != floor.Key))
            {
                throw new ArgumentException(
                    $"Floor {floor.Key} must contain at least one zone, aisle and rack.");
            }
            if (commandCount > SpaceBuiltInWarehouseTemplates.MaximumFloorCommandCount)
            {
                throw new ArgumentException(
                    $"Floor {floor.Key} exceeds the {SpaceBuiltInWarehouseTemplates.MaximumFloorCommandCount} command Apply limit.");
            }
        }
    }

    private static void RequireUnique(
        IEnumerable<string> values,
        string label,
        bool ignoreCase = false)
    {
        var comparer = ignoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        if (values.Any(value => !seen.Add(value)))
            throw new ArgumentException($"Template {label} must be unique.");
    }

    private static string Required(string? value, int maximumLength, string label)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 ||
            normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
            throw new ArgumentException($"{label} must contain 1 to {maximumLength} characters.");
        return normalized;
    }

    private static int Positive(int value, string label)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(label, $"{label} must be positive.");
        return value;
    }

    private static int NonNegative(int value, string label)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(label, $"{label} cannot be negative.");
        return value;
    }

    private static string NormalizeZoneType(string? value)
    {
        var normalized = value?.Trim();
        return normalized?.ToUpperInvariant() switch
        {
            "RECEIVING" => "Receiving",
            "STORAGE" => "Storage",
            "SHIPPING" => "Shipping",
            "PICKING" => "Picking",
            "PACKING" => "Packing",
            "OTHER" => "Other",
            _ => throw new ArgumentException(
                "zone.zoneType must be Receiving, Storage, Shipping, Picking, Packing or Other."),
        };
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record PersistedPlan(
        int SchemaVersion,
        IReadOnlyList<SpaceWarehouseTemplateFloorPlanDto> Floors,
        IReadOnlyList<SpaceWarehouseTemplateZonePlanDto> Zones,
        IReadOnlyList<SpaceWarehouseTemplateAislePlanDto> Aisles,
        IReadOnlyList<SpaceWarehouseTemplateRackPlanDto> Racks,
        int LocationCount);
}
