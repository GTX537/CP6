using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public sealed record WarehouseGenerationOutputValidationLimits(
    long MaxCanonicalJsonBytes = 64L * 1024 * 1024)
{
    public WarehouseGenerationOutputValidationLimits Validate()
    {
        if (MaxCanonicalJsonBytes is < 1 or > 512L * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaxCanonicalJsonBytes));
        return this;
    }
}

public sealed record ValidatedSemanticResult(
    WarehouseGenerationResult Output,
    string CanonicalSha256);

public interface IWarehouseGenerationOutputValidator
{
    ValidatedSemanticResult Validate(
        WarehouseGenerationInput input,
        WarehouseGenerationResult output);
}

public sealed class WarehouseGenerationOutputValidator :
    IWarehouseGenerationOutputValidator
{
    private const int MaximumDiagnostics = 1_000;
    private const int MaximumEvidenceCodes = 16;
    private const int MaximumRelations = 32;
    private const int MaximumSuggestions = 1_000_000;
    private readonly WarehouseGenerationOutputValidationLimits _limits;

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16,
    };

    private static readonly JsonSerializerOptions StrictJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
    };

    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 16,
    };

    public WarehouseGenerationOutputValidator()
        : this(new WarehouseGenerationOutputValidationLimits())
    {
    }

    public WarehouseGenerationOutputValidator(
        WarehouseGenerationOutputValidationLimits limits)
    {
        _limits = (limits ?? throw new ArgumentNullException(nameof(limits)))
            .Validate();
    }

    public ValidatedSemanticResult ValidateJson(
        WarehouseGenerationInput input,
        ReadOnlyMemory<byte> utf8Json)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (utf8Json.IsEmpty || utf8Json.Length > _limits.MaxCanonicalJsonBytes)
            throw Invalid("OUTPUT_JSON_SIZE_INVALID");

        try
        {
            using var document = JsonDocument.Parse(utf8Json, DocumentOptions);
            ValidateJsonShape(input, document.RootElement);
            var output = document.RootElement.Deserialize<WarehouseGenerationResult>(
                             StrictJsonOptions)
                         ?? throw Invalid("OUTPUT_JSON_EMPTY");
            return Validate(input, output);
        }
        catch (SpaceProblemException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw Invalid("OUTPUT_JSON_INVALID");
        }
        catch (NotSupportedException)
        {
            throw Invalid("OUTPUT_JSON_INVALID");
        }
    }

    public ValidatedSemanticResult Validate(
        WarehouseGenerationInput input,
        WarehouseGenerationResult output)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (output is null)
            throw Invalid("OUTPUT_NULL");
        if (!string.Equals(
                output.SchemaVersion,
                WarehouseGenerationInput.CurrentSchemaVersion,
                StringComparison.Ordinal))
        {
            throw Invalid("OUTPUT_SCHEMA_VERSION_UNSUPPORTED");
        }
        EnsureSafeText(output.ProviderRequestId, 256, "OUTPUT_REQUEST_ID_INVALID");
        EnsureSafeText(output.ProviderModel, 128, "OUTPUT_MODEL_INVALID");
        if (output.Usage is null
            || output.Usage.InputUnits < 0
            || output.Usage.OutputUnits < 0)
        {
            throw Invalid("OUTPUT_USAGE_INVALID");
        }
        if (output.Suggestions is null || output.Diagnostics is null)
            throw Invalid("OUTPUT_COLLECTION_INVALID");
        if (output.Suggestions.Count > MaximumSuggestions
            || output.Suggestions.Count > input.Limits.MaxSuggestions)
        {
            throw Invalid("OUTPUT_SUGGESTION_LIMIT_EXCEEDED");
        }
        if (output.Diagnostics.Count > MaximumDiagnostics)
            throw Invalid("OUTPUT_DIAGNOSTIC_LIMIT_EXCEEDED");

        var inputSourceKeys = input.Features
            .Select(item => item.SourceKey)
            .ToHashSet(StringComparer.Ordinal);
        var suggestionSourceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var suggestion in output.Suggestions)
        {
            if (suggestion is null)
                throw Invalid("OUTPUT_SUGGESTION_INVALID");
            EnsureSafeText(suggestion.SourceKey, 256, "OUTPUT_REFERENCE_INVALID");
            if (!inputSourceKeys.Contains(suggestion.SourceKey)
                || !suggestionSourceKeys.Add(suggestion.SourceKey))
            {
                throw Invalid("OUTPUT_REFERENCE_INVALID");
            }
            if (!Enum.IsDefined(suggestion.SuggestedType)
                || suggestion.Confidence is < 0 or > 1
                || suggestion.Attributes is null
                || suggestion.Relations is null
                || suggestion.EvidenceCodes is null)
            {
                throw Invalid("OUTPUT_SUGGESTION_INVALID");
            }
            ValidateAttributes(suggestion.SuggestedType, suggestion.Attributes);
            ValidateRelations(
                inputSourceKeys,
                input.Limits.MaxRelationsPerSuggestion,
                suggestion);
            ValidateEvidence(suggestion.EvidenceCodes);
        }

        foreach (var diagnostic in output.Diagnostics)
        {
            if (diagnostic is null || !Enum.IsDefined(diagnostic.Severity))
                throw Invalid("OUTPUT_DIAGNOSTIC_INVALID");
            EnsureSafeText(diagnostic.Code, 256, "OUTPUT_DIAGNOSTIC_INVALID");
            if (diagnostic.SourceKey is not null)
            {
                EnsureSafeText(
                    diagnostic.SourceKey,
                    256,
                    "OUTPUT_DIAGNOSTIC_REFERENCE_INVALID");
                if (!inputSourceKeys.Contains(diagnostic.SourceKey))
                    throw Invalid("OUTPUT_DIAGNOSTIC_REFERENCE_INVALID");
            }
        }

        byte[] canonical;
        try
        {
            canonical = JsonSerializer.SerializeToUtf8Bytes(
                output,
                CanonicalJsonOptions);
        }
        catch (JsonException)
        {
            throw Invalid("OUTPUT_CANONICALIZATION_FAILED");
        }
        try
        {
            if (canonical.LongLength > _limits.MaxCanonicalJsonBytes)
                throw Invalid("OUTPUT_JSON_SIZE_INVALID");
            var sha256 = Convert.ToHexString(SHA256.HashData(canonical))
                .ToLowerInvariant();
            return new ValidatedSemanticResult(output, sha256);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(canonical);
        }
    }

    private static void ValidateAttributes(
        WarehouseSpaceType suggestedType,
        WarehouseSuggestionAttributes attributes)
    {
        if (attributes.ZonePurpose is { } zonePurpose
            && (!Enum.IsDefined(zonePurpose)
                || suggestedType != WarehouseSpaceType.Zone))
        {
            throw Invalid("OUTPUT_ATTRIBUTE_COMBINATION_INVALID");
        }
        if (attributes.RackType is { } rackType
            && (!Enum.IsDefined(rackType)
                || suggestedType != WarehouseSpaceType.Rack))
        {
            throw Invalid("OUTPUT_ATTRIBUTE_COMBINATION_INVALID");
        }
        if (attributes.DoorType is { } doorType
            && (!Enum.IsDefined(doorType)
                || suggestedType != WarehouseSpaceType.Door))
        {
            throw Invalid("OUTPUT_ATTRIBUTE_COMBINATION_INVALID");
        }
        if (attributes.DockType is { } dockType
            && (!Enum.IsDefined(dockType)
                || suggestedType != WarehouseSpaceType.Dock))
        {
            throw Invalid("OUTPUT_ATTRIBUTE_COMBINATION_INVALID");
        }
        if (attributes.EquipmentType is { } equipmentType
            && (!Enum.IsDefined(equipmentType)
                || suggestedType != WarehouseSpaceType.StaticEquipment))
        {
            throw Invalid("OUTPUT_ATTRIBUTE_COMBINATION_INVALID");
        }
        if (attributes.SemanticLabel is not null)
        {
            EnsureSafeText(
                attributes.SemanticLabel,
                256,
                "OUTPUT_SEMANTIC_LABEL_INVALID");
        }
    }

    private static void ValidateRelations(
        IReadOnlySet<string> sourceKeys,
        int maximumRelationsPerSuggestion,
        WarehouseGenerationSuggestion suggestion)
    {
        if (suggestion.Relations.Count > MaximumRelations
            || suggestion.Relations.Count
                > maximumRelationsPerSuggestion)
        {
            throw Invalid("OUTPUT_RELATION_LIMIT_EXCEEDED");
        }
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relation in suggestion.Relations)
        {
            if (relation is null
                || !Enum.IsDefined(relation.RelationType)
                || relation.Confidence is < 0 or > 1)
            {
                throw Invalid("OUTPUT_RELATION_INVALID");
            }
            EnsureSafeText(
                relation.TargetSourceKey,
                256,
                "OUTPUT_RELATION_REFERENCE_INVALID");
            var identity = $"{relation.RelationType}\n{relation.TargetSourceKey}";
            if (relation.TargetSourceKey.Equals(
                    suggestion.SourceKey,
                    StringComparison.Ordinal)
                || !sourceKeys.Contains(relation.TargetSourceKey)
                || !identities.Add(identity))
            {
                throw Invalid("OUTPUT_RELATION_REFERENCE_INVALID");
            }
        }
    }

    private static void ValidateEvidence(
        IReadOnlyList<WarehouseEvidenceCode> evidenceCodes)
    {
        if (evidenceCodes.Count is < 1 or > MaximumEvidenceCodes
            || evidenceCodes.Any(item => !Enum.IsDefined(item))
            || evidenceCodes.Distinct().Count() != evidenceCodes.Count)
        {
            throw Invalid("OUTPUT_EVIDENCE_INVALID");
        }
    }

    private static void ValidateJsonShape(
        WarehouseGenerationInput input,
        JsonElement root)
    {
        EnsureObject(
            root,
            [
                "schemaVersion",
                "providerRequestId",
                "providerModel",
                "usage",
                "suggestions",
                "diagnostics",
            ]);
        EnsureString(
            root.GetProperty("schemaVersion"),
            1,
            32,
            "OUTPUT_SCHEMA_VERSION_INVALID");
        if (root.GetProperty("schemaVersion").GetString()
            != WarehouseGenerationInput.CurrentSchemaVersion)
        {
            throw Invalid("OUTPUT_SCHEMA_VERSION_UNSUPPORTED");
        }
        EnsureString(root.GetProperty("providerRequestId"), 1, 256);
        EnsureString(root.GetProperty("providerModel"), 1, 128);

        var usage = root.GetProperty("usage");
        EnsureObject(usage, ["inputUnits", "outputUnits"]);
        EnsureNonNegativeInteger(usage.GetProperty("inputUnits"));
        EnsureNonNegativeInteger(usage.GetProperty("outputUnits"));

        var suggestions = root.GetProperty("suggestions");
        EnsureArray(suggestions, "OUTPUT_SUGGESTIONS_INVALID");
        if (suggestions.GetArrayLength() > MaximumSuggestions
            || suggestions.GetArrayLength() > input.Limits.MaxSuggestions)
        {
            throw Invalid("OUTPUT_SUGGESTION_LIMIT_EXCEEDED");
        }
        foreach (var suggestion in suggestions.EnumerateArray())
            ValidateSuggestionJson(input, suggestion);

        var diagnostics = root.GetProperty("diagnostics");
        EnsureArray(diagnostics, "OUTPUT_DIAGNOSTICS_INVALID");
        if (diagnostics.GetArrayLength() > MaximumDiagnostics)
            throw Invalid("OUTPUT_DIAGNOSTIC_LIMIT_EXCEEDED");
        foreach (var diagnostic in diagnostics.EnumerateArray())
            ValidateDiagnosticJson(diagnostic);
    }

    private static void ValidateSuggestionJson(
        WarehouseGenerationInput input,
        JsonElement suggestion)
    {
        EnsureObject(
            suggestion,
            [
                "sourceKey",
                "suggestedType",
                "confidence",
                "attributes",
                "relations",
                "evidenceCodes",
            ]);
        EnsureString(suggestion.GetProperty("sourceKey"), 1, 256);
        EnsureEnumString<WarehouseSpaceType>(
            suggestion.GetProperty("suggestedType"));
        EnsureUnitDecimal(suggestion.GetProperty("confidence"));

        var attributes = suggestion.GetProperty("attributes");
        EnsureObject(
            attributes,
            [],
            [
                "zonePurpose",
                "rackType",
                "doorType",
                "dockType",
                "equipmentType",
                "semanticLabel",
            ]);
        EnsureOptionalEnum<WarehouseZonePurpose>(attributes, "zonePurpose");
        EnsureOptionalEnum<WarehouseRackType>(attributes, "rackType");
        EnsureOptionalEnum<WarehouseDoorType>(attributes, "doorType");
        EnsureOptionalEnum<WarehouseDockType>(attributes, "dockType");
        EnsureOptionalEnum<WarehouseEquipmentType>(attributes, "equipmentType");
        if (attributes.TryGetProperty("semanticLabel", out var semanticLabel))
            EnsureString(semanticLabel, 1, 256);

        var relations = suggestion.GetProperty("relations");
        EnsureArray(relations, "OUTPUT_RELATIONS_INVALID");
        if (relations.GetArrayLength() > MaximumRelations
            || relations.GetArrayLength()
                > input.Limits.MaxRelationsPerSuggestion)
        {
            throw Invalid("OUTPUT_RELATION_LIMIT_EXCEEDED");
        }
        foreach (var relation in relations.EnumerateArray())
        {
            EnsureObject(
                relation,
                ["relationType", "targetSourceKey", "confidence"]);
            EnsureEnumString<WarehouseRelationType>(
                relation.GetProperty("relationType"));
            EnsureString(relation.GetProperty("targetSourceKey"), 1, 256);
            EnsureUnitDecimal(relation.GetProperty("confidence"));
        }

        var evidenceCodes = suggestion.GetProperty("evidenceCodes");
        EnsureArray(evidenceCodes, "OUTPUT_EVIDENCE_INVALID");
        if (evidenceCodes.GetArrayLength() is < 1 or > MaximumEvidenceCodes)
            throw Invalid("OUTPUT_EVIDENCE_INVALID");
        var evidence = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in evidenceCodes.EnumerateArray())
        {
            EnsureEnumString<WarehouseEvidenceCode>(code);
            if (!evidence.Add(code.GetString()!))
                throw Invalid("OUTPUT_EVIDENCE_INVALID");
        }
    }

    private static void ValidateDiagnosticJson(JsonElement diagnostic)
    {
        EnsureObject(
            diagnostic,
            ["code", "severity"],
            ["sourceKey"]);
        EnsureString(diagnostic.GetProperty("code"), 1, 256);
        EnsureEnumString<WarehouseDiagnosticSeverity>(
            diagnostic.GetProperty("severity"));
        if (diagnostic.TryGetProperty("sourceKey", out var sourceKey)
            && sourceKey.ValueKind != JsonValueKind.Null)
        {
            EnsureString(sourceKey, 1, 256);
        }
    }

    private static void EnsureObject(
        JsonElement value,
        IReadOnlyList<string> required,
        IReadOnlyList<string>? optional = null)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid("OUTPUT_SCHEMA_INVALID");
        var allowed = required
            .Concat(optional ?? [])
            .ToHashSet(StringComparer.Ordinal);
        var observed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)
                || !observed.Add(property.Name))
            {
                throw Invalid("OUTPUT_SCHEMA_INVALID");
            }
        }
        if (required.Any(name => !observed.Contains(name)))
            throw Invalid("OUTPUT_SCHEMA_INVALID");
    }

    private static void EnsureArray(JsonElement value, string violation)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw Invalid(violation);
    }

    private static void EnsureOptionalEnum<TEnum>(
        JsonElement value,
        string propertyName)
        where TEnum : struct, Enum
    {
        if (value.TryGetProperty(propertyName, out var property))
            EnsureEnumString<TEnum>(property);
    }

    private static void EnsureEnumString<TEnum>(JsonElement value)
        where TEnum : struct, Enum
    {
        if (value.ValueKind != JsonValueKind.String
            || !Enum.TryParse<TEnum>(value.GetString(), false, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            throw Invalid("OUTPUT_ENUM_INVALID");
        }
    }

    private static void EnsureNonNegativeInteger(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var result)
            || result < 0)
        {
            throw Invalid("OUTPUT_USAGE_INVALID");
        }
    }

    private static void EnsureUnitDecimal(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetDecimal(out var result)
            || result is < 0 or > 1)
        {
            throw Invalid("OUTPUT_RANGE_INVALID");
        }
    }

    private static void EnsureString(
        JsonElement value,
        int minimum,
        int maximum,
        string violation = "OUTPUT_STRING_INVALID")
    {
        if (value.ValueKind != JsonValueKind.String)
            throw Invalid(violation);
        EnsureSafeText(value.GetString(), maximum, violation, minimum);
    }

    private static void EnsureSafeText(
        string? value,
        int maximum,
        string violation,
        int minimum = 1)
    {
        if (value is null
            || value.Length < minimum
            || value.Length > maximum
            || value.Any(character => char.IsControl(character)))
        {
            throw Invalid(violation);
        }
    }

    private static SpaceProblemException Invalid(string violation) =>
        new(
            SpaceErrorCodes.AiOutputInvalid,
            502,
            "The warehouse generation provider output is invalid.",
            $"Provider output failed validation ({violation}).",
            "change-ai-provider-or-model");
}
