using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using static CP6.Core.Services.Erp.OrderSourceFacts;

namespace CP6.Core.Services.Erp;

public sealed record LegacyOrderSourceRead(LegacyOrderSourceSnapshot Current, LegacyOrderSourceSnapshot? Original,
    long? OriginalRevision, string? OriginalDigest, string? Writer, string OriginalCoverage, string ScheduleCoverage);
public sealed record OrderSourceResultRead(string Knowledge, OrderSourceReceipt? Original, OrderSourceGate? Current);
public sealed record OrderScheduleSourceRead(OrderSourceScope Scope, string Knowledge, string? ScheduleSha256,
    string? ConfirmationSha256, long? OriginalOrderRevision, JsonElement? OriginalSchedule);

public sealed class OrderSourceReader(CP6Context db, OrderReadOwnerV1 owner)
{
    public Task<LegacyOrderSourceRead> ReadLegacyAsync(Guid orderId, long? originalRevision = null,
        CancellationToken ct = default)
    {
        if (orderId == Guid.Empty || originalRevision is <= 0) throw Error("SCHEMA_INVALID", 400);
        return Read(async (access, token) =>
        {
            var (current, stored) = await VisibleOriginal(access, orderId, originalRevision, token);
            var original = stored?.Read();
            return new LegacyOrderSourceRead(current, original, stored?.Revision, stored?.Digest, stored?.Writer,
                original is null ? "UNPROVEN" : "ORIGINAL_FOUND", "UNPROVEN");
        }, ct);
    }

    internal const string RequirementSourceOwner = "Main.Erp.SalesOrderSource.v1";

    internal async Task<byte[]> ReadRequirementOriginal(OperationsAccessV1 root, Guid orderId, long revision,
        string digest, CancellationToken ct)
    {
        if (!owner.Owns(db)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        RequireTransaction(db);
        var access = await owner.ParticipateDisclosureAsync("erp-order", "query", ct);
        if (access.SubjectId != root.ActorId || !access.Price || !access.Amount)
            throw Error("FIELD_READ_FORBIDDEN", 403);
        var (_, original) = await VisibleOriginal(access, orderId, revision, ct);
        if (original is null) throw Error("SALES_SOURCE_ORIGINAL_UNPROVEN", 503);
        if (original.Digest != digest) throw Error("REQUIREMENT_ORIGIN_CHANGED", 412);

        root.AtCommit(async token =>
        {
            var final = await owner.ParticipateDisclosureAsync("erp-order", "query", token);
            if (final.SubjectId != access.SubjectId || final.ReadScopeVersion != access.ReadScopeVersion || !final.Price || !final.Amount)
                throw Error("AUTHORITY_CHANGED", 403);
        });
        return original.Body;
    }

    private async Task<(LegacyOrderSourceSnapshot Current, Original? Original)> VisibleOriginal(
        OrderReadOwnerV1.Access access, Guid orderId, long? revision, CancellationToken ct)
    {
        await LockNative(db, orderId, ct);
        var current = await VisibleOrder(access, orderId, ct);
        var currentIds = current.Lines.Select(line => line.Id).ToHashSet();
        var allowed = access.Rows.Where(row => row.WebOrderNo == current.Number);
        if (await allowed.CountAsync(row => currentIds.Contains(row.Id), ct) != currentIds.Count)
            throw Error("OBJECT_FORBIDDEN", 403);
        var stored = await ReadOriginal(db, orderId, revision, ct);
        // 保留历史原件。当前范围无法核实旧行时，封闭该原件。
        return (current, stored?.Read().Lines.Any(line => !currentIds.Contains(line.Id)) == true ? null : stored);
    }

    public Task<OrderScheduleSourceRead> ReadScheduleAsync(OrderSourceScope scope, CancellationToken ct = default)
    {
        OrderSourceParticipant.ValidateScope(scope);
        return Read(async (access, token) =>
        {
            await LockNative(db, scope.OrderId, token);
            await RequireLine(access, scope.OrderId, scope.LineId, token);
            var proof = await new OrderSourceParticipant(db).ConfirmedScope(scope, token);
            return proof is null
                ? new OrderScheduleSourceRead(scope, "UNPROVEN", null, null, null, null)
                : new(scope, "ORIGINAL_FOUND", proof.ScheduleSha256, proof.ProofSha256, proof.LegacyRevision,
                    JsonSerializer.Deserialize<JsonElement>(proof.Schedule));
        }, ct);
    }

    public Task<OrderSourceResultRead> ReadResultAsync(Guid orderId, Guid lineId, string producer, string operationKey,
        CancellationToken ct = default)
    {
        if (orderId == Guid.Empty || lineId == Guid.Empty) throw Error("SCHEMA_INVALID", 400);
        OrderSourceParticipant.Key(producer);
        OrderSourceParticipant.Key(operationKey);
        return Read(async (access, token) =>
        {
            await LockNative(db, orderId, token);
            await RequireLine(access, orderId, lineId, token);
            var participant = new OrderSourceParticipant(db);
            var receipt = (await participant.ReadOperation(producer, operationKey, token))?.Receipt;
            if (receipt is null || receipt.Scope.OrderId != orderId || receipt.Scope.LineId != lineId)
                return new OrderSourceResultRead("UNKNOWN", null, null);
            var current = await participant.CheckCurrentSource(db.Database.CurrentTransaction!.GetDbTransaction(),
                receipt.Scope, receipt.LineageId, receipt.SourceVersion, receipt.SourceDigest, token);
            return new OrderSourceResultRead("FOUND", receipt, current);
        }, ct);
    }

    private Task<T> Read<T>(Func<OrderReadOwnerV1.Access, CancellationToken, Task<T>> read, CancellationToken ct)
    {
        if (!owner.Owns(db)) throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        return owner.ReadAsync("erp-order", FieldPermissionUseV1.Read, true, (access, token) =>
        {
            if (!access.Price || !access.Amount) throw Error("FIELD_READ_FORBIDDEN", 403);
            return read(access, token);
        }, ct);
    }

    private async Task<LegacyOrderSourceSnapshot> VisibleOrder(OrderReadOwnerV1.Access access, Guid orderId,
        CancellationToken ct)
    {
        if (!await access.Headers.AnyAsync(o => o.Id == orderId, ct)) throw Error("OBJECT_FORBIDDEN", 403);
        return await ReadNative(db, orderId, ct) ?? throw Error("OBJECT_FORBIDDEN", 403);
    }

    private static async Task RequireLine(OrderReadOwnerV1.Access access, Guid orderId, Guid lineId, CancellationToken ct)
    {
        var number = await access.Headers.Where(o => o.Id == orderId).Select(o => o.WebOrderNo).SingleOrDefaultAsync(ct);
        if (number is null || !await access.Rows.AnyAsync(d => d.Id == lineId && d.WebOrderNo == number, ct))
            throw Error("OBJECT_FORBIDDEN", 403);
    }
}
