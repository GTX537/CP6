using CP6.Core.Services.Wf;

namespace CP6.Tests.Wf;

public sealed class DefinitionDiagnosticsTests
{
    [Theory]
    [InlineData("{\"fields\":[{\"name\":\"x\"},{\"name\":\"x\"}]}","$.fields[1].name")]
    [InlineData("{\"fields\":[{\"name\":\"lines\",\"type\":\"table\",\"columns\":[{\"name\":\"nested\",\"type\":\"table\"}]}]}","$.fields[0].columns[0].type")]
    [InlineData("{\"fields\":[{\"name\":\"lines\",\"type\":\"table\",\"columns\":[{\"name\":\"n\",\"type\":\"number\"}],\"maxRows\":201}]}","$.fields[0].maxRows")]
    [InlineData("{\"fields\":[{\"name\":\"x\"}],\"rules\":[{\"then\":[{\"action\":\"compute\",\"target\":\"x\",\"expr\":\"absent+1\"}]}]}","$.rules[0].then[0].expr")]
    public void Author_can_locate_the_rejected_field_column_or_rule(string schema,string path)
    {
        var error=Assert.Throws<DefinitionDiagnosticExceptionV2>(()=>FormSchemaValidationV1.Parse(schema));
        Assert.Contains(error.Diagnostics,d=>d.Path==path);
    }
    [Fact]
    public void Invalid_endpoints_and_duplicate_node_ids_keep_their_exact_positions()
    {
        var schema=new FlowSchema{Nodes=[new(){Id="same",Type="start"},new(){Id="same",Type="end"}],Edges=[new(){Id="wire",From="same",To="absent"}]};
        var errors=FlowSchemaValidator.DraftStructureDiagnostics(schema);
        Assert.Contains(errors,d=>d.Path=="$.nodes[1].id");Assert.Contains(errors,d=>d.Path=="$.edges[0].to"&&d.EdgeId=="wire");
        Assert.False(FlowSchemaValidator.HasValidDraftStructure(schema));
    }
    [Fact]
    public void Multiple_incomplete_nodes_are_located_without_changing_legacy_error_codes()
    {
        var schema=new FlowSchema{Start="s",Nodes=[new(){Id="s",Type="start"},new(){Id="a",Type="approval"},new(){Id="b",Type="approval"},new(){Id="e",Type="end"}],Edges=[new(){From="s",To="a"},new(){From="a",To="b"},new(){From="b",To="e"}]};
        var errors=FlowSchemaValidator.ValidateKnownStructureDetailed(schema);
        Assert.Contains(errors,d=>d.NodeId=="a"&&d.Path=="$.nodes[1].approverStrategy");
        Assert.Contains(errors,d=>d.NodeId=="b"&&d.Path=="$.nodes[2].approverStrategy");
        Assert.Equal(new[]{"E-WF-010"},FlowSchemaValidator.Validate(schema));
    }
    [Fact]
    public void Data_map_diagnostic_locates_the_missing_field_without_accusing_a_present_map()
    {
        var schema=new FlowSchema{Start="s",Nodes=[new(){Id="s",Type="start"},new(){Id="a",Type="approval",ApproverStrategy="DataMap",ApproverMapKey="available"},new(){Id="e",Type="end"}],Edges=[new(){From="s",To="a"},new(){From="a",To="e"}]};
        var error=Assert.Single(FlowSchemaValidator.ValidateKnownStructureDetailed(schema),d=>d.Code=="E-WF-014");
        Assert.Equal("$.nodes[1].approverFieldName",error.Path);
    }
}
