using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Tests.Platform;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Contact_history_G05_signed_results_keep_independent_predecessors_and_reconcile_only_named_conflicts()
    {
        await using var h = await Seed();
        var contact = Guid.NewGuid();
        await SeedContactHistory(h, contact);
        var request = OriginalExport(IntentFor(h.Tenant, contact, h.User));
        await using var setup = await ExportServiceFixture.Create(h);
        await using var app = await setup.Start();
        var url = $"/requests/{request.RequestId:D}";
        var authority = await setup.Json(app, "/authority");
        var received = await setup.Json(app, "/requests", request);
        var originals = await ExportPublications(setup, app, request.RequestId);
        Assert.Collection(originals.Select(ExportOriginalBody),
            job => CheckVersion(job, "jobRevision", "1", null, "INITIAL_COMPLETE", "ACCEPTED"),
            job => CheckVersion(job, "jobRevision", "2", "1", "CONTIGUOUS", "RUNNING"),
            job => CheckVersion(job, "jobRevision", "3", "2", "CONTIGUOUS", "COMPLETED"),
            file => CheckVersion(file, "stateRevision", "1", null, "INITIAL_COMPLETE", "READY"));
        Assert.Equal(received.GetProperty("body").GetString(), originals[0].GetProperty("body").GetString());
        var artifact = ExportOriginalBody(originals[3]);
        Assert.Equal("3", artifact.GetProperty("requiredJobRevision").GetString());
        Assert.Equal(ExportOriginalBody(originals[2]).GetProperty("jobId").GetString(), artifact.GetProperty("jobId").GetString());
        var jobStream = StreamDigest(originals[0]);
        var artifactStream = StreamDigest(originals[3]);
        Assert.NotEqual(jobStream, artifactStream);
        var before = await setup.Json(app, url + "/results");
        var cursor = before.GetProperty("nextAfter").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));
        var empty = await setup.Json(app, url + $"/results?after={cursor}");
        Assert.Empty(empty.GetProperty("originals").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("nextAfter").ValueKind);

        // 反序查证会找回原件及其前驱标记。认证可独立完成，消费者须按前驱决定是否应用。
        foreach (var original in originals.Reverse())
        {
            Assert.Equal(StreamIdentity(original, h.Tenant, authority), StreamDigest(original));
            VerifyPublication(original, h.Tenant, request.RequestId, authority);
            var trust = await CheckExportOriginal(setup, app, original);
            var replay = new { original = trust, eventId = original.GetProperty("eventId").GetGuid(),
                bodySha256 = original.GetProperty("bodySha256").GetString() };
            Assert.True((await setup.Json(app, "/proofs/replay", replay)).GetBoolean());
            Assert.False((await setup.Json(app, "/proofs/replay", replay with { bodySha256 = new string('0', 64) })).GetBoolean());
            await setup.Json(app, "/proofs/authenticate", new { replay.eventId, bodySha256 = new string('0', 64) }, 404);
            await setup.Json(app, "/proofs/authenticate", new { replay.eventId, replay.bodySha256 }, 403, tenant: Guid.NewGuid());
        }

        var jobConflict = Guid.NewGuid();
        var artifactConflict = Guid.NewGuid();
        var artifactConflict2 = Guid.NewGuid();
        await Report(originals[0], jobConflict);
        await Report(originals[0], jobConflict);
        await Report(originals[3], artifactConflict);
        await Report(originals[3], artifactConflict2);
        var originalBinding = ExportRead(request, artifact);
        await setup.Json(app, "/permits", originalBinding, 403);
        var invalidCheckpoint = new { streamIdentityDigest = jobStream, expectedRevision = "3", resolvesConflictIds = new[] { artifactConflict } };
        var deniedCheckpoint = await setup.Json(app, url + "/checkpoints", invalidCheckpoint, 409);
        Assert.Equal("CONTACT_EXPORT_CHECKPOINT_SCOPE_MISMATCH", deniedCheckpoint.GetProperty("code").GetString());
        await setup.Json(app, url + "/checkpoints", invalidCheckpoint with { expectedRevision = "2", resolvesConflictIds = new[] { jobConflict } }, 409);
        var jobCheckpoint = await setup.Json(app, url + "/checkpoints", invalidCheckpoint with { resolvesConflictIds = new[] { jobConflict } });
        var fileCheckpoint = await setup.Json(app, url + "/checkpoints", new { streamIdentityDigest = artifactStream,
            expectedRevision = "1", resolvesConflictIds = new[] { artifactConflict } });
        // 只解决了所列冲突，遗漏的同流冲突仍然挡住下载。
        await setup.Json(app, "/permits", originalBinding, 403);
        var fileFinal = await setup.Json(app, url + "/checkpoints", new { streamIdentityDigest = artifactStream,
            expectedRevision = "2", resolvesConflictIds = new[] { artifactConflict2 } });
        CheckVersion(ExportOriginalBody(jobCheckpoint), "jobRevision", "4", null, "CHECKPOINT", "COMPLETED");
        CheckVersion(ExportOriginalBody(fileCheckpoint), "stateRevision", "2", null, "CHECKPOINT", "READY");
        CheckVersion(ExportOriginalBody(fileFinal), "stateRevision", "3", null, "CHECKPOINT", "READY");
        Assert.Equal("3", ExportOriginalBody(fileFinal).GetProperty("requiredJobRevision").GetString());
        Assert.Equal(artifact.GetProperty("artifactVersion").GetString(), ExportOriginalBody(fileFinal).GetProperty("artifactVersion").GetString());
        Assert.Equal(artifact.GetProperty("sha256").GetString(), ExportOriginalBody(fileFinal).GetProperty("sha256").GetString());
        await setup.Json(app, "/permits", originalBinding);

        await CheckProof(jobCheckpoint, artifactStream, "job-checkpoint");
        await CheckProof(fileCheckpoint, jobStream, "artifact-checkpoint");
        await CheckProof(fileFinal, jobStream, "artifact-checkpoint-final");
        var after = await ExportPublications(setup, app, request.RequestId);
        Assert.Equal(7, after.Length);
        Assert.Equal(originals.Select(value => value.GetProperty("body").GetString()),
            after.Take(4).Select(value => value.GetProperty("body").GetString()));
        var tail = await setup.Json(app, url + $"/results?after={cursor}");
        Assert.Equal(after.Skip(4).Select(value => value.GetProperty("eventId").GetGuid()),
            tail.GetProperty("originals").EnumerateArray().Select(value => value.GetProperty("eventId").GetGuid()));
        WriteExportEvidence("checkpoint-request.json", JsonSerializer.SerializeToUtf8Bytes(request, ExportJson));
        WriteExportEvidence("checkpoint-authority.json", authority);
        WriteExportEvidence("checkpoint-invalid-resolution.json", new { request = invalidCheckpoint, response = deniedCheckpoint });
        WriteExportEvidence("checkpoint-results.json", after);

        async Task Report(JsonElement envelope, Guid conflictId) => await setup.Json(app, url + "/conflicts", new
        {
            conflictId, eventId = envelope.GetProperty("eventId").GetGuid(), streamIdentityDigest = StreamDigest(envelope),
            conflictingBodySha256 = new string('f', 64)
        });

        async Task CheckProof(JsonElement envelope, string wrongStream, string evidencePrefix)
        {
            VerifyPublication(envelope, h.Tenant, request.RequestId, authority);
            var body = ExportOriginalBody(envelope);
            var checkpoint = body.GetProperty("checkpointProof");
            var trust = await CheckExportOriginal(setup, app, envelope);
            Assert.True((await setup.Json(app, "/proofs/checkpoint", new { original = trust, checkpoint })).GetBoolean());
            var incorrect = JsonNode.Parse(checkpoint.GetRawText())!.AsObject();
            incorrect["streamIdentityDigest"] = wrongStream;
            Assert.False((await setup.Json(app, "/proofs/checkpoint", new { original = trust, checkpoint = incorrect })).GetBoolean());
            WriteExportEvidence(evidencePrefix + "-original.json", envelope.GetProperty("body").GetBytesFromBase64());
            WriteExportEvidence(evidencePrefix + "-negative.json", new { original = trust, checkpoint = incorrect, response = false });
        }
    }

    private static string StreamDigest(JsonElement envelope) => envelope.GetProperty("streamIdentityDigest").GetString()!;

    private static string StreamIdentity(JsonElement envelope, Guid tenantId, JsonElement authority)
    {
        var raw = ExportOriginalBody(envelope);
        return ExportHash(ExportCanonical(new
        {
            tenantId,
            producer = authority.GetProperty("producer").GetString(),
            epoch = authority.GetProperty("epoch").GetString(),
            requestId = raw.GetProperty("requestId").GetGuid(),
            jobId = raw.GetProperty("jobId").GetString(),
            artifactId = raw.TryGetProperty("artifactId", out var id) ? id.GetString() : null,
            artifactVersion = raw.TryGetProperty("artifactVersion", out var version) ? version.GetString() : null
        }));
    }

    private static void VerifyPublication(JsonElement envelope, Guid tenantId, Guid requestId, JsonElement authority)
    {
        var bodySha256 = envelope.GetProperty("bodySha256").GetString();
        Assert.Equal(bodySha256, ExportHash(envelope.GetProperty("body").GetBytesFromBase64()));
        var keyId = envelope.GetProperty("keyId").GetString();
        Assert.Equal(authority.GetProperty("keyId").GetString(), keyId);
        var statement = ExportCanonical(new
        {
            tenantId, requestId, bodySha256, keyId,
            producer = authority.GetProperty("producer").GetString(), authority = authority.GetProperty("authority").GetString(),
            epoch = authority.GetProperty("epoch").GetString(), eventId = envelope.GetProperty("eventId").GetGuid(),
            kind = envelope.GetProperty("kind").GetString(), streamIdentityDigest = StreamDigest(envelope),
            revision = envelope.GetProperty("revision").GetString()
        });
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(authority.GetProperty("publicKeySpkiBase64").GetBytesFromBase64(), out _);
        byte[] signed = [.. "CONTACT-HISTORY-RESULT-PROOF/1\n"u8, .. statement];
        Assert.True(verifier.VerifyData(signed,
            envelope.GetProperty("signature").GetBytesFromBase64(), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    private static void CheckVersion(JsonElement item, string axis, string revision, string? previous, string kind, string state)
    {
        Assert.Equal(revision, item.GetProperty(axis).GetString());
        Assert.Equal(previous, item.GetProperty("previousRevision").GetString());
        Assert.Equal(kind, item.GetProperty("snapshotKind").GetString());
        Assert.Equal(state, item.GetProperty("state").GetString());
    }
}
