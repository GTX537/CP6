using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.ErpIntegration.IntegrationOpsContractV1;

namespace CP6.Core.Services.ErpIntegration;

public sealed partial class ErpIntegrationOpsV1
{
    internal static async Task HoldWorkerRootAsync(CP6Context db, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '10s'", ct);
        await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
    }

    internal static Task<bool> HasManualBridgeAsync(ErpIntegrationContext queue, Guid tenant, string key, CancellationToken ct) =>
        queue.Set<ErpDeliveryReplayAudit>().AsNoTracking()
            .AnyAsync(a => a.TenantId == tenant && a.Kind == "bridge" && a.TargetId == key, ct);

    internal async Task<bool> CheckRequestExecutionAsync(ErpIntegrationContext queue, CP6Context db, ErpInboxReceipt item,
        ErpRequestHandler.Command original, CancellationToken ct)
    {
        if (effectOwner is null) return false;
        var operations = await queue.ReplayAudits.AsNoTracking().Where(a => a.TenantId == item.TenantId &&
            a.MessageId == item.MessageId && a.ReplayedAtUtc == item.ReplayedAtUtc)
            .Select(a => a.OperationId).Take(2).ToArrayAsync(ct);
        if (operations.Length != 1) return false;
        var intent = await ReadExecutionIntentAsync(db, operations[0], new(Owner, Inbox, item.MessageId),
            Digest(Inbox, original.PayloadSha256), ErpIntegrationOpsSourceV1.Original(original.RequestId, original.RequestVersion),
            Convert.ToBase64String(item.RowVersion), ct);
        return intent is not null && await HoldAsync(db, intent, true, ct) is not null;
    }

    internal async Task<bool> ExecuteBridgeAsync(ErpIntegrationContext queue, CP6Context db, ErpOrderBridgeDispatch bridge,
        CancellationToken ct)
    {
        if (effectOwner is null) return false;
        var latest = await queue.Set<ErpDeliveryReplayAudit>().AsNoTracking().Where(a => a.TenantId == bridge.TenantId &&
                a.Kind == "bridge" && a.TargetId == bridge.OrderKey).OrderByDescending(a => a.ReplayedAtUtc)
            .Select(a => new { a.OperationId, a.ReplayedAtUtc }).Take(2).ToArrayAsync(ct);
        if (latest.Length == 0 || latest.Length == 2 && latest[0].ReplayedAtUtc == latest[1].ReplayedAtUtc) return false;
        ErpIntegrationOpsSourceV1.Native native;
        try { native = await source.ReadNativeAsync(queue, db, new(Owner, Bridge, bridge.OrderKey), ct); }
        catch (IntegrationOpsExceptionV1 error) when (error.Status is 404 or 409) { return false; }
        var intent = await ReadExecutionIntentAsync(db, latest[0].OperationId, native.Evidence.NativeQueueRef,
            native.Evidence.ExpectedDigest, native.OriginalRequest, Convert.ToBase64String(bridge.RowVersion), ct);
        if (intent is null || await HoldAsync(db, intent, true, ct) is null) return false;
        // 回调只在本事务中写入原步骤。返回 false 时外层回滚全部暂存。
        if (!await effectOwner.StageBridgeAsync(db, intent, ct))
            throw Error(Blocked);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<IntegrationEffectIntentV1?> ReadExecutionIntentAsync(CP6Context db, Guid operation, IntegrationQueueRefV1 key,
        IntegrationDigestV1 digest, IntegrationRequestRefV1 original, string version, CancellationToken ct)
    {
        var previous = await MainOwnerOperationJournalV1.Read(db, db.CurrentTenantId, Journal, operation, ct);
        if (previous is null) return null;
        var receipt = ReadReceipt(previous);
        if (receipt.Status != "Scheduled" || receipt.AllowedAction != Action(key.NativeType) || receipt.RouteEpoch != source.RouteEpoch ||
            receipt.NativeQueueRef != key || receipt.ExpectedDigest != digest || receipt.OriginalRequestRef != original)
            return null;
        try
        {
            if (!CanSchedule(await authority.ReadSubjectAsync(db, previous.Actor, ct), receipt.AllowedAction)) return null;
        }
        catch (IntegrationOpsExceptionV1 error) when (error.Status == 403) { return null; }
        return new(db.CurrentTenantId, previous.Actor, operation, source.RouteEpoch, receipt.AllowedAction, key, original, digest, version);
    }
}
