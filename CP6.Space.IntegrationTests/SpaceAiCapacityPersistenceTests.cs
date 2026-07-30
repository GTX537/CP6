using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceAiCapacityPersistenceTests
{
    private static readonly DateTime InitialNow =
        new(2026, 7, 30, 19, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Ef_model_and_registration_freeze_capacity_boundaries()
    {
        var root = new InMemoryDatabaseRoot();
        using var context = CreateInMemoryContext(
            root,
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid(),
            new MutableClock(InitialNow));
        var model = context.GetService<IDesignTimeModel>().Model;
        var slot = model.FindEntityType(
            typeof(SpaceTenantAiWorkSlot))!;
        var budget = model.FindEntityType(
            typeof(SpaceAiBudgetReservation))!;

        Assert.Equal("Space_TenantAiWorkSlot", slot.GetTableName());
        Assert.Equal("Space_AiBudgetReservation", budget.GetTableName());
        Assert.NotNull(slot.GetQueryFilter());
        Assert.NotNull(budget.GetQueryFilter());
        Assert.True(slot.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.True(budget.FindProperty("RowVersion")!.IsConcurrencyToken);
        Assert.Equal(
            ["TenantId", "SlotNo"],
            slot.FindPrimaryKey()!.Properties.Select(item => item.Name));
        Assert.Contains(
            slot.GetIndexes(),
            index =>
                index.IsUnique &&
                index.GetDatabaseName() ==
                "UX_TenantAiWorkSlot_Tenant_Run");
        Assert.Contains(
            budget.GetIndexes(),
            index =>
                index.IsUnique &&
                index.GetDatabaseName() ==
                "UX_AiBudgetReservation_Tenant_Request");

        var services = new ServiceCollection();
        services.AddSingleton<ISpaceExecutionContext>(
            new TestExecutionContext(Guid.NewGuid(), Guid.NewGuid()));
        services.AddSpaceDesignV1Persistence(
            "Server=(localdb)\\mssqllocaldb;Database=unused;");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<EfSpaceAiCapacityLedger>(
            scope.ServiceProvider.GetRequiredService<
                ISpaceAiCapacityLedger>());
        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<
                SpaceAiCapacityCoordinator>());
        scope.ServiceProvider.GetRequiredService<
            SpaceAiCapacityOptions>().Validate();
    }

    [SqlServerFact]
    public async Task Three_concurrent_slots_succeed_and_fourth_is_rejected()
    {
        var tenantId = Guid.NewGuid();
        var clock = new MutableClock(InitialNow);

        await WithDatabaseAsync(
            tenantId,
            clock,
            async connectionString =>
            {
                IReadOnlyList<Guid> runIds;
                await using (var seed = CreateSqlContext(
                                 connectionString,
                                 tenantId,
                                 clock))
                {
                    runIds = await SeedRunsAsync(seed, tenantId, 4);
                    seed.TenantAiWorkSlots.AddRange(
                        Enumerable.Range(1, 3)
                            .Select(slotNo =>
                                SpaceTenantAiWorkSlot.CreateAvailable(
                                    tenantId,
                                    slotNo)));
                    await seed.SaveChangesAsync();
                }

                var acquisitions = await Task.WhenAll(
                    runIds.Select(
                        (runId, index) => AcquireAsync(
                            connectionString,
                            tenantId,
                            clock,
                            runId,
                            $"worker-{index + 1}")));
                var granted = acquisitions
                    .Where(lease => lease is not null)
                    .Cast<SpaceAiWorkSlotLease>()
                    .ToArray();
                Assert.Equal(3, granted.Length);
                Assert.Equal(
                    [1, 2, 3],
                    granted.Select(lease => lease.SlotNo).Order());
                var rejectedRun = runIds.Single(
                    runId => granted.All(lease => lease.RunId != runId));
                await using (var rejectContext = CreateSqlContext(
                                 connectionString,
                                 tenantId,
                                 clock))
                {
                    var coordinator = new SpaceAiCapacityCoordinator(
                        new EfSpaceAiCapacityLedger(
                            rejectContext,
                            clock),
                        new SpaceAiCapacityOptions());
                    var error = await Assert.ThrowsAsync<
                        SpaceProblemException>(() =>
                        coordinator.AcquireWorkSlotAsync(
                            rejectedRun,
                            "worker-rejected",
                            3));
                    Assert.Equal(
                        SpaceErrorCodes.AiQuotaExceeded,
                        error.Code);
                    Assert.Equal(429, error.StatusCode);
                    Assert.True(error.Retryable);
                }

                var slotOne = granted.Single(lease => lease.SlotNo == 1);
                await using (var releaseContext = CreateSqlContext(
                                 connectionString,
                                 tenantId,
                                 clock))
                {
                    var ledger = new EfSpaceAiCapacityLedger(
                        releaseContext,
                        clock);
                    await ledger.ReleaseWorkSlotAsync(slotOne);
                }

                Assert.Null(
                    await AcquireAsync(
                        connectionString,
                        tenantId,
                        clock,
                        rejectedRun,
                        "worker-policy-shrink",
                        maxConcurrentRuns: 1));
                var recovered = await AcquireAsync(
                    connectionString,
                    tenantId,
                    clock,
                    rejectedRun,
                    "worker-retry");
                Assert.NotNull(recovered);
                Assert.Equal(slotOne.SlotNo, recovered.SlotNo);

                await using var verify = CreateSqlContext(
                    connectionString,
                    tenantId,
                    clock);
                Assert.Equal(
                    3,
                    await verify.TenantAiWorkSlots.CountAsync(
                        slot => slot.RunId != null));
            });
    }

    [SqlServerFact]
    public async Task Budget_is_atomic_idempotent_and_usage_charges_once()
    {
        var tenantId = Guid.NewGuid();
        var clock = new MutableClock(InitialNow);

        await WithDatabaseAsync(
            tenantId,
            clock,
            async connectionString =>
            {
                IReadOnlyList<Guid> runIds;
                await using (var seed = CreateSqlContext(
                                 connectionString,
                                 tenantId,
                                 clock))
                {
                    runIds = await SeedRunsAsync(seed, tenantId, 3);
                }

                var limits = new SpaceAiBudgetLimits(100, 100, "USD");
                var requests = new[]
                {
                    Request(runIds[0], 'a', 60, limits),
                    Request(runIds[1], 'b', 60, limits),
                };
                var reservations = await Task.WhenAll(
                    requests.Select(request => ReserveAsync(
                        connectionString,
                        tenantId,
                        clock,
                        request)));
                var reserved = Assert.Single(
                    reservations,
                    item => item is not null)!;
                Assert.Single(reservations, item => item is null);

                var matchingRequest = requests.Single(
                    request =>
                        request.ProviderRequestKey ==
                        reserved.ProviderRequestKey);
                var replay = await ReserveAsync(
                    connectionString,
                    tenantId,
                    clock,
                    matchingRequest);
                Assert.NotNull(replay);
                Assert.Equal(reserved.ReservationId, replay.ReservationId);

                SpaceAiBudgetReservationLease submitted;
                await using (var submitContext = CreateSqlContext(
                                 connectionString,
                                 tenantId,
                                 clock))
                {
                    var ledger = new EfSpaceAiCapacityLedger(
                        submitContext,
                        clock);
                    submitted = await ledger.MarkBudgetSubmittedAsync(
                        replay);
                }

                var report = new SpaceAiUsageReport(
                    submitted.ReservationId,
                    submitted.RowVersion,
                    "local-v1",
                    "warehouse-v1",
                    100,
                    20,
                    55,
                    250,
                    SpaceAiUsageOutcome.Succeeded,
                    clock.UtcNow);
                SpaceAiBudgetReservationLease reconciled;
                await using (var reportContext = CreateSqlContext(
                                 connectionString,
                                 tenantId,
                                 clock))
                {
                    var ledger = new EfSpaceAiCapacityLedger(
                        reportContext,
                        clock);
                    reconciled = await ledger.RecordUsageAsync(report);
                }
                Assert.Equal(
                    SpaceAiBudgetReservationStatus.Reconciled,
                    reconciled.Status);
                Assert.Equal(55, reconciled.ActualCostMinor);

                await using (var replayContext = CreateSqlContext(
                                 connectionString,
                                 tenantId,
                                 clock))
                {
                    var ledger = new EfSpaceAiCapacityLedger(
                        replayContext,
                        clock);
                    var idempotent = await ledger.RecordUsageAsync(report);
                    Assert.Equal(
                        reconciled.ReservationId,
                        idempotent.ReservationId);
                }

                await using var verify = CreateSqlContext(
                    connectionString,
                    tenantId,
                    clock);
                var usage = Assert.Single(
                    await verify.AiUsageRecords.ToListAsync());
                Assert.Equal(55, usage.ActualCostMinor);
                Assert.Equal(
                    reserved.ProviderRequestKey,
                    usage.ProviderRequestIdHash);
                Assert.Equal(
                    1,
                    await verify.AiBudgetReservations.CountAsync());
            });
    }

    [SqlServerFact]
    public async Task Expired_unsent_budget_releases_but_monthly_charge_remains()
    {
        var tenantId = Guid.NewGuid();
        var clock = new MutableClock(InitialNow);

        await WithDatabaseAsync(
            tenantId,
            clock,
            async connectionString =>
            {
                IReadOnlyList<Guid> runIds;
                await using (var seed = CreateSqlContext(
                                 connectionString,
                                 tenantId,
                                 clock))
                {
                    runIds = await SeedRunsAsync(seed, tenantId, 4);
                }

                var limits = new SpaceAiBudgetLimits(100, 100, "USD");
                var expiring = await ReserveAsync(
                    connectionString,
                    tenantId,
                    clock,
                    Request(runIds[0], 'c', 90, limits));
                Assert.NotNull(expiring);

                clock.UtcNow = InitialNow.AddMinutes(16);
                await using (var cleanupContext = CreateSqlContext(
                                 connectionString,
                                 tenantId,
                                 clock))
                {
                    var ledger = new EfSpaceAiCapacityLedger(
                        cleanupContext,
                        clock);
                    Assert.Equal(
                        1,
                        await ledger
                            .ReleaseExpiredBudgetReservationsAsync());
                }

                var charge = await ReserveAsync(
                    connectionString,
                    tenantId,
                    clock,
                    Request(runIds[1], 'd', 60, limits));
                Assert.NotNull(charge);
                SpaceAiBudgetReservationLease submitted;
                await using (var submitContext = CreateSqlContext(
                                 connectionString,
                                 tenantId,
                                 clock))
                {
                    var ledger = new EfSpaceAiCapacityLedger(
                        submitContext,
                        clock);
                    submitted = await ledger.MarkBudgetSubmittedAsync(charge);
                }
                await using (var reportContext = CreateSqlContext(
                                 connectionString,
                                 tenantId,
                                 clock))
                {
                    var ledger = new EfSpaceAiCapacityLedger(
                        reportContext,
                        clock);
                    await ledger.RecordUsageAsync(
                        new SpaceAiUsageReport(
                            submitted.ReservationId,
                            submitted.RowVersion,
                            "local-v1",
                            "warehouse-v1",
                            10,
                            5,
                            60,
                            100,
                            SpaceAiUsageOutcome.Succeeded,
                            clock.UtcNow));
                }

                clock.UtcNow = InitialNow.AddDays(1);
                Assert.Null(
                    await ReserveAsync(
                        connectionString,
                        tenantId,
                        clock,
                        Request(runIds[2], 'e', 50, limits)));
                Assert.NotNull(
                    await ReserveAsync(
                        connectionString,
                        tenantId,
                        clock,
                        Request(runIds[3], 'f', 40, limits)));

                await using var verify = CreateSqlContext(
                    connectionString,
                    tenantId,
                    clock);
                Assert.Equal(
                    SpaceAiBudgetReservationStatus.Released,
                    (await verify.AiBudgetReservations.SingleAsync(
                        item => item.Id == expiring.ReservationId)).Status);
                Assert.Equal(
                    3,
                    await verify.AiBudgetReservations.CountAsync());
                Assert.Single(await verify.AiUsageRecords.ToListAsync());
            });
    }

    private static async Task<SpaceAiWorkSlotLease?> AcquireAsync(
        string connectionString,
        Guid tenantId,
        MutableClock clock,
        Guid runId,
        string owner,
        int maxConcurrentRuns = 3)
    {
        await using var context = CreateSqlContext(
            connectionString,
            tenantId,
            clock);
        var ledger = new EfSpaceAiCapacityLedger(context, clock);
        return await ledger.TryAcquireWorkSlotAsync(
            runId,
            owner,
            maxConcurrentRuns,
            TimeSpan.FromSeconds(60));
    }

    private static async Task<SpaceAiBudgetReservationLease?> ReserveAsync(
        string connectionString,
        Guid tenantId,
        MutableClock clock,
        SpaceAiBudgetReservationRequest request)
    {
        await using var context = CreateSqlContext(
            connectionString,
            tenantId,
            clock);
        var ledger = new EfSpaceAiCapacityLedger(context, clock);
        return await ledger.TryReserveBudgetAsync(request);
    }

    private static SpaceAiBudgetReservationRequest Request(
        Guid runId,
        char key,
        long cost,
        SpaceAiBudgetLimits limits) =>
        new(
            runId,
            new string(key, 64),
            cost,
            limits,
            TimeSpan.FromMinutes(15));

    private static async Task<IReadOnlyList<Guid>> SeedRunsAsync(
        SpaceContext context,
        Guid tenantId,
        int count)
    {
        var model = SpaceModel.Create(tenantId, Guid.NewGuid());
        var version = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "AI capacity draft");
        var source = SpaceModelSource.CreateInlineSource(
            tenantId,
            version.Id,
            SpaceSourceType.Editor,
            "normalized features",
            Hash(1));
        context.AddRange(model, version, source);
        var runs = new List<SpaceGenerationRun>();
        for (var index = 0; index < count; index++)
        {
            var job = SpaceJob.CreateQueued(
                tenantId,
                SpaceJobType.BuildScene,
                SpaceJobSubjectType.ModelVersion,
                version.Id,
                Hash(100 + index),
                Hash(1),
                50,
                3,
                Guid.NewGuid(),
                InitialNow,
                Guid.NewGuid());
            var run = SpaceGenerationRun.Create(
                new SpaceGenerationRunDefinition(
                    tenantId,
                    model.SiteId,
                    version.Id,
                    source.Id,
                    Hash(1),
                    0,
                    Hash(200 + index),
                    Hash(300 + index),
                    null,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "rules-1",
                    SpaceAiPolicySnapshot.StructuredFeatures,
                    Guid.NewGuid(),
                    "1.0",
                    job.Id));
            context.AddRange(job, run);
            runs.Add(run);
        }
        await context.SaveChangesAsync();
        return runs.Select(run => run.Id).ToArray();
    }

    private static string Hash(int value) =>
        value.ToString("x64");

    private static SpaceContext CreateInMemoryContext(
        InMemoryDatabaseRoot root,
        string database,
        Guid tenantId,
        MutableClock clock) =>
        new(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(database, root)
                .Options,
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            clock);

    private static SpaceContext CreateSqlContext(
        string connectionString,
        Guid tenantId,
        MutableClock clock) =>
        new(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        SpaceContext.MigrationsHistoryTable))
                .Options,
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            clock);

    private static async Task WithDatabaseAsync(
        Guid tenantId,
        MutableClock clock,
        Func<string, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceE13S12_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        await using var context = CreateSqlContext(
            connectionString,
            tenantId,
            clock);

        try
        {
            await context.Database.MigrateAsync();
            await action(connectionString);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId)
        : ISpaceExecutionContext;

    private sealed class MutableClock(DateTime utcNow) : ISpaceClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }
}
