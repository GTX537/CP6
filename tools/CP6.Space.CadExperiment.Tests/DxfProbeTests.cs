using CP6.Space.CadExperiment;

namespace CP6.Space.CadExperiment.Tests;

public sealed class DxfProbeTests
{
    [Fact]
    public void Inspect_reads_version_units_entities_layers_and_handles()
    {
        using var fixture = new TemporaryDirectory();
        var path = fixture.Write(
            "sample.dxf",
            """
            0
            SECTION
            2
            HEADER
            9
            $ACADVER
            1
            AC1015
            9
            $INSUNITS
            70
            4
            0
            ENDSEC
            0
            SECTION
            2
            ENTITIES
            0
            LINE
            5
            10
            8
            WALL
            0
            CIRCLE
            5
            11
            8
            COLUMN
            0
            ENDSEC
            0
            EOF
            """);

        var result = DxfProbe.Inspect(path);

        Assert.True(result.HasPairedLines);
        Assert.True(result.HasEofMarker);
        Assert.Equal("AC1015", result.CadVersion);
        Assert.Equal(4, result.InsertionUnitsCode);
        Assert.Equal(2, result.EntityCount);
        Assert.Equal(2, result.HandleCount);
        Assert.Equal(0, result.DuplicateHandleCount);
        Assert.Equal(1, result.EntityTypeCounts["LINE"]);
        Assert.Equal(1, result.LayerCounts["COLUMN"]);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Inspect_reports_malformed_framing_and_duplicate_handles()
    {
        using var fixture = new TemporaryDirectory();
        var path = fixture.Write(
            "bad.dxf",
            "0\nSECTION\n2\nENTITIES\n0\nLINE\n5\n10\n0\nLINE\n5\n10\n");

        var result = DxfProbe.Inspect(path);

        Assert.False(result.HasEofMarker);
        Assert.Equal(1, result.DuplicateHandleCount);
        Assert.NotEmpty(result.Errors);
    }
}
