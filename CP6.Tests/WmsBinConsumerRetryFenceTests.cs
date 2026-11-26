using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Space;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

[Collection("TenantManagementPostgres")]
public sealed class WmsBinConsumerRetryFenceTests(TenantManagementPostgresFixture server)
{
    private readonly TenantContext _tenant = new() { CurrentTenantId = Guid.NewGuid() };

    private CP6Context Context(params IInterceptor[] interceptors) => server.Context(_tenant, null, interceptors);

    [CurrentPermissionPostgresFact]
    public async Task Fenced_consume_commits_bin_and_completion_marker_together()
    {
        var (eventId, leaseId, locationId) = await SeedAsync();
        await using var db = Context();

        var result = await new WmsBinConsumer(db)
            .ConsumeAsync(
                Batch(locationId),
                new SpaceRetryFence(eventId, leaseId));

        Assert.True(result.Success);
        await using var assertDb = Context();
        Assert.Equal(
            7,
            (await assertDb.WmsBins.SingleAsync()).Version);
        var evt = await assertDb.IntegrationEvents.SingleAsync(row => row.Id == eventId);
        Assert.Equal(leaseId, evt.RetryCompletionLeaseId);
        Assert.True(evt.RetryCompletionSucceeded);
    }

    [CurrentPermissionPostgresFact]
    public async Task Fenced_consume_with_lost_lease_makes_no_wms_write()
    {
        var (eventId, _, locationId) = await SeedAsync();
        await using var db = Context();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WmsBinConsumer(db).ConsumeAsync(
                Batch(locationId),
                new SpaceRetryFence(
                    eventId,
                    Guid.NewGuid())));

        Assert.Equal("SPACE_RETRY_FENCE_LOST", error.Message);
        await using var assertDb = Context();
        Assert.Empty(await assertDb.WmsBins.ToListAsync());
        var evt = await assertDb.IntegrationEvents.SingleAsync(row => row.Id == eventId);
        Assert.Null(evt.RetryCompletionLeaseId);
        Assert.Null(evt.RetryCompletionSucceeded);
    }

    [CurrentPermissionPostgresFact]
    public async Task Fenced_consume_rolls_back_bin_and_marker_when_save_fails()
    {
        var (eventId, leaseId, locationId) = await SeedAsync();
        await using var db = Context(new WmsSaveFailingInterceptor());

        await Assert.ThrowsAsync<DbUpdateException>(
            () => new WmsBinConsumer(db).ConsumeAsync(
                Batch(locationId),
                new SpaceRetryFence(eventId, leaseId)));

        await using var assertDb = Context();
        Assert.Empty(await assertDb.WmsBins.ToListAsync());
        var evt = await assertDb.IntegrationEvents.SingleAsync(row => row.Id == eventId);
        Assert.Null(evt.RetryCompletionLeaseId);
        Assert.Null(evt.RetryCompletionSucceeded);
        Assert.Equal(leaseId, evt.RetryLeaseId);
    }

    [CurrentPermissionPostgresFact]
    public async Task Fenced_consume_holds_write_lock_until_marker_is_committed()
    {
        var (eventId, leaseId, locationId) = await SeedAsync();
        var gate = new WmsInsertGateInterceptor();
        await using var consumerDb = Context(gate);
        var consume = new WmsBinConsumer(consumerDb)
            .ConsumeAsync(
                Batch(locationId),
                new SpaceRetryFence(eventId, leaseId));
        Task<int>? takeover = null;
        try
        {
            var first = await Task.WhenAny(consume, gate.Entered.Task).WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Same(gate.Entered.Task, first);
            var takeoverLeaseId = Guid.NewGuid();
            takeover = Task.Run(async () =>
            {
                await using var takeoverDb = Context();
                return await takeoverDb.IntegrationEvents
                    .Where(evt => evt.Id == eventId && evt.Status == IntegrationEventStatus.Failed
                        && evt.RetryLeaseId == leaseId && evt.RetryCompletionLeaseId == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(evt => evt.RetryLeaseId, takeoverLeaseId));
            });
            await using var observer = Context();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                    AND query LIKE 'UPDATE%"T_IntegrationEvent"%') AS "Value"
                """).SingleAsync(timeout.Token))
                await Task.Delay(20, timeout.Token);
            Assert.False(takeover.IsCompleted);
        }
        finally
        {
            gate.Release.TrySetResult();
            await consume;
            if (takeover is not null) await takeover;
        }

        Assert.True((await consume).Success);
        Assert.Equal(0, await takeover!);
        await using var assertDb = Context();
        var evt = await assertDb.IntegrationEvents.SingleAsync(row => row.Id == eventId);
        Assert.Equal(leaseId, evt.RetryLeaseId);
        Assert.Equal(leaseId, evt.RetryCompletionLeaseId);
    }

    private static LocationPublishBatch Batch(
        Guid locationId) =>
        new()
        {
            BatchNo = "LPUB-FENCED",
            PublishedBy = "space-worker",
            Items =
            [
                new LocationPublishItem
                {
                    Op = "UPSERT",
                    LocationId = locationId,
                    LocationCode = "A-01-01-01",
                    CodeOrigin = 1,
                    Version = 7,
                    WarehouseCd = "WH1",
                    Path = new LocationPath
                    {
                        SiteCode = "WH1",
                        FloorLevel = 1,
                        ZoneCode = "A",
                        RackCode = "R1",
                        Col = 1,
                        Level = 1,
                        Depth = 1,
                    },
                },
            ],
        };

    private async Task<(Guid EventId, Guid LeaseId, Guid LocationId)> SeedAsync()
    {
        var eventId = Guid.NewGuid();
        var leaseId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        await using var db = Context();
        db.Space_Locations.Add(new Space_Location
        {
            Id = locationId,
            TenantId = _tenant.CurrentTenantId,
            LocationCode = "A-01-01-01",
            Status = 1,
        });
        db.IntegrationEvents.Add(new IntegrationEvent
        {
            Id = eventId,
            TenantId = _tenant.CurrentTenantId,
            SourceModule = "SPACE",
            TargetModule = "WMS",
            HookName = "OnLocationPublishedAsync",
            SourceNo = "LPUB-FENCED",
            Status = IntegrationEventStatus.Failed,
            Attempts = 1,
            NextRetryAt = DateTime.UtcNow.AddMinutes(5),
            RetryLeaseId = leaseId,
            CorrelationId = Guid.NewGuid(),
            PayloadJson =
                """{"batchNo":"LPUB-FENCED","items":[]}""",
            Creator = "test",
            CreateDate = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return (eventId, leaseId, locationId);
    }

    private sealed class WmsSaveFailingInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<WmsBin>()
                .Any(entry => entry.State is EntityState.Added or EntityState.Modified))
                throw new DbUpdateException("模拟 WMS 持久化失败");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class WmsInsertGateInterceptor :
        DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<
            InterceptionResult<DbDataReader>>
            ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
        {
            await WaitIfWmsInsertAsync(
                command,
                cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>>
            NonQueryExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            await WaitIfWmsInsertAsync(
                command,
                cancellationToken);
            return result;
        }

        private async Task WaitIfWmsInsertAsync(
            DbCommand command,
            CancellationToken ct)
        {
            if (!command.CommandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)
                || !command.CommandText.Contains("\"T_WmsBin\"", StringComparison.Ordinal))
            {
                return;
            }

            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
        }
    }
}
