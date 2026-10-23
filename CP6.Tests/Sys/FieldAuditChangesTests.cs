using CP6.Core.Services.Sys;

namespace CP6.Tests.Sys;

public sealed class FieldAuditChangesTests
{
    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"Field\":\"Status\",\"Old\":null,\"New\":\"1\"}]")]
    [InlineData("[{\"field\":\"Status\",\"old\":\"0\",\"new\":\"1\"}]")]
    public void Original_and_legacy_shapes_are_readable_without_exposing_original_json(string source)
    {
        var result = FieldAuditChangesV1.Summarize(source);
        Assert.Equal(source == "[]" ? 0 : 1, result.ChangeCount); Assert.Equal("READABLE", result.EvidenceStatus);
        Assert.Equal("READABLE", FieldAuditChangesV1.MachineStatus(source).EvidenceStatus);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("[null]")]
    [InlineData("[{\"Field\":\"Status\",\"Old\":\"0\"}]")]
    [InlineData("[{\"Field\":\"Status\",\"field\":\"Remarks\",\"Old\":\"0\",\"New\":\"1\"}]")]
    [InlineData("[{\"Field\":\"Status\",\"Old\":{},\"New\":\"1\"}]")]
    [InlineData("[{\"Field\":\"Status\",\"Old\":\"0\",\"New\":\"1\",\"Extra\":\"secret\"}]")]
    [InlineData("[{\"Field\":\"Status\",\"Old\":\"0\",\"New\":\"1\"},{\"Field\":\"Status\",\"Old\":\"1\",\"New\":\"2\"}]")]
    public void Broken_or_ambiguous_history_is_unreadable_never_zero(string source)
    {
        var summary = FieldAuditChangesV1.Summarize(source);
        Assert.Null(summary.ChangeCount); Assert.Equal("UNREADABLE", summary.EvidenceStatus);
        var projection = FieldAuditChangesV1.MachineStatus(source);
        Assert.Empty(projection.Changes); Assert.Equal("UNREADABLE", projection.EvidenceStatus);
    }

    [Fact]
    public void Unmapped_fields_never_expose_names_or_values_and_invalid_status_never_escapes_under_known_field()
    {
        var safe = FieldAuditChangesV1.MachineStatus("""[{"Field":"Status","Old":"0","New":"2"},{"Field":"Secret","Old":"private","New":null}]""");
        Assert.Equal("RESTRICTED", safe.EvidenceStatus); Assert.Equal(new("Status", "0", "2"), Assert.Single(safe.Changes));
        var bad = FieldAuditChangesV1.MachineStatus("""[{"Field":"Status","Old":"0","New":"private"}]""");
        Assert.Empty(bad.Changes); Assert.Equal("UNREADABLE", bad.EvidenceStatus);
        var flag = FieldAuditChangesV1.MachineStatus("""[{"Field":"ActiveFlg","Old":"True","New":"False"}]""");
        Assert.Equal("False", Assert.Single(flag.Changes).New);
    }
}
