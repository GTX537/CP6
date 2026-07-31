using System.Globalization;
using System.Text.Json;

namespace CP6.Space.Domain;

public static class SpaceElementTypes
{
    public const string Wall = "Wall";
    public const string Column = "Column";
    public const string Door = "Door";
    public const string Dock = "Dock";
    public const string Stair = "Stair";
    public const string Elevator = "Elevator";
    public const string Pallet = "Pallet";
    public const string Device = "Device";
    public const string Workstation = "Workstation";
    public const string Conveyor = "Conveyor";
    public const string StaticEquipment = "StaticEquipment";
    public const string Annotation = "Annotation";
    public const string Dimension = "Dimension";
    public const string Guide = "Guide";
    public const string RestrictedArea = "RestrictedArea";
    public const string Decoration = "Decoration";
    public const string ImportedReference = "ImportedReference";

    private static readonly IReadOnlyDictionary<string, string> Canonical =
        new[]
        {
            Wall,
            Column,
            Door,
            Dock,
            Stair,
            Elevator,
            Pallet,
            Device,
            Workstation,
            Conveyor,
            StaticEquipment,
            Annotation,
            Dimension,
            Guide,
            RestrictedArea,
            Decoration,
            ImportedReference,
        }.ToDictionary(value => value, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> Supported { get; } =
        Canonical.Values.ToArray();

    internal static string Normalize(string value, string parameterName)
    {
        var normalized = value?.Trim();
        if (normalized is null || !Canonical.TryGetValue(normalized, out var canonical))
        {
            throw new ArgumentException(
                "Unsupported Space element type.",
                parameterName);
        }

        return canonical;
    }
}

public static class SpaceElementAttributeValueTypes
{
    public const string String = "String";
    public const string Integer = "Integer";
    public const string Decimal = "Decimal";
    public const string Boolean = "Boolean";
    public const string DateTime = "DateTime";
    public const string Guid = "Guid";
    public const string Json = "Json";

    private static readonly IReadOnlyDictionary<string, string> Canonical =
        new[]
        {
            String,
            Integer,
            Decimal,
            Boolean,
            DateTime,
            Guid,
            Json,
        }.ToDictionary(value => value, StringComparer.OrdinalIgnoreCase);

    internal static (string ValueType, string Value, string? Unit) Normalize(
        string valueType,
        string? value,
        string? unit)
    {
        var requestedType = valueType?.Trim();
        if (requestedType is null
            || !Canonical.TryGetValue(requestedType, out var canonicalType))
        {
            throw new ArgumentException(
                "Unsupported element attribute value type.",
                nameof(valueType));
        }

        var normalizedValue = NormalizeValue(canonicalType, value);
        var normalizedUnit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim();
        if (normalizedUnit?.Length > 50)
        {
            throw new ArgumentException(
                "Attribute unit cannot exceed 50 characters.",
                nameof(unit));
        }

        if (normalizedUnit is not null
            && canonicalType is not (Integer or Decimal))
        {
            throw new ArgumentException(
                "Only numeric element attributes may declare a unit.",
                nameof(unit));
        }

        return (canonicalType, normalizedValue, normalizedUnit);
    }

    private static string NormalizeValue(string valueType, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8000)
        {
            throw new ArgumentException(
                "Element attribute value is required and cannot exceed 8000 characters.",
                nameof(value));
        }

        var normalized = value.Trim();
        return valueType switch
        {
            String => normalized,
            Integer => long.TryParse(
                normalized,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var integer)
                ? integer.ToString(CultureInfo.InvariantCulture)
                : throw InvalidValue(valueType),
            Decimal => decimal.TryParse(
                normalized,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var decimalValue)
                ? decimalValue.ToString("G29", CultureInfo.InvariantCulture)
                : throw InvalidValue(valueType),
            Boolean => bool.TryParse(normalized, out var boolean)
                ? boolean ? "true" : "false"
                : throw InvalidValue(valueType),
            DateTime => DateTimeOffset.TryParse(
                normalized,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dateTime)
                ? dateTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
                : throw InvalidValue(valueType),
            Guid => System.Guid.TryParse(normalized, out var guid)
                && guid != System.Guid.Empty
                ? guid.ToString("D")
                : throw InvalidValue(valueType),
            Json => NormalizeJson(normalized),
            _ => throw new InvalidOperationException(
                "Element attribute type is not implemented."),
        };
    }

    private static string NormalizeJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "Element attribute JSON value is invalid.",
                nameof(value),
                exception);
        }
    }

    private static ArgumentException InvalidValue(string valueType) =>
        new(
            $"Element attribute value is not a valid {valueType}.",
            "value");
}

public static class SpaceElementAttributeNamespaces
{
    public const string Design = "design";
    public const string Owner = "owner";
    public const string Lot = "lot";
    public const string Container = "container";
    public const string Manufacturer = "manufacturer";
    public const string ExternalReference = "external-reference";

    private static readonly string[] RuntimeNamespaceRoots =
    [
        "inventory",
        "stock",
        "task",
        "runtime",
    ];

