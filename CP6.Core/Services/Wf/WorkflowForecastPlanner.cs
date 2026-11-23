using CP6.Core.Services.Sys;

namespace CP6.Core.Services.Wf;

public sealed record WorkflowForecastNodeV1(string NodeId, string Type, WorkflowForecastStageV1[] Stages);
public sealed record WorkflowForecastPersonV1(Guid Id, string DisplayName);
public sealed record WorkflowForecastStageV1(int StageIndex, string State, WorkflowForecastPersonV1[] People);
public sealed record WorkflowForecastEdgeV1(int Index, string From, string To, string Condition, bool Traversed);
public sealed record WorkflowForecastUnvisitedV1(string Path, string Reason);

internal static class WorkflowForecastPlanner
{
    internal sealed record Result(WorkflowForecastNodeV1[] Nodes, WorkflowForecastEdgeV1[] Edges,
        WorkflowForecastUnvisitedV1[] Unvisited);

    internal static async Task<Result> ExplainAsync(FlowSchema schema, string vars,
        Func<FlowNode, Task<WorkflowForecastStageV1[]>> people, List<FlowSimulationDiagnosticV2> diagnostics,
        CancellationToken ct)
    {
        const int stepLimit = 100;
        var nodes = new List<WorkflowForecastNodeV1>();
        var byId = schema.Nodes.Where(n => !string.IsNullOrWhiteSpace(n.Id)).ToLookup(n => n.Id, StringComparer.Ordinal);
        var edges = schema.Edges.Select((edge, index) => (Edge: edge, Index: index)).ToArray();
        var outgoing = edges.ToLookup(x => x.Edge.From, StringComparer.Ordinal);
        var evaluated = new Dictionary<int, WorkflowForecastEdgeV1>();
        var missed = new Dictionary<string, string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var path = new HashSet<string>(StringComparer.Ordinal);

        string Condition(FlowEdge edge, int index)
        {
            try { return FlowEngine.ExactCondition(edge.Condition, vars) ? "TRUE" : "FALSE"; }
            catch (ScopeAuthorizationExceptionV1 error) when (error.Status == 422)
            {
                diagnostics.Add(new($"/edges/{index}/condition", "EDGE_EXPRESSION_UNPROVEN"));
                return "UNKNOWN";
            }
        }

        void Stop(string location, string reason)
        {
            missed.TryAdd(location, reason);
            diagnostics.Add(new(location, reason));
        }

        async Task<bool> Walk(string id, string incoming)
        {
            ct.ThrowIfCancellationRequested();
            var matches = byId[id];
            if (matches.Count() != 1)
            { Stop(incoming, "FORECAST_NODE_UNAVAILABLE"); return false; }
            var node = matches.Single();
            var location = $"/nodes/{schema.Nodes.IndexOf(node)}";
            if (nodes.Count >= stepLimit)
            { Stop(incoming, "SIMULATION_BUDGET_EXCEEDED"); return false; }
            if (!path.Add(id))
            { Stop(incoming, "REPEATED_NODE_UNPROVEN"); return false; }
            try
            {
                var type = node.Type?.Trim().ToLowerInvariant() ?? "";
                visited.Add(id);
                nodes.Add(new(id, type, type == "approval" ? await people(node) : []));
                if (type == "end") return true;
                if (type is "servicetask" or "subflow" or "paralleljoin" or "inclusivejoin")
                {
                    Stop(location, type.EndsWith("join", StringComparison.Ordinal)
                        ? "JOIN_SYNCHRONIZATION_UNPROVEN" : "DEPENDENT_EXECUTION_NOT_SIMULATED");
                    return true;
                }
                if (type is not ("start" or "approval" or "cc" or "gateway" or "condition" or "branch" or "exclusivesplit" or "exclusivegateway" or "parallelsplit" or "inclusivesplit"))
                { Stop(location, "FORECAST_NODE_UNSUPPORTED"); return true; }

                var candidates = outgoing[id].Where(x => x.Edge.IsError != true)
                    .OrderBy(x => x.Edge.Priority ?? int.MaxValue).ToArray();
                var states = candidates.Select(x => type == "parallelsplit" ? "TRUE" : Condition(x.Edge, x.Index)).ToArray();
                var unknown = Array.IndexOf(states, "UNKNOWN");
                var first = Array.IndexOf(states, "TRUE");
                var knownInclusive = type == "inclusivesplit" && candidates.Select((x, i) =>
                    !string.IsNullOrWhiteSpace(x.Edge.Condition) && states[i] == "TRUE").Any(x => x);
                var fallback = Array.FindIndex(candidates, x => string.IsNullOrWhiteSpace(x.Edge.Condition));
                var selected = false;
                for (var i = 0; i < candidates.Length; i++)
                {
                    var (edge, index) = candidates[i];
                    var choose = type switch
                    {
                        "parallelsplit" => true,
                        "inclusivesplit" when knownInclusive => states[i] == "TRUE" && !string.IsNullOrWhiteSpace(edge.Condition),
                        "inclusivesplit" => unknown < 0 && i == fallback,
                        _ => i == first && (unknown < 0 || first < unknown),
                    };
                    selected |= choose;
                    var walked = choose && await Walk(edge.To, $"/edges/{index}");
                    if (evaluated.TryGetValue(index, out var prior)) walked |= prior.Traversed;
                    evaluated[index] = new(index, edge.From, edge.To, states[i], walked);
                }
                if (!selected && unknown < 0) Stop(location, "NO_MATCHING_SUCCESS_EDGE");
                return true;
            }
            finally { path.Remove(id); }
        }

        var start = string.IsNullOrWhiteSpace(schema.Start) ? schema.Nodes.FirstOrDefault()?.Id : schema.Start;
        if (start is null) Stop("/nodes", "START_UNPROVEN");
        else await Walk(start, "/start");

        foreach (var (edge, index) in edges)
        {
            if (!evaluated.TryGetValue(index, out var result))
                evaluated[index] = result = new(index, edge.From, edge.To, "UNKNOWN", false);
            if (!result.Traversed)
                missed.TryAdd($"/edges/{index}", result.Condition switch
                {
                    "FALSE" => "CONDITION_FALSE",
                    "UNKNOWN" when edge.IsError != true && visited.Contains(edge.From) => "CONDITION_OR_PATH_UNPROVEN",
                    _ => "EDGE_NOT_TRAVERSED"
                });
        }
        for (var i = 0; i < schema.Nodes.Count; i++)
            if (!visited.Contains(schema.Nodes[i].Id)) missed.TryAdd($"/nodes/{i}", "NODE_NOT_TRAVERSED");
        return new(nodes.ToArray(), evaluated.OrderBy(p => p.Key).Select(p => p.Value).ToArray(),
            missed.Select(x => new WorkflowForecastUnvisitedV1(x.Key, x.Value)).ToArray());
    }
}
