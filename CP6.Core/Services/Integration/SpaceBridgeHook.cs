using CP6.Core.EFDbContext;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs.Space;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Integration;

/// <summary>Space 发布 bridge hook 契约（ch04 §2.1）。</summary>
public interface ISpaceBridgeHook
{
    /// <param name="persistEvent">true=末尾落 IntegrationEvent（首发路径）；false=不落（Worker 重试路径，
    /// 由 Worker 更新原事件行，避免每次重试新插一行导致事件表翻倍增长）。</param>
    Task<SpaceBridgeResult> OnLocationPublishedAsync(
        LocationPublishBatch batch,
        Guid correlationId,
        bool persistEvent = true,
        SpaceRetryFence? retryFence = null,
        CancellationToken ct = default);
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
    private readonly ISpaceExecutionContextAccessor _execution;
    private readonly ISpaceExecutionContextManager _executionManager;

    public SpaceBridgeHook(
        CP6Context db,
        ILogger<SpaceBridgeHook> logger,
        IWmsLocationConsumer wms,
        ISpaceExecutionContextAccessor execution,
        ISpaceExecutionContextManager executionManager)
        : base(db, logger)
    {
        _wms = wms;
        _execution = execution;
        _executionManager = executionManager;
    }

    /// <summary>发布批次到 WMS，落 IntegrationEvent 持久化记录。</summary>
    public async Task<SpaceBridgeResult> OnLocationPublishedAsync(
        LocationPublishBatch batch,
        Guid correlationId,
        bool persistEvent = true,
        SpaceRetryFence? retryFence = null,
        CancellationToken ct = default)
    {
        var context = _execution.RequireCurrent();
        if (context.CorrelationId != correlationId)
            throw new InvalidOperationException("SPACE_EXECUTION_CONTEXT_CONFLICT");

        var jobId = context.JobId;
        if (persistEvent)
        {
            jobId = Guid.NewGuid();
            _executionManager.Enrich(jobId: jobId);
            context = _execution.RequireCurrent();
        }

        string status;
        string? error = null;
        bool ok = false;
        try
        {
            var res = await _wms.ConsumeAsync(
                batch,
                retryFence,
                ct);
            ok = res.Success;
            status = !res.Success
                ? IntegrationEventStatus.Failed
                : res.Items.Count > 0 && res.Items.All(i => i.Status == "SKIPPED")
                    ? IntegrationEventStatus.Skipped
                    : IntegrationEventStatus.Success;
            if (!res.Success)
                error = "SPACE_ADAPTER_REJECTED";
        }
        catch (Exception) when (!persistEvent)
        {
            // Retry execution owns classification, auditing, and ledger
            // state. Preserve the original exception type and HResult so
            // its worker fingerprint reflects the real adapter failure.
            throw;
        }
        catch (Exception ex)
        {
            status = IntegrationEventStatus.Failed;
            error = SpaceErrorSanitizer.ToStorageCode(
                ex,
                "SPACE_ADAPTER_FAILURE");
        }

        if (persistEvent)
        {
            await PersistEventAsync(
                sourceModule: "SPACE",
                targetModule: "WMS",
                hookName: nameof(OnLocationPublishedAsync),
                sourceNo: batch.BatchNo,
                targetNo: null,
                status: status,
                error: error,
                correlationId: correlationId,
                payload: batch,
                jobId: jobId,
                publishAttemptId: context.PublishAttemptId);
        }

        return new SpaceBridgeResult
        {
            Success = ok && status != IntegrationEventStatus.Failed,
            Message = error
        };
    }
}
