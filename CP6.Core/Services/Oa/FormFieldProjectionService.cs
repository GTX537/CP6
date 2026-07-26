using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed class FormFieldProjectionService : IFormFieldProjectionService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly CP6Context _db;

    public FormFieldProjectionService(CP6Context db) => _db = db;

    public async Task<ProjectedForm> ProjectAsync(
        Guid instanceId, Guid viewerId, string dataJson, CancellationToken ct = default)
    {
        var pinned = await LoadPinnedAsync(instanceId, ct);
        if (pinned.FormVersion == null || pinned.FlowVersion == null)
            return new(pinned.FormDataId, null, null, """{"fields":[]}""", "{}",
                new Dictionary<string, string>(), true);

        var schema = ParseForm(pinned.FormVersion.SchemaJson);
        var flow = ParseFlow(pinned.FlowVersion.SchemaJson);
        var mask = schema.Fields.ToDictionary(x => x.Name, _ => "hidden", StringComparer.Ordinal);

        if (pinned.StarterId == viewerId)
            foreach (var field in schema.Fields) mask[field.Name] = "readonly";

        var currentNodes = await _db.Wf_FlowTasks.AsNoTracking()
            .Where(x => x.InstanceId == instanceId && x.AssigneeId == viewerId &&
                        x.Status == FlowTaskStatus.Pending)
            .Select(x => x.NodeId).Distinct().ToListAsync(ct);
        foreach (var nodeId in currentNodes)
            MergeNodeMask(mask, schema, flow, nodeId, allowEdit: true);

        var historicalNodes = await _db.Wf_FlowFormTos.AsNoTracking()
            .Where(x => x.InstanceId == instanceId &&
                        (x.ExpectedHandlerId == viewerId || x.ActualHandlerId == viewerId ||
                         x.OnBehalfOfId == viewerId))
            .Select(x => x.NodeId).Distinct().ToListAsync(ct);
        foreach (var nodeId in historicalNodes)
            MergeNodeMask(mask, schema, flow, nodeId, allowEdit: false);

        var ccNodes = await _db.Wf_FlowCcs.AsNoTracking()
            .Where(x => x.InstanceId == instanceId && x.RecipientId == viewerId && x.AtNodeId != null)
            .Select(x => x.AtNodeId!).Distinct().ToListAsync(ct);
        foreach (var nodeId in ccNodes)
            MergeNodeMask(mask, schema, flow, nodeId, allowEdit: false);

        ApplyRuleVisibility(mask, schema, dataJson);
        return BuildProjection(pinned, dataJson, mask);
    }

    public async Task<IReadOnlyDictionary<string, string>> DecisionMaskAsync(
        Guid instanceId, string nodeId, string dataJson, CancellationToken ct = default)
    {
        var pinned = await LoadPinnedAsync(instanceId, ct);
        if (pinned.FormVersion == null || pinned.FlowVersion == null)
            return new Dictionary<string, string>();
        var schema = ParseForm(pinned.FormVersion.SchemaJson);
        var flow = ParseFlow(pinned.FlowVersion.SchemaJson);
        var mask = schema.Fields.ToDictionary(x => x.Name, _ => "hidden", StringComparer.Ordinal);
        MergeNodeMask(mask, schema, flow, nodeId, allowEdit: true);
        ApplyRuleVisibility(mask, schema, dataJson);
        return mask;
    }

    private async Task<PinnedData> LoadPinnedAsync(Guid instanceId, CancellationToken ct)
    {
        var instance = await _db.Wf_FlowInstances.AsNoTracking()
            .SingleAsync(x => x.Id == instanceId, ct);
        var flow = instance.FlowDefVersionId is Guid flowId
            ? await _db.Wf_FlowDefVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == flowId, ct)
            : null;
        var form = instance.FormDefVersionId is Guid formId
            ? await _db.Wf_FormDefVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == formId, ct)
            : null;
        var formKey = form == null ? null : await _db.Wf_FormDefs.AsNoTracking()
            .Where(x => x.Id == form.FormDefId).Select(x => x.FormKey).SingleAsync(ct);
        return new(instance.StarterId, instance.FormDataId, formKey, flow, form);
    }

    private static void MergeNodeMask(
        IDictionary<string, string> result, FormSchema schema, FlowSchema flow,
        string nodeId, bool allowEdit)
    {
        var configured = flow.Nodes.FirstOrDefault(x => x.Id == nodeId)?.FieldPerms;
        foreach (var field in schema.Fields)
        {
            var permission = configured != null && configured.TryGetValue(field.Name, out var value)
                ? value.ToLowerInvariant()
                : "readonly";
            if (permission == "hidden") continue;
            if (!allowEdit || permission != "edit") permission = "readonly";
            if (permission == "edit" || result[field.Name] == "hidden")
                result[field.Name] = permission;
        }
    }

    private static void ApplyRuleVisibility(
        IDictionary<string, string> mask, FormSchema schema, string dataJson)
    {
        var hidden = new HashSet<string>(StringComparer.Ordinal);
        var vars = ExpressionEvaluator.ParseVars(dataJson);
        foreach (var rule in schema.Rules)
        {
            if (!ExpressionEvaluator.Evaluate(rule.When, vars)) continue;
            foreach (var effect in rule.Then)
            {
                if (effect.Action == "hide") hidden.Add(effect.Target);
                else if (effect.Action == "show") hidden.Remove(effect.Target);
            }
        }
        foreach (var field in hidden)
            if (mask.ContainsKey(field)) mask[field] = "hidden";
    }

    private static ProjectedForm BuildProjection(
        PinnedData pinned, string dataJson, IReadOnlyDictionary<string, string> mask)
    {
        var schemaRoot = JsonNode.Parse(pinned.FormVersion!.SchemaJson)?.AsObject() ?? new JsonObject();
        var fields = schemaRoot["fields"]?.AsArray() ?? new JsonArray();
        var visibleNames = mask.Where(x => x.Value != "hidden").Select(x => x.Key)
            .ToHashSet(StringComparer.Ordinal);
        for (var index = fields.Count - 1; index >= 0; index--)
        {
            var name = fields[index]?["name"]?.GetValue<string>();
            if (name == null || !visibleNames.Contains(name)) fields.RemoveAt(index);
        }

        if (schemaRoot["rules"] is JsonArray rules)
        {
            foreach (var rule in rules.OfType<JsonObject>().ToList())
            {
                if (rule["then"] is not JsonArray effects) continue;
                for (var index = effects.Count - 1; index >= 0; index--)
                {
                    var target = effects[index]?["target"]?.GetValue<string>();
                    if (target != null && !visibleNames.Contains(target)) effects.RemoveAt(index);
                }
                if (effects.Count == 0) rules.Remove(rule);
            }
        }

        var source = JsonNode.Parse(string.IsNullOrWhiteSpace(dataJson) ? "{}" : dataJson)?.AsObject()
                     ?? new JsonObject();
        foreach (var key in source.Select(x => x.Key).Where(x => !visibleNames.Contains(x)).ToList())
            source.Remove(key);
        var exposedMask = mask.Where(x => x.Value != "hidden")
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        return new(pinned.FormDataId, pinned.FormKey, pinned.FormVersion.Version,
            schemaRoot.ToJsonString(), source.ToJsonString(), exposedMask, false);
    }

    private static FormSchema ParseForm(string json) =>
        JsonSerializer.Deserialize<FormSchema>(json, JsonOptions)
        ?? throw new InvalidOperationException("E-WF-047");

    private static FlowSchema ParseFlow(string json) =>
        JsonSerializer.Deserialize<FlowSchema>(json, JsonOptions)
        ?? throw new InvalidOperationException("E-WF-047");

    private sealed record PinnedData(
        Guid StarterId, Guid? FormDataId, string? FormKey,
        Wf_FlowDefVersion? FlowVersion, Wf_FormDefVersion? FormVersion);
}
