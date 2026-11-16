using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Core.Services.Sys;

public sealed partial class IamAdministrationV1
{
    internal async Task<LegacyMenuMutationResultV1> WriteLegacyMenuAsync(string permission, string kind, int[] menuIds,
        string? menuKey, object input, string? impersonator, Func<CancellationToken, Task<Guid>> currentActor,
        Func<CancellationToken, Task<LegacyMenuMutationResultV1>> write, CancellationToken ct)
    {
        var requestHash = AuthenticationPolicyServiceV1.Hash(new { Kind = kind, MenuIds = menuIds, Input = input, Impersonator = impersonator });
        var includesMappings = kind == "menu-delete";
        var phase = await ExecuteAsync(permission, currentActor, PrepareAsync, ct);
        if (phase.Result is { } immediate) return immediate;

        var accepted = phase.Operation!;
        var expected = phase.Plan!;
        var fence = db.InquiryFence ?? throw Error(503, "AUTHORITY_FENCE_UNAVAILABLE");
        var groups = expected.GroupBy(entry => (entry.TenantId, entry.StoreId)).ToArray();
        var connections = new List<NpgsqlConnection>(groups.Length);
        try
        {
            // No remote write can start while even one of the original stores is unavailable.
            foreach (var group in groups)
                connections.Add(await fence.OpenSealAsync(group.Key.TenantId, group.Key.StoreId, ct));

            var alreadyCompleted = await ExecuteAsync(permission, currentActor, MarkAttemptAsync, ct);
            if (alreadyCompleted is not null) return alreadyCompleted;
            return await ExecuteAsync(permission, currentActor, CompleteAsync, ct);
        }
        finally
        {
            foreach (var connection in connections)
                await connection.DisposeAsync();
        }

        async Task<MenuWritePhase> PrepareAsync(Guid actor, CancellationToken token)
        {
            var tenant = db.CurrentTenantId;
            var pending = await IamLegacyMenuSealV1.ReadPendingAsync(db, menuIds, menuKey, token);
            if (pending is not null)
            {
                RequireOriginal(pending, tenant, actor);
                await RequireSourceAsync(pending, token);
                var plan = await pending.PlanAsync(db, token);
                if (plan.Length == 0) throw Error(503, "IAM_OPERATION_UNKNOWN");
                return new(null, pending, plan);
            }

            var postimage = await IamLegacyMenuSealV1.TargetHashAsync(db, menuIds, includeDisplay: true, includesMappings, token);
            var original = await IamLegacyMenuSealV1.ReadOriginalAsync(db, tenant, actor, kind, requestHash, postimage, token);
            if (original is not null) return new(original.ReadResult(), null, null);

            var source = await IamLegacyMenuSealV1.TargetHashAsync(db, menuIds, includeDisplay: false, includesMappings, token);
            var (result, advances) = await CaptureMenuWriteAsync(write, rollback: true, keepWithoutSeals: true, token);
            await RequireCurrentActorAsync(actor, token);
            if (advances.Length == 0) return new(result, null, null);

            var operation = new IamLegacyMenuSealV1
            {
                OperationId = Guid.NewGuid(), ActorTenant = tenant, ActorId = actor,
                Kind = kind, RequestHash = requestHash, SourceHash = source,
            };
            await RequireSealDurability(token);
            await operation.InsertAsync(db, menuIds, menuKey, advances, fingerprint.Protect(requestHash), token);
            return new(null, operation, advances);
        }

        async Task<LegacyMenuMutationResultV1?> MarkAttemptAsync(Guid actor, CancellationToken token)
        {
            var operation = await CurrentOperationAsync(actor, token);
            if (operation.ResultJson is not null) return operation.ReadResult();

            await RequireSourceAsync(operation, token);
            var (_, advances) = await CaptureMenuWriteAsync(write, rollback: true, keepWithoutSeals: false, token);
            RequirePlan(operation, advances);
            await RequireCurrentActorAsync(actor, token);
            if (!operation.SealStarted)
            {
                await RequireSealDurability(token);
                await operation.MarkStartedAsync(db, token);
            }

            return null;
        }

        async Task<LegacyMenuMutationResultV1> CompleteAsync(Guid actor, CancellationToken token)
        {
            var operation = await CurrentOperationAsync(actor, token);
            if (operation.ResultJson is not null) return operation.ReadResult();
            if (!operation.SealStarted) throw Error(503, "IAM_OPERATION_UNKNOWN");

            // Complete all native local work and verify the entire fanout before touching any CRM store.
            await RequireSourceAsync(operation, token);
            var (result, advances) = await CaptureMenuWriteAsync(write, rollback: false, keepWithoutSeals: false, token);
            RequirePlan(operation, advances);
            await RequireCurrentActorAsync(actor, token);

            for (var index = 0; index < groups.Length; index++)
            {
                var group = groups[index];
                await CrmInquiryTenantPauseV1.AdvanceConnectedRolesAsync(connections[index], group.Key.TenantId, group.Key.StoreId,
                    group.Select(entry => (entry.Aggregate, entry.Version.SuccessorVersion)).ToArray(), token);
            }

            await RequireCurrentActorAsync(actor, token);
            await RequireSealDurability(token);
            Audit(actor, operation.OperationId, kind, "Global menu permission source changed", null);
            await db.SaveChangesAsync(token);
            var postimage = await IamLegacyMenuSealV1.TargetHashAsync(db, menuIds, includeDisplay: true, includesMappings, token);
            await operation.CompleteAsync(db, postimage, result, token);
            return result;
        }

        async Task<IamLegacyMenuSealV1> CurrentOperationAsync(Guid actor, CancellationToken token)
        {
            var operation = await IamLegacyMenuSealV1.ReadAsync(db, accepted.OperationId, token);
            RequireOriginal(operation, db.CurrentTenantId, actor);
            return operation;
        }

        void RequireOriginal(IamLegacyMenuSealV1 operation, Guid tenant, Guid actor)
        {
            if (operation.ActorTenant != tenant || operation.ActorId != actor
                || operation.Kind != kind || operation.RequestHash != requestHash)
                throw Error(409, "IAM_CRM_MENU_SEAL_PENDING");
        }

        async Task RequireSourceAsync(IamLegacyMenuSealV1 operation, CancellationToken token)
        {
            var source = await IamLegacyMenuSealV1.TargetHashAsync(db, menuIds, includeDisplay: false, includesMappings, token);
            if (source != operation.SourceHash) throw SourceChanged(operation);
        }

        void RequirePlan(IamLegacyMenuSealV1 operation, CrmMenuRoleAdvanceV1[] found)
        {
            if (!found.SequenceEqual(expected)) throw SourceChanged(operation);
        }

        static IamAdministrationExceptionV1 SourceChanged(IamLegacyMenuSealV1 operation)
            => operation.SealStarted ? Error(503, "IAM_OPERATION_UNKNOWN") : Error(412, "IAM_REFERENCES_CHANGED");

        async Task RequireCurrentActorAsync(Guid actor, CancellationToken token)
        {
            if (await currentActor(token) != actor) throw Error(401, "REAUTH_REQUIRED");
        }
    }

