using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using static CP6.Core.Services.Platform.ContactHistoryExportWireV11;

namespace CP6.Core.Services.Platform;

public sealed record ContactHistoryDeclinedV11(Guid RequestId, string IntentDigest, string ErrorCode, string EvidenceRef, string FinalityDomain);
public sealed record ContactHistoryReplyV11(byte[]? Body, ContactHistoryTrustV11 Trust, ContactHistoryDeclinedV11? Declined);
public sealed record ContactHistoryPublicationV11(string Kind, Guid EventId, string BodySha256, string StreamIdentityDigest,
    string Revision, string KeyId, string Signature, byte[] Body, ContactHistoryTrustV11 Trust);
public sealed record ContactHistoryResultPageV11(ContactHistoryPublicationV11[] Originals, string? NextAfter);
public sealed record ContactHistoryEvidenceLookupV11([property: JsonRequired] Guid EventId, [property: JsonRequired] string BodySha256);
public sealed record ContactHistoryReplayV11([property: JsonRequired] ContactHistoryTrustV11 Original,
    [property: JsonRequired] Guid EventId, [property: JsonRequired] string BodySha256);
public sealed record ContactHistoryCheckpointVerifyV11([property: JsonRequired] ContactHistoryTrustV11 Original,
    [property: JsonRequired] ContactHistoryCheckpointV11 Checkpoint);

public sealed partial class ContactHistoryExportOwnerV11(CP6Context db, ContactHistoryExportSignerV11 signer)
{
    private const string ResultSignatureDomain = "CONTACT-HISTORY-RESULT-PROOF/1";
    private const string CheckpointSignatureDomain = "CONTACT-HISTORY-CHECKPOINT/1";

    private async Task<T> TransactionAsync<T>(ContactHistoryCallerV11 caller,
        Func<ContactHistoryExportStoreV11, DateTimeOffset, Task<T>> work, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw Unavailable();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='5s'; SET LOCAL statement_timeout='15s'", ct);
        var store = new ContactHistoryExportStoreV11((NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction)tx.GetDbTransaction(), caller);
        var serviceUntil = await store.HoldCallerAsync(ct);
        var result = await work(store, serviceUntil);
        if (await store.NowAsync(ct) >= serviceUntil) throw Denied();
        await tx.CommitAsync(ct);
        return result;
    }

    public Task<object> DescribeAsync(ContactHistoryCallerV11 caller, CancellationToken ct) =>
        TransactionAsync<object>(caller, (_, _) => Task.FromResult<object>(new { producer = Producer, dataset = Dataset,
            schemaVersion = Schema, format = Format, fieldIds = FieldIds, maximumArtifactBytes = MaximumBytes }), ct);

    public object AuthorityDescriptor() => new { contractVersion = Contract, producer = Producer, authority = Authority,
        epoch = signer.Epoch, keyId = signer.KeyId, algorithm = "ES256", publicKeySpkiBase64 = signer.PublicKey };

