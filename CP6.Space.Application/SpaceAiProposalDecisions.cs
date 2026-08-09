using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Space.Contracts;

namespace CP6.Space.Application;

public interface ISpaceAiProposalDecisionService
{
    Task<SpaceAiGenerationReviewDto> GetReviewAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<SpaceAiProposalPageDto> GetProposalsAsync(
        Guid runId,
        SpaceAiProposalQuery query,
        CancellationToken cancellationToken = default);

    Task<SpaceAiProposalIssuePageDto> GetIssuesAsync(
        Guid runId,
        SpaceAiProposalIssueQuery query,
        CancellationToken cancellationToken = default);

    Task<SpaceAiProposalDecisionHistoryDto> GetDecisionsAsync(
        Guid runId,
        Guid? proposalId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<SpaceAiProposalDecisionResponse> CreateDecisionAsync(
        Guid runId,
        CreateSpaceAiProposalDecisionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<SpaceAiProposalDecisionResponse> CreateBatchDecisionAsync(
        Guid runId,
        CreateSpaceAiProposalBatchDecisionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default);
}

public sealed class SpaceAiProposalReviewOptions
{
    public bool EnableHighConfidenceBatchAccept { get; init; }
}

public sealed record SpaceAiProposalPatchResult(
    string AttributesJson,
    string RelationsJson,
    string PatchJson,
    string LockedFieldsJson,
    string FinalSnapshotJson,
    IReadOnlyList<string> PatchedPaths);

public static class SpaceAiProposalPatchPolicyV1
{
    public const string Version = "space-ai-proposal-patch-v1";
    public const int MaximumOperations = 32;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private static readonly IReadOnlyDictionary<string, string[]> Paths =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Floor"] = ["/attributes/name"],
            ["Zone"] = ["/attributes/name", "/attributes/zonePurpose"],
            ["Aisle"] = ["/attributes/name", "/attributes/direction"],
            ["Rack"] =
            [
                "/attributes/name",
                "/attributes/rackType",
                "/relations/zoneSourceKey",
                "/relations/aisleSourceKey",
            ],
            ["Wall"] = ["/attributes/name", "/attributes/wallType"],
            ["Column"] = ["/attributes/name", "/attributes/columnType"],
            ["Door"] =
            [
                "/attributes/name",
                "/attributes/doorType",
                "/relations/wallSourceKey",
            ],
            ["Dock"] =
            [
                "/attributes/name",
                "/attributes/dockType",
                "/relations/zoneSourceKey",
            ],
            ["StaticEquipment"] =
            [
                "/attributes/name",
                "/attributes/equipmentType",
                "/relations/zoneSourceKey",
            ],
        };

    private static readonly IReadOnlyDictionary<string, HashSet<string>> Enums =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["/attributes/zonePurpose"] = Values(
                "Receiving", "Storage", "Picking", "Packing", "Shipping",
                "Staging", "Passage", "Unknown"),
            ["/attributes/rackType"] = Values(
                "Selective", "DriveIn", "Cantilever", "Flow", "Mobile",
                "Unknown"),
            ["/attributes/doorType"] = Values(
                "Personnel", "Rolling", "Fire", "Dock", "Unknown"),
            ["/attributes/dockType"] = Values(
                "Inbound", "Outbound", "Shared", "Unknown"),
            ["/attributes/equipmentType"] = Values(
                "Conveyor", "Agv", "Forklift", "Workstation", "Scale",
                "Charger", "Unknown"),
            ["/attributes/direction"] = Values(
                "OneWay", "TwoWay", "Bidirectional", "Unknown"),
            ["/attributes/wallType"] = Values(
                "Exterior", "Interior", "Partition", "Fire", "Unknown"),
            ["/attributes/columnType"] = Values(
                "Structural", "Guard", "Unknown"),
        };

    public static IReadOnlyList<string> AllowedPaths(string proposalType) =>
        Paths.TryGetValue(proposalType, out var paths)
            ? paths
            : [];

    public static SpaceAiProposalPatchResult Apply(
        string proposalType,
        string geometryJson,
        string attributesJson,
        string relationsJson,
        IReadOnlyList<SpaceAiProposalPatchOperationDto> patch,
        IReadOnlyList<string> lockedFields)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(lockedFields);
        if (!Paths.TryGetValue(proposalType, out var allowed) ||
            patch.Count is < 1 or > MaximumOperations)
        {
            throw new SpaceProposalPatchException(
                "The proposal type or patch operation count is unsupported.");
        }

        var attributes = ParseObject(attributesJson, "attributes");
        var relations = ParseObject(relationsJson, "relations");
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var paths = new List<string>(patch.Count);
        var normalizedPatch = new JsonArray();
        foreach (var operation in patch)
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (!string.Equals(operation.Op, "replace", StringComparison.Ordinal) ||
                !allowedSet.Contains(operation.Path) ||
                paths.Contains(operation.Path, StringComparer.Ordinal))
            {
                throw new SpaceProposalPatchException(
                    "Only unique allowlisted replace operations are permitted.");
            }

