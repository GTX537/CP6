using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
namespace CP6.Tests.Oa;

public sealed class PersonalFormValueTests
{
    [Fact]
    public void Rebase_preview_uses_final_rules_masks_and_preserves_unset_null_empty_and_false()
    {
        const string schema="""{"fields":[{"name":"unset","type":"input"},{"name":"nil","type":"input"},{"name":"empty","type":"input"},{"name":"flag","type":"checkbox","required":true},{"name":"optional","type":"input","required":true},{"name":"secret","type":"input","required":true},{"name":"ruleHidden","type":"input","required":true},{"name":"required","type":"input"}],"rules":[{"when":"true","then":[{"action":"optional","target":"optional"},{"action":"hide","target":"ruleHidden"},{"action":"require","target":"required"}]}]}""";
        var mask=PersonalFormValuesV2.Schema(schema).Fields.ToDictionary(f=>f.Name,f=>f.Name=="secret"?"hidden":"edit");
        var plan=PersonalFormRebaseV2.Build(schema,schema,"""{"nil":null,"empty":"","flag":false}""",mask,mask);
        using var data=System.Text.Json.JsonDocument.Parse(plan.Data);
        Assert.False(data.RootElement.TryGetProperty("unset",out _));Assert.Equal(System.Text.Json.JsonValueKind.Null,data.RootElement.GetProperty("nil").ValueKind);
        Assert.Equal("",data.RootElement.GetProperty("empty").GetString());Assert.False(data.RootElement.GetProperty("flag").GetBoolean());
        Assert.Equal(new[]{"flag","required"},plan.RequiredAfterRebase);Assert.Equal(new PersonalFormRebaseDiagnosticV2("required","REQUIRED"),Assert.Single(plan.Diagnostics));
        Assert.DoesNotContain("secret",plan.RetainedPaths);Assert.DoesNotContain("ruleHidden",plan.RetainedPaths);Assert.Empty(plan.DefaultedPaths);Assert.False(plan.Loss);
    }
    [Fact]
    public void PersonalForm_rebase_preserves_rows_and_exact_values_when_target_row_limit_shrinks()
    {
        const string one="{\"Fields\":[{\"Name\":\"Rows\",\"Type\":\"table\",\"MaxRows\":5,\"Columns\":[{\"Name\":\"n\",\"Type\":\"number\"}]}],\"extension\":{\"a\":1,\"A\":2}}";
        var plan=PersonalFormRebaseV2.Build(one,one.Replace("\"MaxRows\":5","\"MaxRows\":1"),"{\"Rows\":[{\"n\":12345678901234567890.0001},{\"n\":2}]}",new Dictionary<string,string>{{"Rows","edit"}},new Dictionary<string,string>{{"Rows","edit"}});
        Assert.Contains("12345678901234567890.0001",plan.Data);Assert.Contains("{\"n\":2}",plan.Data);Assert.Contains("Rows:MAX_ROWS_EXCEEDED",plan.Errors);Assert.False(plan.Loss);
    }
    [Fact]
    public void PersonalForm_rebase_requires_visible_loss_confirmation_and_blocks_hidden_valued_loss_without_path()
    {
        const string one="{\"fields\":[{\"name\":\"secret\",\"type\":\"number\"}]}";
        const string two="{\"fields\":[{\"name\":\"secret\",\"type\":\"input\",\"default\":\"replacement\"}]}";
        var edit=new Dictionary<string,string>{{"secret","edit"}};var hidden=new Dictionary<string,string>{{"secret","hidden"}};
        var plan=PersonalFormRebaseV2.Build(one,two,"{\"secret\":0}",edit,edit);Assert.True(plan.Loss);Assert.True(Assert.Single(plan.Changes).HasValue);Assert.Contains("replacement",plan.Data);
        var error=Assert.Throws<ScopeAuthorizationExceptionV1>(()=>PersonalFormRebaseV2.Build(one,two,"{\"secret\":0}",hidden,edit));
        Assert.Equal("REBASE_HIDDEN_LOSS_BLOCKED",error.Message);Assert.Equal(403,error.Status);
    }
}