    private async Task<(LegacyMenuMutationResultV1 Result, CrmMenuRoleAdvanceV1[] Plan)> CaptureMenuWriteAsync(
        Func<CancellationToken, Task<LegacyMenuMutationResultV1>> write, bool rollback, bool keepWithoutSeals, CancellationToken ct)
    {
        const string preview = "iam_legacy_menu_fanout";
        var transaction = db.Database.CurrentTransaction!;
        if (rollback) await transaction.CreateSavepointAsync(preview, ct);

        var advances = new List<CrmMenuRoleAdvanceV1>();
        db.WriteCrmRoleSealAsync = (tenant, aggregate, version, _, _) =>
        {
            if (!IdentityEventContracts.TryRoleSubject(aggregate["permission:".Length..], out var roleId)
                || db.InquiryFence?.RegisteredStore(tenant) is not { } store)
                throw Error(503, "IAM_IDENTITY_CONTRACT_UNAVAILABLE");
            advances.Add(new(tenant, roleId, store, version));
            return Task.CompletedTask;
        };

        try
        {
            var result = await write(ct);
            if (rollback)
            {
                if (!keepWithoutSeals || advances.Count > 0)
                {
                    await transaction.RollbackToSavepointAsync(preview, ct);
                    db.ChangeTracker.Clear();
                }

                await transaction.ReleaseSavepointAsync(preview, ct);
            }

            return (result, advances.ToArray());
        }
        finally { db.WriteCrmRoleSealAsync = null; }
    }

    private sealed record MenuWritePhase(LegacyMenuMutationResultV1? Result, IamLegacyMenuSealV1? Operation, CrmMenuRoleAdvanceV1[]? Plan);
}
