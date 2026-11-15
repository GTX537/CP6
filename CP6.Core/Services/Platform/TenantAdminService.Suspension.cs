using System.Text.Json;
using CP6.Core.Auth;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Platform;

public partial class TenantAdminService
{
    private async Task RequireNoPendingSuspension(Guid tenant, CancellationToken ct)
    {
        if (_db.Database.IsNpgsql() && await TenantPendingSuspensionV1.ExistsAsync(_db, tenant, ct))
            throw new TenantManagementExceptionV1("TENANT_SUSPENSION_PENDING");
    }

    private static void RequireSuspensionActor(TenantPendingSuspensionV1 pending, PlatformAdminAuthorization.Actor actor)
    {
        if (pending.Actor != actor.Id || pending.ActorTenant != actor.TenantId)
            throw new TenantManagementExceptionV1("TENANT_ORIGINAL_FORBIDDEN", 403);
    }

    private static void RequireSuspensionIntent(TenantPendingSuspensionV1 pending, TenantManagementCommandV1 command, byte[] input,
        PlatformAdminAuthorization.Actor actor, bool legacy)
    {
        if (pending.Legacy != legacy || !pending.OperationHash.AsSpan().SequenceEqual(SuspensionOperationHash(command.OperationRef, legacy)))
            throw new TenantManagementExceptionV1("TENANT_SUSPENSION_PENDING");
        RequireSuspensionActor(pending, actor);
        if (pending.Revision != command.ExpectedRevision || !pending.Operation.AsSpan().SequenceEqual(ManagementBytes(command.OperationRef)) ||
            !pending.Input.AsSpan().SequenceEqual(input))
            throw new TenantManagementExceptionV1("TENANT_OPERATION_CONFLICT");
    }

    private async Task StageSuspension(Guid id, TenantManagementCommandV1 command, byte[] input, PlatformAdminAuthorization.Actor actor,
        (Guid Store, int Version) boundary, bool legacy, CancellationToken ct)
    {
        if (boundary.Version == int.MaxValue || command.ExpectedRevision == long.MaxValue)
            throw new TenantManagementExceptionV1("TENANT_REVISION_EXHAUSTED", 422);
        await RequireSuspensionDurability(ct);
        await new TenantPendingSuspensionV1
        {
            Legacy = legacy, OperationHash = SuspensionOperationHash(command.OperationRef, legacy), Operation = ManagementBytes(command.OperationRef),
            Input = input, Actor = actor.Id, ActorTenant = actor.TenantId, Revision = command.ExpectedRevision,
            Store = boundary.Store, SourceVersion = boundary.Version
        }.InsertAsync(_db, id, ct);
    }

    private Task RequireSuspensionDurability(CancellationToken ct) => _db.Database.ExecuteSqlRawAsync("""
        SELECT set_config('synchronous_commit','on',true)
        WHERE current_setting('synchronous_commit') IN ('off','local')
        """, ct);

    private Task<TenantManagementReceiptV1> CompleteStagedSuspension(Guid id, TenantManagementCommandV1 command, byte[] input, bool legacy,
        TenantManagementSessionV1? session, CancellationToken ct)
        => ManagementTransaction(true, session, async (actor, token) =>
        {
            var original = await ManagementOriginal(id, command.OperationRef, token, legacy);
            if (original is not null) return ManagementReceipt(original, actor, command.OperationRef, input);
            var pending = await TenantPendingSuspensionV1.ReadAsync(_db, id, token)
                ?? throw new TenantManagementExceptionV1("TENANT_OUTCOME_UNKNOWN", 503);
            RequireSuspensionIntent(pending, command, input, actor, legacy);
            var current = await RequireManagementRevision(id, pending.Revision, token);
            if (!current.Enable) throw new TenantManagementExceptionV1("TENANT_VERSION_CHANGED", 412);
            await RequireSuspensionDurability(token);
            return await CommitManagementCommand(id, command, input, actor, pending, legacy, token);
        }, ct);

    private static string LegacyOperation((Guid Store, int Version) boundary) => FormattableString.Invariant($"{boundary.Store:D}/{boundary.Version}");

    private async Task SuspendRegisteredLegacyAsync(Guid id, CancellationToken ct)
    {
        var command = await ManagementTransaction<TenantManagementCommandV1?>(true, null, async (actor, token) =>
        {
            var current = await ManagementSnapshot(id, token) ?? throw new InvalidOperationException("E-SEC-032");
            var pending = await TenantPendingSuspensionV1.ReadAsync(_db, id, token);
            if (pending is not null)
            {
                if (!pending.Legacy) throw new TenantManagementExceptionV1("TENANT_SUSPENSION_PENDING");
                RequireSuspensionActor(pending, actor);
                return JsonSerializer.Deserialize<TenantManagementCommandV1>(pending.Input)!;
            }
            var boundary = await _crmPause!.ReadSuspensionBoundaryAsync(_db, id, token);
            if (!current.Enable)
            {
                // A legacy caller has no operation key; the disabled C02 successor identifies its predecessor.
                var previous = LegacyOperation((boundary.Store, boundary.Version - 1));
                var original = await ManagementOriginal(id, previous, token, legacy: true);
                if (original is null) await SetEnableAsync(id, false, SecurityEventType.TenantSuspended, actor, token);
                else if (original.Actor == actor.Id && original.ActorTenant == actor.TenantId)
                    _ = ManagementReceipt(original, actor, previous, null);
                return null;
            }
            var request = new TenantManagementCommandV1(LegacyOperation(boundary), current.Revision, "suspend", null);
            await StageSuspension(id, request, ManagementBytes(request), actor, boundary, legacy: true, token);
            return request;
        }, ct);
        if (command is not null)
            await CompleteStagedSuspension(id, command, ManagementBytes(command), legacy: true, null, ct);
    }
}
