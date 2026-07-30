using System.Text.Json;
using System.Security.Cryptography;
using CP6.Space.CadExperiment;

namespace CP6.Space.CadExperiment.Tests;

public sealed class CadTrialPreflightTests
{
    [Fact]
    public async Task Audit_fails_closed_when_external_trial_inputs_are_missing()
    {
        using var fixture = new TemporaryDirectory();
        var configPath = fixture.Write(
            "preflight.json",
            CreateConfig("CP6_SPACE_TEST_MISSING_SECRET"));

        var report = await CadTrialPreflight.AuditAsync(configPath);

        Assert.False(report.Passed);
        Assert.Null(report.DatasetAudit);
        Assert.False(report.Gates.Single(gate => gate.Id == "dataset-inputs").Passed);
        Assert.False(
            report.Gates.Single(gate => gate.Id == "embedded-sdk-packages").Passed);
        Assert.False(report.Gates.Single(gate => gate.Id == "secret-material").Passed);
    }

    [Fact]
    public async Task Audit_runs_the_dataset_audit_when_all_dataset_inputs_exist()
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
        fixture.Write(
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
        var configPath = fixture.Write(
            "preflight.json",
            CreateConfig(
                "CP6_SPACE_TEST_MISSING_SECRET",
                "manifest.json",
                "seeds/sample.dxf",
                "seeds/sample.dxf"));

        var report = await CadTrialPreflight.AuditAsync(configPath);

        Assert.NotNull(report.DatasetAudit);
        Assert.True(report.DatasetAudit.IntegrityPassed);
        Assert.False(report.DatasetAudit.E02ReadinessPassed);
        Assert.True(report.Gates.Single(gate => gate.Id == "dataset-inputs").Passed);
        Assert.False(report.Gates.Single(gate => gate.Id == "dataset-e02-ready").Passed);
    }

    [Fact]
    public async Task Audit_records_secret_presence_without_exposing_the_value()
    {
        using var fixture = new TemporaryDirectory();
        var variable = $"CP6_SPACE_TEST_{Guid.NewGuid():N}".ToUpperInvariant();
        var secretValue = $"do-not-record-{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, secretValue);
        try
        {
            var configPath = fixture.Write(
                "preflight.json",
                CreateConfig(variable));

            var report = await CadTrialPreflight.AuditAsync(configPath);
            var json = JsonSerializer.Serialize(report, CadExperimentJson.Options);

            Assert.True(report.Secrets.Single().Configured);
            Assert.DoesNotContain(secretValue, json, StringComparison.Ordinal);
            Assert.Contains(variable, json, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private static string CreateConfig(
        string secretEnvironmentVariable,
        string manifestPath = "missing/manifest.json",
        string stress50MiBPath = "missing/stress-50mb.dxf",
        string stressOneMillionPath = "missing/stress-million.dxf")
    {
        return $$"""
          {
            "schemaVersion": 1,
            "candidateId": "oda-drawings-sdk",
            "candidateVersion": "pending",
            "deploymentMode": "EmbeddedSdk",
            "dataset": {
              "manifestPath": "{{manifestPath}}",
              "stress50MiBPath": "{{stress50MiBPath}}",
              "stressOneMillionPath": "{{stressOneMillionPath}}"
            },
            "legal": {
              "approvalReference": "<pending>",
              "multiTenantSaasApproved": false,
              "scaledWorkersApproved": false,
              "disasterRecoveryApproved": false,
              "nonProductionApproved": false,
              "redistributionOrHostedServiceApproved": false
            },
            "isolation": {
              "evidenceReference": "<pending>",
              "workerVcpu": 8,
              "workerMemoryMiB": 32768,
              "networkPolicy": "DenyAll",
              "restrictedServiceIdentity": true,
              "noBusinessCredentials": true,
              "dedicatedTemporaryDirectory": true,
              "outOfProcess": true,
              "processTreeKillVerified": true
            },
            "packages": [
              {
                "platform": "windows-x64",
                "path": "missing/oda-windows.zip",
                "sha256": "{{new string('0', 64)}}"
              },
              {
                "platform": "linux-x64",
                "path": "missing/oda-linux.tgz",
                "sha256": "{{new string('0', 64)}}"
              }
            ],
            "requiredSecretEnvironmentVariables": [
              "{{secretEnvironmentVariable}}"
            ]
          }
          """;
    }

    private const string ValidDxf =
        "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1015\n"
        + "9\n$INSUNITS\n70\n4\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n"
        + "0\nLINE\n5\n10\n8\nWALL\n0\nENDSEC\n0\nEOF\n";
}
