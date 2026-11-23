using CP6.Core.Services.Wf;

namespace CP6.Tests.Oa;

public sealed class WorkflowForecastPlannerTests
{
    [Fact]
    public async Task Parallel_paths_remain_visible_when_a_nested_exclusive_condition_is_unknown()
    {
        var schema = new FlowSchema
        {
            Start = "s",
            Nodes = [Node("s", "parallelSplit"), Node("choose", "gateway"), Node("other", "end"),
                Node("a", "end"), Node("fallback", "end"), Node("false", "end")],
            Edges = [Edge("s", "choose"), Edge("s", "other"), Edge("choose", "a", "missing > 10", 1),
                Edge("choose", "false", "amount < 1", 2), Edge("choose", "fallback", priority: 3)]
        };
        var diagnostics = new List<FlowSimulationDiagnosticV2>();
        var result = await Explain(schema, diagnostics);
        Assert.Equal(["s", "choose", "other"], result.Nodes.Select(n => n.NodeId));
        Assert.Equal(["TRUE", "TRUE", "UNKNOWN", "FALSE", "TRUE"], result.Edges.Select(e => e.Condition));
        Assert.Equal([0, 1], result.Edges.Where(e => e.Traversed).Select(e => e.Index));
        Assert.Contains(result.Unvisited, item => item.Path == "/edges/2");
        Assert.Contains(result.Unvisited, item => item.Path == "/nodes/4");
        Assert.Contains(diagnostics, item => item == new FlowSimulationDiagnosticV2("/edges/2/condition", "EDGE_EXPRESSION_UNPROVEN"));
    }

    [Fact]
    public async Task Unsupported_nodes_cycles_and_the_graph_budget_report_the_exact_unvisited_locations()
    {
        var diagnostics = new List<FlowSimulationDiagnosticV2>();
        var cycle = new FlowSchema { Start = "s", Nodes = [Node("s", "start"), Node("loop", "cc")],
            Edges = [Edge("s", "loop"), Edge("loop", "s")] };
        var result = await Explain(cycle, diagnostics);
        Assert.Equal(2, result.Nodes.Length);
        Assert.Contains(result.Unvisited, item => item == new WorkflowForecastUnvisitedV1("/edges/1", "REPEATED_NODE_UNPROVEN"));

        diagnostics.Clear();
        var unsupported = new FlowSchema { Start = "s", Nodes = [Node("s", "start"), Node("x", "unknown"), Node("e", "end")],
            Edges = [Edge("s", "x"), Edge("x", "e")] };
        result = await Explain(unsupported, diagnostics);
        Assert.Contains(result.Unvisited, item => item == new WorkflowForecastUnvisitedV1("/nodes/1", "FORECAST_NODE_UNSUPPORTED"));
        Assert.Contains(result.Unvisited, item => item.Path == "/nodes/2");

        diagnostics.Clear();
        var fanout = new FlowSchema { Start = "s", Nodes = [Node("s", "parallelSplit"), .. Enumerable.Range(0, 110).Select(i => Node("e" + i, "end"))],
            Edges = Enumerable.Range(0, 110).Select(i => Edge("s", "e" + i)).ToList() };
        result = await Explain(fanout, diagnostics);
        Assert.Equal(100, result.Nodes.Length);
        Assert.Equal(99, result.Edges.Count(e => e.Traversed));
        Assert.Contains(result.Unvisited, item => item == new WorkflowForecastUnvisitedV1("/edges/99", "SIMULATION_BUDGET_EXCEEDED"));
        Assert.Contains(result.Unvisited, item => item.Path == "/nodes/100");
    }

    private static FlowNode Node(string id, string type) => new() { Id = id, Type = type };
    private static FlowEdge Edge(string from, string to, string? condition = null, int? priority = null) =>
        new() { From = from, To = to, Condition = condition, Priority = priority };
    private static Task<WorkflowForecastPlanner.Result> Explain(FlowSchema graph, List<FlowSimulationDiagnosticV2> diagnostics) =>
        WorkflowForecastPlanner.ExplainAsync(graph, """{"amount":9007199254740993.00}""",
            _ => Task.FromResult(Array.Empty<WorkflowForecastStageV1>()), diagnostics, default);
}
