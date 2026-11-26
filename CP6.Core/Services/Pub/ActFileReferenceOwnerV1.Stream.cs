using System.Security.Cryptography;
using CP6.Core.Services.Erp;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CP6.Core.Services.Pub.ActFileReferenceV1;

namespace CP6.Core.Services.Pub;

public sealed record ActFileDownloadRequestV1(ActFileIdentityV1 Identity, ActFileAttestationV1 Attestation);
public sealed record ActFileDownloadSliceV1(ReadOnlyMemory<byte> Bytes, int Offset, int Length, string Sha256, string FileName, string MediaType);

public sealed partial class ActFileReferenceOwnerV1
{
    private const int SliceSize = 16 * 1024;
    private static readonly TimeSpan MaxWriteTime = TimeSpan.FromSeconds(2);

    public async Task DownloadAsync(ActFileCallerV1 caller, ActFileDownloadRequestV1 request,
        Func<ActFileDownloadSliceV1, CancellationToken, Task> write, CancellationToken ct)
    {
        var expected = new ActFileProofRequestV1(GuardedStream, [request.Identity]);
        var identity = Validate(expected, caller.TenantId);
        var signed = ActFileSignerV1.Verify(request.Attestation, signer.KeyId, signer.PublicKeySpkiBase64);
        ActFileSignerV1.RequireIdentity(signed, expected, caller.ClientId);
        var decision = signed.Decisions.Single();
        var metadata = decision.Download ?? throw Denied();
        var fileId = NativeFileId(identity);
        var proofDigest = FileContentBytesV1.Digest(request.Attestation.Body);
        var delivery = Guid.NewGuid();
        var offset = 0;
        var started = false;
        var outcome = "INTERRUPTED";
        byte[]? payload = null;

        try
        {
            await WithCurrentDownloadAsync(caller, fileId, decision, signed.Guard, async (store, _) =>
            {
                var row = await store.Row(fileId, ct);
                payload = await ReadExactAsync(row, ct);
                await AuditStreamAsync(identity, fileId, delivery, proofDigest, "STARTED", 0, ct);
                return true;
            }, ct);
            started = true;
            var bytes = payload ?? throw Unproven();
            while (offset < bytes.Length)
            {
                await WithCurrentDownloadAsync(caller, fileId, decision, signed.Guard, async (_, expiresAt) =>
                {
                    var now = await NowAsync(ct);
                    var remaining = expiresAt - now;
                    if (remaining <= TimeSpan.Zero) throw Denied();
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    deadline.CancelAfter(remaining < MaxWriteTime ? remaining : MaxWriteTime);
                    var count = Math.Min(SliceSize, bytes.Length - offset);
                    // 每批写入并刷新时持有本地来源和 CRM guard，批间释放，让等候的撤权先取得排他锁。
                    await write(new(bytes.AsMemory(offset, count), offset, bytes.Length, identity.ContentSha256,
                        metadata.SafeFileName, metadata.MediaType), deadline.Token);
                    offset += count;
                    return true;
                }, ct);
            }
            outcome = "COMPLETED";
        }
        catch (OrderPriceCorrectionException error)
        {
            outcome = error.Status == 403 ? "REVOKED" : "UNPROVEN";
            throw;
        }
        finally
        {
            if (payload is not null) CryptographicOperations.ZeroMemory(payload);
            if (started)
            {
                using var auditDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await AuditStreamAsync(identity, fileId, delivery, proofDigest, outcome, offset, auditDeadline.Token);
            }
        }
    }

    private Task<T> WithCurrentDownloadAsync<T>(ActFileCallerV1 caller, Guid fileId, ActFileDecisionV1 signed,
        ActFileGuardStampV1 expected, Func<FileContentStoreV1, DateTimeOffset, Task<T>> use, CancellationToken ct)
        => TransactionAsync(caller, signed.Identity, async expiresAt =>
        {
            var store = await HoldFileStoreAsync(ct);
            var source = await HoldSourceAsync(caller.TenantId, ct);
            if (source != expected) throw Denied();
            return await guard.HoldAsync(caller.TenantId, source, async _ =>
            {
                var file = await store.Read(fileId, null, ct);
                if (await DecideAsync(signed.Identity, file, ct) != signed) throw Denied();
                return await use(store, expiresAt);
            }, ct);
        }, ct);

    private async Task<byte[]> ReadExactAsync(FileContentRowV1 row, CancellationToken ct)
    {
        try
        {
            await using var input = await files.OpenReadBoundedAsync(row.StorePath, row.ByteLength, ct);
            var bytes = await FileContentBytesV1.Read(input, row.ByteLength, ct);
            if (FileContentBytesV1.Digest(bytes) == row.Digest) return bytes;
            CryptographicOperations.ZeroMemory(bytes);
            throw Unproven();
        }
        catch (Exception error) when (error is IOException or NotSupportedException ||
            error is OrderPriceCorrectionException { Message: "FILE_LENGTH_MISMATCH" or "FILE_SIZE_LIMIT" })
        {
            throw Unproven(error);
        }
    }

    private async Task AuditStreamAsync(ActFileIdentityV1 identity, Guid fileId, Guid delivery, string proofDigest, string state,
        long confirmedBytes, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_act_file_v1.stream_audit
                  (tenant_id,delivery_id,actor_id,file_id,operation_id,proof_digest,state,confirmed_bytes)
                VALUES({identity.TenantId},{delivery},{identity.SubjectId},{fileId},
                  {identity.OperationKey},{proofDigest},{state},{confirmedBytes})
                """, ct);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            throw Unproven(error);
        }
    }
}
