using System.Text.Json;
using CP6.Core.Services.Wf;

namespace CP6.Tests.Wf;

public sealed class FormSchemaBoundaryTests
{
    [Theory]
    [InlineData("null", "OBJECT_REQUIRED")]
    [InlineData("{\"fields\":null}", "$.fields")]
    [InlineData("{\"fields\":[null]}", "$.fields[0]")]
    [InlineData("{\"fields\":[{\"name\":\"a\",\"type\":\"script\"}]}", "UNSUPPORTED_CONTROL")]
    [InlineData("{\"fields\":[{\"name\":\"a\",\"Name\":\"b\"}]}", "AMBIGUOUS_PROPERTY")]
    [InlineData("{\"fields\":[],\"fields\":[]}", "JSON_SHAPE")]
    [InlineData("{\"fields\":[{\"name\":\"a\"},{\"name\":\"a\"}]}", "EMPTY_OR_DUPLICATE")]
    [InlineData("{\"fields\":[{\"name\":\"a\",\"type\":\"table\",\"columns\":[null]}]}", "columns[0]")]
    [InlineData("{\"fields\":[{\"name\":\"a\",\"type\":\"number\",\"default\":\"2\"}]}", "DEFAULT_TYPE_INVALID")]
    [InlineData("{\"fields\":[],\"rules\":null}", "$.rules")]
    [InlineData("{\"fields\":[],\"rules\":[null]}", "$.rules[0]")]
    [InlineData("{\"fields\":[],\"rules\":[{\"then\":[null]}]}", "then[0]")]
    [InlineData("{\"fields\":[],\"rules\":[{\"then\":[{\"action\":\"eval\",\"target\":\"a\"}]}]}", "UNSUPPORTED_ACTION")]
    [InlineData("{\"fields\":[],\"rules\":[{\"then\":[{\"action\":\"hide\",\"target\":\"a\"}]}]}", "FIELD_NOT_FOUND")]
    [InlineData("{\"fields\":[{\"name\":\"a\"}],\"rules\":[{\"when\":\"run()\",\"then\":[]}]}", "EXPRESSION_INVALID")]
    [InlineData("{\"fields\":[{\"name\":\"a\"}],\"rules\":[{\"then\":[{\"action\":\"compute\",\"target\":\"a\",\"expr\":\"missing + 1\"}]}]}", "EXPRESSION_INVALID")]
    public void Malformed_or_unknown_executable_schema_has_an_explicit_path(string raw, string expected)
        => Assert.Contains(expected, Assert.Throws<InvalidOperationException>(() => FormSchemaValidationV1.Parse(raw)).Message);

    [Fact]
    public void Passive_layout_depth_numeric_precision_and_controlled_rules_remain_legal()
    {
        var layout = new string('[', 80) + "9007199254740993.123456789" + new string(']', 80);
        var raw = """{"fields":[{"name":"qty","type":"NUMBER","default":9007199254740993.123456789,"extension":{"原文":true}}],"rules":[{"when":"qty > 0","then":[{"action":"compute","target":"qty","expr":"qty + 1"}]}],"layout":LAYOUT} """.Replace("LAYOUT", layout);
        var parsed = FormSchemaValidationV1.Parse(raw);
        Assert.Equal("qty", Assert.Single(parsed.Fields).Name);
        Assert.Equal("qty + 1", Assert.Single(Assert.Single(parsed.Rules).Then).Expr);
    }

    [Fact]
    public void Fatal_flow_structure_is_distinct_from_incomplete_graph_and_preserves_optional_legacy_edge_ids()
    {
        var draft = new FlowSchema { Nodes = [new() { Id = "s", Type = "start" }] };
        Assert.True(FlowSchemaValidator.HasValidDraftStructure(draft));
        Assert.Contains("E-WF-010", FlowSchemaValidator.Validate(draft));
        draft.Nodes.Add(new() { Id = "e", Type = "end" });
        draft.Edges.Add(new() { From = "s", To = "e" });
        Assert.Empty(FlowSchemaValidator.Validate(draft));
        draft.Edges.Add(new() { Id = "edge", From = "s", To = "e" });
        draft.Edges.Add(new() { Id = "edge", From = "s", To = "e" });
        Assert.False(FlowSchemaValidator.HasValidDraftStructure(draft));
        draft.Edges.RemoveAt(2); draft.Edges[0].To = "missing";
        Assert.False(FlowSchemaValidator.HasValidDraftStructure(draft));
        draft.Edges[0].To = "e"; draft.Nodes[1].Type = "unknown";
        Assert.Equal(["E-WF-010"], FlowSchemaValidator.Validate(draft));
    }
}
