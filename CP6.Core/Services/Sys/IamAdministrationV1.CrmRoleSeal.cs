using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed partial class IamAdministrationV1
{
    internal async Task<IamOperationV1> RoleWriteAsync(string permission, int roleId, string kind, IamCommandV1 command,
        Func<CancellationToken, Task<Guid>> currentActor, Func<Guid, CancellationToken, Task<IamOperationV1>> write,
        CancellationToken ct, Func<CancellationToken, Task>? requireCatalog = null)
    {
        var inputHash = AuthenticationPolicyServiceV1.Hash((object)command);
        var aggregate = "permission:" + IdentityEventContracts.RoleSubject(roleId);
        var phase = await ExecuteAsync<(IamOperationV1? Original, IamCrmRoleSealV1? Pending)>(permission, currentActor, async (who, token) =>
        {
            var original = await Original(who, kind, command, token);
            if (original is not null) return (original, null);
            var tenant = db.CurrentTenantId;
            var pending = await IamCrmRoleSealV1.ReadAsync(db, tenant, command.OperationId, token, roleId);
            if (pending is not null) return (null, RequireOriginalSeal(pending, who));
            var store = db.InquiryFence?.RegisteredStore(tenant);
            if (store is null) return (await write(who, token), null);

            // Run the original validators and its native C02 projection. The role-seal callback
            // stops before opening CRM, then this outer savepoint removes all tentative writes.
            var tx = db.Database.CurrentTransaction!;
            const string preview = "iam_crm_role_seal";
            await tx.CreateSavepointAsync(preview, token);
            db.WriteCrmRoleSealAsync = (sourceTenant, sourceAggregate, version, scopes, _) =>
            {
                RequireSealSource(tenant, sourceTenant, sourceAggregate);
                throw new CrmRoleSealRequiredV1(version, scopes);
            };
            try
            {
                var result = await write(who, token);
                await tx.ReleaseSavepointAsync(preview, token);
                return (result, null);
            }
            catch (CrmRoleSealRequiredV1 required)
            {
                await tx.RollbackToSavepointAsync(preview, token);
                await tx.ReleaseSavepointAsync(preview, token);
                db.ChangeTracker.Clear();
                if (await currentActor(token) != who) throw Error(401, "REAUTH_REQUIRED");
                await RequireSealDurability(token);
                await IamCrmRoleSealV1.InsertAsync(db, tenant, roleId, who, kind, command, fingerprint.Protect(inputHash),
                    store.Value, required, token);
                return (null, await IamCrmRoleSealV1.ReadAsync(db, tenant, command.OperationId, token));
            }
            finally { db.WriteCrmRoleSealAsync = null; }
        }, ct);
        if (phase.Original is { } result) return result;

        var intent = phase.Pending ?? throw Error(503, "IAM_OPERATION_UNKNOWN");
        var tenantId = db.CurrentTenantId;
        var fence = db.InquiryFence ?? throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503);
        // A failed first login cannot mark this request as possibly sealed and holds no Main root.
        await using var connection = await fence.OpenSealAsync(tenantId, intent.StoreId, ct);
        var completed = await ExecuteAsync<IamOperationV1?>(permission, currentActor, async (who, token) =>
        {
            var original = await Original(who, kind, command, token);
            if (original is not null) return original;
            var pending = await RequirePendingAsync(who, token);
            if (command.ExpectedVersion != await Version(IdentityEventContracts.RoleSubject(roleId), token)
                || command.ExpectedReferencesVersion != await ReferencesVersion(token))
                throw pending.SealStarted ? Error(503, "IAM_OPERATION_UNKNOWN") : Error(412, "IAM_REFERENCES_CHANGED");
            if (!pending.SealStarted)
            {
                if (requireCatalog is not null) await requireCatalog(token);
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
            var sealedRole = false;
            db.WriteCrmRoleSealAsync = async (sourceTenant, sourceAggregate, version, _, cancel) =>
            {
                RequireSealSource(tenantId, sourceTenant, sourceAggregate);
                if (sealedRole || version != pending.Version) throw Error(412, "IAM_REFERENCES_CHANGED");
                await CrmInquiryTenantPauseV1.AdvanceConnectedRoleAsync(connection, tenantId, pending.StoreId,
                    aggregate, version.SuccessorVersion, cancel);
                sealedRole = true;
            };
            try
            {
                var receipt = await write(who, token);
                if (!sealedRole) throw Error(412, "IAM_REFERENCES_CHANGED");
                if (await currentActor(token) != who) throw Error(401, "REAUTH_REQUIRED");
                await RequireSealDurability(token);
                await pending.RemoveAsync(db, tenantId, token);
                return receipt;
            }
            catch (IamAdministrationExceptionV1 error) when (error.Status == 412)
            {
                throw Error(503, "IAM_OPERATION_UNKNOWN");
            }
            finally { db.WriteCrmRoleSealAsync = null; }
        }, ct);

        IamCrmRoleSealV1 RequireOriginalSeal(IamCrmRoleSealV1 pending, Guid actor)
        {
            if (pending.OperationId != command.OperationId) throw Error(409, "IAM_CRM_ROLE_SEAL_PENDING");
            pending.RequireOwner(actor, kind);
            if (pending.RoleId != roleId || fingerprint.Unprotect(pending.RequestProtected) != inputHash)
                throw Error(409, "IAM_OPERATION_INPUT_CONFLICT");
            return pending;
        }

        async Task<IamCrmRoleSealV1> RequirePendingAsync(Guid actor, CancellationToken token)
        {
            var pending = await IamCrmRoleSealV1.ReadAsync(db, tenantId, command.OperationId, token)
                ?? throw Error(503, "IAM_OPERATION_UNKNOWN");
            RequireOriginalSeal(pending, actor);
            if (pending.StoreId != intent.StoreId || pending.Version != intent.Version)
                throw Error(503, "IAM_OPERATION_UNKNOWN");
            return pending;
        }

        void RequireSealSource(Guid tenant, Guid sourceTenant, string sourceAggregate)
        {
            if (tenant != sourceTenant || aggregate != sourceAggregate) throw Error(503, "IAM_IDENTITY_CONTRACT_UNAVAILABLE");
        }
    }

    private Task RequireSealDurability(CancellationToken ct) => RequireSealDurabilityAsync(db, ct);

    internal static Task RequireSealDurabilityAsync(CP6Context db, CancellationToken ct) => db.Database.ExecuteSqlRawAsync("""
        SELECT set_config('synchronous_commit','on',true)
        WHERE current_setting('synchronous_commit') IN ('off','local')
        """, ct);
}
