using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using static CP6.Core.Services.Platform.ContactHistoryExportWireV11;

namespace CP6.Core.Services.Platform;

public sealed record ContactHistoryPermitV11(string Producer, ContactHistoryReadV11 Binding, DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc, Guid PermitId);

public sealed record ContactHistoryDownloadSliceV11(ReadOnlyMemory<byte> Bytes, int Offset, int Length, string Sha256);

public sealed partial class ContactHistoryExportOwnerV11
{
    private const int SliceSize = 16 * 1024;
    private static readonly TimeSpan MaxWriteTime = TimeSpan.FromSeconds(2);

    public Task<ContactHistoryPermitV11> AuthorizeReadAsync(ContactHistoryCallerV11 caller, ContactHistoryReadV11 read, CancellationToken ct)
    {
        Validate(read, caller.TenantId);
        return TransactionAsync(caller, async (store, serviceUntil) =>
        {
            var (file, grant) = await CurrentDownloadAsync(store, read, ct);
            await store.HoldPayloadAsync(file, ct);
            var now = await store.NowAsync(ct);
            var until = Earliest(now + signer.DownloadWindow, Earliest(serviceUntil, grant.Until));
            if (until <= now) throw Denied();
            // CRM 在调许可前观察自己的事务时钟。允许该观察提前最多两秒，文件仍须持已签发的许可。
            var from = now - MaxWriteTime;
            if (from < grant.From) from = grant.From;
            var permit = new HistoryExportPermit(Guid.NewGuid(), read, from, until, grant.AccessRevision);
            await store.AddPermitAsync(permit, ct);
            return new ContactHistoryPermitV11(Producer, read, from, until, permit.Id);
        }, ct);
    }

    public async Task DownloadAsync(ContactHistoryCallerV11 caller, Guid permitId,
        Func<ContactHistoryDownloadSliceV11, CancellationToken, Task> write, CancellationToken ct)
    {
        if (permitId == Guid.Empty) throw Denied();
        byte[]? payload = null;
        try
        {
            var offset = 0;
            do
            {
                offset = await TransactionAsync(caller, async (store, serviceUntil) =>
                {
                    var permit = await store.PermitAsync(permitId, ct);
                    var (file, grant) = await CurrentDownloadAsync(store, permit.Binding, ct);
                    if (grant.AccessRevision != permit.AccessRevision) throw Denied();
                    if (offset == 0) payload = await store.PayloadAsync(file, ct);
                    else await store.HoldPayloadAsync(file, ct);
                    var raw = payload ?? throw Unavailable();
                    if (raw.Length != file.Size) throw Unavailable();

                    var stopwatch = Stopwatch.StartNew();
                    var now = await store.NowAsync(ct);
                    var until = Earliest(permit.Until, Earliest(serviceUntil, grant.Until));
                    var remaining = until - now - stopwatch.Elapsed;
                    if (remaining <= TimeSpan.Zero) throw Denied();
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    deadline.CancelAfter(remaining < MaxWriteTime ? remaining : MaxWriteTime);
                    var count = Math.Min(SliceSize, raw.Length - offset);
                    // 这批字节送出前保留当前授权的共享锁。批间释放锁，让撤权事务提交。
                    await write(new(raw.AsMemory(offset, count), offset, raw.Length, file.Digest), deadline.Token);
                    return offset + count;
                }, ct);
            } while (offset < payload!.Length);
        }
        finally
        {
            if (payload is not null) CryptographicOperations.ZeroMemory(payload);
        }
    }

    private async Task<(HistoryExportFile File, HistoryExportGrant Grant)> CurrentDownloadAsync(ContactHistoryExportStoreV11 store,
        ContactHistoryReadV11 read, CancellationToken ct)
    {
        var original = read.Artifact;
        var subject = read.Subject;
        await store.HoldRequestAsync(original.RequestId, ct);
        var request = await store.RequestAsync(original.RequestId, ct) ?? throw Missing();
        if (request.Job is null || request.Job.Value.ToString("D") != original.JobId ||
            request.Intent.ContactId != subject.ContactId || request.Intent.ReaderSubjectId != subject.ReaderSubjectId ||
            request.Digest != original.IntentDigest || subject.ReadPurpose != request.Intent.ReadPurpose ||
            !request.Intent.FieldIds.SequenceEqual(subject.FieldIds, StringComparer.Ordinal)) throw Denied();
        var grant = await store.GrantAsync(request.Intent, ct);
        if (!grant.Download || !grant.Retained || !grant.Allows(request.Intent) || grant.Watermark != subject.CurrentAccessEvidence) throw Denied();
        if (!Guid.TryParseExact(original.ArtifactId, "D", out var id) || original.ArtifactId != id.ToString("D")) throw Denied();
        var file = await store.FileAsync(request.Id, id, original.ArtifactVersion, false, ct) ?? throw Missing();
        if (file.State != "READY" || file.Digest != original.ContentSha256 || Text(file.Size) != original.Bytes ||
            file.PrivacyRevision != grant.PrivacyRevision || file.Watermark != grant.Watermark) throw Denied();

        var jobStream = Stream(request);
        var fileStream = Stream(request, file);
        if ((await store.ConflictsAsync(request.Id, [jobStream, fileStream], ct)).Length != 0) throw Denied();
        var head = await store.HeadAsync(request.Id, jobStream, ct) ?? throw Unavailable();
        var artifact = await store.HeadAsync(request.Id, fileStream, ct) ?? throw Unavailable();
        RequireEvidence(request.Epoch, head, subject.TenantId);
        RequireEvidence(request.Epoch, artifact, subject.TenantId);
        using var doc = JsonDocument.Parse(artifact.Body);
        var result = doc.RootElement;
        if (artifact.Kind != "ARTIFACT" || result.GetProperty("state").GetString() != "READY" ||
            result.GetProperty("artifactId").GetString() != original.ArtifactId ||
            result.GetProperty("artifactVersion").GetString() != file.Version ||
            result.GetProperty("sha256").GetString() != file.Digest || result.GetProperty("bytes").GetString() != original.Bytes)
            throw Unavailable();
        var required = Sequence(result.GetProperty("requiredJobRevision").GetString());
        var predecessor = required == head.Revision ? head :
            await store.RevisionAsync(request.Id, jobStream, required, ct) ?? throw Unavailable();
        if (predecessor != head) RequireEvidence(request.Epoch, predecessor, subject.TenantId);
        if (required > head.Revision || !IsCompleted(head) || !IsCompleted(predecessor)) throw Denied();
        return (file, grant);
    }

    private static bool IsCompleted(HistoryExportOriginal job)
    {
        using var evidence = JsonDocument.Parse(job.Body);
        return job.Kind == "JOB" && evidence.RootElement.GetProperty("state").GetString() == "COMPLETED";
    }

    private static DateTimeOffset Earliest(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;
}
