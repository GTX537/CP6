using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CP6.Space.CadExperiment;

public sealed record StressGenerationReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string GeneratorVersion,
    string Kind,
    string OutputPath,
    long SizeBytes,
    long EntityCount,
    string Sha256,
    string CadVersion,
    string Unit);

public static class StressDxfGenerator
{
    public const string GeneratorVersion = "1.0.0";
    private const long FiftyMegabytes = 50L * 1024 * 1024;

    public static async Task<StressGenerationReport> GenerateAsync(
        string kind,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (kind is not ("50mb" or "million"))
        {
            throw new ArgumentException("Stress kind must be '50mb' or 'million'.");
        }

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The output directory is invalid."));

        long entityCount = 0;
        await using (var stream = new FileStream(
                         fullPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         1024 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var writer = new StreamWriter(
                         stream,
                         new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                         1024 * 1024,
                         leaveOpen: true)
        {
            NewLine = "\n"
        })
        {
            await writer.WriteAsync(
                """
                0
                SECTION
                2
                HEADER
                9
                $ACADVER
                1
                AC1032
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

                """);

            while (kind == "million" ? entityCount < 1_000_000 : true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var x = entityCount % 10_000;
                var y = entityCount / 10_000;
                await writer.WriteAsync(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"""
                         0
                         LINE
                         5
                         {entityCount + 0x1000:X}
                         8
                         STRESS
                         10
                         {x}
                         20
                         {y}
                         30
                         0
                         11
                         {x + 1}
                         21
                         {y}
                         31
                         0

                         """));
                entityCount++;

                if (entityCount % 10_000 != 0)
                {
                    continue;
                }

                await writer.FlushAsync(cancellationToken);
                if (kind == "50mb" && stream.Length >= FiftyMegabytes)
                {
                    break;
                }
            }

            await writer.WriteAsync("0\nENDSEC\n0\nEOF\n");
            await writer.FlushAsync(cancellationToken);
        }

        var hash = await DatasetAuditor.ComputeSha256Async(fullPath, cancellationToken);
        var report = new StressGenerationReport(
            1,
            DateTimeOffset.UtcNow,
            GeneratorVersion,
            kind,
            fullPath,
            new FileInfo(fullPath).Length,
            entityCount,
            hash,
            "AC1032",
            "Millimeter");
        await CadExperimentJson.WriteAsync(
            fullPath + ".cad-stress.json",
            report,
            cancellationToken);
        return report;
    }
}
