using CP6.Core.Services.Wf;

namespace CP6.Tests.Oa;

public sealed class FlowSimulationPlannerTests
{
    private static FlowNode Node(string id, string type = "approval") => new() { Id = id, Type = type };
    private static FlowEdge Edge(string from, string to, string? condition = null, int? priority = null) => new() { From = from, To = to, Condition = condition, Priority = priority };
    private static Task<FlowSimulationStageV2[]> NoPeople(FlowNode node, string path) => Task.FromResult(Array.Empty<FlowSimulationStageV2>());
    [Fact]
    public async Task Exact_numeric_branch_uses_runtime_priority_and_unknown_condition_does_not_fall_back()
    {
        var graph = new FlowSchema { Start = "s", Nodes = [Node("s", "start"), Node("a", "end"), Node("b", "end")],
            Edges = [Edge("s", "b", priority: 2), Edge("s", "a", "amount > 9007199254740992", 1)] };
        var diagnostics = new List<FlowSimulationDiagnosticV2>();
        var result = await FlowSimulationPlannerV2.Predict(graph, "{\"amount\":9007199254740993.00}", NoPeople, diagnostics, default);
        Assert.Equal(new[] { "s", "a" }, result.Nodes.Select(x => x.NodeId)); Assert.Empty(diagnostics);
        result = await FlowSimulationPlannerV2.Predict(graph, "{}", NoPeople, diagnostics, default);
        Assert.Single(result.Nodes); Assert.Empty(result.Edges);
        Assert.Contains(diagnostics, x => x.Path == "/edges/1/condition" && x.Code == "EDGE_EXPRESSION_UNPROVEN");
    }
    [Theory]
    [InlineData("parallelSplit", true, "a,b")]
    [InlineData("inclusiveSplit", true, "a")]
    [InlineData("inclusiveSplit", false, "b")]
    public async Task Fork_prediction_matches_parallel_unconditional_and_inclusive_default_semantics(string type, bool condition, string expected)
    {
        var graph = new FlowSchema { Start = "s", Nodes = [Node("s", type), Node("a", "end"), Node("b", "end")], Edges = [Edge("s", "a", "flag == true"), Edge("s", "b")] };
        var diagnostics = new List<FlowSimulationDiagnosticV2>();
        var result = await FlowSimulationPlannerV2.Predict(graph, condition ? "{\"flag\":true}" : "{\"flag\":false}", NoPeople, diagnostics, default);
        Assert.Equal(expected.Split(','), result.Edges.Select(x => x.To)); Assert.Empty(diagnostics);
    }
    [Theory]
    [InlineData("serviceTask", "DEPENDENT_EXECUTION_NOT_SIMULATED")]
    [InlineData("subFlow", "DEPENDENT_EXECUTION_NOT_SIMULATED")]
    [InlineData("parallelJoin", "JOIN_SYNCHRONIZATION_UNPROVEN")]
    public async Task Effect_or_synchronization_boundary_stops_before_a_fictional_downstream_result(string type, string code)
    {
        var graph = new FlowSchema { Start = "s", Nodes = [Node("s", "start"), Node("x", type), Node("e", "end")], Edges = [Edge("s", "x"), Edge("x", "e")] };
        var diagnostics = new List<FlowSimulationDiagnosticV2>();
        var result = await FlowSimulationPlannerV2.Predict(graph, "{}", NoPeople, diagnostics, default);
        Assert.Equal(new[] { "s", "x" }, result.Nodes.Select(x => x.NodeId)); Assert.Contains(diagnostics, x => x.Code == code);
    }
    [Fact]
    public async Task Cycle_and_large_fanout_stop_with_an_explicit_partial_result()
    {
        var graph = new FlowSchema { Start = "s", Nodes = [Node("s", "start"), Node("a")], Edges = [Edge("s", "a"), Edge("a", "s")] };
        var diagnostics = new List<FlowSimulationDiagnosticV2>();
        var result = await FlowSimulationPlannerV2.Predict(graph, "{}", NoPeople, diagnostics, default);
        Assert.Equal(2, result.Nodes.Length); Assert.Contains(diagnostics, x => x.Code == "REPEATED_NODE_UNPROVEN");
        graph = new() { Start = "s", Nodes = [Node("s", "parallelSplit"), .. Enumerable.Range(0, 600).Select(i => Node("end" + i, "end"))], Edges = Enumerable.Range(0, 600).Select(i => Edge("s", "end" + i)).ToList() };
        diagnostics.Clear(); result = await FlowSimulationPlannerV2.Predict(graph, "{}", NoPeople, diagnostics, default);
        Assert.Equal(512, result.Nodes.Length); Assert.Equal(511, result.Edges.Length); Assert.Contains(diagnostics, x => x.Code == "SIMULATION_BUDGET_EXCEEDED");
    }
}
