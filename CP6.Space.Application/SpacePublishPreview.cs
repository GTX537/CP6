using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public static class SpacePublishPlanRuleSet
{
    public const string Version = "space-publish-plan-v1";
}

public static class SpacePublishObjectTypes
{
    public const string Floor = "Floor";
    public const string Zone = "Zone";
    public const string Aisle = "Aisle";
    public const string Rack = "Rack";
    public const string RackLevel = "RackLevel";
    public const string Location = "Location";
    public const string Element = "Element";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [
                Floor,
                Zone,
                Aisle,
                Rack,
                RackLevel,
                Location,
                Element,
            ],
            StringComparer.OrdinalIgnoreCase);
}

public static class SpacePublishActions
{
    public const string Create = "Create";
    public const string UpdateMaster = "UpdateMaster";
    public const string UpdateGeometryOnly = "UpdateGeometryOnly";
    public const string Disable = "Disable";
    public const string Restore = "Restore";
    public const string NoOp = "NoOp";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [
                Create,
                UpdateMaster,
                UpdateGeometryOnly,
                Disable,
                Restore,
                NoOp,
            ],
            StringComparer.OrdinalIgnoreCase);
}

public static class SpacePublishImpactCodes
{
    public const string WmsCreateLocation = "WMS_CREATE_LOCATION";
    public const string WmsUpdateLocation = "WMS_UPDATE_LOCATION";
    public const string WmsDisableLocation = "WMS_DISABLE_LOCATION";
    public const string WmsRestoreLocation = "WMS_RESTORE_LOCATION";
    public const string WmsRenameBlocked = "WMS_RENAME_BLOCKED";
    public const string WmsNoOp = "WMS_NO_OP";
    public const string RuntimeOnly = "RUNTIME_ONLY";
    public const string None = "NONE";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(
            [
                WmsCreateLocation,
                WmsUpdateLocation,
                WmsDisableLocation,
                WmsRestoreLocation,
                WmsRenameBlocked,
                WmsNoOp,
                RuntimeOnly,
                None,
            ],
            StringComparer.OrdinalIgnoreCase);
}

public interface ISpacePublishPreviewService
{
    Task<SpacePublishPreviewDto> GetPreviewAsync(
        Guid versionId,
        Guid? floorLogicalId,
        string? objectType,
        string? action,
        string? impactCode,
        bool includeNoOp,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default);
}

public sealed class SpacePublishObjectSnapshot
{
    private SpacePublishObjectSnapshot(
        string objectType,
        Guid logicalId,
        Guid? floorLogicalId,
        SpaceLifecycleState lifecycleState,
        string? code,
        string masterHash,
        string geometryHash,
        string provenanceHash,
        string wmsHash,
        string payloadHash,
        string? externalBindingId)
    {
        ObjectType = objectType;
        LogicalId = logicalId;
        FloorLogicalId = floorLogicalId;
        LifecycleState = lifecycleState;
        Code = code;
        MasterHash = masterHash;
        GeometryHash = geometryHash;
        ProvenanceHash = provenanceHash;
        WmsHash = wmsHash;
        PayloadHash = payloadHash;
        ExternalBindingId = externalBindingId;
    }

    public string ObjectType { get; }
    public Guid LogicalId { get; }
    public Guid? FloorLogicalId { get; }
    public SpaceLifecycleState LifecycleState { get; }
    public string? Code { get; }
    public string MasterHash { get; }
    public string GeometryHash { get; }
    public string ProvenanceHash { get; }
    public string WmsHash { get; }
    public string PayloadHash { get; }
    public string? ExternalBindingId { get; }

    public bool IsActive => LifecycleState == SpaceLifecycleState.Active;