    internal static string Normalize(string value, string parameterName)
    {
        var normalized = SpaceRevisionValue.RequiredText(
            value,
            100,
            parameterName);
        if (RuntimeNamespaceRoots.Any(root =>
                normalized.Equals(root, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith($"{root}.", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Runtime inventory and task state cannot be stored as an element attribute.",
                parameterName);
        }

        return normalized;
    }
}

internal static class SpaceElementGeometry
{
    private const int SupportedSchemaVersion = 1;

    public static string Validate(string value, string parameterName)
    {
        var json = SpaceRevisionValue.Json(value, parameterName);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(
                    parameterName,
                    "Element geometry must be a JSON object.");
            }

            if (!root.TryGetProperty("schemaVersion", out var schemaVersion)
                || schemaVersion.ValueKind != JsonValueKind.Number
                || !schemaVersion.TryGetInt32(out var version)
                || version != SupportedSchemaVersion)
            {
                throw Invalid(
                    parameterName,
                    $"Element geometry schemaVersion must be {SupportedSchemaVersion}.");
            }

            if (!root.TryGetProperty("kind", out var kindValue)
                || kindValue.ValueKind != JsonValueKind.String)
            {
                throw Invalid(
                    parameterName,
                    "Element geometry kind is required.");
            }

            var kind = kindValue.GetString();
            switch (kind)
            {
                case "point":
                    RequirePoint(root, parameterName);
                    break;
                case "path":
                    RequirePoints(root, "points", 2, parameterName);
                    RequirePositiveInteger(root, "width", parameterName);
                    break;
                case "polygon":
                    RequirePoints(root, "outer", 3, parameterName);
                    RequirePolygonHoles(root, parameterName);
                    RequirePositiveInteger(root, "height", parameterName);
                    break;
                case "box":
                    RequirePositiveInteger(root, "width", parameterName);
                    RequirePositiveInteger(root, "height", parameterName);
                    RequirePositiveInteger(root, "depth", parameterName);
                    break;
                case "asset":
                    RequireAsset(root, parameterName);
                    break;
                default:
                    throw Invalid(
                        parameterName,
                        "Unsupported element geometry kind.");
            }

            return json;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "Element geometry JSON is invalid.",
                parameterName,
                exception);
        }
    }

    private static void RequirePoint(JsonElement root, string parameterName)
    {
        RequireInteger(root, "x", parameterName);
        RequireInteger(root, "y", parameterName);
        RequireInteger(root, "z", parameterName);
    }

    private static void RequirePoints(
        JsonElement root,
        string propertyName,
        int minimumCount,
        string parameterName)
    {
        if (!root.TryGetProperty(propertyName, out var points)
            || points.ValueKind != JsonValueKind.Array
            || points.GetArrayLength() < minimumCount)
        {
            throw Invalid(
                parameterName,
                $"Geometry '{propertyName}' requires at least {minimumCount} points.");
        }

        foreach (var point in points.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(parameterName, "Geometry points must be objects.");
            }

            RequireInteger(point, "x", parameterName);
            RequireInteger(point, "y", parameterName);
            if (point.TryGetProperty("z", out var z)
                && (z.ValueKind != JsonValueKind.Number || !z.TryGetInt32(out _)))
            {
                throw Invalid(
                    parameterName,
                    "Geometry point z must be an integer millimeter value.");
            }
        }
    }

    private static void RequirePolygonHoles(JsonElement root, string parameterName)
    {
        if (!root.TryGetProperty("holes", out var holes))
        {
            return;
        }

        if (holes.ValueKind != JsonValueKind.Array)
        {
            throw Invalid(parameterName, "Geometry holes must be an array.");
        }

        foreach (var hole in holes.EnumerateArray())
        {
            if (hole.ValueKind != JsonValueKind.Array || hole.GetArrayLength() < 3)
            {
                throw Invalid(
                    parameterName,
                    "Each polygon hole requires at least three points.");
            }

            foreach (var point in hole.EnumerateArray())
            {
                if (point.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid(parameterName, "Geometry points must be objects.");
                }

                RequireInteger(point, "x", parameterName);
                RequireInteger(point, "y", parameterName);
            }
        }
    }

    private static void RequireAsset(JsonElement root, string parameterName)
    {
        if (!root.TryGetProperty("assetVersionId", out var assetVersion)
            || assetVersion.ValueKind != JsonValueKind.String
            || !System.Guid.TryParse(assetVersion.GetString(), out var assetVersionId)
            || assetVersionId == System.Guid.Empty)
        {
            throw Invalid(
                parameterName,
                "Asset geometry requires a non-empty assetVersionId.");
        }

        if (!root.TryGetProperty("transform", out var transform)
            || transform.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(
                parameterName,
                "Asset geometry requires a transform object.");
        }
    }

    private static void RequirePositiveInteger(
        JsonElement root,
        string propertyName,
        string parameterName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var integer)
            || integer <= 0)
        {
            throw Invalid(
                parameterName,
                $"Geometry '{propertyName}' must be a positive integer millimeter value.");
        }
    }

    private static void RequireInteger(
        JsonElement root,
        string propertyName,
        string parameterName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out _))
        {
            throw Invalid(
                parameterName,
                $"Geometry '{propertyName}' must be an integer millimeter value.");
        }
    }

    private static ArgumentException Invalid(string parameterName, string message) =>
        new(message, parameterName);
}
