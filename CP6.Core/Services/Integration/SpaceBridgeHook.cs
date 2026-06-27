using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs.Space;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Integration;

/// <summary>Space 发布 bridge hook 契约（ch04 §2.1）。</summary>
public interface ISpaceBridgeHook
{
    Task<SpaceBridgeResult> OnLocationPublishedAsync(LocationPublishBatch batch, Guid correlationId);
}

/// <summary>Space bridge hook 调用结果。</summary>
public class SpaceBridgeResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Space→WMS 发布 bridge hook（ch04 §2.1）。
/// 调用 WMS 消费端契约，落 IntegrationEvent 持久化记录，复用 BridgeHookBase 基建。
/// </summary>
public class SpaceBridgeHook : BridgeHookBase, ISpaceBridgeHook
{
    private readonly IWmsLocationConsumer _wms;

    public SpaceBridgeHook(CP6Context db, ILogger<SpaceBridgeHook> logger, IWmsLocationConsumer wms)
        : base(db, logger)
    {
        _wms = wms;
    }

    /// <summary>发布批次到 WMS，落 IntegrationEvent 持久化记录。</summary>
    public async Task<SpaceBridgeResult> OnLocationPublishedAsync(LocationPublishBatch batch, Guid correlationId)
    {
        string status;
        string? error = null;
        bool ok = false;
        try
        {
            var res = await _wms.ConsumeAsync(batch);
            ok = res.Success;
            status = !res.Success
                ? IntegrationEventStatus.Failed
                : res.Items.Count > 0 && res.Items.All(i => i.Status == "SKIPPED")
                    ? IntegrationEventStatus.Skipped
                    : IntegrationEventStatus.Success;
            if (!res.Success) error = "WMS consume returned failure";
        }
        catch (Exception ex)
        {
            status = IntegrationEventStatus.Failed;
            error = ex.ToString();
        }

        await PersistEventAsync(
            sourceModule: "SPACE",
            targetModule: "WMS",
            hookName: nameof(OnLocationPublishedAsync),
            sourceNo: batch.BatchNo,
            targetNo: null,
            status: status,
            error: error,
            correlationId: correlationId,
            payload: batch);

        return new SpaceBridgeResult
        {
            Success = ok && status != IntegrationEventStatus.Failed,
            Message = error
        };
    }
}