    public static SpacePublishObjectSnapshot Create(
        string objectType,
        Guid logicalId,
        Guid? floorLogicalId,
        SpaceLifecycleState lifecycleState,
        string? code,
        string masterJson,
        string geometryJson,
        string wmsJson,
        string provenanceJson,
        string? externalBindingId = null)
    {
        var normalizedType = objectType?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedType) ||
            !SpacePublishObjectTypes.All.Contains(normalizedType))
        {
            throw new ArgumentException(
                "A supported publish object type is required.",
                nameof(objectType));
        }
        if (logicalId == Guid.Empty)
            throw new ArgumentException(
                "A logical identity is required.",
                nameof(logicalId));
        if (floorLogicalId == Guid.Empty)
            throw new ArgumentException(
                "A floor logical identity cannot be empty.",
                nameof(floorLogicalId));

        var master = SpaceCanonicalJson.Normalize(masterJson);
        var geometry = SpaceCanonicalJson.Normalize(geometryJson);
        var wms = SpaceCanonicalJson.Normalize(wmsJson);
        var provenance = SpaceCanonicalJson.Normalize(provenanceJson);
        var normalizedExternalBindingId =
            NormalizeOptional(externalBindingId);
        var payload = string.Join(
            "\n",
            (short)lifecycleState,
            master,
            geometry,
            wms,
            provenance,
            normalizedExternalBindingId ?? "-");
        return new SpacePublishObjectSnapshot(
            SpacePublishObjectTypes.All.Single(value =>
                string.Equals(
                    value,
                    normalizedType,
                    StringComparison.OrdinalIgnoreCase)),
            logicalId,
            floorLogicalId,
            lifecycleState,
            NormalizeOptional(code),
            Hash(master),
            Hash(geometry),
            Hash(provenance),
            Hash($"{wms}\n{normalizedExternalBindingId ?? "-"}"),
            Hash(payload),
            normalizedExternalBindingId);
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}

public sealed record SpacePublishPlanInput(
    Guid TargetVersionId,
    Guid? BaseVersionId,
    Guid ValidationRunId,
    string ValidationStatus,
    int ValidationBlockingCount,
    string ContentHash,
    string AdapterId,
    string CapabilityHash,
    IReadOnlyList<SpacePublishObjectSnapshot> TargetObjects,
    IReadOnlyList<SpacePublishObjectSnapshot> BaseObjects);

public sealed record SpacePublishPlanItem(
    int SequenceNo,
    string ObjectType,
    Guid LogicalId,
    Guid? FloorLogicalId,
    string Action,
    string? BeforeHash,
    string? AfterHash,
    string? BeforeCode,
    string? AfterCode,
    string? ExternalBindingId,
    string PayloadHash,
    string ImpactCode,
    bool MasterChanged,
    bool GeometryChanged,
    bool ProvenanceChanged,
    bool WmsChanged,
    bool Blocking);

public sealed record SpacePublishChangeSummary(
    int CreateCount,
    int UpdateMasterCount,
    int UpdateGeometryOnlyCount,
    int DisableCount,
    int RestoreCount,
    int NoOpCount);

public sealed record SpacePublishImpactSummary(
    int WmsCreateCount,
    int WmsUpdateCount,
    int WmsDisableCount,
    int WmsRestoreCount,
    int WmsNoOpCount,
    int RuntimeOnlyCount,
    int BlockingCount);

public sealed record SpacePublishPlanResult(
    string PlanHash,
    IReadOnlyList<SpacePublishPlanItem> Items,
    SpacePublishChangeSummary Changes,
    SpacePublishImpactSummary WmsImpact)
{
    public int ChangeCount =>
        Items.Count(item => item.Action != SpacePublishActions.NoOp);

    public bool HasBlockingImpact => WmsImpact.BlockingCount > 0;
}

public sealed class SpacePublishPlanEngine
{
    public SpacePublishPlanResult Build(SpacePublishPlanInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateInput(input);

        var before = ToObjectMap(input.BaseObjects, nameof(input.BaseObjects));
        var after = ToObjectMap(
            input.TargetObjects,
            nameof(input.TargetObjects));
        var keys = before.Keys
            .Concat(after.Keys)
            .Distinct()
            .OrderBy(key => key.ObjectType, StringComparer.Ordinal)
            .ThenBy(key => key.LogicalId)
            .ToArray();
        var items = new List<SpacePublishPlanItem>(keys.Length);
        foreach (var key in keys)
        {
            before.TryGetValue(key, out var oldValue);
            after.TryGetValue(key, out var newValue);
            items.Add(CreateItem(
                items.Count + 1,
                oldValue,
                newValue,
                key));
        }

        var changes = new SpacePublishChangeSummary(
            Count(items, SpacePublishActions.Create),
            Count(items, SpacePublishActions.UpdateMaster),
            Count(items, SpacePublishActions.UpdateGeometryOnly),
            Count(items, SpacePublishActions.Disable),
            Count(items, SpacePublishActions.Restore),
            Count(items, SpacePublishActions.NoOp));
        var impacts = new SpacePublishImpactSummary(
            CountImpact(items, SpacePublishImpactCodes.WmsCreateLocation),
            CountImpact(items, SpacePublishImpactCodes.WmsUpdateLocation),
            CountImpact(items, SpacePublishImpactCodes.WmsDisableLocation),
            CountImpact(items, SpacePublishImpactCodes.WmsRestoreLocation),
            CountImpact(items, SpacePublishImpactCodes.WmsNoOp),
            CountImpact(items, SpacePublishImpactCodes.RuntimeOnly),
            items.Count(item => item.Blocking));
        return new SpacePublishPlanResult(
            ComputePlanHash(input, items),
            items,
            changes,
            impacts);
    }

