using CP6.Core.Services.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed partial class IamAdministrationV1
{
    internal async Task<IamOperationV1> UserWriteAsync(string kind, IamUserCommandV1 command,
        Func<CancellationToken, Task<Guid>> currentActor, Func<Guid, CancellationToken, Task<IamOperationV1>> write,
        CancellationToken ct)
    {
        var permission = Permission(kind);
        var user = command.Id!.Value;
        var aggregate = $"user:{user:D}";
        var inputHash = AuthenticationPolicyServiceV1.Hash((object)command);
        var phase = await ExecuteAsync<(IamOperationV1? Original, IamCrmUserSealV1? Pending)>(permission, currentActor, async (who, token) =>
        {
            var original = await Original(who, kind, command, token);
            if (original is not null) return (original, null);
            var tenant = db.CurrentTenantId;
            var pending = await IamCrmUserSealV1.ReadAsync(db, tenant, command.OperationId, token, user);
            if (pending is not null) return (null, RequireOriginalSeal(pending, who));
            if (db.InquiryFence?.RegisteredStore(tenant) is not { } store)
                return (await write(who, token), null);

            var tx = db.Database.CurrentTransaction!;
            const string preview = "iam_crm_user_seal";
            await tx.CreateSavepointAsync(preview, token);
            var (receipt, advances) = await CaptureAsync(who, token);
            if (advances.Count == 0)
            {
                await tx.ReleaseSavepointAsync(preview, token);
                return (receipt, null);
            }

            await tx.RollbackToSavepointAsync(preview, token);
            await tx.ReleaseSavepointAsync(preview, token);
            db.ChangeTracker.Clear();
            if (await currentActor(token) != who) throw Error(401, "REAUTH_REQUIRED");
            await RequireSealDurability(token);
            await IamCrmUserSealV1.InsertAsync(db, tenant, user, who, kind, command.OperationId, fingerprint.Protect(inputHash),
                store, advances, token);
            return (null, await IamCrmUserSealV1.ReadAsync(db, tenant, command.OperationId, token));
        }, ct);
        if (phase.Original is { } result) return result;

        var intent = phase.Pending ?? throw Error(503, "IAM_OPERATION_UNKNOWN");
        var tenantId = db.CurrentTenantId;
        var fence = db.InquiryFence ?? throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await using var connection = await fence.OpenSealAsync(tenantId, intent.StoreId, ct);
        var completed = await ExecuteAsync<IamOperationV1?>(permission, currentActor, async (who, token) =>
        {
            var original = await Original(who, kind, command, token);
            if (original is not null) return original;
            var pending = await RequirePendingAsync(who, token);
            if (command.ExpectedVersion != await Version(aggregate, token) ||
                command.ExpectedReferencesVersion != await ReferencesVersion(token))
                throw pending.SealStarted ? Error(503, "IAM_OPERATION_UNKNOWN") : Error(412, "IAM_REFERENCES_CHANGED");
            if (!pending.SealStarted)
            {
                await RequireSealDurability(token);
                await pending.MarkStartedAsync(db, tenantId, token);
            }
            return null;
        }, ct);
        if (completed is not null) return completed;

        return await ExecuteAsync(permission, currentActor, async (who, token) =>
        {
            var original = await Original(who, kind, command, token);
            if (original is not null) return original;
            var pending = await RequirePendingAsync(who, token);
            if (!pending.SealStarted) throw Error(503, "IAM_OPERATION_UNKNOWN");
            try
            {
                var (receipt, advances) = await CaptureAsync(who, token);
                var originalAdvances = pending.ReadAdvances().ToDictionary(advance => advance.Aggregate, StringComparer.Ordinal);
                originalAdvances.TryGetValue(aggregate, out var originalUser);
                if (advances.SingleOrDefault(advance => advance.Aggregate == aggregate) != originalUser ||
                    advances.Any(advance => originalAdvances.TryGetValue(advance.Aggregate, out var saved) && saved != advance))
                    throw Error(412, "IAM_REFERENCES_CHANGED");

                // Grants can be created or independently retired while the original user intent is pending.
                // The native final write owns the current grants of this subject. Seal each exact transition it captured.
                foreach (var advance in advances)
                    await CrmInquiryTenantPauseV1.AdvanceConnectedIdentityAsync(connection, tenantId, pending.StoreId,
                        advance.Aggregate, advance.Version, token);
                await RequireSealDurability(token);
                await pending.RemoveAsync(db, tenantId, token);
                return receipt;
            }
            catch (IamAdministrationExceptionV1 error) when (error.Status == 412)
            {
                throw Error(503, "IAM_OPERATION_UNKNOWN");
            }
        }, ct);

        async Task<(IamOperationV1 Receipt, List<CrmUserSealAdvanceV1> Advances)> CaptureAsync(Guid actor, CancellationToken token)
        {
            var tenant = db.CurrentTenantId;
            var advances = new List<CrmUserSealAdvanceV1>();
            db.CaptureCrmUserSeal = (sourceTenant, subject, advance) =>
            {
                if (sourceTenant != tenant || subject != user) throw Error(503, "IAM_IDENTITY_CONTRACT_UNAVAILABLE");
                advances.Add(advance);
            };
            try { return (await write(actor, token), advances); }
            finally { db.CaptureCrmUserSeal = null; }
        }

        IamCrmUserSealV1 RequireOriginalSeal(IamCrmUserSealV1 pending, Guid actor)
        {
            if (pending.OperationId != command.OperationId) throw Error(409, "IAM_CRM_USER_SEAL_PENDING");
            pending.RequireOwner(actor, kind);
            if (pending.UserId != user || fingerprint.Unprotect(pending.RequestProtected) != inputHash)
                throw Error(409, "IAM_OPERATION_INPUT_CONFLICT");
            return pending;
        }

        async Task<IamCrmUserSealV1> RequirePendingAsync(Guid actor, CancellationToken token)
        {
            var pending = await IamCrmUserSealV1.ReadAsync(db, tenantId, command.OperationId, token)
                ?? throw Error(503, "IAM_OPERATION_UNKNOWN");
            RequireOriginalSeal(pending, actor);
            if (pending.StoreId != intent.StoreId || pending.SourceSeals != intent.SourceSeals)
                throw Error(503, "IAM_OPERATION_UNKNOWN");
            return pending;
        }
    }
}
