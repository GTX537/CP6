using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Sys;
using CP6.Entity.Contracts.Wire;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FlowSimulationInputV2(Guid DraftVersionId, string ExpectedVersion, string VarsJson);
public sealed record FlowSimulationDiagnosticV2(string Path, string Code, string EvidenceState = "UNPROVEN");
public sealed record FlowSimulationStageV2(int StageIndex, string Countersign, string State, Guid[] ApproverIds);
public sealed record FlowSimulationNodeV2(string NodeId, string Type, FlowSimulationStageV2[] Stages);
public sealed record FlowSimulationEdgeV2(int Index, string From, string To);
public sealed record FlowSimulationV2(string Mode, Guid DefinitionId, Guid DraftVersionId, string ExpectedVersion,
    string[] Assumptions, FlowSimulationNodeV2[] Nodes, FlowSimulationEdgeV2[] Edges,
    FlowSimulationDiagnosticV2[] Diagnostics, FlowDependencyRefV2[] Dependencies);

public sealed partial class FlowAuthoringOwnerV2
{
    private static int SimulationResolutionCost(FlowNode node)
    {
        // A read-only explanation has a bounded expansion budget, independent of save/runtime
        // admission. Manager-chain planning itself probes every prefix before resolving stages.
        static int RuleCost(int? levels, List<ApproverSpec>? members)
        {
            var cost = Math.Min(1025, Math.Max(1, levels ?? 1));
            foreach (var member in members ?? [])
            { cost += Math.Min(1025, Math.Max(1, member.ApproverLevels ?? 1)); if (cost > 1024) return 1025; }
            return cost;
        }
        if (node.Stages is not { Count: > 0 }) return RuleCost(node.ApproverLevels, node.ApproverMembers);
        var total = 0;
        foreach (var stage in node.Stages)
        {
            if (string.Equals(stage.Kind?.Trim(), ApprovalStageKinds.ManagerChain, StringComparison.OrdinalIgnoreCase))
            {
                var levels = Math.Max(1, stage.MaxLevels ?? 1);
                if (levels > 32) return 1025;
                total += levels * (levels + 1);
            }
            else total += RuleCost(stage.ApproverLevels, stage.ApproverMembers);
            if (total > 1024) return 1025;
        }
        return total;
    }
    private static FlowSimulationInputV2 SimulationInput(byte[] raw)
    {
        try
        {
            using var document = WorkflowCommandJsonV1.Parse(raw);
            var input = document.Deserialize<FlowSimulationInputV2>(Json) ?? throw new JsonException();
            if (input.DraftVersionId == Guid.Empty || !Guid.TryParseExact(input.ExpectedVersion, "D", out _)
                || input.VarsJson == null) throw new JsonException();
            using var vars = WorkflowCommandJsonV1.Parse(Encoding.UTF8.GetBytes(input.VarsJson));
            if (vars.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            return input;
        }
        catch (JsonException) { throw Error("SIMULATION_INPUT_INVALID", 400); }
    }

    public async Task<FlowSimulationV2> SimulateAsync(string key, byte[] raw, CancellationToken ct = default)
    {
        var input = SimulationInput(raw);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct);
        var actor = await Actor(ct);
        var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
        var fields = await Permission(actor, head, false, false, ct);
        if (!fields.Fields["SchemaJson"].CanRead || !fields.Fields["FormKey"].CanRead) throw Error("FIELD_READ_FORBIDDEN", 403);
        if (head.Status != 0) throw Error("DRAFT_REQUIRED", 409);
        if (head.DraftId != input.DraftVersionId || head.Version.ToString("D") != input.ExpectedVersion)
            throw Error("DEFINITION_CHANGED", 409);

        var dependencies = await PublicationInputs(head, actor, ct);
        var diagnostics = dependencies.Diagnostics.Select(code => new FlowSimulationDiagnosticV2("$", code)).ToList();
        var schema = Schema(head.SchemaJson);
        var resolver = new ApproverResolver(db, exactConditions: true);
        var stages = new ApprovalStagePlanner(resolver);
        var grants = new Dictionary<string, bool>();
        var remainingResolutionWork = 1024; var remainingPeople = 1000;
        async Task<bool> CanQuery(string menu)
        {
            if (grants.TryGetValue(menu, out var known)) return known;
            var allowed = actor.ActionKeys.Contains(menu + ":query") && await
                (from a in db.Sys_MenuActions join m in db.Sys_Menus on a.MenuId equals m.MenuId
                 where m.Enable && m.MenuKey == menu && a.ActionCode == "query" select a).AnyAsync(ct);
            return grants[menu] = allowed;
        }
        async Task<FlowSimulationStageV2[]> Resolve(FlowNode node, string path)
        {
            var cost = SimulationResolutionCost(node);
            if (cost > remainingResolutionWork)
            { diagnostics.Add(new(path, "APPROVER_EXPANSION_LIMIT")); return []; }
            remainingResolutionWork -= cost;
            // Current user:query is the existing IAM tenant-user read boundary. A designer read
            // alone must not disclose membership, account status, names, counts or candidates.
            var strategies = new List<string?> { node.ApproverStrategy };
            strategies.AddRange(node.ApproverMembers?.Select(x => x.Strategy) ?? []);
            foreach (var stage in node.Stages ?? [])
            {
                strategies.Add(stage.ApproverStrategy);
                strategies.AddRange(stage.ApproverMembers?.Select(x => x.Strategy) ?? []);
            }
            if (!await CanQuery("user")
                || strategies.Any(x => string.Equals(x, "DeptLeader", StringComparison.OrdinalIgnoreCase)) && !await CanQuery("pub-dept")
                || strategies.Any(x => string.Equals(x, "DataMap", StringComparison.OrdinalIgnoreCase)) && !await CanQuery("oa-approver-map"))
            {
                diagnostics.Add(new(path, "APPROVER_AUTHORITY_UNPROVEN")); return [];
            }
            try
            {
                // This detached input only serves the existing pure stage planner. No engine,
                // persistence, task factory, notification, callback or connector is invoked.
                var planned = await stages.BuildAsync(new Wf_FlowInstance { StarterId = actor.UserId, VarsJson = input.VarsJson }, schema, node);
                var result = new List<FlowSimulationStageV2>();
                foreach (var stage in planned)
                {
                    ct.ThrowIfCancellationRequested();
                    var resolved = await resolver.ResolveAsync(stage.Rule, new() { StarterUserId = actor.UserId, VarsJson = input.VarsJson });
                    if (resolved.ApproverIds.Count > remainingPeople) { diagnostics.Add(new(path, "APPROVER_RESULT_LIMIT")); return []; }
                    remainingPeople -= resolved.ApproverIds.Count;
                    result.Add(new(stage.StageIndex, stage.Countersign, resolved.Resolved ? "RESOLVED_NOW" : "UNRESOLVED", resolved.ApproverIds.ToArray()));
                    if (!resolved.Resolved) diagnostics.Add(new(path + "/stages/" + stage.StageIndex, "APPROVER_UNRESOLVED"));
                }
                return result.ToArray();
            }
            catch (ScopeAuthorizationExceptionV1 error) when (error.Status == 422)
            { diagnostics.Add(new(path, "APPROVER_EXPRESSION_UNPROVEN")); return []; }
        }
        var prediction = await FlowSimulationPlannerV2.Predict(schema, input.VarsJson, Resolve, diagnostics, ct);
        var finalActor = await Actor(ct);
        if (finalActor.UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401);
        CompletionClock();
        // Read transaction is disposed without commit; no preview/receipt/audit rows are created.
        return new("EXPLANATION_ONLY", head.Id, head.DraftId, input.ExpectedVersion,
            ["CURRENT_ACTOR_AS_STARTER", "ALL_APPROVALS_ASSUMED_APPROVED", "MOCK_VARIABLES_UNCHANGED", "NO_EXECUTION_OR_BUSINESS_ADMISSION"],
            prediction.Nodes, prediction.Edges, diagnostics.ToArray(), dependencies.Dependencies);
    }
}

