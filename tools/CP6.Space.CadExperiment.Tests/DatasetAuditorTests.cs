using System.Security.Cryptography;
using System.Text;
using CP6.Space.CadExperiment;

namespace CP6.Space.CadExperiment.Tests;

public sealed class DatasetAuditorTests
{
    [Fact]
    public async Task Audit_accepts_a_valid_development_seed_but_does_not_mark_e02_ready()
    {
        using var fixture = new TemporaryDirectory();
        var source = fixture.Write("seeds/sample.dxf", ValidDxf);
        var hash = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(source)))
            .ToLowerInvariant();
        fixture.Write(
            "expected-elements.jsonl",
            """{"sampleId":"L1-SEED-001","expectedId":"FLOOR-1"}""" + "\n");
        fixture.Write("expected-issues.json", """{"samples":[]}""");
        fixture.Write("provider-ir.jsonl", """{"sampleId":"L1-SEED-001"}""" + "\n");
        fixture.Write("layer-mapping.json", "{}");
        fixture.Write("LICENSE.md", "Synthetic");
        var manifest = fixture.Write(
            "manifest.json",
            $$"""
              {
                "datasetName": "test",
                "datasetVersion": "1.0.0",
                "schemaVersion": 1,
                "purpose": "DevelopmentSeed",
                "countsTowardReleaseGate": false,
                "unit": "Millimeter",
                "coordinateSystem": "FloorLocal-ZUp",
                "samples": [{
                  "sampleId": "L1-SEED-001",
                  "layoutFamily": "L1-Regular",
                  "split": "DevelopmentSeed",
                  "sourceFile": "seeds/sample.dxf",
                  "sourceSha256": "{{hash}}",
                  "expectedTargetCount": 1
                }],
                "files": {
                  "expectedElements": "expected-elements.jsonl",
                  "expectedIssues": "expected-issues.json",
                  "providerIr": "provider-ir.jsonl",
                  "layerMapping": "layer-mapping.json",
                  "license": "LICENSE.md"
                }
              }
              """);

        var report = await DatasetAuditor.AuditAsync(manifest);

        Assert.True(report.IntegrityPassed);
        Assert.False(report.E02ReadinessPassed);
        Assert.Empty(report.Errors);
        Assert.Equal(1, report.Samples[0].Dxf!.EntityCount);
        Assert.False(report.Gates.Single(gate => gate.Id == "formal-golden-20").Passed);
        Assert.False(
            report.Gates.Single(gate => gate.Id == "golden-split-distribution").Passed);
        Assert.False(
            report.Gates.Single(gate => gate.Id == "four-per-layout-family").Passed);
        Assert.Empty(report.StressAssets);
    }

    [Fact]
    public async Task Audit_rejects_a_hash_mismatch()
    {
        using var fixture = new TemporaryDirectory();
        fixture.Write("seeds/sample.dxf", ValidDxf);
        fixture.Write(
            "expected-elements.jsonl",
            """{"sampleId":"L1-SEED-001","expectedId":"FLOOR-1"}""" + "\n");
        fixture.Write("expected-issues.json", """{"samples":[]}""");
        fixture.Write("provider-ir.jsonl", """{"sampleId":"L1-SEED-001"}""" + "\n");
        fixture.Write("layer-mapping.json", "{}");
        fixture.Write("LICENSE.md", "Synthetic");
        var manifest = fixture.Write(
            "manifest.json",
            $$"""
              {
                "datasetName": "test",
                "datasetVersion": "1.0.0",
                "schemaVersion": 1,
                "purpose": "DevelopmentSeed",
                "countsTowardReleaseGate": false,
                "unit": "Millimeter",
                "coordinateSystem": "FloorLocal-ZUp",
                "samples": [{
                  "sampleId": "L1-SEED-001",
                  "layoutFamily": "L1-Regular",
                  "split": "DevelopmentSeed",
                  "sourceFile": "seeds/sample.dxf",
                  "sourceSha256": "{{new string('0', 64)}}",
                  "expectedTargetCount": 1
                }],
                "files": {
                  "expectedElements": "expected-elements.jsonl",
                  "expectedIssues": "expected-issues.json",
                  "providerIr": "provider-ir.jsonl",
                  "layerMapping": "layer-mapping.json",
                  "license": "LICENSE.md"
                }
              }
              """);

        var report = await DatasetAuditor.AuditAsync(manifest);

        Assert.False(report.IntegrityPassed);
        Assert.Contains(report.Errors, error => error.Contains("SHA-256"));
    }

    private const string ValidDxf =
        "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1015\n"
        + "9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n"
        + "0\nLINE\n5\n10\n8\nWALL\n0\nENDSEC\n0\nEOF\n";
}
