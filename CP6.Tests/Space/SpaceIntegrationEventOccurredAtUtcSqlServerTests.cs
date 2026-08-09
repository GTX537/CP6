using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels.Integration;
using CP6.Tests.Infra;
using CP6.WebApi.BackgroundServices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CP6.Tests.Space;

/// <summary>
/// Executable SQL Server proof for the production Session-owned application
/// lock. Each test creates and destroys only its own validated temporary
/// database and uses independent DbContext/connection instances.
/// </summary>
public sealed class
    SpaceIntegrationEventOccurredAtUtcSqlServerTests
{
    private static readonly SpaceObservabilityOptions UtcOptions =
        new()
        {
            LegacyIntegrationEventTimeZoneId = "UTC",
        };

    [SqlServerFact]
    public async Task Two_connections_serialize_all_batches_under_session_app_lock()
    {
        await using var database =
            await SqlServerBackfillDatabase.CreateAsync();
        await database.SeedAsync(
            SpaceIntegrationEventOccurredAtUtcBackfill.BatchSize + 1,
            includeNonSpace: true);
        await database.CreateOneShotDelayTriggerAsync();

        await using var first = database.NewContext();
        await using var second = database.NewContext();
        Assert.NotSame(
            first.Database.GetDbConnection(),
            second.Database.GetDbConnection());

        // Keep the first production call on a known physical session. The
        // trigger clears this flag before one WAITFOR, so only the first
        // UPDATE is delayed and all remaining 500+ rows proceed normally.
        await first.Database.OpenConnectionAsync();
        await using (var enableDelay =
                     first.Database.GetDbConnection().CreateCommand())
        {
            enableDelay.CommandText =
                """
                EXEC sys.sp_set_session_context
                    @key = N'CP6TestBackfillDelay',
                    @value = 1;
                """;
            await enableDelay.ExecuteNonQueryAsync();
        }

        var firstRun =
            SpaceIntegrationEventOccurredAtUtcBackfill.RunAsync(
                first,
                UtcOptions,
                NullLogger.Instance);
        await database.WaitUntilProductionLockIsHeldAsync(
            TimeSpan.FromSeconds(15));

        // Prove the same production session continues to own the app lock
        // during the one-shot trigger window. This second independent probe
        // intentionally happens before secondRun exists, so a SQL row-lock
        // wait from the second backfill cannot explain the observation.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(firstRun.IsCompleted);
        Assert.False(
            await database.CanAcquireAndReleaseProductionLockAsync());

        var secondWait = Stopwatch.StartNew();
        var secondRun =
            SpaceIntegrationEventOccurredAtUtcBackfill.RunAsync(
                second,
                UtcOptions,
                NullLogger.Instance);

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(firstRun.IsCompleted);
        Assert.False(secondRun.IsCompleted);

        await firstRun.WaitAsync(TimeSpan.FromSeconds(90));
        await secondRun.WaitAsync(TimeSpan.FromSeconds(90));
        secondWait.Stop();
        Assert.True(
            secondWait.Elapsed >= TimeSpan.FromSeconds(2),
            $"Second backfill did not wait for the first lock owner: {secondWait.Elapsed}.");

        await using (var barrierState =
                     first.Database.GetDbConnection().CreateCommand())
        {
            barrierState.CommandText =
                """
                SELECT TRY_CONVERT(
                    int,
                    SESSION_CONTEXT(N'CP6TestBackfillDelay'));
                """;
            Assert.Equal(
                0,
                Convert.ToInt32(
                    await barrierState.ExecuteScalarAsync()));
        }

        await using var assertion = database.NewContext();
        var spaceRows = await assertion.IntegrationEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.SourceModule == "SPACE")
            .OrderBy(x => x.SourceNo)
            .Select(x => new
            {
                x.Id,
                x.CreateDate,
                x.OccurredAtUtc,
            })
            .ToListAsync();
        Assert.Equal(
            SpaceIntegrationEventOccurredAtUtcBackfill.BatchSize + 1,
            spaceRows.Count);
        Assert.Equal(
            spaceRows.Count,
            spaceRows.Select(x => x.Id).Distinct().Count());
        Assert.All(spaceRows, row =>
        {
            Assert.NotNull(row.OccurredAtUtc);
            Assert.Equal(
                row.CreateDate.Ticks,
                row.OccurredAtUtc.Value.Ticks);
        });
        Assert.Null(
            await assertion.IntegrationEvents
                .IgnoreQueryFilters()
                .Where(x => x.SourceModule == "ERP")
                .Select(x => x.OccurredAtUtc)
                .SingleAsync());

        Assert.True(
            await database.CanAcquireAndReleaseProductionLockAsync(),
            "Production Session app lock was not released after both calls.");
    }

    [SqlServerFact]
    public async Task Invalid_time_zone_after_lock_releases_for_next_context()
    {
        await using var database =
            await SqlServerBackfillDatabase.CreateAsync();
        await database.SeedAsync(
            spaceCount: 1,
            includeNonSpace: false);
        await using var invalid = database.NewContext();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SpaceIntegrationEventOccurredAtUtcBackfill.RunAsync(
                invalid,
                new SpaceObservabilityOptions
                {
                    LegacyIntegrationEventTimeZoneId =
                        "Definitely/Not/A/TimeZone",
                },
                NullLogger.Instance));
        Assert.Equal(
            "SPACE_LEGACY_TIME_ZONE_INVALID",
            error.Message);
        Assert.True(
            await database.CanAcquireAndReleaseProductionLockAsync(),
            "Invalid-time-zone failure leaked its Session app lock.");

        await using var valid = database.NewContext();
        await SpaceIntegrationEventOccurredAtUtcBackfill.RunAsync(
                valid,
                UtcOptions,
                NullLogger.Instance)
            .WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(
            0,
            await valid.IntegrationEvents
                .IgnoreQueryFilters()
                .CountAsync(x =>
                    x.SourceModule == "SPACE" &&
                    x.OccurredAtUtc == null));
        Assert.True(
            await database.CanAcquireAndReleaseProductionLockAsync());
    }

    private sealed class SqlServerBackfillDatabase :
        IAsyncDisposable
    {
        private const string DatabasePrefix =
            "CP6Test_SpaceUtcBackfill_";
        private static readonly Regex SafeDatabaseName = new(
            "^CP6Test_SpaceUtcBackfill_[0-9a-f]{32}$",
            RegexOptions.CultureInvariant);

        private readonly string _masterConnectionString;
        private readonly string _databaseName;

        private SqlServerBackfillDatabase(
            string masterConnectionString,
            string databaseName,
            string connectionString)
        {
            _masterConnectionString = masterConnectionString;
            _databaseName = databaseName;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public static async Task<SqlServerBackfillDatabase>
            CreateAsync()
        {
            var configured = Environment.GetEnvironmentVariable(
                SqlServerFactAttribute.EnvVar);
            if (string.IsNullOrWhiteSpace(configured))
            {
                throw new InvalidOperationException(
                    "CP6_TEST_SQLSERVER_REQUIRED");
            }

            var databaseName =
                $"{DatabasePrefix}{Guid.NewGuid():N}";
            ValidateDatabaseName(databaseName);
            var masterBuilder =
                new SqlConnectionStringBuilder(configured)
                {
                    InitialCatalog = "master",
                };
            var databaseBuilder =
                new SqlConnectionStringBuilder(configured)
                {
                    InitialCatalog = databaseName,
                };
            var instance = new SqlServerBackfillDatabase(
                masterBuilder.ConnectionString,
                databaseName,
                databaseBuilder.ConnectionString);

            await using (var master = new SqlConnection(
                             instance._masterConnectionString))
            {
                await master.OpenAsync();
                await using var create = master.CreateCommand();
                create.CommandText =
                    $"CREATE DATABASE [{databaseName}];";
                await create.ExecuteNonQueryAsync();
            }

            try
            {
                await using var context = instance.NewContext();
                await context.Database.EnsureCreatedAsync();
                return instance;
            }
            catch
            {
                await instance.DisposeAsync();
                throw;
            }
        }

        public CP6Context NewContext()
        {
            var options = new DbContextOptionsBuilder<CP6Context>()
                .UseSqlServer(ConnectionString)
                .Options;
            return new CP6Context(options);
        }

        public async Task SeedAsync(
            int spaceCount,
            bool includeNonSpace)
        {
            await using var db = NewContext();
            var createDate = new DateTime(
                2026,
                7,
                25,
                12,
                0,
                0,
                DateTimeKind.Unspecified);
            for (var i = 0; i < spaceCount; i++)
            {
                var id = Guid.NewGuid();
                db.IntegrationEvents.Add(new IntegrationEvent
                {
                    Id = id,
                    SourceModule = "SPACE",
                    TargetModule = "WMS",
                    HookName =
                        "SpaceBridgeHook.OnLocationPublishedAsync",
                    SourceNo = $"LEG-{i:D4}",
                    Status = IntegrationEventStatus.Failed,
                    Attempts = 1,
                    CorrelationId = Guid.NewGuid(),
                    JobId = id,
                    PublishAttemptId = Guid.NewGuid(),
                    PayloadJson = "{}",
                    Creator = "sql-test",
                    CreateDate = createDate.AddTicks(i),
                });
            }

            if (includeNonSpace)
            {
                db.IntegrationEvents.Add(new IntegrationEvent
                {
                    Id = Guid.NewGuid(),
                    SourceModule = "ERP",
                    TargetModule = "WMS",
                    HookName = "ErpBridgeHook.OnTestAsync",
                    SourceNo = "ERP-UNCHANGED",
                    Status = IntegrationEventStatus.Failed,
                    Attempts = 1,
                    CorrelationId = Guid.NewGuid(),
                    PayloadJson = "{}",
                    Creator = "sql-test",
                    CreateDate = createDate,
                });
            }

            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        public async Task CreateOneShotDelayTriggerAsync()
        {
            await using var connection =
                new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var trigger = connection.CreateCommand();
            trigger.CommandText =
                """
                CREATE OR ALTER TRIGGER
                    [TR_CP6Test_SpaceUtcBackfillDelay]
                ON [T_IntegrationEvent]
                AFTER UPDATE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF TRY_CONVERT(
                           int,
                           SESSION_CONTEXT(
                               N'CP6TestBackfillDelay')) = 1
                    BEGIN
                        EXEC sys.sp_set_session_context
                            @key = N'CP6TestBackfillDelay',
                            @value = 0;
                        WAITFOR DELAY '00:00:04';
                    END
                END;
                """;
            await trigger.ExecuteNonQueryAsync();
        }

        public async Task WaitUntilProductionLockIsHeldAsync(
            TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (!await CanAcquireAndReleaseProductionLockAsync())
                    return;

                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }

            throw new TimeoutException(
                "Production backfill app lock was not observed.");
        }

        public async Task<bool>
            CanAcquireAndReleaseProductionLockAsync()
        {
            await using var connection =
                new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var acquire = connection.CreateCommand();
            acquire.CommandText =
                """
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = @resource,
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Session',
                    @LockTimeout = 0,
                    @DbPrincipal = N'public';
                SELECT @result;
                """;
            acquire.Parameters.AddWithValue(
                "@resource",
                SpaceIntegrationEventOccurredAtUtcBackfill
                    .LockResource);
            var result = Convert.ToInt32(
                await acquire.ExecuteScalarAsync());
            if (result < 0)
                return false;

            await using var release = connection.CreateCommand();
            release.CommandText =
                """
                EXEC sys.sp_releaseapplock
                    @Resource = @resource,
                    @LockOwner = N'Session',
                    @DbPrincipal = N'public';
                """;
            release.Parameters.AddWithValue(
                "@resource",
                SpaceIntegrationEventOccurredAtUtcBackfill
                    .LockResource);
            await release.ExecuteNonQueryAsync();
            return true;
        }

        public async ValueTask DisposeAsync()
        {
            ValidateDatabaseName(_databaseName);
            await using var master =
                new SqlConnection(_masterConnectionString);
            await master.OpenAsync();
            await using var drop = master.CreateCommand();
            drop.CommandText =
                $"""
                 IF DB_ID(N'{_databaseName}') IS NOT NULL
                 BEGIN
                     ALTER DATABASE [{_databaseName}]
                         SET SINGLE_USER
                         WITH ROLLBACK IMMEDIATE;
                     DROP DATABASE [{_databaseName}];
                 END
                 """;
            await drop.ExecuteNonQueryAsync();
        }

        private static void ValidateDatabaseName(
            string databaseName)
        {
            if (!SafeDatabaseName.IsMatch(databaseName))
            {
                throw new InvalidOperationException(
                    "CP6_TEST_DATABASE_NAME_UNSAFE");
            }
        }
    }
}
