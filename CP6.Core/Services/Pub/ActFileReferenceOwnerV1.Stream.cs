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
            await TransactionAsync(caller, identity, async expiresAt =>
            {
                var store = await HoldFileStoreAsync(ct, write: true);
                var file = await WithCurrentDownloadAsync(store, decision, signed.Guard, async current =>
                {
                    if (await NowAsync(ct) >= expiresAt) throw Denied();
                    payload = await fileOwner.ReadVerifiedDownloadAsync(store, fileId, ct);
                    return current;
                }, ct, holdForIntegrity: true);
                if (payload is null)
                {
                    // CRM 共享锁已经释放。本地写锁仍保护原状态，FILE 隔离写可排他推进同一 CRM guard。
                    await fileOwner.QuarantineIntegrityFailureAsync(store, file, identity.SubjectId, ct);
                    await AuditStreamAsync(identity, fileId, delivery, proofDigest, "UNPROVEN", 0, ct);
                }
                else
                {
                    await AuditStreamAsync(identity, fileId, delivery, proofDigest, "STARTED", 0, ct);
                }
                return true;
            }, ct);
            var bytes = payload ?? throw new OrderPriceCorrectionException(FileContentOwnerV1.IntegrityUnavailable, 503);
            started = true;
            while (offset < bytes.Length)
            {
                await TransactionAsync(caller, identity, async expiresAt =>
                {
                    var store = await HoldFileStoreAsync(ct);
                    return await WithCurrentDownloadAsync(store, decision, signed.Guard, async _ =>
                    {
                        var remaining = expiresAt - await NowAsync(ct);
                        if (remaining <= TimeSpan.Zero) throw Denied();
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        deadline.CancelAfter(remaining < MaxWriteTime ? remaining : MaxWriteTime);
                        var count = Math.Min(SliceSize, bytes.Length - offset);
                        // 每批写入并刷新时持有本地来源和 CRM guard，批间释放，让等候的撤权先取得排他锁。
                        await write(new(bytes.AsMemory(offset, count), offset, bytes.Length, decision.ContentSha256,
                            metadata.SafeFileName, metadata.MediaType), deadline.Token);
                        offset += count;
                        return true;
                    }, ct);
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

    private async Task<T> WithCurrentDownloadAsync<T>(FileContentStoreV1 store, ActFileDecisionV1 signed,
        ActFileGuardStampV1 expected, Func<FileContentSnapshotV1, Task<T>> use, CancellationToken ct,
        bool holdForIntegrity = false)
    {
        var tenant = signed.Identity.TenantId;
        var source = await HoldSourceAsync(tenant, ct, write: holdForIntegrity);
        if (source != expected) throw Denied();
        return await guard.HoldAsync(tenant, source, async _ =>
        {
            var (file, decision) = await DecideAsync(signed.Identity, store, ct);
            if (decision != signed) throw Denied();
            return await use(file);
        }, ct);
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
