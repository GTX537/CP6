using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests;

/// <summary>
/// SpaceBridgeHook 测试（ch04 §2）。[InMemory 仅测逻辑]
/// </summary>
public class SpaceBridgeHookTests
{
    private static CP6Context Db() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Publish_PersistsIntegrationEvent_Success()
    {
        using var db = Db();
        var hook = new SpaceBridgeHook(db, NullLogger<SpaceBridgeHook>.Instance, new NoOpWmsLocationConsumer());
        var batch = new LocationPublishBatch
        {
            BatchNo = "LPUB-20260613-0001",
            Items =
            {
                new LocationPublishItem { Op = "UPSERT", LocationId = Guid.NewGuid(), LocationCode = "A-01-01-01", Version = 1 }
            }
        };
        var r = await hook.OnLocationPublishedAsync(batch, Guid.NewGuid());
        Assert.True(r.Success);
        var evt = await db.IntegrationEvents.SingleAsync();
        Assert.Equal("SPACE", evt.SourceModule);
        Assert.Equal("WMS", evt.TargetModule);
        Assert.Equal("LPUB-20260613-0001", evt.SourceNo);
        Assert.Equal(IntegrationEventStatus.Success, evt.Status);
    }

    [Fact]
    public async Task Publish_AllSkipped_PersistsSkippedStatus()
    {
        using var db = Db();
        // Consumer that returns all SKIPPED
        var consumer = new AllSkippedConsumer();
        var hook = new SpaceBridgeHook(db, NullLogger<SpaceBridgeHook>.Instance, consumer);
        var locId = Guid.NewGuid();
        var batch = new LocationPublishBatch
        {
            BatchNo = "LPUB-20260613-0002",
            Items = { new LocationPublishItem { Op = "UPSERT", LocationId = locId, LocationCode = "B-01-01-01", Version = 1 } }
        };
        var r = await hook.OnLocationPublishedAsync(batch, Guid.NewGuid());
        Assert.True(r.Success); // still ok (skipped is not failure)
        var evt = await db.IntegrationEvents.SingleAsync();
        Assert.Equal(IntegrationEventStatus.Skipped, evt.Status);
    }

    [Fact]
    public async Task Hook_PersistEventFalse_DoesNotInsertEventRow()
    {
        using var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var hook = new SpaceBridgeHook(db, NullLogger<SpaceBridgeHook>.Instance, new NoOpWmsLocationConsumer());
        var batch = new LocationPublishBatch { BatchNo = "LPUB-20260705-0001" };

        var r = await hook.OnLocationPublishedAsync(batch, Guid.NewGuid(), persistEvent: false);

        Assert.True(r.Success);
        // 重试路径（Dispatcher）走此分支：Worker 更新原事件行，hook 不得再新插一行
        Assert.Equal(0, await db.IntegrationEvents.CountAsync());
    }

    private sealed class AllSkippedConsumer : IWmsLocationConsumer
    {
        public Task<WmsConsumeResult> ConsumeAsync(LocationPublishBatch batch) =>
            Task.FromResult(new WmsConsumeResult
            {
                Success = true,
                AllSkipped = true,
                Items = batch.Items.ConvertAll(i => new WmsItemResult { LocationId = i.LocationId, Status = "SKIPPED" })
            });
    }
}
