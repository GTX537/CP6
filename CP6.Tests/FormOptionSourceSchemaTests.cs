using CP6.Core.Services.Wf;

namespace CP6.Tests;
public sealed class FormOptionSourceSchemaTests
{
    [Theory]
    [InlineData("{\"owner\":\"arbitrary-owner\",\"scope\":\"SHARED\",\"typeId\":1}")]
    [InlineData("{\"owner\":\"Main.Sys.DictionaryOptions.v1\",\"scope\":\"TENANT\",\"typeId\":1}")]
    [InlineData("{\"owner\":\"Main.Sys.DictionaryOptions.v1\",\"scope\":\"SHARED\",\"typeId\":\"1\"}")]
    [InlineData("{\"owner\":\"Main.Sys.DictionaryOptions.v1\",\"scope\":\"SHARED\",\"typeId\":1,\"url\":\"https://arbitrary.invalid\"}")]
    [InlineData("{\"owner\":\"Main.Sys.DictionaryOptions.v1\",\"scope\":\"SHARED\",\"typeId\":1,\"TypeId\":2}")]
    public void FormOptions_schema_source_is_versioned_closed_and_never_accepts_external_owner_or_url(string source)
        =>Assert.Throws<DefinitionDiagnosticExceptionV2>(()=>FormSchemaValidationV1.Parse("{\"fields\":[{\"name\":\"a\",\"type\":\"select\",\"optionSourceV1\":"+source+"}]}"));
    [Fact]
    public void FormOptions_schema_rejects_static_or_rule_options_conflicting_with_live_source()
    {
        const string source="\"optionSourceV1\":{\"owner\":\"Main.Sys.DictionaryOptions.v1\",\"scope\":\"SHARED\",\"typeId\":1}";
        foreach(var text in new[]{"{\"fields\":[{\"name\":\"a\",\"type\":\"select\","+source+",\"options\":[{\"label\":\"Fake\",\"value\":\"fake\"}]}]}",
            "{\"fields\":[{\"name\":\"a\",\"type\":\"select\","+source+"}],\"rules\":[{\"then\":[{\"action\":\"setOptions\",\"target\":\"a\",\"options\":[]}]}]}"})
            Assert.Contains("OPTION_SOURCE_CONFLICT",Assert.Throws<DefinitionDiagnosticExceptionV2>(()=>FormSchemaValidationV1.Parse(text)).Message);
    }
    [Fact]
    public void FormOptions_uppercase_table_also_validates_nested_source_boundary()
        =>Assert.Throws<DefinitionDiagnosticExceptionV2>(()=>FormSchemaValidationV1.Parse("""{"Fields":[{"Name":"rows","Type":"TABLE","Columns":[{"Name":"choice","Type":"select","OptionSourceV1":{"owner":"foreign","scope":"SHARED","typeId":1}}]}]}"""));
}
