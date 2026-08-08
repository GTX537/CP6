using System.Reflection;
using System.Text.Json.Serialization;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

internal static class SpaceAiExternalProviderRequestGate
{
    private const int RunHashLength = 64;
    private const int SourceHashLength = 32;
    private const int TokenHashLength = 24;

    private static readonly HashSet<string> SafeCategories = new(
        [
            "agv", "aisle", "charger", "column", "conveyor", "dock",
            "door", "equipment", "floor", "forklift", "generic", "packing",
            "passage", "picking", "rack", "receiving", "room", "scale",
            "shelf", "shipping", "staging", "storage", "wall",
            "workstation", "zone",
        ],
        StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, Type> LockedFieldEnums =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["type"] = typeof(WarehouseSpaceType),
            ["attributes.zonePurpose"] = typeof(WarehouseZonePurpose),
            ["attributes.rackType"] = typeof(WarehouseRackType),
            ["attributes.doorType"] = typeof(WarehouseDoorType),
            ["attributes.dockType"] = typeof(WarehouseDockType),
            ["attributes.equipmentType"] = typeof(WarehouseEquipmentType),
        };

    private static readonly bool ContractShapeIsAllowlisted =
        HasExactJsonProperties<WarehouseGenerationInput>(
            "schemaVersion", "runCorrelationKey", "policy", "warehouseKind",
            "limits", "features", "mappingHints", "lockedFacts") &&
        HasExactJsonProperties<WarehouseGenerationLimits>(
            "maxSuggestions", "maxRelationsPerSuggestion") &&
        HasExactJsonProperties<WarehouseGenerationFeature>(
            "sourceKey", "cadEntityType", "layerToken", "blockToken",
            "entityCount", "normalizedBounds", "angleBucket",
            "repetitionGroup", "attributeKeyTokens", "relationSourceKeys",
            "aspectRatioBucket") &&
        HasExactJsonProperties<WarehouseNormalizedBounds>(
            "x", "y", "width", "height") &&
        HasExactJsonProperties<WarehouseGenerationMappingHint>(
            "token", "targetType", "strength") &&
        HasExactJsonProperties<WarehouseGenerationLockedFact>(
            "sourceKey", "fieldPath", "valueToken");

    public static void EnsureSafe(WarehouseGenerationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!ContractShapeIsAllowlisted ||
            !IsOpaqueToken(input.RunCorrelationKey, "run-", RunHashLength))
        {
            throw Denied();
        }

        foreach (var feature in input.Features)
        {
            if (!IsSourceKey(feature.SourceKey) ||
                !IsNamedToken(feature.LayerToken, "layer-") ||
                feature.BlockToken is not null &&
                    !IsNamedToken(feature.BlockToken, "block-") ||
                feature.RepetitionGroup is not null &&
                    !IsOpaqueToken(
                        feature.RepetitionGroup,
                        "repeat-",
                        TokenHashLength) ||
                feature.AttributeKeyTokens.Any(token =>
                    !IsOpaqueToken(token, "attribute-", TokenHashLength)) ||
                feature.RelationSourceKeys.Any(key => !IsSourceKey(key)))
            {
                throw Denied();
            }
        }

        if (input.MappingHints.Any(hint =>
                !IsOpaqueToken(hint.Token, "hint-", TokenHashLength)))
        {
            throw Denied();
        }

        foreach (var fact in input.LockedFacts)
        {
            if (!IsSourceKey(fact.SourceKey) ||
                !LockedFieldEnums.TryGetValue(fact.FieldPath, out var enumType) ||
                !Enum.TryParse(
                    enumType,
                    fact.ValueToken,
                    ignoreCase: false,
                    out var parsed) ||
                parsed is null ||
                !Enum.IsDefined(enumType, parsed))
            {
                throw Denied();
            }
        }
    }

    private static bool IsSourceKey(string value) =>
        IsOpaqueToken(value, "source-", SourceHashLength) ||
        IsOpaqueToken(value, "group-", SourceHashLength);

    private static bool IsNamedToken(string value, string prefix)
    {
        if (!value.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var hashSeparator = value.LastIndexOf('-');
        if (hashSeparator <= prefix.Length ||
            !IsLowerHex(value.AsSpan(hashSeparator + 1), TokenHashLength))
        {
            return false;
        }
        var categories = value[prefix.Length..hashSeparator].Split('-');
        return categories.Length > 0 && categories.All(SafeCategories.Contains);
    }

    private static bool IsOpaqueToken(
        string value,
        string prefix,
        int hashLength) =>
        value.StartsWith(prefix, StringComparison.Ordinal) &&
        IsLowerHex(value.AsSpan(prefix.Length), hashLength);

    private static bool IsLowerHex(ReadOnlySpan<char> value, int length)
    {
        if (value.Length != length) return false;
        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                return false;
        }
        return true;
    }

    private static bool HasExactJsonProperties<T>(params string[] allowlist)
    {
        var actual = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property
                .GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
            .ToArray();
        return actual.All(name => name is not null) &&
               actual.Order(StringComparer.Ordinal).SequenceEqual(
                   allowlist.Order(StringComparer.Ordinal),
                   StringComparer.Ordinal);
    }

    private static SpaceProblemException Denied() => new(
        SpaceErrorCodes.AiOutboundPayloadDenied,
        403,
        "The external AI Provider payload is not allowlisted.",
        recoveryAction: "regenerate-minimized-provider-input");
}
