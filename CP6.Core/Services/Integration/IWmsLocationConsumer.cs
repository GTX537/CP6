using CP6.Entity.DTOs.Space;

namespace CP6.Core.Services.Integration;

/// <summary>
/// Persistent owner fence for a Space retry entering a transactional WMS
/// consumer. Null is reserved for the original, non-retry publish path.
/// </summary>
public sealed record SpaceRetryFence(
    Guid EventId,
    Guid LeaseId);

/// <summary>
/// WMS 库位消费端契约（ch04 §3，Space 侧定义，WMS 实现）。
/// P1 用 <see cref="NoOpWmsLocationConsumer"/> 桩；真实消费属 WMS 模块工作。
/// </summary>
public interface IWmsLocationConsumer
{
    /// <summary>幂等 upsert（按 LocationId+Version，ch04 §5）。</summary>
    Task<WmsConsumeResult> ConsumeAsync(
        LocationPublishBatch batch,
        SpaceRetryFence? retryFence = null,
        CancellationToken ct = default);
}

/// <summary>P1 桩：接受全部，标 UPSERTED/DEACTIVATED（真实 WMS 消费属 WMS 模块工作）。</summary>
public sealed class NoOpWmsLocationConsumer : IWmsLocationConsumer
{
    public Task<WmsConsumeResult> ConsumeAsync(
        LocationPublishBatch batch,
        SpaceRetryFence? retryFence = null,
        CancellationToken ct = default) =>
        Task.FromResult(new WmsConsumeResult
        {
            Success = true,
            Items = batch.Items.ConvertAll(i => new WmsItemResult
            {
                LocationId = i.LocationId,
                Status = i.Op == "DEACTIVATE" ? "DEACTIVATED" : "UPSERTED"
            })
        });
}
