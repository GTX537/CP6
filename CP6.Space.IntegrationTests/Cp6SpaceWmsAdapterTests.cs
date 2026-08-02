using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Wms;
using CP6.Space.Application;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class Cp6SpaceWmsAdapterTests
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
    public async Task Apply_persists_bin_and_replayable_operation_result()
    {
        await using var db = NewDb();
        var adapter = new Cp6SpaceWmsAdapter(db);
        var batch = Batch(Mutation(1, "A-01"));

        var first = await adapter.ApplyBatchAsync(batch);
        var replay = await adapter.ApplyBatchAsync(batch);
        var status = await adapter.GetOperationStatusAsync(
            new SpaceWmsOperationQuery(
                Context(),
                batch.OperationKey,
                batch.PayloadHash));

        Assert.Equal(first.OperationKey, replay.OperationKey);
        Assert.Equal(first.PayloadHash, replay.PayloadHash);
        Assert.Equal(first.ExternalOperationId, replay.ExternalOperationId);
        Assert.Equal(first.Items, replay.Items);
        Assert.Equal(first.ObservedAtUtc, replay.ObservedAtUtc);
        Assert.Equal(
            SpaceWmsBatchAssessmentKind.Succeeded,
            SpaceWmsContract.AssessBatchResult(batch, first).Kind);
        Assert.Equal(SpaceWmsOperationState.Applied, status.State);
        Assert.Single(await db.WmsBins.ToListAsync());
        Assert.Single(await db.SpaceWmsOperations.ToListAsync());
        var bin = await db.WmsBins.SingleAsync();
        Assert.True(bin.IsActive);
        Assert.Equal(1, bin.Version);
        Assert.Equal("WH-01", bin.WarehouseCd);
    }

    [Fact]
    public async Task Reusing_operation_key_with_new_payload_is_zero_effect()
    {
        await using var db = NewDb();
        var adapter = new Cp6SpaceWmsAdapter(db);
        var first = Batch(Mutation(1, "A-01"));
        var conflict = Batch(Mutation(1, "A-02"));

        await adapter.ApplyBatchAsync(first);
        var result = await adapter.ApplyBatchAsync(conflict);

        Assert.Equal(
            SpaceWmsBatchAssessmentKind.FailedNoEffect,
            SpaceWmsContract.AssessBatchResult(conflict, result).Kind);
        Assert.All(
            result.Items,
            item => Assert.Equal(
                "WMS_IDEMPOTENCY_CONFLICT",
                item.ErrorCode));
        Assert.Single(await db.WmsBins.ToListAsync());
        Assert.Equal("A-01", (await db.WmsBins.SingleAsync()).LocationCode);
        Assert.Single(await db.SpaceWmsOperations.ToListAsync());
    }

    [Fact]
    public async Task Stock_blocked_disable_and_successful_create_are_partial()
    {
        await using var db = NewDb();
        var existingId = LocationId(1);
        db.WmsBins.Add(new WmsBin
        {
            Id = existingId,
            LocationCode = "A-01",
            WarehouseCd = "WH-01",
            Version = 1,
            PathJson = "{}",
            AttrsJson = "{}",
            IsActive = true,
        });
        db.Stocks.Add(new Stock
        {
            Id = Guid.NewGuid(),
            WarehouseCd = "WH-01",
            LocationCd = "A-01",
            ProductCd = "SKU-01",
            LotNo = "LOT-01",
            PhysicalQty = 10,
            AllocatedQty = 0,
            AvailableQty = 10,
        });
        await db.SaveChangesAsync();
        var adapter = new Cp6SpaceWmsAdapter(db);
        var batch = Batch(
            Mutation(
                1,
                "A-01",
                SpaceWmsLocationAction.Disable,
                version: 2),
            Mutation(2, "A-02"));

        var preflight = await adapter.PreflightAsync(
            new SpaceWmsPreflightRequest(
                Context(),
                AttemptId,
                PlanHash,
                (await adapter.GetCapabilitiesAsync(Context()))
                    .CapabilityHash,
                batch.Items));
        var result = await adapter.ApplyBatchAsync(batch);
        var status = await adapter.GetOperationStatusAsync(
            new SpaceWmsOperationQuery(
                Context(),
                batch.OperationKey,
                batch.PayloadHash));

        Assert.False(preflight.CanApply);
        Assert.Contains(
            preflight.Issues,
            issue =>
                issue.LogicalId == existingId &&
                issue.Code == "SPACE_LOCATION_IN_USE");
        Assert.Equal(
            SpaceWmsBatchAssessmentKind.Partial,
            SpaceWmsContract.AssessBatchResult(batch, result).Kind);
        Assert.Equal(SpaceWmsOperationState.Partial, status.State);
        Assert.True((await db.WmsBins.SingleAsync(
            bin => bin.Id == existingId)).IsActive);
        Assert.True((await db.WmsBins.SingleAsync(
            bin => bin.Id == LocationId(2))).IsActive);
    }

    [Fact]
    public async Task Disable_creates_tombstone_and_readback_proves_state()
    {
        await using var db = NewDb();
        var adapter = new Cp6SpaceWmsAdapter(db);
        var batch = Batch(Mutation(
            1,
            "A-01",
            SpaceWmsLocationAction.Disable,
            version: 2));

        var result = await adapter.ApplyBatchAsync(batch);
        var readBack = await adapter.ReadBackAsync(
            new SpaceWmsReadBackRequest(
                Context(),
                batch.OperationKey,
                batch.PayloadHash,
                batch.PlanHash,
                [LocationId(1)]));

        Assert.Equal(
            SpaceWmsBatchAssessmentKind.Succeeded,
            SpaceWmsContract.AssessBatchResult(batch, result).Kind);
        var state = Assert.Single(readBack.Items);
        Assert.False(state.IsActive);
        Assert.Equal("2", state.ExternalVersion);
        Assert.Equal(64, state.StateHash.Length);
        Assert.Equal(64, readBack.AggregateHash.Length);
    }

    [Fact]
    public async Task Inventory_and_task_queries_keep_real_source_provenance()
    {
        await using var db = NewDb();
        var locationId = LocationId(1);
        db.WmsBins.Add(new WmsBin
        {
            Id = locationId,
            LocationCode = "A-01",
            WarehouseCd = "WH-01",
            Version = 1,
            IsActive = true,
        });
        db.Stocks.Add(new Stock
        {
            Id = Guid.NewGuid(),
            WarehouseCd = "WH-01",
            LocationCd = "A-01",
            ProductCd = "SKU-01",
            LotNo = "LOT-01",
            PhysicalQty = 10,
            AllocatedQty = 3,
            AvailableQty = 7,
        });
        db.OutboundOrders.Add(new OutboundOrder
        {
            Id = Guid.NewGuid(),
            OutboundNo = "OUT-001",
            WarehouseCd = "WH-01",
            Status = OutboundOrderStatus.Picking,
        });
        db.OutboundOrderDetails.Add(new OutboundOrderDetail
        {
            Id = Guid.NewGuid(),
            OutboundNo = "OUT-001",
            LineNo = 1,
            ProductCd = "SKU-01",
            RequiredQty = 3,
            AllocatedQty = 3,
            ShippedQty = 0,
            LocationCd = "A-01",
            WarehouseCd = "WH-01",
        });
        await db.SaveChangesAsync();
        var adapter = new Cp6SpaceWmsAdapter(db);

        var inventory = await adapter.QueryInventoryAsync(
            new SpaceWmsInventoryQuery(Context(), [locationId]));
        var tasks = await adapter.QueryTasksAsync(
            new SpaceWmsTaskQuery(Context(), [locationId]));
        var blocking = await adapter.GetBlockingReferencesAsync(
            new SpaceWmsBlockingReferencesRequest(
                Context(),
                [locationId]));

        Assert.Equal(SpaceWmsDataSourceKind.Real, inventory.Source.Kind);
        Assert.Equal(Cp6SpaceWmsAdapter.DataSourceId, tasks.Source.DataSourceId);
        Assert.Equal(10, Assert.Single(inventory.Items).PhysicalQuantity);
        Assert.Equal("OUT-001", Assert.Single(tasks.Items).TaskId);
        Assert.Contains(
            blocking.Items,
            item => item.Kind == SpaceWmsBlockingReferenceKind.Inventory);
        Assert.Contains(
            blocking.Items,
            item => item.Kind == SpaceWmsBlockingReferenceKind.ActiveTask);
    }

    [Fact]
    public async Task Inventory_locate_filters_stock_and_active_container_with_and_semantics()
    {
        await using var db = NewDb();
        var firstId = LocationId(1);
        var secondId = LocationId(2);
        db.WmsBins.AddRange(
            new WmsBin
            {
                Id = firstId,
                LocationCode = "A-01",
                WarehouseCd = "WH-01",
                Version = 1,
                IsActive = true,
            },
            new WmsBin
            {
                Id = secondId,
                LocationCode = "A-02",
                WarehouseCd = "WH-01",
                Version = 1,
                IsActive = true,
            });
        db.Stocks.AddRange(
            new Stock
            {
                Id = Guid.NewGuid(),
                WarehouseCd = "WH-01",
                LocationCd = "A-01",
                ProductCd = "SKU-01",
                LotNo = "LOT-01",
                PhysicalQty = 10,
            },
            new Stock
            {
                Id = Guid.NewGuid(),
                WarehouseCd = "WH-01",
                LocationCd = "A-02",
                ProductCd = "SKU-01",
                LotNo = "LOT-02",
                PhysicalQty = 20,
                OwnerCd = "OWNER-A",
            });
        db.Pallets.AddRange(
            new Pallet
            {
                Id = Guid.NewGuid(),
                PalletNo = "PALLET-01",
                ProductCd = "SKU-01",
                LotNo = "LOT-02",
                CartonQty = 8,
                WarehouseCd = "WH-01",
                LocationCd = "A-02",
                Status = PalletStatus.InStock,
            },
            new Pallet
            {
                Id = Guid.NewGuid(),
                PalletNo = "PALLET-SHIPPED",
                ProductCd = "SKU-01",
                LotNo = "LOT-01",
                CartonQty = 9,
                WarehouseCd = "WH-01",
                LocationCd = "A-01",
                Status = PalletStatus.Shipped,
            });
        await db.SaveChangesAsync();
        var adapter = new Cp6SpaceWmsAdapter(db);

        var stock = await adapter.QueryInventoryAsync(
            new SpaceWmsInventoryQuery(
                Context(),
                [firstId, secondId],
                LocateCriteria: new SpaceWmsInventoryLocateCriteria(
                    "SKU-01",
                    "LOT-01",
                    null)));
        var container = await adapter.QueryInventoryAsync(
            new SpaceWmsInventoryQuery(
                Context(),
                [firstId, secondId],
                LocateCriteria: new SpaceWmsInventoryLocateCriteria(
                    "SKU-01",
                    "LOT-02",
                    "PALLET-01",
                    "owner-a")));
        var wrongOwner = await adapter.QueryInventoryAsync(
            new SpaceWmsInventoryQuery(
                Context(),
                [firstId, secondId],
                LocateCriteria: new SpaceWmsInventoryLocateCriteria(
                    "SKU-01",
                    "LOT-02",
                    "PALLET-01",
                    "OWNER-B")));
        var shipped = await adapter.QueryInventoryAsync(
            new SpaceWmsInventoryQuery(
                Context(),
                [firstId, secondId],
                LocateCriteria: new SpaceWmsInventoryLocateCriteria(
                    null,
                    null,
                    "PALLET-SHIPPED")));

        Assert.Equal(firstId, Assert.Single(stock.Items).LogicalId);
        var pallet = Assert.Single(container.Items);
        Assert.Equal(secondId, pallet.LogicalId);
        Assert.Equal("PALLET-01", pallet.ContainerNumber);
        Assert.Equal(8, pallet.PhysicalQuantity);
        Assert.Equal("OWNER-A", pallet.OwnerId);
        Assert.Empty(wrongOwner.Items);
        Assert.Empty(shipped.Items);
    }

    [Fact]
    public async Task Tenant_mismatch_is_rejected_before_any_wms_query()
    {
        await using var db = NewDb();
        var adapter = new Cp6SpaceWmsAdapter(db);
        var otherTenantContext = Context() with
        {
            TenantId = Guid.Parse(
                "99999999-9999-9999-9999-999999999999"),
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.GetCapabilitiesAsync(otherTenantContext));

        Assert.Equal("SPACE_TENANT_SCOPE_DENIED", error.Message);
        Assert.Empty(await db.WmsBins.ToListAsync());
        Assert.Empty(await db.SpaceWmsOperations.ToListAsync());
    }

    [Fact]
    public async Task Operation_status_rejects_another_site_before_ledger_query()
    {
        await using var db = NewDb();
        var adapter = new Cp6SpaceWmsAdapter(db);
        var otherSiteId =
            Guid.Parse("99999999-9999-9999-9999-999999999999");
        var foreignKey = SpaceWmsContract.CreateOperationKey(
            TenantId,
            otherSiteId,
            AttemptId,
            1);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.GetOperationStatusAsync(
                new SpaceWmsOperationQuery(
                    Context(),
                    foreignKey,
                    new string('1', 64))));

        Assert.Equal("SPACE_WMS_OPERATION_SCOPE_DENIED", error.Message);
        Assert.Empty(await db.SpaceWmsOperations.ToListAsync());
    }

    [Fact]
    public async Task Apply_rejects_a_dirty_context_without_saving_caller_writes()
    {
        await using var db = NewDb();
        var pending = new Stock
        {
            Id = Guid.NewGuid(),
            WarehouseCd = "WH-01",
            LocationCd = "A-99",
            ProductCd = "SKU-01",
            LotNo = "LOT-01",
            PhysicalQty = 1,
            AllocatedQty = 0,
            AvailableQty = 1,
        };
        db.Stocks.Add(pending);
        var adapter = new Cp6SpaceWmsAdapter(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => adapter.ApplyBatchAsync(Batch(Mutation(1, "A-01"))));

        Assert.Equal("SPACE_WMS_CONTEXT_DIRTY", error.Message);
        Assert.Equal(EntityState.Added, db.Entry(pending).State);
        Assert.Empty(await db.WmsBins.ToListAsync());
        Assert.Empty(await db.SpaceWmsOperations.ToListAsync());
    }

    private static CP6Context NewDb()
    {
        var tenant = new TenantContext
        {
            CurrentTenantId = TenantId,
        };
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CP6Context(options, tenant);
    }

    private static SpaceWmsContext Context() =>
        new(TenantId, SiteId, "WH-01", CorrelationId);

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
                "SITE-01",
                1,
                "ZONE-01",
                "AISLE-01",
                "RACK-01",
                sequenceNo,
                1,
                1),
            version: version);

    private static Guid LocationId(int value) =>
        Guid.Parse($"aaaaaaaa-aaaa-aaaa-aaaa-{value:D12}");
}
