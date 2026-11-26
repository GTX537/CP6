using System.Data;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CP6.Core.Services.Pub.ActFileReferenceV1;

namespace CP6.Core.Services.Pub;

public sealed partial class ActFileReferenceOwnerV1(CP6Context db, IFileStore files, ActFileGuardReadV1 guard, ActFileSignerV1 signer)
{
    public Task<ActFileAttestationV1> ProveAsync(ActFileCallerV1 caller, ActFileProofRequestV1 request, CancellationToken ct)
    {
        var identity = Validate(request, caller.TenantId);
        var inputDigest = FileContentBytesV1.Digest(JsonSerializer.SerializeToUtf8Bytes(request, Wire));
        return TransactionAsync(caller, identity, async expiresAt =>
        {
            // 同一键串行恢复原证明。旧证明仍须由 CRM 用当前实际持有的代次核对。
            var key = $"{Contract}/{caller.TenantId:D}/{identity.SubjectId:D}/{identity.OperationKind}/{identity.OperationKey:D}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key},0))", ct);
            var previous = await db.Database.SqlQuery<Receipt>($"""
                SELECT client_id AS "ClientId",input_digest AS "InputDigest",proof AS "Proof"
                FROM cp6_act_file_v1.proof_receipts WHERE tenant_id={caller.TenantId} AND actor_id={identity.SubjectId}
                  AND operation_kind={identity.OperationKind} AND operation_id={identity.OperationKey}
                """).SingleOrDefaultAsync(ct);
            if (previous is not null)
            {
                if (previous.ClientId != caller.ClientId) throw Denied();
                if (previous.InputDigest != inputDigest)
                    throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
                if (await NowAsync(ct) >= expiresAt) throw Denied();
                return JsonSerializer.Deserialize<ActFileAttestationV1>(previous.Proof, Wire) ?? throw Unproven();
            }

            var store = await HoldFileStoreAsync(ct);
            var source = await HoldSourceAsync(caller.TenantId, ct);
            return await guard.HoldAsync(caller.TenantId, source, async held =>
            {
                var decisions = new List<ActFileDecisionV1>(request.References.Length);
                foreach (var reference in request.References)
                {
                    var current = await store.Read(reference.AttachmentId, null, ct);
                    decisions.Add(await DecideAsync(reference, current, ct));
                }
                if (await NowAsync(ct) >= expiresAt) throw Denied();
                var proof = signer.Sign(new(Contract, ActFileReferenceV1.Authority, request.Mode, signer.KeyId,
                    Guid.NewGuid(), caller.ClientId, held, decisions.ToArray()));
                var encoded = JsonSerializer.SerializeToUtf8Bytes(proof, Wire);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO cp6_act_file_v1.proof_receipts
                      (tenant_id,actor_id,client_id,operation_kind,operation_id,input_digest,proof)
                    VALUES({caller.TenantId},{identity.SubjectId},{caller.ClientId},{identity.OperationKind},
                      {identity.OperationKey},{inputDigest},{encoded})
                    """, ct);
                return proof;
            }, ct);
        }, ct);
    }

    private async Task<T> TransactionAsync<T>(ActFileCallerV1 caller, ActFileIdentityV1 identity,
        Func<DateTimeOffset, Task<T>> execute, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is not null ||
            System.Transactions.Transaction.Current is not null || db.ChangeTracker.HasChanges())
            throw Unproven();
        if (caller.TenantId == Guid.Empty || db.CurrentTenantId != caller.TenantId || caller.TenantId != identity.TenantId)
            throw Denied();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='8s'; SET LOCAL statement_timeout='15s'", ct);
            var expiry = await HoldCallerAsync(caller, identity.SubjectId, ct);
            var result = await execute(expiry);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            throw Unproven(error);
        }
    }

    private async Task<DateTimeOffset> HoldCallerAsync(ActFileCallerV1 caller, Guid subject, CancellationToken ct)
    {
        var until = await db.Database.SqlQuery<DateTimeOffset?>($"""
            SELECT least(s."ExpiresAtUtc",{caller.ExpiresAt},t."ExpireDate") AS "Value"
            FROM crm_identity."ServiceToken" s
            JOIN public."Sys_Tenants" t ON t."Id"=s."TenantId"
            JOIN public."Sys_Users" u ON u."TenantId"=s."TenantId" AND u."Id"={subject}
            WHERE s."TenantId"={caller.TenantId} AND s."Issuer"={caller.Issuer} AND s."ClientId"={caller.ClientId}
              AND s."Jti"={caller.TokenId} AND s."RevokedAtUtc" IS NULL
              AND s."ExpiresAtUtc">clock_timestamp() AND {caller.ExpiresAt}>clock_timestamp()
              AND t."Enable" AND (t."ExpireDate" IS NULL OR t."ExpireDate">clock_timestamp())
              AND u."Enable" AND (u."LockedUntil" IS NULL OR u."LockedUntil"<=clock_timestamp())
            FOR SHARE OF s,t,u
            """).SingleOrDefaultAsync(ct);
        return until ?? throw Denied();
    }

    private Task<DateTimeOffset> NowAsync(CancellationToken ct) =>
        db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);

    private async Task<FileContentStoreV1> HoldFileStoreAsync(CancellationToken ct)
    {
        var store = new FileContentStoreV1(db);
        await store.Fence(false, ct);
        return store;
    }

    private async Task<ActFileGuardStampV1> HoldSourceAsync(Guid tenant, CancellationToken ct)
    {
        var source = await db.Database.SqlQuery<Source>($"""
            SELECT store_id AS "StoreId",epoch AS "Epoch",receipt AS "Receipt" FROM cp6_act_file_v1.hold_source_v1({tenant})
            """).SingleOrDefaultAsync(ct) ?? throw Unproven();
        return new(source.StoreId, source.Epoch, source.Receipt);
    }

    private async Task<ActFileDecisionV1> DecideAsync(ActFileIdentityV1 identity, FileContentSnapshotV1 file, CancellationToken ct)
    {
        if (file.TenantId != identity.TenantId || file.ContentRef.Id != identity.AttachmentId ||
            file.ContentRef.Version != identity.Version || file.ContentRef.Digest != identity.ContentSha256)
            throw Denied();
        var grant = await db.Database.SqlQuery<Grant>($"""
            SELECT can_add AS "CanAdd",can_retain AS "CanRetain",can_unlink AS "CanUnlink",
              can_download AS "CanDownload",policy_version AS "PolicyVersion",retention_ref AS "RetentionRef"
            FROM cp6_act_file_v1.grants WHERE tenant_id={identity.TenantId} AND actor_id={identity.SubjectId}
              AND file_id={identity.AttachmentId} AND purpose={identity.Purpose}
            """).SingleOrDefaultAsync(ct) ?? throw Denied();
        var allowed = identity.Action switch
        {
            "ADD" => grant.CanAdd && file.Availability == "AVAILABLE",
            "RETAIN" => grant.CanRetain && grant.RetentionRef is not null,
            "REMOVE" => grant.CanUnlink && !file.Held && !await HasRetentionHoldAsync(identity, ct),
            "DOWNLOAD" => grant.CanDownload && file.Availability == "AVAILABLE",
            _ => false
        };
        if (!allowed) throw Denied();
        var scan = file.Inspection.Status switch
        {
            "CLEAN" => "CLEAN",
            "PENDING" => "PENDING",
            _ => "BLOCKED"
        };
        return new(identity, "ALLOW", scan, file.StateVersion, file.OriginalDigest, grant.PolicyVersion, grant.RetentionRef);
    }

    private Task<bool> HasRetentionHoldAsync(ActFileIdentityV1 identity, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT (
              EXISTS(SELECT FROM (
                SELECT DISTINCT ON (kind,reference_id) active FROM cp6_audit_v1.safeguards
                 WHERE tenant_id={identity.TenantId} AND retention_class='TECHNICAL_CONTENT_EVIDENCE'
                   AND native_id={identity.AttachmentId.ToString("D")}
                 ORDER BY kind,reference_id,sequence DESC
              ) safeguards WHERE active)
              OR EXISTS(SELECT FROM (
                SELECT DISTINCT ON (environment_id,subject_id) active FROM cp6_privacy_disposition_v1.holds
                 WHERE target_tenant={identity.TenantId} ORDER BY environment_id,subject_id,sequence DESC
              ) holds WHERE active)
            ) AS "Value"
            """).SingleAsync(ct);

    private sealed record Receipt(string ClientId, string InputDigest, byte[] Proof);
    private sealed record Source(Guid StoreId, long Epoch, Guid Receipt);
    private sealed record Grant(bool CanAdd, bool CanRetain, bool CanUnlink, bool CanDownload, Guid PolicyVersion, Guid? RetentionRef);
}