internal static class FlowSimulationPlannerV2
{
    internal static async Task<(FlowSimulationNodeV2[] Nodes, FlowSimulationEdgeV2[] Edges)> Predict(FlowSchema schema, string vars,
        Func<FlowNode, string, Task<FlowSimulationStageV2[]>> resolve, List<FlowSimulationDiagnosticV2> diagnostics, CancellationToken ct)
    {
        var nodes = new List<FlowSimulationNodeV2>(); var edges = new List<FlowSimulationEdgeV2>();
        var indexed = schema.Edges.Select((edge, index) => (Edge: edge, Index: index)).ToArray();
        var lookup = schema.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal); var visits = 0;
        if (schema.Nodes.Count == 0) { diagnostics.Add(new("/nodes", "START_UNPROVEN")); return ([], []); }
        var start = string.IsNullOrEmpty(schema.Start) ? schema.Nodes[0].Id : schema.Start;
        bool Matches((FlowEdge Edge, int Index) edge)
        {
            try { return FlowEngine.ExactCondition(edge.Edge.Condition, vars); }
            catch (ScopeAuthorizationExceptionV1 error) when (error.Status == 422)
            { diagnostics.Add(new("/edges/" + edge.Index + "/condition", "EDGE_EXPRESSION_UNPROVEN")); throw; }
        }
        async Task Walk(string id, HashSet<string> ancestors)
        {
            ct.ThrowIfCancellationRequested();
            var node = lookup[id]; var path = "/nodes/" + schema.Nodes.IndexOf(node);
            if (++visits > 512) { diagnostics.Add(new(path, "SIMULATION_BUDGET_EXCEEDED")); return; }
            if (!ancestors.Add(id)) { diagnostics.Add(new(path, "REPEATED_NODE_UNPROVEN")); return; }
            var type = node.Type.Trim().ToLowerInvariant();
            nodes.Add(new(id, node.Type, type == "approval" ? await resolve(node, path) : []));
            if (type == "end") return;
            if (type is "servicetask" or "subflow")
            { diagnostics.Add(new(path, "DEPENDENT_EXECUTION_NOT_SIMULATED")); return; }
            // Forecast branch reachability, not token synchronization or an inferred completion.
            if (type is "paralleljoin" or "inclusivejoin")
            { diagnostics.Add(new(path, "JOIN_SYNCHRONIZATION_UNPROVEN")); return; }
            var outgoing = indexed.Where(x => x.Edge.From == id && x.Edge.IsError != true).ToArray();
            (FlowEdge Edge, int Index)[] selected;
            try
            {
                if (type == "parallelsplit") selected = outgoing;
                else if (type == "inclusivesplit")
                {
                    selected = outgoing.Where(x => !string.IsNullOrWhiteSpace(x.Edge.Condition) && Matches(x)).ToArray();
                    if (selected.Length == 0) selected = outgoing.Where(x => string.IsNullOrWhiteSpace(x.Edge.Condition)).Take(1).ToArray();
                }
                else
                {
                    if (outgoing.Any(x => x.Edge.Priority.HasValue)) outgoing = outgoing.OrderBy(x => x.Edge.Priority ?? int.MaxValue).ToArray();
                    selected = outgoing.Where(Matches).Take(1).ToArray();
                }
            }
            catch (ScopeAuthorizationExceptionV1 error) when (error.Status == 422)
            { return; }
            if (selected.Length == 0) diagnostics.Add(new(path, "NO_MATCHING_SUCCESS_EDGE"));
            foreach (var edge in selected)
            {
                if (visits >= 512) { diagnostics.Add(new(path, "SIMULATION_BUDGET_EXCEEDED")); return; }
                edges.Add(new(edge.Index, edge.Edge.From, edge.Edge.To));
                await Walk(edge.Edge.To, new(ancestors, StringComparer.Ordinal));
            }
        }
        await Walk(start, new(StringComparer.Ordinal));
        return (nodes.ToArray(), edges.ToArray());
    }
}
