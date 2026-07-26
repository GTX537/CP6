using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public sealed class FlowFormCompatibilityValidator : IFlowFormCompatibilityValidator
{
    private static readonly Regex Identifier = new(@"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.Compiled);
    private static readonly Regex StringLiteral = new(@"(['""]).*?\1", RegexOptions.Compiled);
    private static readonly HashSet<string> Keywords =
        new(StringComparer.OrdinalIgnoreCase) { "true", "false", "null", "and", "or" };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly CP6Context _db;
    public FlowFormCompatibilityValidator(CP6Context db) => _db = db;

    public async Task ValidateFlowPublishAsync(Guid flowDefId, string flowSchemaJson, CancellationToken ct = default)
    {
        var formSchemas = await (
            from binding in _db.Wf_FormFlowBindings
            join version in _db.Wf_FormDefVersions on binding.FormDefId equals version.FormDefId
            where binding.FlowDefId == flowDefId && binding.Enable &&
                  version.Status == WfDefinitionVersionStatus.Published
            select new { binding.FormDefId, version.Version, version.SchemaJson })
            .ToListAsync(ct);

        foreach (var latest in formSchemas.GroupBy(x => x.FormDefId).Select(x => x.MaxBy(y => y.Version)!))
            EnsureCompatible(latest.SchemaJson, flowSchemaJson, "E-WF-030");
    }

    public async Task ValidateFormPublishAsync(Guid formDefId, string formSchemaJson, CancellationToken ct = default)
    {
        var flowSchemas = await (
            from binding in _db.Wf_FormFlowBindings
            join version in _db.Wf_FlowDefVersions on binding.FlowDefId equals version.FlowDefId
            where binding.FormDefId == formDefId && binding.Enable &&
                  version.Status == WfDefinitionVersionStatus.Published
            select new { binding.FlowDefId, version.Version, version.SchemaJson })
            .ToListAsync(ct);

        foreach (var latest in flowSchemas.GroupBy(x => x.FlowDefId).Select(x => x.MaxBy(y => y.Version)!))
            EnsureCompatible(formSchemaJson, latest.SchemaJson, "E-WF-036");
    }

    public async Task ValidateBindingAsync(Guid formDefId, Guid flowDefId, CancellationToken ct = default)
    {
        var form = await _db.Wf_FormDefVersions.AsNoTracking()
            .Where(x => x.FormDefId == formDefId && x.Status == WfDefinitionVersionStatus.Published)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("E-WF-036");
        var flow = await _db.Wf_FlowDefVersions.AsNoTracking()
            .Where(x => x.FlowDefId == flowDefId && x.Status == WfDefinitionVersionStatus.Published)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("E-WF-029");
        EnsureCompatible(form.SchemaJson, flow.SchemaJson, "E-WF-030");
    }

    internal static void EnsureCompatible(string formSchemaJson, string flowSchemaJson, string errorCode)
    {
        FormSchema form;
        FlowSchema flow;
        try
        {
            form = JsonSerializer.Deserialize<FormSchema>(formSchemaJson, JsonOptions) ?? new FormSchema();
            flow = JsonSerializer.Deserialize<FlowSchema>(flowSchemaJson, JsonOptions) ?? new FlowSchema();
        }
        catch (JsonException) { throw new InvalidOperationException(errorCode); }

        var fields = form.Fields.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var refs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in flow.Nodes)
        {
            if (node.FieldPerms != null) refs.UnionWith(node.FieldPerms.Keys);
            Add(refs, node.ApproverFieldName);
            AddExpression(refs, node.ApproverWhen);
            AddExpression(refs, node.ApproverFilter);
            if (node.Stages == null) continue;
            foreach (var stage in node.Stages)
            {
                Add(refs, stage.ApproverFieldName);
                AddExpression(refs, stage.ApproverWhen);
                AddExpression(refs, stage.ApproverFilter);
            }
        }
        foreach (var edge in flow.Edges) AddExpression(refs, edge.Condition);
        if (refs.Any(x => !fields.Contains(x))) throw new InvalidOperationException(errorCode);
    }

    private static void Add(HashSet<string> refs, string? field)
    {
        if (!string.IsNullOrWhiteSpace(field)) refs.Add(field);
    }

    private static void AddExpression(HashSet<string> refs, string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return;
        var scrubbed = StringLiteral.Replace(expression, string.Empty);
        foreach (Match match in Identifier.Matches(scrubbed))
            if (!Keywords.Contains(match.Value) && !double.TryParse(match.Value, out _))
                refs.Add(match.Value);
    }
}
