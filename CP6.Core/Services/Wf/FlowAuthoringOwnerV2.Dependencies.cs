using System.Text.Json;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public sealed partial class FlowAuthoringOwnerV2
{
    private sealed class DependencyReader(FlowAuthoringOwnerV2 owner, UserPermissionContext? actor, string? menu, CancellationToken ct)
    {
        private readonly FormAuthoringOwnerV2 forms = new(owner.db, owner.http, owner.fence, owner.currentActor, owner.catalog);
        private readonly Dictionary<(string Key, Guid? Version), FlowPublicationV2> flows = [];
        private readonly Dictionary<(string Key, Guid? Version), FormPublicationV2> formVersions = [];
        private readonly Dictionary<Guid, GraphSummary> qualified = [];
        private sealed record GraphSummary(HashSet<Guid> Definitions, int Height);
        private bool flowReady;
        internal async Task<FormPublicationV2> Form(string key, Guid? version)
        {
            if (formVersions.TryGetValue((key, version), out var known)) return known;
            var p = await forms.ReadDependencyAsync(actor, key, version, menu, ct);
            formVersions[(key, version)] = p; return p;
        }
        private async Task<FlowPublicationV2> Flow(string key, Guid? version)
        {
            if (flows.TryGetValue((key, version), out var known)) return known;
            if (!flowReady) { await owner.ActivationReady(ct); flowReady = true; }
            var head = await owner.Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEPENDENCY_NOT_AVAILABLE", 404);
            if (actor != null)
            {
                var fields = await owner.Permission(actor, head, false, false, ct, menu!);
                if (Fields.Any(f => !fields.Fields[f].CanRead)) throw Error("DEPENDENCY_NOT_READABLE", 403);
            }
            var activation = await owner.Activation(head.Id, ct);
            if (!activation.Enabled) throw Error("DEPENDENCY_DISABLED", 422);
            var id = version ?? activation.PublishedVersionId!.Value;
            var row = await owner.Published(head.Id, id, ct) ?? throw Error("DEPENDENCY_VERSION_NOT_AVAILABLE", 404);
            var p = JsonSerializer.Deserialize<FlowPublicationV2>(row.Snapshot, Json)!;
            flows[(key, version)] = p; return p;
        }
        internal async Task<FlowPublicationV2> Child(string key, Guid? version, HashSet<Guid> ancestors)
        {
            var p = await Flow(key, version); await Qualify(p, ancestors); return p;
        }
        internal async Task QualifyRoot(FlowPublicationV2 publication) => _ = await Qualify(publication, []);
        private async Task<GraphSummary> Qualify(FlowPublicationV2 p, HashSet<Guid> ancestors)
        {
            ct.ThrowIfCancellationRequested();
            if (ancestors.Contains(p.DefinitionId)) throw Error("DEPENDENCY_CYCLE", 422);
            if (ancestors.Count >= SubFlowLimits.MaxDepth) throw Error("DEPENDENCY_DEPTH_EXCEEDED", 422);
            if (qualified.TryGetValue(p.VersionId, out var cached))
            {
                if (cached.Definitions.Overlaps(ancestors)) throw Error("DEPENDENCY_CYCLE", 422);
                if (ancestors.Count + cached.Height > SubFlowLimits.MaxDepth) throw Error("DEPENDENCY_DEPTH_EXCEEDED", 422);
                return cached;
            }
            var path = new HashSet<Guid>(ancestors) { p.DefinitionId }; var definitions = new HashSet<Guid> { p.DefinitionId }; var height = 1;
            FlowSchema? schema = null;
            foreach (var reference in p.DependencyRefs)
            {
                if (reference.Kind == "FORM") _ = await Form(reference.Reference, Pin(reference));
                else if (reference.Kind == "FLOW")
                {
                    var child = await Flow(reference.Reference, Pin(reference)); var graph = await Qualify(child, path);
                    definitions.UnionWith(graph.Definitions); height = Math.Max(height, graph.Height + 1);
                }
                else if (reference.Kind == "SERVICE")
                {
                    schema ??= Schema(p.Fields["SchemaJson"]!);
                    var node = schema.Nodes.Single(n => n.Id == reference.Location);
                    if (owner.ServiceDependency(node) != reference) throw Error("SERVICE_DEPENDENCY_CHANGED", 422);
                }
                else throw Error("DEPENDENCY_KIND_UNSUPPORTED", 422);
            }
            var result = new GraphSummary(definitions, height); qualified[p.VersionId] = result; return result;
        }
    }
    private static Guid Pin(FlowDependencyRefV2 reference)
        => Guid.TryParseExact(reference.Version, "D", out var id) && id != Guid.Empty ? id : throw Error("DEPENDENCY_PIN_REQUIRED", 422);
    private FlowDependencyRefV2? ServiceDependency(FlowNode node)
    {
        object? provider = node.ServiceKind == ServiceKind.DataWriteback
            ? executors.SingleOrDefault(x => x.Kind == ServiceKind.DataWriteback && string.Equals(x.Key, node.ServiceActionName, StringComparison.OrdinalIgnoreCase))
            : node.ServiceKind == ServiceKind.WebApi ? connectors.SingleOrDefault(x => string.Equals(x.Name, node.ServiceConnectorName, StringComparison.OrdinalIgnoreCase)) : null;
        if (provider == null) return null;
        var type = provider.GetType();
        return new("SERVICE", node.Id, node.ServiceActionName ?? node.ServiceConnectorName ?? "",
            type.AssemblyQualifiedName + ":" + type.Module.ModuleVersionId.ToString("D"), "CURRENT_HOST_REGISTRATION");
    }
    private async Task<(string[] Diagnostics, FlowDependencyRefV2[] Dependencies,DefinitionDiagnosticV2[] Details)> PublicationInputs(Head head,
        UserPermissionContext actor, CancellationToken ct, FlowDependencyRefV2[]? pins = null)
    {
        var diagnostics = new List<DefinitionDiagnosticV2>();
        try { diagnostics.AddRange(ValidateSchema(head.Key, head.SchemaJson, ct)); }
        catch (ScopeAuthorizationExceptionV1 e) when (e.Status == 422) { diagnostics.AddRange(DefinitionDiagnosticsV2.From(e)); }
        var schema = Schema(head.SchemaJson); var dependencies = new List<FlowDependencyRefV2>();
        var reader = new DependencyReader(this, actor, "oa-designer", ct);
        async Task Resolve(string kind, string location, string key)
        {
            try
            {
                var pinned = pins?.SingleOrDefault(p => p.Kind == kind && p.Location == location);
                var id = pins == null ? (Guid?)null : pinned == null ? throw Error("DEPENDENCY_PIN_REQUIRED", 422) : Pin(pinned);
                Guid selected;
                if (kind == "FORM")
                {
                    var form = await reader.Form(key, id); selected = form.VersionId;
                    FlowFormCompatibilityValidator.EnsureCompatible(form.Fields["SchemaJson"]!, head.SchemaJson, "FORM_FLOW_INCOMPATIBLE");
                }
                else selected = (await reader.Child(key, id, [head.Id])).VersionId;
                dependencies.Add(new(kind, location, key, selected.ToString("D"), "PUBLISHED_ENABLED"));
            }
            catch (ScopeAuthorizationExceptionV1 e) when (e.Status is 403 or 404 or 422)
            {
                diagnostics.Add(Located(e.Status == 404 ? "DEPENDENCY_NOT_AVAILABLE" : e.Status == 403 ? "DEPENDENCY_NOT_READABLE" : e.Message));
                dependencies.Add(new(kind, location, key, null, "UNAVAILABLE"));
            }
            catch (InvalidOperationException e) when (e.Message == "FORM_FLOW_INCOMPATIBLE")
            { diagnostics.Add(Located(e.Message)); dependencies.Add(new(kind, location, key, null, "INCOMPATIBLE")); }
            DefinitionDiagnosticV2 Located(string code)=>kind=="FORM"?new("$.formKey",code):new($"$.nodes[{schema.Nodes.FindIndex(n=>n.Id==location)}].subFlowKey",code,NodeId:location);
        }
        if (!string.IsNullOrWhiteSpace(head.FormKey)) await Resolve("FORM", "formKey", head.FormKey);
        foreach (var node in schema.Nodes)
        {
            if (string.Equals(node.Type?.Trim(), "subFlow", StringComparison.OrdinalIgnoreCase)) await Resolve("FLOW", node.Id, node.SubFlowKey ?? "");
            if (string.Equals(node.Type?.Trim(), "serviceTask", StringComparison.OrdinalIgnoreCase) && ServiceDependency(node) is { } service) dependencies.Add(service);
        }
        return (diagnostics.Select(d=>d.Code).Distinct().ToArray(), dependencies.ToArray(),diagnostics.Distinct().ToArray());
    }
}
