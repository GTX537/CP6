using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Infra;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace CP6.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WmsProductionSqlCollection : ICollectionFixture<WmsProductionSqlFixture>
{
    public const string Name = "WMS production SQL Server";
}

public sealed class WmsProductionSqlFixture : IAsyncLifetime
{
    private bool _ownsDatabase;
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvVar);
        if (string.IsNullOrWhiteSpace(configured)) return;
        var builder = new SqlConnectionStringBuilder(configured)
        {
            TrustServerCertificate = true,
            MultipleActiveResultSets = true,
        };
        if (string.IsNullOrWhiteSpace(builder.InitialCatalog)
            || string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase))
        {
            builder.InitialCatalog = $"CP6_R2_TEST_{Guid.NewGuid():N}";
            _ownsDatabase = true;
        }
        ConnectionString = builder.ConnectionString;
        await using var db = Create(Guid.NewGuid());
        await db.Database.MigrateAsync();
    }

    public CP6Context Create(Guid tenantId)
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw SkipException.ForSkip("SQL Server integration connection is not configured.");
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseSqlServer(ConnectionString, sql => sql.CommandTimeout(60))
            .Options;
        return new CP6Context(options, new TenantContext { CurrentTenantId = tenantId });
    }

    public async Task DisposeAsync()
    {
        if (!_ownsDatabase || string.IsNullOrWhiteSpace(ConnectionString)) return;
        await using var db = Create(Guid.NewGuid());
        await db.Database.EnsureDeletedAsync();
    }
}

[Collection(WmsProductionSqlCollection.Name)]
public sealed class WmsProductionSqlServerTests(WmsProductionSqlFixture fixture)
{
    [SqlServerFact]
    public async Task Move_ConcurrentClaim_PartialCompletion_AndReplay_AreAtomic()
    {
        var tenant = Guid.NewGuid();
        await using (var seed = fixture.Create(tenant))
        {
            SeedWarehouse(seed, serialEnabled: false);
            seed.ClientDevices.AddRange(
                Device("device-a"), Device("device-b"));
            await seed.SaveChangesAsync();
        }

        MobileTaskV2Dto created;
        await using (var db = fixture.Create(tenant))
            created = await MoveService(db).CreateAsync(new CreateMoveTaskV2Request
            {
                OperationId = Guid.NewGuid(),
                WarehouseCd = "W01",
                AreaCd = "PICK-A",
                FromLocationCd = "A-01",
                ToLocationCd = "B-02",
                ProductCd = "P-100",
                Qty = 5,
            }, "dispatcher");

        Assert.NotEmpty(created.RowVersion);
        var claims = await Task.WhenAll(
            TryClaim(tenant, created, "device-a", "alice"),
            TryClaim(tenant, created, "device-b", "bob"));
        var winner = Assert.Single(claims, x => x.Task is not null);
        Assert.Single(claims, x => x.Error is MobileTaskConflictException);

        await using var work = fixture.Create(tenant);
        var service = MoveService(work);
        var task = await service.GetAsync(created.TaskNo) ?? throw new XunitException("Task disappeared.");
        var device = winner.Task!.AssignedTo == "alice" ? "device-a" : "device-b";
        var user = winner.Task.AssignedTo!;
        foreach (var (step, barcode) in new[]
                 {
                     ("SourceLocation", "A-01"),
                     ("Product", "P-100"),
                     ("TargetLocation", "B-02"),
                     ("Quantity", "3"),
                 })
            Assert.True((await service.ScanAsync(task.TaskNo, new ScanCommand
            {
                OperationId = Guid.NewGuid(),
                RowVersion = task.RowVersion,
                DeviceId = device,
                ExecutionVersion = task.ExecutionVersion,
                Step = step,
                RawBarcode = barcode,
                ClientScanNo = Guid.NewGuid().ToString("N"),
                ScannedAt = DateTimeOffset.UtcNow,
            }, user)).Matched);

        var completionId = Guid.NewGuid();
        var complete = new CompleteMoveV2Request
        {
            OperationId = completionId,
            RowVersion = task.RowVersion,
            DeviceId = device,
            ExecutionVersion = task.ExecutionVersion,
            ScannedQty = 3,
            ToLocationCd = "B-02",
            PartialReason = "DAMAGED_REMAINDER",
        };
        var first = await service.CompleteAsync(task.TaskNo, complete, user);
        var replay = await service.CompleteAsync(task.TaskNo, complete, user);

        Assert.Equal(MobileTaskStatus.PartiallyCompleted, first.Status);
        Assert.Equal(first.TaskNo, replay.TaskNo);
        Assert.Equal(completionId, replay.CompletionOperationId);
        var source = await work.Stocks.SingleAsync(x => x.LocationCd == "A-01");
        var target = await work.Stocks.SingleAsync(x => x.LocationCd == "B-02");
        Assert.Equal(7m, source.PhysicalQty);
        Assert.Equal(2m, source.AllocatedQty);
        Assert.Equal(3m, target.PhysicalQty);
        Assert.Equal(2m, (await work.Locations.SingleAsync(x => x.LocationCd == "B-02"))
            .ReservedCapacityQty);
        var remainder = await work.MobileTasks.SingleAsync(
            x => x.ParentTaskNo == task.TaskNo);
        Assert.Equal(2m, remainder.Qty);
        Assert.Equal(MobileTaskStatus.Pending, remainder.Status);
        Assert.Equal(3, await work.StockTransactions.CountAsync());
        Assert.Single(await work.TaskCommandReceipts
            .Where(x => x.OperationId == completionId).ToListAsync());
    }