    private static SpacePublishPlanItem CreateItem(
        int sequenceNo,
        SpacePublishObjectSnapshot? before,
        SpacePublishObjectSnapshot? after,
        SpacePublishObjectKey key)
    {
        var beforeActive = before?.IsActive == true;
        var afterActive = after?.IsActive == true;
        var masterChanged = !SameHash(before?.MasterHash, after?.MasterHash);
        var geometryChanged =
            !SameHash(before?.GeometryHash, after?.GeometryHash);
        var provenanceChanged =
            !SameHash(before?.ProvenanceHash, after?.ProvenanceHash);
        var wmsChanged = !SameHash(before?.WmsHash, after?.WmsHash);
        var action = ResolveAction(
            before,
            after,
            beforeActive,
            afterActive,
            masterChanged,
            geometryChanged,
            wmsChanged);
        var impact = ResolveImpact(
            key.ObjectType,
            action,
            before,
            after,
            wmsChanged);
        var payloadHash = ResolvePayloadHash(
            action,
            before,
            after,
            impact.Code);
        return new SpacePublishPlanItem(
            sequenceNo,
            key.ObjectType,
            key.LogicalId,
            after?.FloorLogicalId ?? before?.FloorLogicalId,
            action,
            before?.PayloadHash,
            after?.PayloadHash,
            before?.Code,
            after?.Code,
            after?.ExternalBindingId ?? before?.ExternalBindingId,
            payloadHash,
            impact.Code,
            masterChanged,
            geometryChanged,
            provenanceChanged,
            wmsChanged,
            impact.Blocking);
    }

    private static string ResolveAction(
        SpacePublishObjectSnapshot? before,
        SpacePublishObjectSnapshot? after,
        bool beforeActive,
        bool afterActive,
        bool masterChanged,
        bool geometryChanged,
        bool wmsChanged)
    {
        if (!beforeActive && afterActive)
            return before is null
                ? SpacePublishActions.Create
                : SpacePublishActions.Restore;
        if (beforeActive && !afterActive)
            return SpacePublishActions.Disable;
        if (!beforeActive && !afterActive)
            return SpacePublishActions.NoOp;
        if (SameHash(before!.PayloadHash, after!.PayloadHash))
            return SpacePublishActions.NoOp;
        if (!masterChanged && !wmsChanged && geometryChanged)
            return SpacePublishActions.UpdateGeometryOnly;
        return SpacePublishActions.UpdateMaster;
    }

    private static (string Code, bool Blocking) ResolveImpact(
        string objectType,
        string action,
        SpacePublishObjectSnapshot? before,
        SpacePublishObjectSnapshot? after,
        bool wmsChanged)
    {
        if (action == SpacePublishActions.NoOp)
            return (SpacePublishImpactCodes.None, false);
        if (objectType != SpacePublishObjectTypes.Location)
            return (SpacePublishImpactCodes.RuntimeOnly, false);
        return action switch
        {
            SpacePublishActions.Create
                when after?.ExternalBindingId is not null =>
                (SpacePublishImpactCodes.WmsNoOp, false),
            SpacePublishActions.Create =>
                (SpacePublishImpactCodes.WmsCreateLocation, false),
            SpacePublishActions.Disable =>
                (SpacePublishImpactCodes.WmsDisableLocation, false),
            SpacePublishActions.Restore =>
                (SpacePublishImpactCodes.WmsRestoreLocation, false),
            SpacePublishActions.UpdateGeometryOnly =>
                (SpacePublishImpactCodes.WmsNoOp, false),
            SpacePublishActions.UpdateMaster
                when !string.Equals(
                    before?.Code,
                    after?.Code,
                    StringComparison.Ordinal) =>
                (SpacePublishImpactCodes.WmsRenameBlocked, true),
            SpacePublishActions.UpdateMaster when wmsChanged =>
                (SpacePublishImpactCodes.WmsUpdateLocation, false),
            _ => (SpacePublishImpactCodes.WmsNoOp, false),
        };
    }

