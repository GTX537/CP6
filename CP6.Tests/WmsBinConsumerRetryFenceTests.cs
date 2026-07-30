using System.Data.Common;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Space;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.Sqlite;

namespace CP6.Tests;

public sealed class WmsBinConsumerRetryFenceTests
{
    private static readonly Guid TenantId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Fenced_consume_commits_bin_and_completion_marker_together()
    {
        await using var database = NewDatabase();
        var (eventId, leaseId, locationId) =
            await SeedAsync(database);
        await using var db =
            new CP6Context(database.Options, database.Tenant);

        var result = await new WmsBinConsumer(db)
            .ConsumeAsync(
                Batch(locationId),
                new SpaceRetryFence(eventId, leaseId));

        Assert.True(result.Success);
        await using var assertDb =
            new CP6Context(database.Options, database.Tenant);
        Assert.Equal(
            7,
            (await assertDb.WmsBins.SingleAsync()).Version);
        var evt = await assertDb.IntegrationEvents
            .IgnoreQueryFilters()
            .SingleAsync();
        Assert.Equal(leaseId, evt.RetryCompletionLeaseId);
        Assert.True(evt.RetryCompletionSucceeded);
    }

    [Fact]
    public async Task Fenced_consume_with_lost_lease_makes_no_wms_write()
    {
        await using var database = NewDatabase();
        var (eventId, _, locationId) =
            await SeedAsync(database);
        await using var db =
            new CP6Context(database.Options, database.Tenant);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WmsBinConsumer(db).ConsumeAsync(
                Batch(locationId),
                new SpaceRetryFence(
                    eventId,
                    Guid.NewGuid())));

        Assert.Equal("SPACE_RETRY_FENCE_LOST", error.Message);
        await using var assertDb =
            new CP6Context(database.Options, database.Tenant);
        Assert.Empty(await assertDb.WmsBins.ToListAsync());
        var evt = await assertDb.IntegrationEvents
            .IgnoreQueryFilters()
            .SingleAsync();
        Assert.Null(evt.RetryCompletionLeaseId);
        Assert.Null(evt.RetryCompletionSucceeded);
    }

    [Fact]
    public async Task Fenced_consume_rolls_back_bin_and_marker_when_save_fails()
    {
        await using var database = NewDatabase();
        var (eventId, leaseId, locationId) =
            await SeedAsync(database);
        await using var db = new WmsSaveFailingContext(
            database.Options,
            database.Tenant);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => new WmsBinConsumer(db).ConsumeAsync(
                Batch(locationId),
                new SpaceRetryFence(eventId, leaseId)));

        await using var assertDb =
            new CP6Context(database.Options, database.Tenant);
        Assert.Empty(await assertDb.WmsBins.ToListAsync());
        var evt = await assertDb.IntegrationEvents
            .IgnoreQueryFilters()
            .SingleAsync();
        Assert.Null(evt.RetryCompletionLeaseId);
        Assert.Null(evt.RetryCompletionSucceeded);
        Assert.Equal(leaseId, evt.RetryLeaseId);
    }

    [Fact]
    public async Task Fenced_consume_holds_write_lock_until_marker_is_committed()
    {
        await using var database = NewDatabase();
        var (eventId, leaseId, locationId) =
            await SeedAsync(database);
        var gate = new WmsInsertGateInterceptor();
        var consumerOptions =
            new DbContextOptionsBuilder<CP6Context>(
                    database.Options)
                .AddInterceptors(gate)
                .Options;
        await using var consumerDb =
            new CP6Context(consumerOptions, database.Tenant);
        var consume = new WmsBinConsumer(consumerDb)
            .ConsumeAsync(
                Batch(locationId),
                new SpaceRetryFence(eventId, leaseId));
        await gate.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        var takeoverLeaseId = Guid.NewGuid();
        var takeover = Task.Run(async () =>
        {
            await using var takeoverDb =
                new CP6Context(
                    database.Options,
                    database.Tenant);
            return await takeoverDb.IntegrationEvents
                .IgnoreQueryFilters()
                .Where(evt =>
                    evt.Id == eventId &&
                    evt.TenantId == TenantId &&
                    evt.Status ==
                        IntegrationEventStatus.Failed &&
                    evt.RetryLeaseId == leaseId &&
                    evt.RetryCompletionLeaseId == null)
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(
                        evt => evt.RetryLeaseId,
                        takeoverLeaseId));
        });

        try
        {
            var premature = await Task.WhenAny(
                takeover,
                Task.Delay(250));
            Assert.NotSame(takeover, premature);
        }
        finally
        {
            gate.Release.TrySetResult();
        }

        Assert.True((await consume).Success);
        Assert.Equal(0, await takeover);
        await using var assertDb =
            new CP6Context(database.Options, database.Tenant);
        var evt = await assertDb.IntegrationEvents
            .IgnoreQueryFilters()
            .SingleAsync();
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

    private static async Task<(Guid EventId, Guid LeaseId, Guid LocationId)>
        SeedAsync(TestDatabase database)
    {
        var eventId = Guid.NewGuid();
        var leaseId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        await using var db =
            new CP6Context(database.Options, database.Tenant);
        db.Space_Locations.Add(new Space_Location
        {
            Id = locationId,
            TenantId = TenantId,
            LocationCode = "A-01-01-01",
            Status = 1,
        });
        db.IntegrationEvents.Add(new IntegrationEvent
        {
            Id = eventId,
            TenantId = TenantId,
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

    private static TestDatabase NewDatabase()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"cp6-wms-fence-{Guid.NewGuid():N}.db");
        var connectionString =
            $"Data Source={path};Default Timeout=10";
        var options =
            new DbContextOptionsBuilder<CP6Context>()
                .UseSqlite(connectionString)
                .Options;
        var tenant =
            new TenantContext { CurrentTenantId = TenantId };
        using (var setup = new CP6Context(options, tenant))
        {
            var script = Regex.Replace(
                setup.Database.GenerateCreateScript(),
                "n?varchar\\(max\\)",
                "TEXT",
                RegexOptions.IgnoreCase);
            using var connection =
                new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = script;
            command.ExecuteNonQuery();
        }
        return new TestDatabase(path, options, tenant);
    }

    private sealed record TestDatabase(
        string Path,
        DbContextOptions<CP6Context> Options,
        TenantContext Tenant) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(Path))
                File.Delete(Path);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class WmsSaveFailingContext :
        CP6Context
    {
        public WmsSaveFailingContext(
            DbContextOptions<CP6Context> options,
            ITenantContext tenant)
            : base(options, tenant)
        {
        }

        public override Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            if (ChangeTracker.Entries<WmsBin>().Any(entry =>
                    entry.State is EntityState.Added or
                        EntityState.Modified))
            {
                throw new DbUpdateException(
                    "simulated WMS persistence failure");
            }
            return base.SaveChangesAsync(cancellationToken);
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
            if (!command.CommandText.Contains(
                    "INSERT INTO \"T_WmsBin\"",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
        }
    }
}