            var segments = operation.Path.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length != 2 ||
                segments[0] is not ("attributes" or "relations"))
            {
                throw new SpaceProposalPatchException(
                    "The proposal patch path is invalid.");
            }
            var target = segments[0] == "attributes" ? attributes : relations;
            if (!target.ContainsKey(segments[1]))
            {
                throw new SpaceProposalPatchException(
                    "RFC 6902 replace requires an existing target field.");
            }

            var value = NormalizeValue(operation.Path, operation.Value);
            target[segments[1]] = value.DeepClone();
            normalizedPatch.Add(new JsonObject
            {
                ["op"] = "replace",
                ["path"] = operation.Path,
                ["value"] = value,
            });
            paths.Add(operation.Path);
        }

        var normalizedLocks = lockedFields
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrEmpty(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalizedLocks.Length != lockedFields.Count ||
            !normalizedLocks.ToHashSet(StringComparer.Ordinal)
                .SetEquals(paths))
        {
            throw new SpaceProposalPatchException(
                "Locked fields must exactly match the modified field paths.");
        }

        var canonicalAttributes = Canonicalize(attributes);
        var canonicalRelations = Canonicalize(relations);
        return new SpaceAiProposalPatchResult(
            canonicalAttributes,
            canonicalRelations,
            Canonicalize(normalizedPatch),
            Canonicalize(JsonSerializer.SerializeToNode(normalizedLocks, Json)!),
            BuildSnapshot(
                proposalType,
                geometryJson,
                canonicalAttributes,
                canonicalRelations),
            paths.Order(StringComparer.Ordinal).ToArray());
    }

    public static string BuildSnapshot(
        string proposalType,
        string geometryJson,
        string attributesJson,
        string relationsJson)
    {
        if (string.IsNullOrWhiteSpace(proposalType) ||
            proposalType.Length > 64 ||
            proposalType.Any(char.IsControl))
        {
            throw new SpaceProposalPatchException(
                "The proposal type is invalid.");
        }
        var snapshot = new JsonObject
        {
            ["proposalType"] = proposalType,
            ["geometry"] = ParseNode(geometryJson, "geometry"),
            ["attributes"] = ParseNode(attributesJson, "attributes"),
            ["relations"] = ParseNode(relationsJson, "relations"),
        };
        return Canonicalize(snapshot);
    }

    private static JsonNode NormalizeValue(string path, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new SpaceProposalPatchException(
                "Proposal patch values must be strings.");
        }
        var text = value.GetString()?.Trim();
        var maximum = path == "/attributes/name" ? 128 : 256;
        if (string.IsNullOrWhiteSpace(text) ||
            text.Length > maximum ||
            text.Any(char.IsControl))
        {
            throw new SpaceProposalPatchException(
                "The proposal patch value is invalid.");
        }
        if (Enums.TryGetValue(path, out var values) && !values.Contains(text))
        {
            throw new SpaceProposalPatchException(
                "The proposal patch enum value is unsupported.");
        }
        return JsonValue.Create(text)!;
    }

    private static JsonObject ParseObject(string json, string label) =>
        ParseNode(json, label) as JsonObject
        ?? throw new SpaceProposalPatchException(
            $"Proposal {label} must be a JSON object.");

    private static JsonNode ParseNode(string json, string label)
    {
        if (string.IsNullOrWhiteSpace(json) ||
            Encoding.UTF8.GetByteCount(json) > 16 * 1024 * 1024)
        {
            throw new SpaceProposalPatchException(
                $"Proposal {label} JSON is invalid or too large.");
        }
        try
        {
            return JsonNode.Parse(
                       json,
                       nodeOptions: null,
                       documentOptions: new JsonDocumentOptions
                       {
                           AllowTrailingCommas = false,
                           CommentHandling = JsonCommentHandling.Disallow,
                           MaxDepth = 64,
                       })
                   ?? throw new SpaceProposalPatchException(
                       $"Proposal {label} JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new SpaceProposalPatchException(
                $"Proposal {label} JSON is invalid.",
                exception);
        }
    }

    private static string Canonicalize(JsonNode node)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteCanonical(writer, node);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var property in obj.OrderBy(
                             value => value.Key,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array)
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer);
                break;
        }
    }

    private static HashSet<string> Values(params string[] values) =>
        values.ToHashSet(StringComparer.Ordinal);
}

public sealed class SpaceProposalPatchException : Exception
{
    public SpaceProposalPatchException(string message)
        : base(message)
    {
    }

    public SpaceProposalPatchException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
