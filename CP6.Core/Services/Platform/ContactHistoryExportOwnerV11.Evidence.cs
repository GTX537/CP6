using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using static CP6.Core.Services.Platform.ContactHistoryExportWireV11;

namespace CP6.Core.Services.Platform;

public sealed record ContactHistoryConflictNoticeV11([property: JsonRequired] Guid ConflictId, [property: JsonRequired] Guid EventId,
    [property: JsonRequired] string StreamIdentityDigest, [property: JsonRequired] string ConflictingBodySha256);

public sealed record ContactHistoryCheckpointRequestV11([property: JsonRequired] string StreamIdentityDigest,
    [property: JsonRequired] string ExpectedRevision, [property: JsonRequired] Guid[] ResolvesConflictIds);

public sealed record ContactHistoryArtifactChangeV11([property: JsonRequired] Guid ArtifactId, [property: JsonRequired] string ArtifactVersion,
    [property: JsonRequired] string ExpectedRevision);

public sealed partial class ContactHistoryExportOwnerV11
{
    public Task<object> GetAuthorityAsync(ContactHistoryCallerV11 caller, CancellationToken ct) =>
        TransactionAsync(caller, (_, _) => Task.FromResult(AuthorityDescriptor()), ct);

    public Task<ContactHistoryTrustV11> AuthenticateAsync(ContactHistoryCallerV11 caller, ContactHistoryEvidenceLookupV11 lookup, CancellationToken ct) =>
        TransactionAsync(caller, async (store, _) =>
        {
            var (raw, _) = await CurrentEvidenceAsync(store, lookup.EventId, ct);
            if (raw.BodyHash != Sha(lookup.BodySha256)) throw Missing();
            return Trust(caller.TenantId, raw.Event, await store.NowAsync(ct));
        }, ct);

    public Task<bool> VerifyReplayAsync(ContactHistoryCallerV11 caller, ContactHistoryReplayV11 replay, CancellationToken ct) =>
        TransactionAsync(caller, async (store, _) =>
        {
            if (!MatchesTrust(replay.Original, caller.TenantId, replay.EventId)) return false;
            var (raw, _) = await CurrentEvidenceAsync(store, replay.EventId, ct);
            return raw.BodyHash == Sha(replay.BodySha256);
        }, ct);

    public Task<bool> VerifyCheckpointAsync(ContactHistoryCallerV11 caller, ContactHistoryCheckpointVerifyV11 verify, CancellationToken ct) =>
        TransactionAsync(caller, async (store, _) =>
        {
            if (verify.Checkpoint is not { } proof || !Guid.TryParseExact(proof.Reference, "D", out var id) ||
                proof.Reference != id.ToString("D") || !MatchesTrust(verify.Original, caller.TenantId, id)) return false;
            var (raw, request) = await CurrentEvidenceAsync(store, id, ct);
            if (raw.Kind == "DECLINED" || proof.CoversThrough != Text(raw.Revision) || proof.StreamIdentityDigest != raw.Stream || proof.Authority != Authority)
                return false;
            using var doc = JsonDocument.Parse(raw.Body);
            if (doc.RootElement.GetProperty("snapshotKind").GetString() != "CHECKPOINT") return false;
            var saved = Read<ContactHistoryCheckpointV11>(doc.RootElement.GetProperty("checkpointProof"));
            return Canonical(saved).AsSpan().SequenceEqual(Canonical(proof)) && signer.Verify(CheckpointSignatureDomain,
                CheckpointStatement(request, proof), proof.Proof);
        }, ct);

    private async Task<(HistoryExportOriginal, HistoryExportRequest)> CurrentEvidenceAsync(ContactHistoryExportStoreV11 store, Guid eventId, CancellationToken ct)
    {
        var raw = await store.EventAsync(eventId, ct) ?? throw Missing();
        var request = await RequireOriginalAsync(store, raw.Request, ct);
        RequireEvidence(request.Epoch, raw, request.Intent.TenantId);
        return (raw, request);
    }

    private static bool MatchesTrust(ContactHistoryTrustV11? trust, Guid tenant, Guid id) =>
        trust is { Producer: Producer, Authority: Authority } && trust.TenantId == tenant && trust.EvidenceRef == Ref(id) && trust.VerifiedAtUtc != default;

    public Task<object> RecordConflictAsync(ContactHistoryCallerV11 caller, Guid request, ContactHistoryConflictNoticeV11 notice, CancellationToken ct)
    {
        if (!caller.CanReconcile) throw Denied();
        return TransactionAsync<object>(caller, async (store, _) =>
        {
            await store.HoldRequestAsync(request, ct);
            var (raw, saved) = await CurrentEvidenceAsync(store, notice.EventId, ct);
            if (saved.Id != request || raw.Kind == "DECLINED" || raw.Stream != Sha(notice.StreamIdentityDigest) ||
                notice.ConflictId == Guid.Empty || raw.BodyHash == Sha(notice.ConflictingBodySha256))
                throw Conflict("CONTACT_EXPORT_CONFLICT_SCOPE_MISMATCH");
            await store.ReportConflictAsync(raw, notice.ConflictId, notice.ConflictingBodySha256, ct);
            return new { conflictId = notice.ConflictId, streamIdentityDigest = raw.Stream };
        }, ct);
    }

