using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using Microsoft.Extensions.Options;
using static CP6.Core.Services.Wms.Transfer.TransferEnvironmentStoreV1;

namespace CP6.Core.Services.Wms.Transfer;

public sealed class TransferEnvironmentOwnerV1(CP6Context db, OperationsOwnerV1 owner, ScopeAuthorizationFenceV1 scope,
    IOptions<TransferEnvironmentOptionsV1> options, QualitySpecificationRuntimeV1 runtime)
{
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE");
    private TransferEnvironmentStoreV1 Store() => new(db, scope, options, runtime);
    public Task<TransferEnvironmentContextV1> ContextAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.TransferEnvironment, "query", false, async (a, t) =>
        {
            var s = Store(); var deployment = await s.Fence(a, false, t); var r = await s.Read(null, t);
            return new TransferEnvironmentContextV1(db.CurrentTenantId, a.ActorId, deployment, r?.Reference.Version ?? "absent", r, [.. Owners]);
        }, ct);
    public Task<TransferEnvironmentRegistrationV1> HistoryAsync(string version, CancellationToken ct = default)
    {
        var revision = EngineeringProductValuesV1.Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.TransferEnvironment, "query", false, async (a, t) =>
        { var s = Store(); await s.Fence(a, false, t); return await s.Read(revision, t) ?? throw Error("TRANSFER_REGISTRATION_NOT_FOUND", 404); }, ct);
    }
    public Task<TransferEnvironmentResultV1> SaveAsync(TransferEnvironmentCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty || !EngineeringProductValuesV1.ValidText(input.Reason, 1000, true))
            throw Error("TRANSFER_REGISTRATION_COMMAND_INVALID", 400);
        var c = OperationsOwnerV1.Freeze(input); if (c.ExpectedVersion != "absent") EngineeringProductValuesV1.Revision(c.ExpectedVersion);
        var bytes = MainOwnerOperationJournalV1.Encode(new { Deployment = Store().Deployment(), Command = c });
        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.TransferEnvironment, "edit", true, async (a, t) =>
        {
            var s = Store(); var deployment = await s.Fence(a, true, t);
            if (original != null) return await Disclose(a, s, original, c.OperationId, bytes, t);
            var before = await s.Read(null, t);
            if (c.ExpectedVersion != (before?.Reference.Version ?? "absent")) throw Error("TRANSFER_REGISTRATION_CHANGED", 412);
            if (before == null && !c.Enabled) throw Error("TRANSFER_INITIAL_REGISTRATION_REQUIRED", 422);
            await s.Bind(deployment, t); var r = await s.Append(c, a.ActorId, before, deployment, t);
            var result = new TransferEnvironmentResultV1(c.OperationId, r.Reference, r.Original.Enabled, before?.Reference != r.Reference);
            await journal.Store(Owner, Contract, c.OperationId, a.ActorId, bytes, result, [r.Reference.Id], new { c.Reason, result }, t);
            a.AtCommit(async token => { if ((await s.Read(null, token))?.Reference != r.Reference) throw Error("TRANSFER_REGISTRATION_CHANGED", 412); });
            return result;
        }, ct, actionAtFence: async t => { original = await journal.Read(Owner, c.OperationId, t); return original == null ? "edit" : "query"; });
    }
    private static async Task<TransferEnvironmentResultV1> Disclose(OperationsAccessV1 a, TransferEnvironmentStoreV1 s,
        MainOwnerOperationJournalV1.Original o, Guid operation, byte[]? input, CancellationToken ct)
    {
        if (o.Actor != a.ActorId || o.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (o.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(o, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var r = JsonSerializer.Deserialize<TransferEnvironmentResultV1>(o.Result);
        if (r == null || r.OperationId != operation || !o.Objects.SequenceEqual(new[] { r.Reference.Id })
            || (await s.Read(EngineeringProductValuesV1.Revision(r.Reference.Version), ct))?.Reference != r.Reference)
            throw Error("ORIGINAL_OPERATION_UNAVAILABLE");
        return r;
    }
    public Task<TransferEnvironmentOperationV1> OperationAsync(Guid operation, bool close, CancellationToken ct = default)
    {
        if (operation == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.TransferEnvironment, "query", close, async (a, t) =>
        {
            var s = Store(); await s.Fence(a, close, t); var j = new MainOwnerOperationJournalV1(db); var o = await j.Read(Owner, operation, t);
            if (o != null)
            {
                if (o.Actor != a.ActorId || o.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return o.State == "CLOSED" ? new(operation, "CLOSED") : new TransferEnvironmentOperationV1(operation, "COMMITTED", await Disclose(a, s, o, operation, null, t));
            }
            if (!close) return new TransferEnvironmentOperationV1(operation, "UNKNOWN");
            var result = new TransferEnvironmentOperationV1(operation, "CLOSED");
            await j.Store(Owner, Contract, operation, a.ActorId, null, result, [], new { action = "close-uncommitted-transfer-registration" }, t); return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
