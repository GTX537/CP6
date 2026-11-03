using CP6.Core.Services.Wf;

namespace CP6.Tests.Oa;

public sealed class DefinitionComparisonTests
{
    private static DefinitionChangeV2[] Compare(string a, string b, string kind = "FLOW")
        => DefinitionComparerV2.Compare(kind, new() { ["SchemaJson"] = a }, new() { ["SchemaJson"] = b }, [], []);

    [Fact]
    public void Stable_nodes_compare_by_id_preserve_order_rules_approvers_and_decimal_lexemes()
    {
        const string before = """{"nodes":[{"id":"a","type":"approval","approverIds":"alice","amount":9007199254740993.00},{"id":"b","type":"end"}],"edges":[{"id":"ab","from":"a","to":"b","priority":1}]}""";
        const string after = """{"nodes":[{"id":"b","type":"end"},{"id":"a","type":"serviceTask","approverIds":"bob","amount":9007199254740992.00}],"edges":[{"id":"ab","from":"a","to":"b","priority":2}]}""";
        var result = Compare(before, after);
        Assert.Contains(result, x => x.Path == "/SchemaJson/nodes" && x.Category == "ORDER");
        Assert.Contains(result, x => x.Path == "/SchemaJson/nodes/@a/type" && x.Category == "TYPE");
        Assert.Contains(result, x => x.Path == "/SchemaJson/nodes/@a/approverIds" && x.Category == "APPROVER");
        Assert.Contains(result, x => x.Path == "/SchemaJson/edges/@ab/priority" && x.Category == "RULE");
        Assert.Contains(result, x => x.BeforeJson == "9007199254740993.00" && x.AfterJson == "9007199254740992.00");
        Assert.DoesNotContain(result, x => x.Change is "ADDED" or "REMOVED");
    }
    [Fact]
    public void Unkeyed_edges_keep_full_sequence_and_never_invent_correspondence()
    {
        var result = Compare("""{"edges":[{"from":"s","to":"a"},{"from":"s","to":"b"}]}""",
            """{"edges":[{"from":"s","to":"b"},{"from":"s","to":"a"}]}""");
        var change = Assert.Single(result); Assert.Equal("UNKEYED_SEQUENCE", change.Category);
        Assert.Contains("\"to\":\"a\"", change.BeforeJson); Assert.Contains("\"to\":\"b\"", change.AfterJson);
    }
    [Fact]
    public void Form_fields_columns_rules_and_unknown_extensions_are_not_lost()
    {
        var result = Compare("""{"Fields":[{"Name":"lines","type":"table","columns":[{"name":"price","type":"number"}]},{"name":"old","type":"input"}],"rules":[{"when":"x","then":[]}],"layout":{"custom":{"value":1}}}""",
            """{"Fields":[{"Name":"lines","type":"table","columns":[{"name":"price","type":"input"},{"name":"new","type":"number"}]},{"name":"renamed","type":"input"}],"rules":[{"when":"y","then":[]}],"layout":{"custom":{"value":2}}}""", "FORM");
        Assert.Contains(result, x => x.Path == "/SchemaJson/Fields/@lines/columns/@price/type" && x.Category == "TYPE");
        Assert.Contains(result, x => x.Path == "/SchemaJson/Fields/@old" && x.Change == "REMOVED");
        Assert.Contains(result, x => x.Path == "/SchemaJson/Fields/@renamed" && x.Change == "ADDED");
        Assert.Contains(result, x => x.Path == "/SchemaJson/rules" && x.Category == "RULE");
        Assert.Contains(result, x => x.Path == "/SchemaJson/layout/custom/value");
    }
    [Fact]
    public void Object_order_and_whitespace_are_ignored_but_dependency_versions_are_explicit()
    {
        Assert.Empty(Compare("""{"nodes":[{"id":"a","type":"end"}],"start":"a"}""", """{ "start":"a", "nodes":[{"type":"end", "id":"a"}] }"""));
        var diff = DefinitionComparerV2.Compare("FLOW", new(), new(),
            [new("FORM", "FormKey", "orders", "v1", "PUBLISHED_ENABLED")],
            [new("FORM", "FormKey", "orders", "v2", "PUBLISHED_ENABLED")]);
        Assert.Equal("DEPENDENCY", Assert.Single(diff).Category);
        Assert.Contains("v1", diff[0].BeforeJson); Assert.Contains("v2", diff[0].AfterJson);
    }
}
