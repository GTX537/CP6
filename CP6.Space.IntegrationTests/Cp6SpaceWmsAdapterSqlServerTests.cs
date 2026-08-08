using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Wms;
using CP6.Space.Application;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class Cp6SpaceWmsAdapterSqlServerTests
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

    [SqlServerFact]
    public async Task Migration_adapter_and_transaction_contracts_close()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var firstBatch = Batch(1, Mutation(1, "A-01"));
            await using (var writer = CreateContext(connectionString))
            {
                var adapter = new Cp6SpaceWmsAdapter(writer);

                var applied = await adapter.ApplyBatchAsync(firstBatch);

                Assert.Equal(
                    SpaceWmsBatchAssessmentKind.Succeeded,
                    SpaceWmsContract
                        .AssessBatchResult(firstBatch, applied)
                        .Kind);
            }

            await using (var replayContext =
                         CreateContext(connectionString))
            {
                var replayAdapter =
                    new Cp6SpaceWmsAdapter(replayContext);
                var replay =
                    await replayAdapter.ApplyBatchAsync(firstBatch);
                var status = await replayAdapter.GetOperationStatusAsync(
                    new SpaceWmsOperationQuery(
                        Context(),
                        firstBatch.OperationKey,
                        firstBatch.PayloadHash));

                Assert.Equal(
                    SpaceWmsOperationState.Applied,
                    status.State);
                Assert.Equal(
                    firstBatch.OperationKey,
                    replay.OperationKey);
                Assert.Single(
                    await replayContext.WmsBins.ToListAsync());
                Assert.Single(
                    await replayContext.SpaceWmsOperations.ToListAsync());
                var completeCatalog =
                    await replayAdapter.QueryLocationsAsync(
                        new SpaceWmsLocationQuery(Context(), []));
                Assert.Equal(
                    LocationId(1),
                    Assert.Single(completeCatalog.Items).LogicalId);
            }

            await using (var partialContext =
                         CreateContext(connectionString))
            {
                partialContext.Stocks.Add(new Stock
                {
                    Id = Guid.NewGuid(),
                    WarehouseCd = "WH-01",
                    LocationCd = "A-01",
                    ProductCd = "SKU-01",
                    LotNo = "LOT-01",
                    PhysicalQty = 5,
                    AvailableQty = 5,
                });
                await partialContext.SaveChangesAsync();
                var adapter =
                    new Cp6SpaceWmsAdapter(partialContext);
                var partialBatch = Batch(
                    2,
                    Mutation(
                        1,
                        "A-01",
                        SpaceWmsLocationAction.Disable,
                        version: 2),
                    Mutation(2, "A-02"));

                var result =
                    await adapter.ApplyBatchAsync(partialBatch);

                Assert.Equal(
                    SpaceWmsBatchAssessmentKind.Partial,
                    SpaceWmsContract
                        .AssessBatchResult(partialBatch, result)
                        .Kind);
                Assert.True((await partialContext.WmsBins.SingleAsync(
                    bin => bin.Id == LocationId(1))).IsActive);
                Assert.True((await partialContext.WmsBins.SingleAsync(
                    bin => bin.Id == LocationId(2))).IsActive);
                Assert.Equal(
                    (int)SpaceWmsOperationState.Partial,
                    (await partialContext.SpaceWmsOperations.SingleAsync(
                        operation =>
                            operation.OperationKey ==
                            partialBatch.OperationKey)).State);
            }

            await using var uniqueContext =
                CreateContext(connectionString);
            var original = await uniqueContext.SpaceWmsOperations
                .AsNoTracking()
                .FirstAsync();
            uniqueContext.SpaceWmsOperations.Add(new SpaceWmsOperation
            {
                Id = Guid.NewGuid(),
                OperationKey = original.OperationKey,
                PayloadHash = original.PayloadHash,
                State = original.State,
                ResultJson = original.ResultJson,
                ObservedAtUtc = original.ObservedAtUtc,
            });

            await Assert.ThrowsAsync<DbUpdateException>(
                () => uniqueContext.SaveChangesAsync());
        });
    }

    [SqlServerFact]
    public async Task Inventory_spatial_filter_joins_container_to_its_unique_stock_owner()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var adapter = new Cp6SpaceWmsAdapter(context);
            var batch = Batch(1, Mutation(1, "A-01"));
            var applied = await adapter.ApplyBatchAsync(batch);
            Assert.Equal(
                SpaceWmsBatchAssessmentKind.Succeeded,
                SpaceWmsContract.AssessBatchResult(batch, applied).Kind);

            context.Stocks.Add(new Stock
            {
                Id = Guid.NewGuid(),
                WarehouseCd = "WH-01",
                LocationCd = "A-01",
                ProductCd = "SKU-01",
                LotNo = "LOT-01",
                PhysicalQty = 12,
                AvailableQty = 12,
                OwnerCd = "OWNER-A",
            });
            context.Pallets.Add(new Pallet
            {
                Id = Guid.NewGuid(),
                PalletNo = "PALLET-01",
                ProductCd = "SKU-01",
                LotNo = "LOT-01",
                CartonQty = 8,
                WarehouseCd = "WH-01",
                LocationCd = "A-01",
                Status = PalletStatus.InStock,
            });
            await context.SaveChangesAsync();

            var matched = await adapter.QueryInventoryAsync(
                new SpaceWmsInventoryQuery(
                    Context(),
                    [LocationId(1)],
                    LocateCriteria: new SpaceWmsInventoryLocateCriteria(
                        "SKU-01",
                        "LOT-01",
                        "PALLET-01",
                        "owner-a")));
            var rejected = await adapter.QueryInventoryAsync(
                new SpaceWmsInventoryQuery(
                    Context(),
                    [LocationId(1)],
                    LocateCriteria: new SpaceWmsInventoryLocateCriteria(
                        "SKU-01",
                        "LOT-01",
                        "PALLET-01",
                        "OWNER-B")));

            var item = Assert.Single(matched.Items);
            Assert.Equal("OWNER-A", item.OwnerId);
            Assert.Equal("PALLET-01", item.ContainerNumber);
            Assert.Empty(rejected.Items);
        });
    }

    [SqlServerFact]
    public async Task Abc_query_translates_and_enforces_warehouse_and_half_open_window()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            await using var context = CreateContext(connectionString);
            var windowStart = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
            context.StockTransactions.AddRange(
                Transaction("TX-ABC-001", WmsTxnType.OUT, "WH-01", "SKU-A", 8, windowStart.AddDays(1)),
                Transaction("TX-ABC-002", WmsTxnType.OUT, "WH-01", "SKU-A", 2, windowStart.AddDays(2)),
                Transaction("TX-ABC-003", WmsTxnType.OUT, "WH-01", "SKU-B", 4, windowStart.AddDays(2)),
                Transaction("TX-ABC-004", WmsTxnType.IN, "WH-01", "SKU-C", 20, windowStart.AddDays(2)),
                Transaction("TX-ABC-005", WmsTxnType.OUT, "WH-02", "SKU-D", 30, windowStart.AddDays(2)),
                Transaction("TX-ABC-006", WmsTxnType.OUT, "WH-01", "SKU-E", 40, windowStart.AddDays(4)));
            await context.SaveChangesAsync();
            var adapter = new Cp6SpaceWmsAdapter(context);

            var result = await adapter.QueryAbcAsync(
                new SpaceWmsAbcQuery(
                    Context(),
                    DateOnly.FromDateTime(windowStart),
                    DateOnly.FromDateTime(windowStart.AddDays(4))));

            Assert.Equal(SpaceWmsDataSourceKind.Real, result.Source.Kind);
            Assert.Collection(
                result.Items,
                item =>
                {
                    Assert.Equal("SKU-A", item.MaterialNumber);
                    Assert.Equal(2, item.OutboundMovementCount);
                    Assert.Equal(10, item.OutboundQuantity);
                },
                item =>
                {
                    Assert.Equal("SKU-B", item.MaterialNumber);
                    Assert.Equal(1, item.OutboundMovementCount);
                    Assert.Equal(4, item.OutboundQuantity);
                });
        });
    }

    private static async Task WithDatabaseAsync(
        Func<string, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6SpaceE07_{Guid.NewGuid():N}",
            TrustServerCertificate = true,
        }.ConnectionString;
        await using var context = CreateContext(connectionString);
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

    private static CP6Context CreateContext(string connectionString)
    {
        var tenant = new TenantContext
        {
            CurrentTenantId = TenantId,
        };
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseSqlServer(connectionString)
            .Options;
        return new CP6Context(options, tenant);
    }

    private static SpaceWmsContext Context() =>
        new(TenantId, SiteId, "WH-01", CorrelationId);

    private static StockTransaction Transaction(
        string transactionNumber,
        string transactionType,
        string warehouseCode,
        string productCode,
        decimal quantity,
        DateTime transactionTimeUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            TxnNo = transactionNumber,
            TxnType = transactionType,
            TxnDateTime = transactionTimeUtc,
            WarehouseCd = warehouseCode,
            LocationCd = "A-01",
            ProductCd = productCode,
            LotNo = "LOT-01",
            Qty = quantity,
        };

    private static SpaceWmsBatch Batch(
        int batchNo,
        params SpaceWmsLocationMutation[] items) =>
        SpaceWmsBatch.Create(
            Context(),
            AttemptId,
            batchNo,
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