    public Task<ContactHistoryReplyV11> SubmitAsync(ContactHistoryCallerV11 caller, ContactHistoryRequestV11 submitted, CancellationToken ct)
    {
        var request = Validate(submitted, caller.TenantId);
        return TransactionAsync(caller, async (store, _) =>
        {
            await store.HoldRequestAsync(request.RequestId, ct);
            var prior = await store.RequestAsync(request.RequestId, ct);
            if (prior is not null)
            {
                await store.GrantAsync(prior.Intent, ct);
                if (prior.Digest != request.IntentDigest || !Canonical(prior.Intent).AsSpan().SequenceEqual(Canonical(request.Intent)))
                    throw Conflict("CONTACT_EXPORT_ORIGINAL_CONFLICT");
                return await ReplyAsync(store, prior, ct);
            }

            var grant = await store.GrantAsync(request.Intent, ct);
            var now = await store.NowAsync(ct);
            if (grant.Watermark != request.SourcePermissionWatermark) throw Conflict("CONTACT_EXPORT_SOURCE_WATERMARK_CHANGED");
            var reason = !grant.Retained || !grant.Request || !grant.Allows(request.Intent) ? "CONTACT_EXPORT_SCOPE_DENIED" : null;
            byte[]? payload = null;
            if (reason is null)
            {
                if (ParseTime(request.Intent.RecordedThrough) > now || ParseTime(request.Intent.RecordedThrough) > grant.Through)
                    throw Unavailable();
                try { payload = await store.CaptureAsync(request.Intent, grant, false, ct); }
                catch (ContactHistoryExportExceptionV11 error) when (error.Status == 422) { reason = error.Message; }
            }
            try
            {
                now = await store.NowAsync(ct);
                if (now >= grant.Until) throw Denied();
                var saved = new HistoryExportRequest(request.RequestId, caller.ClientId, request.EventId, signer.Epoch,
                    request.IntentDigest, request.Intent, reason is null ? Guid.NewGuid() : null, Guid.NewGuid());
                await store.AddRequestAsync(saved, ct);
                if (reason is not null)
                {
                    await SaveDeclineAsync(store, saved, reason, ct);
                }
                else
                {
                    await SaveJobAsync(store, saved, "ACCEPTED", 1, saved.ReceiptEvent, now, ct);
                    await SaveJobAsync(store, saved, "RUNNING", 2, Guid.NewGuid(), now, ct);
                    await SaveJobAsync(store, saved, "COMPLETED", 3, Guid.NewGuid(), now, ct);
                    await PublishFileAsync(store, saved, Guid.NewGuid(), payload!, grant, false, 3, now, ct);
                }
                if (await store.NowAsync(ct) >= grant.Until) throw Denied();
                return await ReplyAsync(store, saved, ct);
            }
            finally
            {
                if (payload is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(payload);
            }
        }, ct);
    }

    public Task<ContactHistoryReplyV11> OriginalAsync(ContactHistoryCallerV11 caller, Guid request, CancellationToken ct) =>
        TransactionAsync(caller, async (store, _) => await ReplyAsync(store, await RequireOriginalAsync(store, request, ct), ct), ct);

    public Task<ContactHistoryResultPageV11> ResultsAsync(ContactHistoryCallerV11 caller, Guid request, string? after, CancellationToken ct) =>
        TransactionAsync(caller, async (store, _) =>
        {
            var saved = await RequireOriginalAsync(store, request, ct);
            var rows = await store.PageAsync(request, after is null ? 0 : Sequence(after, allowZero: true), ct);
            var now = await store.NowAsync(ct);
            var page = rows.Select(row => Publication(saved.Epoch, row, caller.TenantId, now)).ToArray();
            return new ContactHistoryResultPageV11(page, rows.Length > 0 ? Text(rows[^1].Ordinal) : null);
        }, ct);

    private static async Task<HistoryExportRequest> RequireOriginalAsync(ContactHistoryExportStoreV11 store, Guid id, CancellationToken ct)
    {
        var saved = await store.RequestAsync(id, ct) ?? throw Missing();
        _ = await store.GrantAsync(saved.Intent, ct);
        return saved;
    }

    private async Task<ContactHistoryReplyV11> ReplyAsync(ContactHistoryExportStoreV11 store, HistoryExportRequest request, CancellationToken ct)
    {
        var original = await store.EventAsync(request.ReceiptEvent, ct) ?? throw Unavailable();
        RequireEvidence(request.Epoch, original, request.Intent.TenantId);
        if (original.Request != request.Id || original.Revision != 1 || original.Kind != (request.Job.HasValue ? "JOB" : "DECLINED")) throw Unavailable();
        var trust = Trust(request.Intent.TenantId, original.Event, await store.NowAsync(ct));
        return request.Job.HasValue ? new(original.Body, trust, null) :
            new(null, trust, JsonSerializer.Deserialize<ContactHistoryDeclinedV11>(original.Body, WireJson) ?? throw Unavailable());
    }

    private async Task SaveDeclineAsync(ContactHistoryExportStoreV11 store, HistoryExportRequest request, string reason, CancellationToken ct)
    {
        var decision = new ContactHistoryDeclinedV11(request.Id, request.Digest, reason, Ref(request.ReceiptEvent), DenialDomain);
        await SaveEvidenceAsync(store, request, request.ReceiptEvent, "DECLINED", Stream(request), 1, Bytes(decision), ct);
    }

    private Task SaveJobAsync(ContactHistoryExportStoreV11 store, HistoryExportRequest request, string state, long revision,
        Guid id, DateTimeOffset at, CancellationToken ct) => SaveEvidenceAsync(store, request, id, "JOB", Stream(request), revision,
        Bytes(new { contractVersion = Contract, producer = Producer, epoch = request.Epoch, eventId = id, requestId = request.Id,
            jobId = request.Job!.Value.ToString("D"), jobRevision = Text(revision), previousRevision = revision > 1 ? Text(revision - 1) : null,
            snapshotKind = revision == 1 ? "INITIAL_COMPLETE" : "CONTIGUOUS", checkpointProof = (object?)null,
            state, intentDigest = request.Digest, intent = request.Intent, sourceAsOf = Time(at) }), ct);

    private async Task<HistoryExportOriginal> PublishFileAsync(ContactHistoryExportStoreV11 store, HistoryExportRequest request, Guid artifact,
        byte[] bytes, HistoryExportGrant grant, bool redact, long jobRevision, DateTimeOffset at, CancellationToken ct)
    {
        var file = new HistoryExportFile(artifact, (redact ? "redacted/" : "original/") + Guid.NewGuid().ToString("N"),
            Hash(bytes), bytes.Length, grant.PrivacyRevision, grant.Watermark, "READY", redact);
        await store.AddFileAsync(request, file, bytes, ct);
        return await SaveArtifactAsync(store, request, file, 1, jobRevision, redact ? "PARTIAL" : grant.Coverage, at, ct);
    }

    private Task<HistoryExportOriginal> SaveArtifactAsync(ContactHistoryExportStoreV11 store, HistoryExportRequest request, HistoryExportFile file,
        long revision, long jobRevision, string coverage, DateTimeOffset at, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        return SaveEvidenceAsync(store, request, id, "ARTIFACT", Stream(request, file), revision,
            Bytes(new { contractVersion = Contract, producer = Producer, epoch = request.Epoch, eventId = id, requestId = request.Id,
                jobId = request.Job!.Value.ToString("D"), stateRevision = Text(revision), requiredJobRevision = Text(jobRevision),
                artifactId = file.Id.ToString("D"), artifactVersion = file.Version, sha256 = file.Digest, bytes = Text(file.Size), coverage,
                previousRevision = revision > 1 ? Text(revision - 1) : null, snapshotKind = revision == 1 ? "INITIAL_COMPLETE" : "CONTIGUOUS",
                checkpointProof = (object?)null, state = file.State, intentDigest = request.Digest, intent = request.Intent, sourceAsOf = Time(at) }), ct);
    }

    private async Task<HistoryExportOriginal> SaveEvidenceAsync(ContactHistoryExportStoreV11 store, HistoryExportRequest request, Guid id,
        string kind, string stream, long revision, byte[] body, CancellationToken ct)
    {
        if (request.Epoch != signer.Epoch) throw Unavailable();
        var raw = new HistoryExportOriginal(id, request.Id, kind, stream, revision, body, Hash(body), signer.KeyId, "");
        var signed = raw with { Signature = signer.Sign(ResultSignatureDomain, SignedFields(request.Intent.TenantId, request.Epoch, raw)) };
        await store.AddOriginalAsync(signed, ct);
        return signed;
    }

    private static string Stream(HistoryExportRequest request, HistoryExportFile? file = null) => Hash(Canonical(new
    {
        tenantId = request.Intent.TenantId, producer = Producer, epoch = request.Epoch, requestId = request.Id,
        jobId = request.Job?.ToString("D"), artifactId = file?.Id.ToString("D"), artifactVersion = file?.Version
    }));

    private static object SignedFields(Guid tenant, string epoch, HistoryExportOriginal raw) => new
    {
        tenantId = tenant, producer = Producer, authority = Authority, epoch, eventId = raw.Event, requestId = raw.Request, kind = raw.Kind,
        streamIdentityDigest = raw.Stream, revision = Text(raw.Revision), bodySha256 = raw.BodyHash, keyId = raw.KeyId
    };

    private static string Ref(Guid id) => "contact-history-proof/" + id.ToString("D");
    private static ContactHistoryTrustV11 Trust(Guid tenant, Guid id, DateTimeOffset now) => new(tenant, Producer, Authority, Ref(id), now);

    private void RequireEvidence(string epoch, HistoryExportOriginal raw, Guid tenant)
    {
        if (epoch != signer.Epoch || raw.KeyId != signer.KeyId || raw.BodyHash != Hash(raw.Body) ||
            !signer.Verify(ResultSignatureDomain, SignedFields(tenant, epoch, raw), raw.Signature)) throw Unavailable();
    }

    private ContactHistoryPublicationV11 Publication(string epoch, HistoryExportOriginal raw, Guid tenant, DateTimeOffset now)
    {
        RequireEvidence(epoch, raw, tenant);
        return new(raw.Kind, raw.Event, raw.BodyHash, raw.Stream, Text(raw.Revision), raw.KeyId, raw.Signature, raw.Body, Trust(tenant, raw.Event, now));
    }
}
