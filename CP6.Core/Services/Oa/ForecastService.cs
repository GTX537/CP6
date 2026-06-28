using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CP6.Core.Services.Oa;

public class ForecastService : IForecastService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly CP6Context _db;
    private readonly IApproverResolver _approver;
    private readonly IApprovalStagePlanner _planner;
    public ForecastService(CP6Context db, IApproverResolver approver, IApprovalStagePlanner planner)
    { _db = db; _approver = approver; _planner = planner; }

    public async Task<ForecastResult> ForecastAsync(string flowKey, string varsJson, Guid starterId, string? fromNodeId = null)
    {
        var def = await _db.Wf_FlowDefs.FirstOrDefaultAsync(x => x.FlowKey == flowKey && x.Enable)
                  ?? throw new InvalidOperationException("E-WF-006");
        var schema = JsonSerializer.Deserialize<FlowSchema>(def.SchemaJson, JsonOpts) ?? new FlowSchema();

        var steps = new List<ForecastStep>();
        var visited = new HashSet<string>();
        bool branched = false;

        var cursor = fromNodeId is null
            ? (!string.IsNullOrEmpty(schema.Start) ? schema.Start : schema.Nodes.FirstOrDefault()?.Id)
            : NextNodeId(schema, fromNodeId, varsJson);

        int guard = 0;
        while (cursor is not null && visited.Add(cursor) && guard++ < 100)
        {
            var node = schema.Nodes.FirstOrDefault(n => n.Id == cursor);
            if (node is null) break;
            var type = (node.Type ?? "approval").Trim().ToLowerInvariant();

            switch (type)
            {
                case "end":
                    steps.Add(new ForecastStep(node.Id, node.Name, "end", Array.Empty<string>(), true, null));
                    cursor = null;
                    break;
                case "start":
                    cursor = NextNodeId(schema, cursor, varsJson);
                    break;
                case "parallelsplit":
                    branched = true;
                    steps.Add(new ForecastStep(node.Id, node.Name, "parallelSplit", Array.Empty<string>(), true, "并行分叉"));
                    cursor = NextNodeId(schema, cursor, varsJson);
                    break;
                case "paralleljoin":
                    steps.Add(new ForecastStep(node.Id, node.Name, "parallelJoin", Array.Empty<string>(), true, "汇聚"));
                    cursor = NextNodeId(schema, cursor, varsJson);
                    break;
                default: // approval
                    var plan = await _planner.BuildAsync(new Wf_FlowInstance { StarterId = starterId }, schema, node);
                    foreach (var rs in plan)
                    {
                        var (names, resolved) = await ResolveRuleNamesAsync(rs.Rule, starterId);
                        steps.Add(new ForecastStep(node.Id, rs.StageName ?? node.Name, "approval", names, resolved,
                            resolved ? null : "审批人到达时解析", rs.StageIndex, rs.StageName));
                    }
                    cursor = NextNodeId(schema, cursor, varsJson);
                    break;
            }
        }
        return new ForecastResult(steps, branched);
    }

    private static string? NextNodeId(FlowSchema schema, string from, string varsJson)
    {
        foreach (var e in schema.Edges.Where(e => e.From == from))
            if (ExpressionEvaluator.Evaluate(e.Condition, varsJson)) return e.To;
        return null;
    }

    private async Task<(IReadOnlyList<string> Names, bool Resolved)> ResolveRuleNamesAsync(ApproverRule rule, Guid starterId)
    {
        try
        {
            var res = await _approver.ResolveAsync(rule, new ApproverResolveContext { StarterUserId = starterId });
            if (!res.Resolved) return (Array.Empty<string>(), false);
            var names = await OaUserNames.ResolveAsync(_db, res.ApproverIds);
            return (res.ApproverIds.Select(id => names.GetValueOrDefault(id, id.ToString())).ToList(), true);
        }
        catch { return (Array.Empty<string>(), false); }
    }
}
