using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Local selected-field vectors only. Complete collector success requires a real hosted protected S06 run.
public sealed class S06ValidationCollectorTests
{
    private static GitHubWorkflowIdentity Producer => new("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
        new string('5', 40), 999991, 1, new string('a', 40));

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("producer")]
    [InlineData("noncanonical-producer")]
    [InlineData("package")]
    [InlineData("empty-payload")]
    [InlineData("oversized-payload")]
    public void Preparation_stage_cannot_change_entry_set_producer_or_package_bytes(string mutation)
    {
        var files = S06ValidationInputs.PreparationNames.ToDictionary(n => n, _ => (ReadOnlyMemory<byte>)"{}"u8.ToArray());
        files["producer.json"] = S06ArtifactAssembly.Canonical(S06InToto.Workflow(Producer));
        if (mutation == "missing") files.Remove("producer.json");
        if (mutation == "extra") files.Add("extra.json", "{}"u8.ToArray());
        if (mutation == "producer") files["producer.json"] = S06ArtifactAssembly.Canonical(S06InToto.Workflow(Producer with { RunAttempt = 2 }));
        if (mutation == "noncanonical-producer") files["producer.json"] = files["producer.json"].ToArray().Append((byte)'\n').ToArray();
        if (mutation == "empty-payload") files["payloads/CrmConsumer.json"] = ReadOnlyMemory<byte>.Empty;
        if (mutation == "oversized-payload") files["payloads/CrmConsumer.json"] = new byte[4194305];
        var expected = mutation switch
        {
            "missing" or "extra" => "validation-stage-files",
            "producer" or "noncanonical-producer" => "validation-stage-producer",
            "empty-payload" or "oversized-payload" => "validation-stage-size",
            _ => "validation-stage-package"
        };
        Assert.Equal(expected, Assert.Throws<Cp6ReleaseContractException>(() => S06ValidationInputs.ReadPreparation(files, Producer)).Code);
    }

    [Fact]
    public void A_publication_identity_cannot_consume_a_validation_preparation() =>
        Assert.Equal("s06-attestation-producer", Assert.Throws<Cp6ReleaseContractException>(() =>
            S06ValidationInputs.ReadPreparation(new Dictionary<string, ReadOnlyMemory<byte>>(),
                Producer with { WorkflowPath = S06ReleaseIdentity.PublicationPath })).Code);

    [Fact]
    public void Image_control_metadata_preserves_native_report_and_bundle_bytes_without_authenticating_them()
    {
        var files = ImageFiles();
        var value = S06ValidationInputs.ReadImage(files);
        Assert.Equal("sha256:" + new string('b', 64), value.Digest);
        Assert.Equal(files["spdx.json"].ToArray(), value.Spdx);
        Assert.Equal(files["sarif.json"].ToArray(), value.Sarif);
        Assert.Equal(files["oci.sigstore.json"].ToArray(), value.SignatureBundle);
        Assert.Equal(files["buildx-metadata.json"].ToArray(), value.BuildMetadata);
        value.Spdx[0] = 99;
        Assert.Equal((byte)'{', S06ValidationInputs.ReadImage(files).Spdx[0]);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("empty")]
    [InlineData("oversize")]
    [InlineData("unknown-field")]
    [InlineData("digest")]
    [InlineData("reversed")]
    [InlineData("future")]
    [InlineData("noncanonical")]
    [InlineData("offset-time")]
    public void Invalid_image_stage_data_fails_before_native_evidence_authentication(string mutation)
    {
        var files = ImageFiles();
        if (mutation == "missing") files.Remove("spdx.json");
        if (mutation == "extra") files.Add("extra.json", "{}"u8.ToArray());
        if (mutation == "empty") files["spdx.json"] = ReadOnlyMemory<byte>.Empty;
        if (mutation == "oversize") files["spdx.json"] = new byte[4194305];
        if (mutation == "noncanonical") files["build-input.json"] = files["build-input.json"].ToArray().Append((byte)'\n').ToArray();
        if (mutation is "unknown-field" or "digest" or "reversed" or "future" or "offset-time")
        {
            var root = JsonNode.Parse(files["build-input.json"].Span)!.AsObject();
            if (mutation == "unknown-field") root["extra"] = true;
            if (mutation == "digest") root["imageDigest"] = "mutable:latest";
            if (mutation == "reversed") root["buildCompletedAtUtc"] = "2026-09-01T11:00:00.000Z";
            if (mutation == "future") root["buildCompletedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UtcNow.AddDays(1));
            if (mutation == "offset-time") root["buildStartedAtUtc"] = "2026-09-01T12:00:00.000+00:00";
            files["build-input.json"] = Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(root));
        }
        Assert.Throws<Cp6ReleaseContractException>(() => S06ValidationInputs.ReadImage(files));
    }

    [Fact]
    public async Task Precancelled_preparation_cannot_read_secrets_or_create_stage_files()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            S06ValidationCollector.PrepareAsync("never-created", "", "", "", cancellation.Token));
    }

    [Fact]
    public async Task Precancelled_finalization_cannot_create_a_success_gate()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => S06ValidationCollector.FinalizeAsync(
            "never-read", "never-read", "never-created", new("missing-cosign"), "", "", cancellation.Token));
    }

    private static Dictionary<string, ReadOnlyMemory<byte>> ImageFiles() => new()
    {
        ["build-input.json"] = S06ArtifactAssembly.Canonical(new
        {
            imageDigest = "sha256:" + new string('b', 64),
            buildStartedAtUtc = "2026-09-01T12:00:00.000Z",
            buildCompletedAtUtc = "2026-09-01T12:01:00.000Z"
        }),
        ["buildx-metadata.json"] = "{ \"native\": 1 }\n"u8.ToArray(),
        ["spdx.json"] = "{ \"native\": 2 }\n"u8.ToArray(),
        ["sarif.json"] = "{ \"native\": 3 }\n"u8.ToArray(),
        ["oci.sigstore.json"] = "{ \"native\": 4 }\n"u8.ToArray()
    };
}
