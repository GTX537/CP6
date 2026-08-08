using CP6.Space.CadExperiment;

namespace CP6.Space.CadExperiment.Tests;

public sealed class AdapterRunnerTests
{
    [Fact]
    public async Task RunAsync_resolves_relative_adapter_arguments_from_invocation_directory()
    {
        using var fixture = new TemporaryDirectory();
        var input = fixture.Write(
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
            ENDSEC
            0
            EOF
            """);
        var adapterAssembly = typeof(AdapterRunner).Assembly.Location;
        var adapterDirectory = Path.GetDirectoryName(adapterAssembly)
            ?? throw new InvalidOperationException("Adapter assembly has no directory.");

        var report = await AdapterRunner.RunAsync(
            new AdapterRunOptions(
                "fixture",
                "1.0",
                "dotnet",
                [Path.GetFileName(adapterAssembly)],
                [input],
                Path.Combine(fixture.Path, "evidence"),
                1,
                TimeSpan.FromSeconds(30),
                adapterDirectory));

        var attempt = Assert.Single(report.Attempts);
        Assert.Equal("Success", attempt.Outcome);
        Assert.Equal(Path.GetFullPath(adapterDirectory), report.AdapterWorkingDirectory);
    }
}
