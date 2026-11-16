using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Platform;

public sealed partial class PrivacyDispositionOwnerV1
{
    private const string DispositionSealKind = "privacy-disposition";

    private sealed record DispositionSealInput(string RequestDigest, Guid Epoch, DateTime PasswordChangedAt);
    private sealed record DispositionPendingSeal(Guid Tenant, IamCrmUserSealV1 Record);

    private async Task<PrivacyDispositionOperationV1> ExecuteDispositionAsync(Guid id, PrivacyDispositionExecuteV1 input,
        byte[] bytes, TenantManagementSessionV1 session, CancellationToken ct)
    {
        var digest = MainOwnerOperationJournalV1.Digest(bytes);
        var phase = await Transaction<(PrivacyDispositionOperationV1? Result, DispositionPendingSeal? Pending)>(true,
            session, async (a, token) =>
            {
                Can(a, "read");
                var original = await Original(a, input.OperationId, bytes, token);
                if (original is not null)
                    return (original, null);
                Can(a, "execute");

                var pending = await FindDispositionSealAsync(a, input.OperationId, token);
                if (pending is not null)
                {
                    ReadDispositionSealInput(pending, digest);
                    await RequirePendingApprovalAsync(a, id, input, pending, token);
                    return (null, pending);
                }

                var plan = await RequireExecutionApprovalAsync(a, id, input, token);
                var scope = plan.Scope;
                var tenant = scope.TargetTenantId;
                if (db.InquiryFence?.RegisteredStore(tenant) is not { } store)
                {
                    var version = await PrivacyIdentityEffectV1.ApplyAsync(db, hasher, tenant, scope.SubjectId, a.Actor.Id, token);
                    return (await StoreExecutionAsync(a, plan, input, bytes, version, token), null);
                }

                if (await IamCrmUserSealV1.ExistsAsync(db, tenant, scope.SubjectId, token))
                    throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_SUBJECT_SEAL_PENDING");

                var passwordChangedAt = await db.Database.SqlQueryRaw<DateTime>("SELECT LOCALTIMESTAMP AS \"Value\"").SingleAsync(token);
                var accepted = new DispositionSealInput(digest, Guid.NewGuid(), passwordChangedAt);
                var transaction = db.Database.CurrentTransaction!;
                const string preview = "privacy_disposition_crm_seal";
                await transaction.CreateSavepointAsync(preview, token);
                var captured = await CaptureDispositionAsync(a, scope, accepted, token);
                if (captured.Advances.Length == 0)
                {
                    await transaction.ReleaseSavepointAsync(preview, token);
                    return (await StoreExecutionAsync(a, plan, input, bytes, captured.Version, token), null);
                }

                await transaction.RollbackToSavepointAsync(preview, token);
                await transaction.ReleaseSavepointAsync(preview, token);
                db.ChangeTracker.Clear();
                await RequireDispositionSealDurabilityAsync(token);
                var sealOperation = DispositionSealOperation(a, input.OperationId);
                // The private row binds the request digest and generated source values. It holds no profile fields or credentials.
                await IamCrmUserSealV1.InsertAsync(db, tenant, scope.SubjectId, a.Actor.Id, DispositionSealKind, sealOperation,
                    JsonSerializer.Serialize(accepted), store, captured.Advances, token);
                var saved = await IamCrmUserSealV1.ReadAsync(db, tenant, sealOperation, token) ?? throw DispositionSealUnknown();
                return (null, new DispositionPendingSeal(tenant, saved));
            }, ct);
        if (phase.Result is { } completed)
            return completed;

        var intent = phase.Pending!;
        var fence = db.InquiryFence ?? throw new PrivacyDispositionExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503);
        try
        {
            // The first connection can fail without any CRM request. Keep the accepted operation outside that outage.
            await using var crm = await fence.OpenSealAsync(intent.Tenant, intent.Record.StoreId, ct);
            var terminal = await Transaction(true, session, async (a, token) =>
            {
                Can(a, "read");
                var original = await Original(a, input.OperationId, bytes, token);
                if (original is not null)
                    return original;
                Can(a, "execute");
                var pending = await ReloadDispositionSealAsync(intent, token);
                await RequirePendingApprovalAsync(a, id, input, pending, token);
                if (!pending.Record.SealStarted)
                {
                    await RequireDispositionSealDurabilityAsync(token);
                    await pending.Record.MarkStartedAsync(db, pending.Tenant, token);
                }
                return null;
            }, ct);
            if (terminal is not null)
                return terminal;

            return await Transaction(true, session, async (a, token) =>
            {
                Can(a, "read");
                var original = await Original(a, input.OperationId, bytes, token);
                if (original is not null)
                    return original;
                Can(a, "execute");
                var pending = await ReloadDispositionSealAsync(intent, token);
                if (!pending.Record.SealStarted)
                    throw DispositionSealUnknown();
                var plan = await RequirePendingApprovalAsync(a, id, input, pending, token);
                var accepted = ReadDispositionSealInput(pending, digest);
                var current = await CaptureDispositionAsync(a, plan.Scope, accepted, token);
                RequireDispositionAdvances(pending.Record, current.Advances);

                await RequireDispositionSealDurabilityAsync(token);
                var receipt = await StoreExecutionAsync(a, plan, input, bytes, current.Version, token);
                await pending.Record.RemoveAsync(db, pending.Tenant, token);
                // Check the originating session before sending. The transaction also checks it at local completion.
                await session.Complete(token);
                try
                {
                    await CrmInquiryTenantPauseV1.AdvanceConnectedIdentitiesAsync(crm, pending.Tenant, pending.Record.StoreId,
                        current.Advances.Select(advance => (advance.Aggregate, advance.Version)).ToArray(), token);
                }
                catch (TenantManagementExceptionV1 error)
                {
                    throw DispositionSealUnknown(error);
                }
                return receipt;
            }, ct);
        }
        catch (TenantManagementExceptionV1 error)
        {
            throw new PrivacyDispositionExceptionV1(error.Message, error.Status, error);
        }
    }

    private async Task<DispositionPendingSeal?> FindDispositionSealAsync(Authority a, Guid operation, CancellationToken ct)
    {
        var key = DispositionSealOperation(a, operation);
        // The caller's journal is actor-scoped. The pending row also blocks signing for the target's tenant.
        var tenant = await db.Database.SqlQuery<Guid?>($"""
            SELECT tenant_id AS "Value" FROM cp6_authentication_v1.pending_crm_user_seals
            WHERE operation_id={key} AND kind={DispositionSealKind} AND actor_id={a.Actor.Id}
            """).SingleOrDefaultAsync(ct);
        if (tenant is not { } target)
            return null;
        var pending = await IamCrmUserSealV1.ReadAsync(db, target, key, ct) ?? throw DispositionSealUnknown();
        return new DispositionPendingSeal(target, pending);
    }

    private Guid DispositionSealOperation(Authority a, Guid operation)
    {
        var key = $"{Journal(a)}/{a.Actor.TenantId:D}/{operation:D}/crm-user-seal";
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
    }

    private async Task<DispositionPendingSeal> ReloadDispositionSealAsync(DispositionPendingSeal intent, CancellationToken ct)
    {
        var saved = intent.Record;
        var current = await IamCrmUserSealV1.ReadAsync(db, intent.Tenant, saved.OperationId, ct);
        if (current is null || current.Kind != DispositionSealKind || current.ActorId != saved.ActorId ||
            current.UserId != saved.UserId || current.StoreId != saved.StoreId ||
            current.RequestProtected != saved.RequestProtected || current.SourceSeals != saved.SourceSeals)
            throw DispositionSealUnknown();
        return new DispositionPendingSeal(intent.Tenant, current);
    }

    private static DispositionSealInput ReadDispositionSealInput(DispositionPendingSeal pending, string digest)
    {
        var saved = JsonSerializer.Deserialize<DispositionSealInput>(pending.Record.RequestProtected) ?? throw DispositionSealUnknown();
        if (!StringComparer.Ordinal.Equals(saved.RequestDigest, digest))
            throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_OPERATION_CONFLICT");
        return saved;
    }

    private async Task<PrivacyDispositionPlanV1> RequirePendingApprovalAsync(Authority a, Guid planId, PrivacyDispositionExecuteV1 input,
        DispositionPendingSeal pending, CancellationToken ct)
    {
        try
        {
            var plan = await RequireExecutionApprovalAsync(a, planId, input, ct);
            if ((plan.Scope.TargetTenantId, plan.Scope.SubjectId) != (pending.Tenant, pending.Record.UserId))
                throw DispositionSealUnknown();
            return plan;
        }
        catch (PrivacyDispositionExceptionV1 error) when (pending.Record.SealStarted && error.Status is 404 or 409 or 412)
        {
            throw DispositionSealUnknown(error);
        }
    }

    private async Task<(Guid Version, CrmUserSealAdvanceV1[] Advances)> CaptureDispositionAsync(Authority a,
        PrivacyDispositionScopeV1 scope, DispositionSealInput input, CancellationToken ct)
    {
        var advances = new List<CrmUserSealAdvanceV1>();
        db.CaptureCrmUserSeal = (tenant, subject, advance) =>
        {
            if ((tenant, subject) != (scope.TargetTenantId, scope.SubjectId))
                throw new PrivacyDispositionExceptionV1("PRIVACY_DISPOSITION_IDENTITY_WRITER_UNAVAILABLE", 503);
            advances.Add(advance);
        };
        Guid version;
        try
        {
            version = await PrivacyIdentityEffectV1.ApplyAsync(db, hasher, scope.TargetTenantId, scope.SubjectId, a.Actor.Id,
                input.Epoch, input.PasswordChangedAt, ct);
        }
        finally { db.CaptureCrmUserSeal = null; }

        var payloads = db.CrmIdentitySnapshots.Local.Where(row => row.TenantId == scope.TargetTenantId)
            .ToDictionary(row => row.AggregateId, row => row.PayloadSha256, StringComparer.Ordinal);
        return (version, advances.Select(advance => advance with { IntentHash = payloads[advance.Aggregate] }).ToArray());
    }

    private static void RequireDispositionAdvances(IamCrmUserSealV1 pending, CrmUserSealAdvanceV1[] advances)
    {
        var accepted = pending.ReadAdvances().ToDictionary(advance => advance.Aggregate, StringComparer.Ordinal);
        var user = $"user:{pending.UserId:D}";
        // A separate legitimate session retirement or issuance can change the currently affected grant set.
        // The approved user edit stays fixed, and every current grant is covered by the final batch.
        if (advances.SingleOrDefault(advance => advance.Aggregate == user) != accepted.GetValueOrDefault(user) ||
            advances.Any(advance => accepted.TryGetValue(advance.Aggregate, out var original) && original != advance))
            throw DispositionSealUnknown();
    }

    private Task RequireDispositionSealDurabilityAsync(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        SELECT set_config('synchronous_commit','on',true)
        WHERE current_setting('synchronous_commit') IN ('off','local')
        """, ct);

    private static PrivacyDispositionExceptionV1 DispositionSealUnknown(Exception? error = null) =>
        new("PRIVACY_DISPOSITION_OUTCOME_UNKNOWN", 503, error);
}
