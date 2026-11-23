using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.Contracts.Wire;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowForecastPinV1(string Kind, string Location, Guid VersionId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WorkflowForecastInputV1(Guid? PublishedVersionId, Guid? DraftVersionId, string? ExpectedVersion,
    string VarsJson, WorkflowForecastPinV1[]? DraftDependencies);

public sealed record WorkflowForecastDependencyV1(string Kind, string Location, Guid? VersionId, string EvidenceState);
public sealed record WorkflowForecastV1(string Mode, bool IsExecutionPermit, string Source, Guid DefinitionId, Guid VersionId,
    string? DraftRevision, DateTimeOffset KnowledgeCutUtc, string StarterBasis, string Coverage,
    int VisitedNodeCount, int TotalNodeCount, int TraversedEdgeCount, int TotalEdgeCount, WorkflowForecastNodeV1[] Nodes,
    WorkflowForecastEdgeV1[] Edges, WorkflowForecastUnvisitedV1[] Unvisited, FlowSimulationDiagnosticV2[] Diagnostics,
    WorkflowForecastDependencyV1[] Dependencies);

public sealed partial class FlowAuthoringOwnerV2
{
    private static WorkflowForecastInputV1 ReadForecast(byte[] raw)
    {
        try
        {
            using var request = WorkflowCommandJsonV1.Parse(raw);
            var input = request.Deserialize<WorkflowForecastInputV1>(Json) ?? throw new JsonException();
            if (input.PublishedVersionId is Guid pub)
            {
                if (pub == Guid.Empty || input.DraftVersionId != null || input.ExpectedVersion != null || input.DraftDependencies != null)
                    throw new JsonException();
            }
            else if (input.DraftVersionId is not Guid draft || draft == Guid.Empty ||
                     !Guid.TryParseExact(input.ExpectedVersion, "D", out _)) throw new JsonException();
            var pins = input.DraftDependencies ?? [];
            if (pins.Length > 100 || pins.Any(p => p is null || p.Kind is not ("FORM" or "FLOW") ||
                string.IsNullOrWhiteSpace(p.Location) || p.VersionId == Guid.Empty) ||
                pins.Select(p => (p.Kind, p.Location)).Distinct().Count() != pins.Length) throw new JsonException();
            using var vars = WorkflowCommandJsonV1.Parse(Encoding.UTF8.GetBytes(input.VarsJson ?? throw new JsonException()));
            if (vars.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            return input;
        }
        catch (JsonException) { throw Error("FORECAST_INPUT_INVALID", 400); }
    }

    private async Task ForecastReadPermission(UserPermissionContext actor, Head head, CancellationToken ct)
    {
        var fields = await Permission(actor, head, false, false, ct);
        if (!fields.Fields["SchemaJson"].CanRead || !fields.Fields["FormKey"].CanRead)
            throw Error("FIELD_READ_FORBIDDEN", 403);
    }

    private async Task<bool> CanReadForecastPeople(UserPermissionContext actor, string menu, CancellationToken ct) =>
        actor.MenuKeys.Contains(menu) && actor.ActionKeys.Contains(menu + ":query") && await
            (from action in db.Sys_MenuActions
             join row in db.Sys_Menus on action.MenuId equals row.MenuId
             where row.Enable && row.MenuKey == menu && action.ActionCode == "query"
             select action).AnyAsync(ct);

    public async Task<WorkflowForecastV1> ForecastAsync(string key, byte[] raw, CancellationToken ct = default)
    {
        var input = ReadForecast(raw);
        Entry();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await fence.Root(ct);
        var actor = await Actor(ct);
        await WorkflowForecastAuthorityV1.RequireAsync(db, actor, ct);
        var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
        await ForecastReadPermission(actor, head, ct);

        var isDraft = input.PublishedVersionId is null;
        var version = input.PublishedVersionId ?? input.DraftVersionId!.Value;
        var definition = head;
        FlowDependencyRefV2[] pins;
        if (isDraft)
        {
            if (head.Status != 0 || head.DraftId != version || head.Version.ToString("D") != input.ExpectedVersion)
                throw Error("DEFINITION_CHANGED", 409);
            pins = (input.DraftDependencies ?? []).Select(p =>
                new FlowDependencyRefV2(p.Kind, p.Location, "", p.VersionId.ToString("D"), "FORECAST_INPUT_PIN")).ToArray();
        }
        else
        {
            var row = await Published(head.Id, version, ct) ?? throw Error("VERSION_NOT_FOUND", 404);
            var publication = JsonSerializer.Deserialize<FlowPublicationV2>(row.Snapshot, Json) ?? throw Error("PUBLICATION_INVALID");
            if (publication.VersionId != version || publication.DefinitionId != head.Id ||
                publication.DependencyRefs is not { } savedPins || publication.Fields.GetValueOrDefault("SchemaJson") is not { Length: > 0 } graph ||
                publication.SchemaDigest != SchemaDigest(graph))
                throw Error("PUBLICATION_INVALID");
            definition = head with { SchemaJson = graph, FormKey = publication.Fields.GetValueOrDefault("FormKey") };
            pins = savedPins;
        }

        var cut = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        var dependencies = await PublicationInputs(definition, actor, ct, pins);
        var diagnostics = dependencies.Details.Select(d => new FlowSimulationDiagnosticV2(d.Path, d.Code)).ToList();
        var schema = Schema(definition.SchemaJson);
        var resolver = new ApproverResolver(db, exactConditions: true);
        var planner = new ApprovalStagePlanner(resolver);
        var remainingWork = 1024;
        var remainingPeople = 1000;
        var approvedMenus = new HashSet<string>(StringComparer.Ordinal);
        var canReadUsers = await CanReadForecastPeople(actor, "user", ct);

        async Task<WorkflowForecastStageV1[]> People(FlowNode node)
        {
            var location = $"/nodes/{schema.Nodes.IndexOf(node)}";
            var cost = SimulationResolutionCost(node);
            if (cost > remainingWork)
            { diagnostics.Add(new(location, "APPROVER_EXPANSION_LIMIT")); return [new(0, "ARRIVAL_TIME", [])]; }
            remainingWork -= cost;
            var strategies = new[] { node.ApproverStrategy }.Concat(node.ApproverMembers?.Select(x => x.Strategy) ?? [])
                .Concat((node.Stages ?? []).SelectMany(s => new[] { s.ApproverStrategy }.Concat(s.ApproverMembers?.Select(x => x.Strategy) ?? [])))
                .ToArray();
            var organization = strategies.Contains("DeptLeader", StringComparer.OrdinalIgnoreCase);
            var map = strategies.Contains("DataMap", StringComparer.OrdinalIgnoreCase);
            if (!canReadUsers || organization && !await CanReadForecastPeople(actor, "pub-dept", ct) ||
                map && !await CanReadForecastPeople(actor, "oa-approver-map", ct))
                return [new(0, "REDACTED", [])];

            try
            {
                // 仅构造脱离 DbContext 跟踪的人员规则输入。
                var steps = await planner.BuildAsync(new Wf_FlowInstance { StarterId = actor.UserId, VarsJson = input.VarsJson }, schema, node);
                var results = new List<WorkflowForecastStageV1>();
                foreach (var step in steps)
                {
                    ct.ThrowIfCancellationRequested();
                    var value = await resolver.ResolveAsync(step.Rule, new() { StarterUserId = actor.UserId, VarsJson = input.VarsJson });
                    if (!value.Resolved)
                    { results.Add(new(step.StageIndex, "ARRIVAL_TIME", [])); continue; }
                    if (value.ApproverIds.Count > remainingPeople)
                    {
                        diagnostics.Add(new(location, "APPROVER_RESULT_LIMIT"));
                        results.Add(new(step.StageIndex, "ARRIVAL_TIME", []));
                        continue;
                    }
                    remainingPeople -= value.ApproverIds.Count;
                    // 只投影当前 IAM 用户查询已允许的本租户显示名称；不披露身份属性、组织路径或解析输入。
                    var ids = value.ApproverIds.ToArray();
                    var people = await db.Sys_Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.Enable)
                        .OrderBy(u => u.Id).Select(u => new WorkflowForecastPersonV1(u.Id, u.NickName ?? u.UserName)).ToArrayAsync(ct);
                    if (people.Length != ids.Length || people.Any(p => string.IsNullOrWhiteSpace(p.DisplayName)))
                    { results.Add(new(step.StageIndex, "ARRIVAL_TIME", [])); continue; }
                    approvedMenus.Add("user");
                    if (organization) approvedMenus.Add("pub-dept");
                    if (map) approvedMenus.Add("oa-approver-map");
                    results.Add(new(step.StageIndex, "RESOLVED_AT_CUT", people));
                }
                return results.Count > 0 ? results.ToArray() : [new(0, "ARRIVAL_TIME", [])];
            }
            catch (ScopeAuthorizationExceptionV1 e) when (e.Status == 422)
            {
                diagnostics.Add(new(location, "APPROVER_EXPRESSION_UNPROVEN"));
                return [new(0, "ARRIVAL_TIME", [])];
            }
        }

        var result = await WorkflowForecastPlanner.ExplainAsync(schema, input.VarsJson, People, diagnostics, ct);
        var current = await Actor(ct);
        if (current.UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401);
        await WorkflowForecastAuthorityV1.RequireAsync(db, current, ct);
        await ForecastReadPermission(current, head, ct);
        foreach (var menu in approvedMenus)
            if (!await CanReadForecastPeople(current, menu, ct)) throw Error("ACTION_FORBIDDEN", 403);
        CompletionClock();

        var unvisited = result.Unvisited.Concat(dependencies.Details.Where(d =>
            d.Code is "DEPENDENCY_DEPTH_EXCEEDED" or "DEPENDENCY_CYCLE" or "DEPENDENCY_PIN_REQUIRED")
            .Select(d => new WorkflowForecastUnvisitedV1(d.Path, d.Code))).ToArray();

        return new("EXPLANATION_ONLY", false, isDraft ? "DRAFT_SIMULATION" : "PUBLISHED_PIN", head.Id, version,
            isDraft ? input.ExpectedVersion : null, cut, "CURRENT_ACTOR_AS_STARTER",
            diagnostics.Count == 0 ? "COMPLETE_FOR_SUPPORTED_GRAPH" : "PARTIAL",
            result.Nodes.Select(n => n.NodeId).Distinct(StringComparer.Ordinal).Count(), schema.Nodes.Count,
            result.Edges.Count(e => e.Traversed), schema.Edges.Count, result.Nodes, result.Edges, unvisited,
            diagnostics.Distinct().ToArray(), dependencies.Dependencies.Select(d =>
                new WorkflowForecastDependencyV1(d.Kind, d.Location, d.Kind is "FORM" or "FLOW" && Guid.TryParse(d.Version, out var id)
                    ? id : null, d.EvidenceState)).ToArray());
    }
}