    public Task<ContactHistoryPublicationV11> CheckpointAsync(ContactHistoryCallerV11 caller, Guid id, ContactHistoryCheckpointRequestV11 input,
        CancellationToken ct)
    {
        if (!caller.CanReconcile) throw Denied();
        var stream = Sha(input.StreamIdentityDigest);
        var expected = Sequence(input.ExpectedRevision);
        var resolves = input.ResolvesConflictIds;
        if (resolves is null || resolves.Length > 64 || resolves.Contains(Guid.Empty) || resolves.Distinct().Count() != resolves.Length) throw Invalid();
        return TransactionAsync(caller, async (store, _) =>
        {
            await store.HoldRequestAsync(id, ct);
            var request = await RequireOriginalAsync(store, id, ct);
            var head = await store.HeadAsync(id, stream, ct) ?? throw Missing();
            RequireEvidence(request.Epoch, head, caller.TenantId);
            if (head.Kind == "DECLINED" || head.Revision != expected) throw Conflict("CONTACT_EXPORT_PREDECESSOR_CHANGED");
            var open = await store.ConflictsAsync(id, [stream], ct);
            if (resolves.Except(open).Any()) throw Conflict("CONTACT_EXPORT_CHECKPOINT_SCOPE_MISMATCH");
            var next = checked(head.Revision + 1);
            var newId = Guid.NewGuid();
            var now = await store.NowAsync(ct);
            var proof = new ContactHistoryCheckpointV11(Authority, newId.ToString("D"), Text(next), stream, Text(next),
                resolves.Select(value => value.ToString("D")).ToArray(), "");
            proof = proof with { Proof = signer.Sign(CheckpointSignatureDomain, CheckpointStatement(request, proof)) };
            // 完整状态沿可信前沿保留。Job、Artifact 各有独立轴，checkpoint 不重写原版本。
            var body = JsonNode.Parse(head.Body)!.AsObject();
            body[head.Kind == "JOB" ? "jobRevision" : "stateRevision"] = Text(next);
            body["previousRevision"] = null;
            body["checkpointProof"] = JsonSerializer.SerializeToNode(proof, WireJson);
            body["snapshotKind"] = "CHECKPOINT";
            body["eventId"] = newId.ToString("D");
            body["sourceAsOf"] = Time(now);
            var raw = await SaveEvidenceAsync(store, request, newId, head.Kind, stream, next, Bytes(body), ct);
            await store.ResolveAsync(id, newId, resolves, ct);
            return Publication(request.Epoch, raw, caller.TenantId, now);
        }, ct);
    }

    private static object CheckpointStatement(HistoryExportRequest request, ContactHistoryCheckpointV11 proof) => new
    {
        tenantId = request.Intent.TenantId, producer = Producer, epoch = request.Epoch, proof.Authority, proof.Reference, proof.Revision,
        proof.StreamIdentityDigest, proof.CoversThrough, proof.ResolvesConflictIds
    };

    public Task<ContactHistoryPublicationV11> ChangeArtifactAsync(ContactHistoryCallerV11 caller, Guid id,
        ContactHistoryArtifactChangeV11 change, bool redact, CancellationToken ct)
    {
        if (!caller.CanReconcile) throw Denied();
        var expected = Sequence(change.ExpectedRevision);
        return TransactionAsync(caller, async (store, _) =>
        {
            await store.HoldRequestAsync(id, ct);
            var request = await store.RequestAsync(id, ct) ?? throw Missing();
            var grant = await store.GrantAsync(request.Intent, ct);
            if (request.Job is null) throw Missing();
            var file = await store.FileAsync(id, change.ArtifactId, Key(change.ArtifactVersion), true, ct) ?? throw Missing();
            var stream = Stream(request, file);
            var head = await store.HeadAsync(id, stream, ct) ?? throw Unavailable();
            RequireEvidence(request.Epoch, head, caller.TenantId);
            if (head.Revision != expected || file.State != "READY") throw Conflict("CONTACT_EXPORT_PREDECESSOR_CHANGED");
            var now = await store.NowAsync(ct);
            HistoryExportOriginal raw;
            if (redact)
            {
                if (file.Redacted || !grant.Retained || !grant.Request || !grant.Allows(request.Intent) || grant.Watermark != file.Watermark ||
                    grant.PrivacyRevision != file.PrivacyRevision || ParseTime(request.Intent.RecordedThrough) > grant.Through ||
                    (await store.ConflictsAsync(id, [stream, Stream(request)], ct)).Length != 0) throw Denied();
                var job = await store.HeadAsync(id, Stream(request), ct) ?? throw Unavailable();
                RequireEvidence(request.Epoch, job, caller.TenantId);
                // 对同一制品的重试使用已持久的独立脱敏版本。
                var existing = await store.RedactedForAsync(id, file.Id, ct);
                if (existing is not null)
                    raw = await store.HeadAsync(id, Stream(request, existing), ct) ?? throw Unavailable();
                else
                {
                    var payload = await store.CaptureAsync(request.Intent, grant, true, ct);
                    try { raw = await PublishFileAsync(store, request, file.Id, payload, grant, true, job.Revision, now, ct); }
                    finally { CryptographicOperations.ZeroMemory(payload); }
                }
            }
            else
            {
                using var prior = JsonDocument.Parse(head.Body);
                var result = prior.RootElement;
                await store.RevokeFileAsync(file, ct);
                raw = await SaveArtifactAsync(store, request, file with { State = "REVOKED" }, checked(head.Revision + 1),
                    Sequence(result.GetProperty("requiredJobRevision").GetString()), result.GetProperty("coverage").GetString()!, now, ct);
            }
            if (await store.NowAsync(ct) >= grant.Until) throw Denied();
            return Publication(request.Epoch, raw, caller.TenantId, now);
        }, ct);
    }
}
