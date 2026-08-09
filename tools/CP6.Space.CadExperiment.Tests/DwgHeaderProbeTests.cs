using CP6.Space.CadExperiment;

namespace CP6.Space.CadExperiment.Tests;

public sealed class DwgHeaderProbeTests
{
    [Fact]
    public void Inspect_reads_a_dwg_version_header_without_claiming_a_full_parse()
    {
        using var fixture = new TemporaryDirectory();
        var source = fixture.Write("sample.dwg", "AC1032synthetic-test-body");

        var result = DwgHeaderProbe.Inspect(source);

        Assert.True(result.HeaderValid);
        Assert.Equal("AC1032", result.CadVersion);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Inspect_rejects_a_non_dwg_header()
    {
        using var fixture = new TemporaryDirectory();
        var source = fixture.Write("sample.dwg", "NOTDWGsynthetic-test-body");

        var result = DwgHeaderProbe.Inspect(source);

        Assert.False(result.HeaderValid);
        Assert.Null(result.CadVersion);
        Assert.Single(result.Errors);
    }
}
