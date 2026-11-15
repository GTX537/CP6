using CP6.Core.Services.Platform;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CP6.Core.Services.Sys;

public sealed partial class IamAdministrationV1
{
    internal async Task<IamOperationV1> DepartmentWriteAsync(string kind, IamDepartmentCommandV1 command,
        Guid registeredStore, Func<CancellationToken, Task<Guid>> currentActor,
        Func<Guid, CancellationToken, Task<(IamOperationV1 Receipt, CrmDepartmentSealAdvanceV1[] Advances)>> capture,
        CancellationToken ct)
    {
        var permission = Permission(kind);
        var department = command.Id!.Value;
        var departmentKey = $"dept:{department:D}";
        var inputHash = AuthenticationPolicyServiceV1.Hash((object)command);
        var phase = await ExecuteAsync<(IamOperationV1? Original, IamCrmDepartmentSealV1? Pending)>(permission, currentActor,
            async (who, token) =>
            {
                var original = await Original(who, kind, command, token);
                if (original is not null) return (original, null);
                var tenant = db.CurrentTenantId;
                var pending = await IamCrmDepartmentSealV1.ReadAsync(db, tenant, command.OperationId, token);
                if (pending is not null)
                {
                    RequireOriginalSeal(pending, who);
                    return (null, pending);
                }

                var transaction = db.Database.CurrentTransaction!;
                const string preview = "iam_crm_department_seal";
                await transaction.CreateSavepointAsync(preview, token);
                var (receipt, advances) = await capture(who, token);
                var affected = advances.Select(advance => advance.DepartmentId).Append(department).Distinct().ToArray();
                if (await IamCrmDepartmentSealV1.ConflictsAsync(db, tenant, affected, token))
                    throw Error(409, "IAM_CRM_DEPARTMENT_SEAL_PENDING");
                if (advances.Length == 0)
                {
                    await transaction.ReleaseSavepointAsync(preview, token);
                    return (receipt, null);
                }

                await transaction.RollbackToSavepointAsync(preview, token);
                await transaction.ReleaseSavepointAsync(preview, token);
                db.ChangeTracker.Clear();
                if (await currentActor(token) != who) throw Error(401, "REAUTH_REQUIRED");
                await RequireSealDurability(token);
                await IamCrmDepartmentSealV1.InsertAsync(db, tenant, department, who, kind, command.OperationId,
                    fingerprint.Protect(inputHash), registeredStore, advances, token);
                return (null, await IamCrmDepartmentSealV1.ReadAsync(db, tenant, command.OperationId, token));
            }, ct);
        if (phase.Original is { } result) return result;

        var intent = phase.Pending ?? throw Error(503, "IAM_OPERATION_UNKNOWN");
        var tenantId = db.CurrentTenantId;
        var fence = db.InquiryFence ?? throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await using var crm = await fence.OpenSealAsync(tenantId, intent.StoreId, ct);
        var completed = await ExecuteAsync<IamOperationV1?>(permission, currentActor, async (who, token) =>
        {
            var original = await Original(who, kind, command, token);
            if (original is not null) return original;
            var pending = await RequirePending(who, token);
            if (command.ExpectedVersion != await Version(departmentKey, token)
                || command.ExpectedTreeVersion != await Version("tree", token)
                || command.ExpectedReferencesVersion != await ReferencesVersion(token))
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
            var pending = await RequirePending(who, token);
            if (!pending.SealStarted) throw Error(503, "IAM_OPERATION_UNKNOWN");

            (IamOperationV1 Receipt, CrmDepartmentSealAdvanceV1[] Advances) final;
            try { final = await capture(who, token); }
            catch (IamAdministrationExceptionV1 error) when (error.Status == 412) { throw Error(503, "IAM_OPERATION_UNKNOWN"); }
            if (!final.Advances.SequenceEqual(pending.ReadAdvances()))
                throw Error(503, "IAM_OPERATION_UNKNOWN");

            // One remote transaction makes the whole saved subtree visible to the CRM owner together.
            await AdvanceConnectedDepartments(crm, tenantId, pending.StoreId, final.Advances, token);
            await RequireSealDurability(token);
            await pending.RemoveAsync(db, tenantId, token);
            return final.Receipt;
        }, ct);

        void RequireOriginalSeal(IamCrmDepartmentSealV1 pending, Guid actor)
        {
            pending.RequireOwner(actor, kind);
            if (pending.DepartmentId != department || fingerprint.Unprotect(pending.RequestProtected) != inputHash)
                throw Error(409, "IAM_OPERATION_INPUT_CONFLICT");
        }

        async Task<IamCrmDepartmentSealV1> RequirePending(Guid actor, CancellationToken token)
        {
            var pending = await IamCrmDepartmentSealV1.ReadAsync(db, tenantId, command.OperationId, token)
                ?? throw Error(503, "IAM_OPERATION_UNKNOWN");
            RequireOriginalSeal(pending, actor);
            if (pending.StoreId != intent.StoreId || pending.SourceSeals != intent.SourceSeals)
                throw Error(503, "IAM_OPERATION_UNKNOWN");
            return pending;
        }
    }

    private static async Task AdvanceConnectedDepartments(NpgsqlConnection crm, Guid tenant, Guid store,
        CrmDepartmentSealAdvanceV1[] advances, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var token = deadline.Token;
        try
        {
            await using var transaction = await crm.BeginTransactionAsync(token);
            await using var durability = new NpgsqlCommand("SET LOCAL synchronous_commit='on'", crm, transaction);
            await durability.ExecuteNonQueryAsync(token);
            await using var command = new NpgsqlCommand(
                "SELECT crm_inquiry_tenant_pause_v1.advance_department_v1(@tenant,@store,@department,@version)", crm, transaction);
            command.Parameters.AddWithValue("tenant", tenant);
            command.Parameters.AddWithValue("store", store);
            var department = command.Parameters.Add("department", NpgsqlDbType.Uuid);
            var version = command.Parameters.Add("version", NpgsqlDbType.Integer);
            foreach (var advance in advances)
            {
                department.Value = advance.DepartmentId;
                version.Value = advance.Version;
                if (await command.ExecuteScalarAsync(token) is not int recorded || recorded != advance.Version)
                    throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503);
            }
            await transaction.CommitAsync(token);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException
            || error is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new TenantManagementExceptionV1("AUTHORITY_FENCE_UNAVAILABLE", 503, error);
        }
    }
}