    [SqlServerFact]
    public async Task Replenish_SourceAndMoveTask_StayTransactionalThroughCompletion()
    {
        var tenant = Guid.NewGuid();
        await using var db = fixture.Create(tenant);
        SeedWarehouse(db, serialEnabled: false);
        db.ClientDevices.Add(Device("replenish-rf"));
        await db.SaveChangesAsync();

        var sequence = new WmsSequenceService(db);
        var stock = new StockMovementService(db, sequence);
        var accessScopes = new FixedWmsAccessScopeProvider(
            new WmsAccessScope(
                false,
                [new WmsScopeGrant("W01", "PICK-A")]));
        var tasks = new MobileTaskV2Service(
            db,
            sequence,
            stock,
            accessScopes);
        var replenish = new ReplenishService(
            db,
            sequence,
            tasks,
            accessScopes);
        var sourceNo = await replenish.CreateAsync(new ReplenishOrderDto
        {
            ProductCd = "P-100",
            WarehouseCd = "W01",
            FromLocationCd = "A-01",
            ToLocationCd = "B-02",
            Qty = 5
        }, "dispatcher");
        var taskNo = await replenish.ExecuteAsync(sourceNo, "dispatcher");

        var source = await db.Stocks.SingleAsync(x => x.LocationCd == "A-01");
        Assert.Equal(10m, source.PhysicalQty);
        Assert.Equal(5m, source.AllocatedQty);
        Assert.Equal(ReplenishStatus.TaskIssued,
            (await db.ReplenishOrders.SingleAsync(
                x => x.ReplenishNo == sourceNo)).Status);

        var task = await tasks.GetAsync(taskNo)
                   ?? throw new XunitException("Replenishment task disappeared.");
        task = await tasks.ClaimAsync(taskNo, new ClaimTaskV2Request
        {
            OperationId = Guid.NewGuid(),
            RowVersion = task.RowVersion,
            DeviceId = "replenish-rf"
        }, "worker");
        var blocked = await Assert.ThrowsAsync<MobileTaskConflictException>(
            () => replenish.CancelAsync(sourceNo, "dispatcher"));
        Assert.Equal("WM-V2-SOURCE-TASK-STARTED", blocked.Code);

        foreach (var (step, barcode) in new[]
                 {
                     ("SourceLocation", "A-01"),
                     ("Product", "P-100"),
                     ("TargetLocation", "B-02"),
                     ("Quantity", "5")
                 })
            Assert.True((await tasks.ScanAsync(taskNo, new ScanCommand
            {
                OperationId = Guid.NewGuid(),
                RowVersion = task.RowVersion,
                DeviceId = "replenish-rf",
                ExecutionVersion = task.ExecutionVersion,
                Step = step,
                RawBarcode = barcode,
                ClientScanNo = Guid.NewGuid().ToString("N"),
                ScannedAt = DateTimeOffset.UtcNow
            }, "worker")).Matched);

        await tasks.CompleteAsync(taskNo, new CompleteMoveV2Request
        {
            OperationId = Guid.NewGuid(),
            RowVersion = task.RowVersion,
            DeviceId = "replenish-rf",
            ExecutionVersion = task.ExecutionVersion,
            ScannedQty = 5,
            ToLocationCd = "B-02"
        }, "worker");

        var order = await db.ReplenishOrders.SingleAsync(
            x => x.ReplenishNo == sourceNo);
        Assert.Equal(ReplenishStatus.Executed, order.Status);
        Assert.NotNull(order.OutTxnNo);
        Assert.NotNull(order.InTxnNo);
        Assert.Equal(5m, (await db.Stocks.SingleAsync(
            x => x.LocationCd == "A-01")).PhysicalQty);
        Assert.Equal(5m, (await db.Stocks.SingleAsync(
            x => x.LocationCd == "B-02")).PhysicalQty);
        Assert.Equal(2, await db.StockTransactions.CountAsync(
            x => x.TxnType == WmsTxnType.MOVE));
    }

