using CP6.Space.Application;
using CP6.Space.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Space.IntegrationTests;

public sealed class StandardSpaceWmsSimulatorTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SiteId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AttemptId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid CorrelationId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly string PlanHash = new('1', 64);

    [Fact]
    public async Task Capabilities_and_queries_identify_simulated_source()
    {
        var simulator = new StandardSpaceWmsSimulator();
        var capabilities = await simulator.GetCapabilitiesAsync(Context());
        var health = await simulator.CheckHealthAsync(Context());
        var locations = await simulator.QueryLocationsAsync(
            new SpaceWmsLocationQuery(Context(), []));

        Assert.Equal(
            SpaceWmsDataSourceKind.Simulated,
            capabilities.DataSourceKind);
        Assert.Equal(
            SpaceWmsCertificationLevel.CertifiedAtomic,
            capabilities.CertificationLevel);
        Assert.True(capabilities.SupportsProductionPublishing);
        Assert.Equal(SpaceWmsHealthState.Healthy, health.State);
        Assert.Equal(
            SpaceWmsDataSourceKind.Simulated,
            locations.Source.Kind);
        Assert.Equal(
            StandardSpaceWmsSimulator.DataSourceId,
            locations.Source.DataSourceId);
    }

    [Fact]
    public async Task Apply_replays_same_payload_and_rejects_key_conflict()
    {
        var simulator = new StandardSpaceWmsSimulator();
        var batch = Batch(Mutation(1, "A-01"));

        var first = await simulator.ApplyBatchAsync(batch);
        var replay = await simulator.ApplyBatchAsync(batch);
        var conflict = await simulator.ApplyBatchAsync(
            Batch(Mutation(1, "A-02")));
        var readBack = await simulator.ReadBackAsync(
            new SpaceWmsReadBackRequest(
                Context(),
                batch.OperationKey,
                batch.PayloadHash,
                batch.PlanHash,
                [LocationId(1)]));

        Assert.Equal(first, replay);
        Assert.Equal(
            SpaceWmsBatchAssessmentKind.Succeeded,
            SpaceWmsContract.AssessBatchResult(batch, first).Kind);
        Assert.Equal(
            SpaceWmsBatchAssessmentKind.FailedNoEffect,
            SpaceWmsContract.AssessBatchResult(
                Batch(Mutation(1, "A-02")),
                conflict).Kind);
        Assert.Equal(
            "WMS_IDEMPOTENCY_CONFLICT",
            Assert.Single(conflict.Items).ErrorCode);
        Assert.Equal("A-01", Assert.Single(readBack.Items).LocationCode);
        Assert.Equal(64, readBack.AggregateHash.Length);
    }

    [Fact]
    public async Task Seeded_inventory_and_tasks_are_queryable_and_block_disable()
    {
        var simulator = new StandardSpaceWmsSimulator();
        var locationId = LocationId(1);
        simulator.SeedInventory(
            Context(),
            [
                new SpaceWmsInventoryItem(
                    locationId,
                    "A-01",
                    12,
                    3,
                    "SKU-01",
                    "LOT-01",
                    "PALLET-01"),
            ]);
        simulator.SeedTasks(
            Context(),
            [
                new SpaceWmsTaskItem(
                    "PICK-001",
                    "Pick",
                    "Released",
                    1,
                    locationId,
                    "A-01",
                    3,
                    "SKU-01"),
            ]);
        var capabilities = await simulator.GetCapabilitiesAsync(Context());
        var mutation = Mutation(
            1,
            "A-01",
            SpaceWmsLocationAction.Disable);

        var inventory = await simulator.QueryInventoryAsync(
            new SpaceWmsInventoryQuery(Context(), [locationId]));
        var tasks = await simulator.QueryTasksAsync(
            new SpaceWmsTaskQuery(Context(), [locationId]));
        var references = await simulator.GetBlockingReferencesAsync(
            new SpaceWmsBlockingReferencesRequest(
                Context(),
                [locationId]));
        var preflight = await simulator.PreflightAsync(
            new SpaceWmsPreflightRequest(
                Context(),
                AttemptId,
                PlanHash,
                capabilities.CapabilityHash,
                [mutation]));
        var applied = await simulator.ApplyBatchAsync(Batch(mutation));

        Assert.Equal(12, Assert.Single(inventory.Items).PhysicalQuantity);
        Assert.Equal("PICK-001", Assert.Single(tasks.Items).TaskId);
        Assert.True(inventory.Source.IsSimulated);
        Assert.True(tasks.Source.IsSimulated);
        Assert.Equal(2, references.Items.Count);
        Assert.False(preflight.CanApply);
        Assert.Contains(
            preflight.Issues,
            issue => issue.Code == "SPACE_LOCATION_IN_USE");
        Assert.Equal(
            SpaceWmsBatchAssessmentKind.FailedNoEffect,
            SpaceWmsContract.AssessBatchResult(
                Batch(mutation),
                applied).Kind);
    }

    [Fact]
    public async Task Portal_scope_filters_owner_and_task_before_results_return()
    {
        var simulator = new StandardSpaceWmsSimulator();
        var locationId = LocationId(1);
        simulator.SeedInventory(
            Context(),
            [
                new SpaceWmsInventoryItem(
                    locationId,
                    "A-01",
                    12,
                    0,
                    "SKU-A",
                    null,
                    null,
                    "OWNER-A"),
                new SpaceWmsInventoryItem(
                    locationId,
                    "A-01",
                    99,
                    0,
                    "SKU-B",
                    null,
                    null,
                    "OWNER-B"),
            ]);
        simulator.SeedTasks(
            Context(),
            [
                new SpaceWmsTaskItem(
                    "PICK-A",
                    "Pick",
                    "Released",
                    1,
                    locationId,
                    "A-01",
                    1,
                    "SKU-A"),
                new SpaceWmsTaskItem(
                    "PICK-B",
                    "Pick",
                    "Released",
                    2,
                    locationId,
                    "A-01",
                    1,
                    "SKU-B"),
            ]);

        var inventory = await simulator.QueryInventoryAsync(
            new SpaceWmsInventoryQuery(
                Context(),
                [locationId],
                ["OWNER-A"]));
        var tasks = await simulator.QueryTasksAsync(
            new SpaceWmsTaskQuery(
                Context(),
                [locationId],
                ["PICK-A"]));

        Assert.Equal("SKU-A", Assert.Single(inventory.Items).MaterialNumber);
        Assert.Equal("PICK-A", Assert.Single(tasks.Items).TaskId);
    }

    [Fact]
    public async Task Partial_fault_applies_configured_prefix_and_is_replayable()
    {
        var simulator = new StandardSpaceWmsSimulator();
        simulator.ConfigureFault(
            Context(),
            new SpaceWmsSimulatorFaultProfile(
                SpaceWmsSimulatorFaultMode.Partial,
                ApplyCount: 1));
        var batch = Batch(
            Mutation(1, "A-01"),
            Mutation(2, "A-02"));

        var result = await simulator.ApplyBatchAsync(batch);
        simulator.ConfigureFault(
            Context(),
            SpaceWmsSimulatorFaultProfile.None);
        var replay = await simulator.ApplyBatchAsync(batch);
        var locations = await simulator.QueryLocationsAsync(
            new SpaceWmsLocationQuery(Context(), []));

        Assert.Equal(
            SpaceWmsBatchAssessmentKind.Partial,
            SpaceWmsContract.AssessBatchResult(batch, result).Kind);
        Assert.Equal(result, replay);
        Assert.Single(locations.Items);
        Assert.Equal("A-01", locations.Items[0].LocationCode);
    }

    [Fact]
    public async Task Unknown_after_apply_preserves_readback_evidence()
    {
        var simulator = new StandardSpaceWmsSimulator();
        simulator.ConfigureFault(
            Context(),
            new SpaceWmsSimulatorFaultProfile(
                SpaceWmsSimulatorFaultMode.UnknownAfterApply));
        var batch = Batch(Mutation(1, "A-01"));

        var result = await simulator.ApplyBatchAsync(batch);
        var status = await simulator.GetOperationStatusAsync(
            new SpaceWmsOperationQuery(
                Context(),
                batch.OperationKey,
                batch.PayloadHash));
        var readBack = await simulator.ReadBackAsync(
            new SpaceWmsReadBackRequest(
                Context(),
                batch.OperationKey,
                batch.PayloadHash,
                batch.PlanHash,
                [LocationId(1)]));

        Assert.Equal(
            SpaceWmsBatchAssessmentKind.Uncertain,
            SpaceWmsContract.AssessBatchResult(batch, result).Kind);
        Assert.Equal(SpaceWmsOperationState.Unknown, status.State);
        Assert.Single(readBack.Items);
        Assert.True(readBack.Items[0].IsActive);
    }

    [Fact]
    public async Task Timeout_unavailable_and_reject_all_are_deterministic()
    {
        var simulator = new StandardSpaceWmsSimulator();
        var batch = Batch(Mutation(1, "A-01"));
        simulator.ConfigureFault(
            Context(),
            new SpaceWmsSimulatorFaultProfile(
                SpaceWmsSimulatorFaultMode.Timeout));
        var timeout = await Assert.ThrowsAsync<TimeoutException>(
            () => simulator.ApplyBatchAsync(batch));
        Assert.Equal("SPACE_WMS_RETRYABLE", timeout.Message);

        simulator.ConfigureFault(
            Context(),
            new SpaceWmsSimulatorFaultProfile(
                SpaceWmsSimulatorFaultMode.Unavailable));
        var health = await simulator.CheckHealthAsync(Context());
        Assert.Equal(SpaceWmsHealthState.Unavailable, health.State);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => simulator.ApplyBatchAsync(batch));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => simulator.QueryInventoryAsync(
                new SpaceWmsInventoryQuery(Context(), [])));

        simulator.ConfigureFault(
            Context(),
            new SpaceWmsSimulatorFaultProfile(
                SpaceWmsSimulatorFaultMode.RejectAll,
                ErrorCode: "SIM_VALIDATION_REJECTED"));
        var rejected = await simulator.ApplyBatchAsync(batch);
        Assert.Equal(
            SpaceWmsBatchAssessmentKind.FailedNoEffect,
            SpaceWmsContract.AssessBatchResult(batch, rejected).Kind);
        Assert.Equal(
            "SIM_VALIDATION_REJECTED",
            Assert.Single(rejected.Items).ErrorCode);
    }

    [Fact]
    public async Task State_is_isolated_by_scope_and_reset_is_targeted()
    {
        var simulator = new StandardSpaceWmsSimulator();
        var batch = Batch(Mutation(1, "A-01"));
        await simulator.ApplyBatchAsync(batch);
        var other = Context() with { SiteId = Guid.NewGuid() };

        var otherLocations = await simulator.QueryLocationsAsync(
            new SpaceWmsLocationQuery(other, []));
        simulator.Reset(Context());
        var resetLocations = await simulator.QueryLocationsAsync(
            new SpaceWmsLocationQuery(Context(), []));

        Assert.Empty(otherLocations.Items);
        Assert.Empty(resetLocations.Items);
    }

    [Fact]
    public async Task Injected_latency_honors_cancellation()
    {
        var simulator = new StandardSpaceWmsSimulator();
        simulator.ConfigureFault(
            Context(),
            new SpaceWmsSimulatorFaultProfile(
                SpaceWmsSimulatorFaultMode.None,
                Delay: TimeSpan.FromSeconds(30)));
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => simulator.QueryTasksAsync(
                new SpaceWmsTaskQuery(Context(), []),
                cancellation.Token));
    }

    [Fact]
    public void Registration_exposes_control_without_replacing_cp6_adapter()
    {
        var services = new ServiceCollection();
        services.AddSpaceDesignV1Persistence(
            "Server=(localdb)\\MSSQLLocalDB;Database=unused");

        var adapter = Assert.Single(
            services,
            value => value.ServiceType == typeof(ISpaceWmsAdapter));
        Assert.Equal(typeof(Cp6SpaceWmsAdapter), adapter.ImplementationType);
        var runtimeSource = Assert.Single(
            services,
            value => value.ServiceType == typeof(ISpaceWmsRuntimeSource));
        Assert.NotNull(runtimeSource.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, runtimeSource.Lifetime);
        var runtimeService = Assert.Single(
            services,
            value => value.ServiceType == typeof(ISpaceWmsRuntimeService));
        Assert.Equal(
            typeof(SpaceWmsRuntimeService),
            runtimeService.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, runtimeService.Lifetime);
        Assert.Contains(
            services,
            value =>
                value.ServiceType == typeof(StandardSpaceWmsSimulator) &&
                value.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(
            services,
            value =>
                value.ServiceType == typeof(ISpaceWmsSimulatorControl) &&
                value.Lifetime == ServiceLifetime.Singleton);
        using var provider = services.BuildServiceProvider();
        Assert.Same(
            provider.GetRequiredService<StandardSpaceWmsSimulator>(),
            provider.GetRequiredService<ISpaceWmsSimulatorControl>());
    }

    private static SpaceWmsContext Context() =>
        new(TenantId, SiteId, "SIM-WH-01", CorrelationId);

    private static SpaceWmsBatch Batch(
        params SpaceWmsLocationMutation[] items) =>
        SpaceWmsBatch.Create(
            Context(),
            AttemptId,
            1,
            PlanHash,
            items);

    private static SpaceWmsLocationMutation Mutation(
        int sequenceNo,
        string code,
        SpaceWmsLocationAction action = SpaceWmsLocationAction.Create,
        long version = 1) =>
        SpaceWmsLocationMutation.Create(
            sequenceNo,
            LocationId(sequenceNo),
            code,
            action,
            new SpaceWmsLocationPath(
                "SIM-SITE",
                1,
                "ZONE-A",
                "AISLE-01",
                "RACK-01",
                sequenceNo,
                1,
                1),
            version: version);

    private static Guid LocationId(int value) =>
        Guid.Parse($"aaaaaaaa-aaaa-aaaa-aaaa-{value:D12}");
}