    private static string ResolvePayloadHash(
        string action,
        SpacePublishObjectSnapshot? before,
        SpacePublishObjectSnapshot? after,
        string impactCode)
    {
        var material = string.Join(
            "\n",
            action,
            before?.PayloadHash ?? "-",
            after?.PayloadHash ?? "-",
            impactCode);
        return Hash(material);
    }

    private static string ComputePlanHash(
        SpacePublishPlanInput input,
        IReadOnlyCollection<SpacePublishPlanItem> items)
    {
        var builder = new StringBuilder();
        builder
            .Append(SpacePublishPlanRuleSet.Version).Append('\n')
            .Append(input.TargetVersionId.ToString("D")).Append('\n')
            .Append(input.BaseVersionId?.ToString("D") ?? "-").Append('\n')
            .Append(input.ValidationRunId.ToString("D")).Append('\n')
            .Append(input.ContentHash.ToLowerInvariant()).Append('\n')
            .Append(input.AdapterId).Append('\n')
            .Append(input.CapabilityHash.ToLowerInvariant()).Append('\n');
        foreach (var item in items)
        {
            builder
                .Append(item.SequenceNo).Append('|')
                .Append(item.ObjectType).Append('|')
                .Append(item.LogicalId.ToString("D")).Append('|')
                .Append(item.Action).Append('|')
                .Append(item.BeforeHash ?? "-").Append('|')
                .Append(item.AfterHash ?? "-").Append('|')
                .Append(item.ExternalBindingId ?? "-").Append('|')
                .Append(item.PayloadHash).Append('|')
                .Append(item.ImpactCode).Append('|')
                .Append(item.Blocking ? '1' : '0')
                .Append('\n');
        }
        return Hash(builder.ToString());
    }

    private static Dictionary<SpacePublishObjectKey, SpacePublishObjectSnapshot>
        ToObjectMap(
            IEnumerable<SpacePublishObjectSnapshot> values,
            string parameterName)
    {
        var result =
            new Dictionary<SpacePublishObjectKey, SpacePublishObjectSnapshot>();
        foreach (var value in values)
        {
            var key = new SpacePublishObjectKey(
                value.ObjectType,
                value.LogicalId);
            if (!result.TryAdd(key, value))
            {
                throw new ArgumentException(
                    $"Duplicate publish object {key.ObjectType}/" +
                    $"{key.LogicalId:D}.",
                    parameterName);
            }
        }
        return result;
    }

    private static void ValidateInput(SpacePublishPlanInput input)
    {
        if (input.TargetVersionId == Guid.Empty)
            throw new ArgumentException(
                "Target version is required.",
                nameof(input));
        if (input.BaseVersionId == Guid.Empty)
            throw new ArgumentException(
                "Base version cannot be empty.",
                nameof(input));
        if (input.ValidationRunId == Guid.Empty)
            throw new ArgumentException(
                "ValidationRun is required.",
                nameof(input));
        RequireHash(input.ContentHash, nameof(input.ContentHash));
        RequireHash(input.CapabilityHash, nameof(input.CapabilityHash));
        if (string.IsNullOrWhiteSpace(input.AdapterId))
            throw new ArgumentException("Adapter is required.", nameof(input));
    }

    private static int Count(
        IEnumerable<SpacePublishPlanItem> items,
        string action) =>
        items.Count(item => item.Action == action);

    private static int CountImpact(
        IEnumerable<SpacePublishPlanItem> items,
        string impactCode) =>
        items.Count(item => item.ImpactCode == impactCode);

    private static bool SameHash(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void RequireHash(string value, string parameterName)
    {
        if (value?.Length != 64 || !value.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "A SHA-256 hex value is required.",
                parameterName);
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record SpacePublishObjectKey(
        string ObjectType,
        Guid LogicalId);
}

public static class SpaceCanonicalJson
{
    public static string Normalize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON is required.", nameof(json));
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions { Indented = false }))
        {
            Write(writer, document.RootElement);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value
                             .EnumerateObject()
                             .OrderBy(
                                 property => property.Name,
                                 StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    Write(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number when value.TryGetInt64(out var integer):
                writer.WriteNumberValue(integer);
                break;
            case JsonValueKind.Number when value.TryGetDecimal(out var number):
                writer.WriteRawValue(
                    number.ToString("G29", CultureInfo.InvariantCulture),
                    skipInputValidation: true);
                break;
            case JsonValueKind.Number:
                writer.WriteNumberValue(value.GetDouble());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new JsonException(
                    $"Unsupported JSON value kind {value.ValueKind}.");
        }
    }
}