    [SqlServerFact]
    public async Task SourceDocumentScopes_TranslateAndFailClosedOnSqlServer()
    {
        var tenant = Guid.NewGuid();
        await using var db = fixture.Create(tenant);
        SeedWarehouse(db, serialEnabled: false);
        db.Locations.Add(new Location
        {
            WarehouseCd = "W01",
            LocationCd = "C-03",
            LocationName = "Restricted target",
            AreaCd = "PICK-B",
            CapacityQty = 100
        });
        db.ReplenishOrders.AddRange(
            Replenishment("R-SCOPE-A", "B-02"),
            Replenishment("R-SCOPE-B", "C-03"));
        db.SlottingPlans.Add(new SlottingPlan
        {
            SlottingPlanNo = "S-SCOPE-W01",
            WarehouseCd = "W01",
            Status = SlottingStatus.Recommended,
            AnalyzedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var areaScope = new WmsAccessScope(
            false,
            [new WmsScopeGrant("W01", "PICK-A")]);
        var replenish = ReplenishService(db, areaScope);
        var visible = await replenish.SearchAsync(
            new ReplenishSearchQuery());

        Assert.Equal(
            ["R-SCOPE-A"],
            visible.Select(x => x.ReplenishNo));
        Assert.NotNull(await replenish.GetAsync("R-SCOPE-A"));
        Assert.Null(await replenish.GetAsync("R-SCOPE-B"));
        var replenishDenied =
            await Assert.ThrowsAsync<WmsAccessDeniedException>(() =>
                replenish.CreateAsync(new ReplenishOrderDto
                {
                    WarehouseCd = "W01",
                    FromLocationCd = "A-01",
                    ToLocationCd = "C-03",
                    ProductCd = "P-100",
                    Qty = 1
                }, "dispatcher"));
        Assert.Equal("WM-V2-SCOPE-DENIED", replenishDenied.Message);

        var areaSlotting = SlottingService(db, areaScope);
        Assert.Empty(await areaSlotting.SearchAsync("W01", null));
        Assert.Null(await areaSlotting.GetAsync("S-SCOPE-W01"));
        var slottingDenied =
            await Assert.ThrowsAsync<WmsAccessDeniedException>(() =>
                areaSlotting.AnalyzeAsync("W01", 90, "supervisor"));
        Assert.Equal("WM-V2-SCOPE-DENIED", slottingDenied.Message);

        var warehouseSlotting = SlottingService(
            db,
            new WmsAccessScope(
                false,
                [new WmsScopeGrant("W01", null)]));
        Assert.Equal(
            ["S-SCOPE-W01"],
            (await warehouseSlotting.SearchAsync("W01", null))
            .Select(x => x.SlottingPlanNo));
        Assert.NotNull(
            await warehouseSlotting.GetAsync("S-SCOPE-W01"));
        Assert.StartsWith(
            "SLP",
            await warehouseSlotting.AnalyzeAsync(
                "W01", 90, "supervisor"));
    }

    [SqlServerFact]
    public async Task Serial_Lifecycle_EnforcesUniqueLedgerAndAggregateReconciliation()
    {
        var tenant = Guid.NewGuid();
        await using var db = fixture.Create(tenant);
        SeedWarehouse(db, serialEnabled: true);
        db.Locations.Add(new Location
        {
            WarehouseCd = "W01",
            LocationCd = "C-03",
            LocationName = "Shipping stage",
            AreaCd = "PICK-A",
            CapacityQty = 100,
        });
        db.ProductMasters.Add(new ProductMaster
        {
            ProductCd = "SER-100",
            ItemCd = "SER-100",
            TrackingMode = ProductTrackingMode.Serial,
        });
        await db.SaveChangesAsync();
        var stock = new StockMovementService(db, new WmsSequenceService(db));
        var service = new SerialInventoryService(db, stock);
        var receive = SerialLifecycle(
            "RECEIVE", "SER-100", ["S-001", "S-002"], to: "A-01");

        var first = await service.PostAsync(receive, "operator");
        var replay = await service.PostAsync(receive, "operator");

        Assert.Equal(2, first.SerialCount);
        Assert.Equal(2, replay.SerialCount);
        Assert.Equal(2, await db.StockSerials.CountAsync());
        Assert.Equal(2, await db.StockSerialTransactions.CountAsync());
        Assert.Equal(2m, (await db.Stocks.SingleAsync(x => x.LocationCd == "A-01")).PhysicalQty);
        await Assert.ThrowsAsync<MobileTaskConflictException>(() =>
            service.PostAsync(SerialLifecycle(
                "RECEIVE", "SER-100", ["S-001"], to: "A-01"), "operator"));

        await service.PostAsync(SerialLifecycle(
            "PUTAWAY", "SER-100", ["S-001", "S-002"],
            from: "A-01", to: "B-02"), "operator");
        await service.PostAsync(SerialLifecycle(
            "MOVE", "SER-100", ["S-001", "S-002"],
            from: "B-02", to: "C-03"), "operator");
        await service.PostAsync(SerialLifecycle(
            "PICK", "SER-100", ["S-001", "S-002"],
            from: "C-03"), "operator");
        await service.PostAsync(SerialLifecycle(
            "COUNT", "SER-100", ["S-001", "S-002"],
            from: "C-03"), "auditor");
        await service.PostAsync(SerialLifecycle(
            "SHIP", "SER-100", ["S-001", "S-002"],
            from: "C-03"), "operator");
        await service.PostAsync(SerialLifecycle(
            "RETURN", "SER-100", ["S-001", "S-002"],
            to: "A-01"), "operator");

        Assert.Equal(2m, (await db.Stocks.SingleAsync(
            x => x.LocationCd == "A-01")).PhysicalQty);
        Assert.Equal(0m, (await db.Stocks.SingleAsync(
            x => x.LocationCd == "B-02")).PhysicalQty);
        Assert.Equal(0m, (await db.Stocks.SingleAsync(
            x => x.LocationCd == "C-03")).PhysicalQty);
        Assert.Equal(2, await db.StockSerials.CountAsync(
            x => x.LocationCd == "A-01"
                 && x.Status == StockSerialStatus.Returned));
        Assert.Equal(14, await db.StockSerialTransactions.CountAsync());
        Assert.Equal(7, await db.StockTransactions.CountAsync());
        Assert.Equal(
            ["COUNT", "MOVE", "PICK", "PUTAWAY", "RECEIVE", "RETURN", "SHIP"],
            await db.StockSerialTransactions
                .Select(x => x.TxnType)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync());
    }

    [SqlServerFact]
    public async Task Serial_EnableTracking_RequiresFeatureAndExactControlledCount()
    {
        var tenant = Guid.NewGuid();
        await using var db = fixture.Create(tenant);
        SeedWarehouse(db, serialEnabled: true);
        var feature = db.WmsFeatureFlags.Local.Single();
        feature.SerialLpnEnabled = false;
        db.ProductMasters.Add(new ProductMaster
        {
            ProductCd = "CONVERT-100",
            ItemCd = "CONVERT-100",
            TrackingMode = ProductTrackingMode.None,
        });
        db.Stocks.Add(new Stock
        {
            WarehouseCd = "W01",
            LocationCd = "A-01",
            ProductCd = "CONVERT-100",
            LotNo = string.Empty,
            PhysicalQty = 2,
            AvailableQty = 2,
        });
        await db.SaveChangesAsync();
        var service = new SerialInventoryService(
            db,
            new StockMovementService(db, new WmsSequenceService(db)));
        var exact = new EnableSerialTrackingRequest
        {
            OperationId = Guid.NewGuid(),
            ProductCd = "CONVERT-100",
            TrackingMode = ProductTrackingMode.Serial,
            ExistingSerials =
            [
                ExistingSerial("CV-001"),
                ExistingSerial("CV-002"),
            ],
        };

        var disabled = await Assert.ThrowsAsync<MobileTaskConflictException>(
            () => service.EnableTrackingAsync(exact, "supervisor"));
        Assert.Equal("WM-R2B-FEATURE-DISABLED", disabled.Code);
        Assert.Empty(await db.StockSerials.ToListAsync());

        feature.SerialLpnEnabled = true;
        await db.SaveChangesAsync();
        var mismatch = await Assert.ThrowsAsync<MobileTaskConflictException>(
            () => service.EnableTrackingAsync(new EnableSerialTrackingRequest
            {
                OperationId = Guid.NewGuid(),
                ProductCd = "CONVERT-100",
                TrackingMode = ProductTrackingMode.Serial,
                ExistingSerials = [ExistingSerial("CV-001")],
            }, "supervisor"));
        Assert.Equal("WM-SERIAL-CONVERSION-QTY-MISMATCH", mismatch.Code);
        Assert.Empty(await db.StockSerials.ToListAsync());

        var converted = await service.EnableTrackingAsync(exact, "supervisor");
        var replay = await service.EnableTrackingAsync(exact, "supervisor");
        Assert.Equal(2, converted.SerialCount);
        Assert.Equal(2, replay.SerialCount);
        Assert.Equal(2, await db.StockSerials.CountAsync());
        Assert.Equal(2, await db.StockSerialTransactions.CountAsync(
            x => x.TxnType == "SERIALIZE"));
        var product = await db.ProductMasters.SingleAsync(
            x => x.ProductCd == "CONVERT-100");
        Assert.Equal(ProductTrackingMode.Serial, product.TrackingMode);
        Assert.NotNull(product.SerialTrackingLockedAt);

        product.TrackingMode = ProductTrackingMode.None;
        var downgrade = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.SaveChangesAsync());
        Assert.Equal("WM-SERIAL-TRACKING-LOCKED", downgrade.Message);
    }

    [SqlServerFact]
    public async Task Lpn_UsesCompositeSerialIdentity_AndMovesSplitsMergesWholeTree()
    {
        var tenant = Guid.NewGuid();
        await using var db = fixture.Create(tenant);
        SeedWarehouse(db, serialEnabled: true);
        db.ProductMasters.AddRange(
            new ProductMaster
            {
                ProductCd = "SER-A",
                ItemCd = "SER-A",
                TrackingMode = ProductTrackingMode.Serial,
            },
            new ProductMaster
            {
                ProductCd = "SER-B",
                ItemCd = "SER-B",
                TrackingMode = ProductTrackingMode.Serial,
            });
        await db.SaveChangesAsync();
        var stock = new StockMovementService(db, new WmsSequenceService(db));
        var serials = new SerialInventoryService(db, stock);
        await serials.PostAsync(SerialLifecycle(
            "RECEIVE", "SER-A", ["DUP-001"], to: "A-01"), "receiver");
        await serials.PostAsync(SerialLifecycle(
            "RECEIVE", "SER-B", ["DUP-001"], to: "A-01"), "receiver");

        var service = new LpnService(
            db,
            stock);
        var root = await service.CreateAsync(Lpn("PALLET-1", "PALLET"), "supervisor");
        var child = await service.CreateAsync(Lpn("BOX-1", "BOX"), "supervisor");
        child = await service.PackAsync(child.LpnNo, new PackLpnRequest
        {
            OperationId = Guid.NewGuid(),
            RowVersion = child.RowVersion,
            Contents =
            [
                new LpnContentInput
                {
                    ProductCd = "SER-A",
                    SerialNo = "DUP-001",
                    Qty = 1,
                }
            ],
        }, "supervisor");

        var mixedProduct = await Assert.ThrowsAsync<MobileTaskConflictException>(
            () => service.PackAsync(child.LpnNo, new PackLpnRequest
            {
                OperationId = Guid.NewGuid(),
                RowVersion = child.RowVersion,
                Contents =
                [
                    new LpnContentInput
                    {
                        ProductCd = "SER-B",
                        SerialNo = "DUP-001",
                        Qty = 1,
                    }
                ],
            }, "supervisor"));
        Assert.Equal("WM-LPN-MIXED-PRODUCTS", mixedProduct.Code);

        root = await service.PackAsync(root.LpnNo, new PackLpnRequest
        {
            OperationId = Guid.NewGuid(),
            RowVersion = root.RowVersion,
            ChildLpns = [child.LpnNo],
        }, "supervisor");

        child = await service.GetOneAsync(child.LpnNo)
                ?? throw new XunitException("Child LPN disappeared.");
        await Assert.ThrowsAsync<MobileTaskConflictException>(() =>
            service.PackAsync(child.LpnNo, new PackLpnRequest
            {
                OperationId = Guid.NewGuid(),
                RowVersion = child.RowVersion,
                ChildLpns = [root.LpnNo],
            }, "supervisor"));

        root = await service.GetOneAsync(root.LpnNo)
               ?? throw new XunitException("Root LPN disappeared.");
        var move = new MoveLpnRequest
        {
            OperationId = Guid.NewGuid(),
            RowVersion = root.RowVersion,
            ToLocationCd = "B-02",
        };
        var moved = await service.MoveAsync(root.LpnNo, move, "operator");
        var replay = await service.MoveAsync(root.LpnNo, move, "operator");
        Assert.Equal(moved.LpnNo, replay.LpnNo);

        Assert.Equal(2, await db.LogisticsUnits.CountAsync(x => x.LocationCd == "B-02"));
        Assert.Contains(await db.LpnClosures.ToListAsync(),
            x => x.AncestorLpnNo == "PALLET-1"
                  && x.DescendantLpnNo == "BOX-1"
                  && x.Depth == 1);
        var serialA = await db.StockSerials.SingleAsync(x =>
            x.ProductCd == "SER-A" && x.SerialNo == "DUP-001");
        var serialB = await db.StockSerials.SingleAsync(x =>
            x.ProductCd == "SER-B" && x.SerialNo == "DUP-001");
        Assert.Equal("B-02", serialA.LocationCd);
        Assert.Equal("BOX-1", serialA.LpnNo);
        Assert.Equal("A-01", serialB.LocationCd);
        Assert.Null(serialB.LpnNo);
        Assert.Equal(1m, (await db.Stocks.SingleAsync(x =>
            x.ProductCd == "SER-A" && x.LocationCd == "B-02")).PhysicalQty);
        Assert.Equal(1m, (await db.Stocks.SingleAsync(x =>
            x.ProductCd == "SER-B" && x.LocationCd == "A-01")).PhysicalQty);
        Assert.Single(await db.StockSerialTransactions.Where(x =>
            x.TxnType == "LPN_MOVE" && x.ProductCd == "SER-A").ToListAsync());
        Assert.Empty(await db.StockSerialTransactions.Where(x =>
            x.TxnType == "LPN_MOVE" && x.ProductCd == "SER-B").ToListAsync());

        child = await service.GetOneAsync(child.LpnNo)
                ?? throw new XunitException("Child LPN disappeared after move.");
        await service.SplitAsync(child.LpnNo, new SplitLpnRequest
        {
            OperationId = Guid.NewGuid(),
            RowVersion = child.RowVersion,
            TargetLpnNo = "BOX-SPLIT",
            TargetContainerType = "BOX",
            SerialNos = ["DUP-001"],
        }, "supervisor");
        child = await service.GetOneAsync(child.LpnNo)
                ?? throw new XunitException("Child LPN disappeared after split.");
        await service.MergeAsync(child.LpnNo, new MergeLpnRequest
        {
            OperationId = Guid.NewGuid(),
            RowVersion = child.RowVersion,
            SourceLpnNo = "BOX-SPLIT",
        }, "supervisor");
        child = await service.GetOneAsync(child.LpnNo)
                ?? throw new XunitException("Child LPN disappeared after merge.");
        await service.UnpackAsync(child.LpnNo, new UnpackLpnRequest
        {
            OperationId = Guid.NewGuid(),
            RowVersion = child.RowVersion,
            SerialNos = ["DUP-001"],
        }, "supervisor");

        serialA = await db.StockSerials.SingleAsync(x =>
            x.ProductCd == "SER-A" && x.SerialNo == "DUP-001");
        serialB = await db.StockSerials.SingleAsync(x =>
            x.ProductCd == "SER-B" && x.SerialNo == "DUP-001");
        Assert.Equal("B-02", serialA.LocationCd);
        Assert.Null(serialA.LpnNo);
        Assert.Equal("A-01", serialB.LocationCd);
        Assert.Null(serialB.LpnNo);
        Assert.Empty(await db.LpnContents.Where(x =>
            x.ProductCd == "SER-A" && x.SerialNo == "DUP-001").ToListAsync());
        Assert.True(await db.LogisticsUnits.IgnoreQueryFilters().AnyAsync(x =>
            x.LpnNo == "BOX-SPLIT" && x.IsDeleted && x.Status == "MERGED"));
    }

    private async Task<(MobileTaskV2Dto? Task, Exception? Error)> TryClaim(
        Guid tenant,
        MobileTaskV2Dto created,
        string device,
        string user)
    {
        await using var db = fixture.Create(tenant);
        try
        {
            var task = await MoveService(db).ClaimAsync(created.TaskNo, new ClaimTaskV2Request
            {
                OperationId = Guid.NewGuid(),
                RowVersion = created.RowVersion,
                DeviceId = device,
            }, user);
            return (task, null);
        }
        catch (Exception ex) { return (null, ex); }
    }

    private static MobileTaskV2Service MoveService(CP6Context db)
    {
        var sequence = new WmsSequenceService(db);
        return new MobileTaskV2Service(
            db,
            sequence,
            new StockMovementService(db, sequence),
            new FixedWmsAccessScopeProvider(WmsAccessScope.All));
    }

    private static ReplenishService ReplenishService(
        CP6Context db,
        WmsAccessScope scope)
    {
        var sequence = new WmsSequenceService(db);
        var accessScopes = new FixedWmsAccessScopeProvider(scope);
        return new ReplenishService(
            db,
            sequence,
            new MobileTaskV2Service(
                db,
                sequence,
                new StockMovementService(db, sequence),
                accessScopes),
            accessScopes);
    }

    private static SlottingService SlottingService(
        CP6Context db,
        WmsAccessScope scope)
    {
        var sequence = new WmsSequenceService(db);
        var accessScopes = new FixedWmsAccessScopeProvider(scope);
        return new SlottingService(
            db,
            sequence,
            new MobileTaskV2Service(
                db,
                sequence,
                new StockMovementService(db, sequence),
                accessScopes),
            accessScopes);
    }

    private static ReplenishOrder Replenishment(
        string replenishNo,
        string targetLocation)
        => new()
        {
            ReplenishNo = replenishNo,
            WarehouseCd = "W01",
            FromLocationCd = "A-01",
            ToLocationCd = targetLocation,
            ProductCd = "P-100",
            Qty = 1,
            Status = ReplenishStatus.Pending
        };

    private static CreateLpnRequest Lpn(string no, string type) => new()
    {
        OperationId = Guid.NewGuid(),
        LpnNo = no,
        ContainerType = type,
        WarehouseCd = "W01",
        LocationCd = "A-01",
    };

    private static SerialLifecycleRequest SerialLifecycle(
        string type,
        string productCd,
        List<string> serialNos,
        string? from = null,
        string? to = null)
        => new()
        {
            OperationId = Guid.NewGuid(),
            TxnType = type,
            ProductCd = productCd,
            SerialNos = serialNos,
            WarehouseCd = "W01",
            FromLocationCd = from,
            ToLocationCd = to,
        };

    private static ExistingSerialInput ExistingSerial(string serialNo)
        => new()
        {
            SerialNo = serialNo,
            WarehouseCd = "W01",
            LocationCd = "A-01",
            LotNo = string.Empty,
        };

    private static ClientDevice Device(string id) => new()
    {
        DeviceId = id,
        DeviceMode = ClientDeviceMode.Shared,
        Platform = "Android",
        PublicKey = "-----BEGIN PUBLIC KEY-----\nTEST\n-----END PUBLIC KEY-----",
        Status = ClientDeviceStatus.Active,
        WarehouseCd = "W01",
        AreaCd = "PICK-A",
        ActivatedAt = DateTime.UtcNow,
    };

    private static void SeedWarehouse(CP6Context db, bool serialEnabled)
    {
        db.Warehouses.Add(new Warehouse
        {
            WarehouseCd = "W01",
            WarehouseName = "SQL integration warehouse",
            AllowNegative = false,
        });
        db.Locations.AddRange(
            new Location
            {
                WarehouseCd = "W01", LocationCd = "A-01",
                LocationName = "Source", AreaCd = "PICK-A", CapacityQty = 100,
            },
            new Location
            {
                WarehouseCd = "W01", LocationCd = "B-02",
                LocationName = "Target", AreaCd = "PICK-A", CapacityQty = 100,
            });
        db.WmsFeatureFlags.Add(new WmsFeatureFlag
        {
            WarehouseCd = "W01",
            ProductionMoveEnabled = !serialEnabled,
            SerialLpnEnabled = serialEnabled,
        });
        if (!serialEnabled)
            db.Stocks.Add(new Stock
            {
                WarehouseCd = "W01",
                LocationCd = "A-01",
                ProductCd = "P-100",
                LotNo = string.Empty,
                PhysicalQty = 10,
                AvailableQty = 10,
            });
    }
}
