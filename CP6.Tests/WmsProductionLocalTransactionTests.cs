using System.Data.Common;
using System.Text.Json;
using System.Transactions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Infra;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Tests;

public sealed partial class WmsProductionSqlServerTests
{
    [WmsProductionFact]
    public async Task Bug163_LpnCreate_UsesOneLocalTransactionAndReplaysOnce()
    {
        var tenant = await SeedBug163Async();
        var trace = new WmsTransactionTrace(output);
        await using var db = fixture.Create(tenant, trace);
        var service = NewLpn(db);
        var request = Lpn("CREATED", "BOX");
        var created = await service.CreateAsync(request, "operator");
        var replay = await service.CreateAsync(request, "operator");
        Assert.Equal(JsonSerializer.Serialize(created), JsonSerializer.Serialize(replay));
        Assert.Null(db.Database.CurrentTransaction);
        trace.AssertStandaloneLocalTransaction();
        await using var verify = fixture.Create(tenant);
        Assert.Single(await verify.LogisticsUnits.ToListAsync());
        Assert.Single(await verify.LpnClosures.ToListAsync());
        Assert.Single(await verify.LpnEvents.ToListAsync());
        Assert.Single(await verify.TaskCommandReceipts.ToListAsync());
    }

    [WmsProductionFact]
    public Task Bug163_LabelClaim_UsesLocalTransactionAndReplaysOnce()
        => VerifyBug163LabelAsync(claim: true, success: true);

    [WmsProductionFact]
    public Task Bug163_LabelComplete_UsesLocalTransactionAndReplaysOnce()
        => VerifyBug163LabelAsync(claim: false, success: true);

    [WmsProductionFact]
    public Task Bug163_LabelFail_UsesLocalTransactionAndReplaysOnce()
        => VerifyBug163LabelAsync(claim: false, success: false);

    [WmsProductionFact]
    public Task Bug163_LpnCreate_RollsBackBusinessSaveBeforeReceipt()
        => VerifyBug163LpnRollbackAsync("create");

    [WmsProductionFact]
    public Task Bug163_LpnUnpack_RollsBackTreeContentSerialAndEvent()
        => VerifyBug163LpnRollbackAsync("unpack");

    [WmsProductionFact]
    public Task Bug163_LpnSplit_RollsBackNewUnitTreeContentSerialAndEvents()
        => VerifyBug163LpnRollbackAsync("split");

    [WmsProductionFact]
    public Task Bug163_LpnMerge_RollsBackSourceDeletionTreeContentSerialAndEvent()
        => VerifyBug163LpnRollbackAsync("merge");

    [WmsProductionFact]
    public Task Bug163_LabelClaim_RollsBackStatusAttemptAndReceipt()
        => VerifyBug163LabelRollbackAsync(claim: true);

    [WmsProductionFact]
    public Task Bug163_LabelComplete_RollsBackStatusResultAndReceipt()
        => VerifyBug163LabelRollbackAsync(claim: false);

    [WmsProductionFact]
    public async Task Bug163_LpnCreate_LeavesCallerLocalTransactionUncommitted()
    {
        var tenant = await SeedBug163Async();
        await using (var db = fixture.Create(tenant))
        await using (var caller = await db.Database.BeginTransactionAsync())
        {
            await NewLpn(db).CreateAsync(Lpn("CALLER", "BOX"), "operator");
            Assert.Same(caller, db.Database.CurrentTransaction);
            Assert.Single(await db.TaskCommandReceipts.ToListAsync());
            await caller.RollbackAsync();
        }
        await using var verify = fixture.Create(tenant);
        Assert.Empty(await verify.LogisticsUnits.ToListAsync());
        Assert.Empty(await verify.LpnClosures.ToListAsync());
        Assert.Empty(await verify.LpnEvents.ToListAsync());
        Assert.Empty(await verify.TaskCommandReceipts.ToListAsync());
    }

    [WmsProductionFact]
    public async Task Bug163_LabelCommands_LeaveCallerLocalTransactionUncommitted()
    {
        var tenant = await SeedBug163Async(labelStatus: LabelJobStatus.Pending);
        var baseline = await Bug163StateAsync(tenant);
        await using (var db = fixture.Create(tenant))
        await using (var caller = await db.Database.BeginTransactionAsync())
        {
            await ClaimAndCompleteBug163LabelAsync(db);
            Assert.Same(caller, db.Database.CurrentTransaction);
            await caller.RollbackAsync();
        }
        Assert.Equal(baseline, await Bug163StateAsync(tenant));
    }

    [WmsProductionFact]
    public async Task Bug163_LpnCreate_JoinsPreopenedCallerAmbientTransaction()
    {
        var tenant = await SeedBug163Async();
        await using (var db = fixture.Create(tenant))
        {
            using (var caller = Bug163AmbientScope())
            {
                await db.Database.OpenConnectionAsync();
                await NewLpn(db).CreateAsync(Lpn("AMBIENT", "BOX"), "operator");
                Assert.NotNull(Transaction.Current);
                Assert.Equal(TransactionStatus.Active, Transaction.Current!.TransactionInformation.Status);
                Assert.Equal(Guid.Empty, Transaction.Current.TransactionInformation.DistributedIdentifier);
                Assert.Single(await db.TaskCommandReceipts.ToListAsync());
                // The caller intentionally omits Complete: the service must not commit it.
            }
            await db.Database.CloseConnectionAsync();
        }
        await using var verify = fixture.Create(tenant);
        Assert.Empty(await verify.LogisticsUnits.ToListAsync());
        Assert.Empty(await verify.TaskCommandReceipts.ToListAsync());
    }

    [WmsProductionFact]
    public async Task Bug163_LabelCommands_JoinPreopenedCallerAmbientTransaction()
    {
        var tenant = await SeedBug163Async(labelStatus: LabelJobStatus.Pending);
        var baseline = await Bug163StateAsync(tenant);
        await using (var db = fixture.Create(tenant))
        {
            using (var caller = Bug163AmbientScope())
            {
                await db.Database.OpenConnectionAsync();
                await ClaimAndCompleteBug163LabelAsync(db);
                Assert.Equal(TransactionStatus.Active, Transaction.Current!.TransactionInformation.Status);
                Assert.Equal(Guid.Empty, Transaction.Current.TransactionInformation.DistributedIdentifier);
            }
            await db.Database.CloseConnectionAsync();
        }
        Assert.Equal(baseline, await Bug163StateAsync(tenant));
    }

    [WmsProductionFact]
    public async Task Bug163_LpnCreate_CancellationAfterBusinessSaveRollsBack()
    {
        var tenant = await SeedBug163Async();
        var baseline = await Bug163StateAsync(tenant);
        using var cancellation = new CancellationTokenSource();
        var fault = new AfterWmsBusinessSave(() => cancellation.Cancel());
        await using (var db = fixture.Create(tenant, fault))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                NewLpn(db).CreateAsync(Lpn("CANCELLED", "BOX"), "operator", cancellation.Token));
            Assert.Equal(1, fault.BusinessSaves);
            Assert.Null(db.Database.CurrentTransaction);
        }
        Assert.Equal(baseline, await Bug163StateAsync(tenant));
    }

    [WmsProductionFact]
    public async Task Bug163_LpnCreate_FailureLeavesCallerLocalTransactionForCallerRollback()
    {
        var tenant = await SeedBug163Async();
        var baseline = await Bug163StateAsync(tenant);
        var fault = new AfterWmsBusinessSave(() => throw new WmsBusinessSaveFailure());
        await using (var db = fixture.Create(tenant, fault))
        await using (var caller = await db.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<WmsBusinessSaveFailure>(() =>
                NewLpn(db).CreateAsync(Lpn("CALLER-FAIL", "BOX"), "operator"));
            Assert.Equal(1, fault.BusinessSaves);
            Assert.Same(caller, db.Database.CurrentTransaction);
            Assert.Single(await db.LogisticsUnits.AsNoTracking().ToListAsync());
            Assert.Empty(await db.TaskCommandReceipts.AsNoTracking().ToListAsync());
            await caller.RollbackAsync();
        }
        Assert.Equal(baseline, await Bug163StateAsync(tenant));
    }

    [WmsProductionFact]
    public async Task Bug163_LpnCreate_FailureRetainsCallerAmbientAbortSemantics()
    {
        var tenant = await SeedBug163Async();
        var baseline = await Bug163StateAsync(tenant);
        var fault = new AfterWmsBusinessSave(() => throw new WmsBusinessSaveFailure());
        await using (var db = fixture.Create(tenant, fault))
        {
            using (var caller = Bug163AmbientScope())
            {
                await db.Database.OpenConnectionAsync();
                await Assert.ThrowsAsync<WmsBusinessSaveFailure>(() =>
                    NewLpn(db).CreateAsync(Lpn("AMBIENT-FAIL", "BOX"), "operator"));
                Assert.Equal(1, fault.BusinessSaves);
                Assert.Equal(TransactionStatus.Aborted, Transaction.Current!.TransactionInformation.Status);
            }
            await db.Database.CloseConnectionAsync();
        }
        Assert.Equal(baseline, await Bug163StateAsync(tenant));
    }

    private async Task VerifyBug163LabelAsync(bool claim, bool success)
    {
        var tenant = await SeedBug163Async(labelStatus: claim ? LabelJobStatus.Pending : LabelJobStatus.Printing);
        var trace = new WmsTransactionTrace(output);
        await using var db = fixture.Create(tenant, trace);
        var job = await db.LabelJobs.SingleAsync();
        var request = Bug163LabelCommand(Bug163RowVersion(job.RowVersion));
        var service = new LabelJobService(db);
        Task<LabelJobDto> Execute() => claim
            ? service.ClaimAsync(job.JobNo, request, "operator")
            : service.CompleteAsync(job.JobNo, request, success, "operator");
        var first = await Execute();
        var replay = await Execute();
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(replay));
        Assert.Equal(claim ? LabelJobStatus.Printing : success ? LabelJobStatus.Completed : LabelJobStatus.Failed,
            first.Status);
        Assert.Equal(claim ? 1 : 2, first.AttemptCount);
        Assert.Null(db.Database.CurrentTransaction);
        trace.AssertStandaloneLocalTransaction();
        await using var verify = fixture.Create(tenant);
        Assert.Single(await verify.TaskCommandReceipts.ToListAsync());
        Assert.Equal(first.Status, (await verify.LabelJobs.SingleAsync()).Status);
    }

    private async Task VerifyBug163LpnRollbackAsync(string command)
    {
        var tenant = await SeedBug163Async(tree: command != "create");
        var baseline = await Bug163StateAsync(tenant);
        var fault = new AfterWmsBusinessSave(() => throw new WmsBusinessSaveFailure());
        await using (var db = fixture.Create(tenant, fault))
        {
            var service = NewLpn(db);
            var source = command == "create" ? null : await service.GetOneAsync(command == "merge" ? "OTHER" : "ROOT");
            var version = source?.RowVersion ?? string.Empty;
            await Assert.ThrowsAsync<WmsBusinessSaveFailure>(async () =>
            {
                _ = command switch
                {
                    "create" => await service.CreateAsync(Lpn("ROLLBACK", "BOX"), "operator"),
                    "unpack" => await service.UnpackAsync("ROOT", new UnpackLpnRequest
                    { OperationId = Guid.NewGuid(), RowVersion = version, ChildLpns = ["CHILD"], SerialNos = ["SER-ROOT"] }, "operator"),
                    "split" => await service.SplitAsync("ROOT", new SplitLpnRequest
                    { OperationId = Guid.NewGuid(), RowVersion = version, TargetLpnNo = "SPLIT", TargetContainerType = "BOX",
                        ChildLpns = ["CHILD"], SerialNos = ["SER-ROOT"] }, "operator"),
                    "merge" => await service.MergeAsync("OTHER", new MergeLpnRequest
                    { OperationId = Guid.NewGuid(), RowVersion = version, SourceLpnNo = "ROOT" }, "operator"),
                    _ => throw new ArgumentOutOfRangeException(nameof(command))
                };
            });
            Assert.Equal(1, fault.BusinessSaves);
            Assert.Null(db.Database.CurrentTransaction);
        }
        Assert.Equal(baseline, await Bug163StateAsync(tenant));
    }

    private async Task VerifyBug163LabelRollbackAsync(bool claim)
    {
        var tenant = await SeedBug163Async(labelStatus: claim ? LabelJobStatus.Pending : LabelJobStatus.Printing);
        var baseline = await Bug163StateAsync(tenant);
        var fault = new AfterWmsBusinessSave(() => throw new WmsBusinessSaveFailure());
        await using (var db = fixture.Create(tenant, fault))
        {
            var row = await db.LabelJobs.SingleAsync();
            var request = Bug163LabelCommand(Bug163RowVersion(row.RowVersion));
            var service = new LabelJobService(db);
            await Assert.ThrowsAsync<WmsBusinessSaveFailure>(() => claim
                ? service.ClaimAsync(row.JobNo, request, "operator")
                : service.CompleteAsync(row.JobNo, request, true, "operator"));
            Assert.Equal(1, fault.BusinessSaves);
            Assert.Null(db.Database.CurrentTransaction);
        }
        Assert.Equal(baseline, await Bug163StateAsync(tenant));
    }

    private async Task<Guid> SeedBug163Async(bool tree = false, string? labelStatus = null)
    {
        var tenant = Guid.NewGuid();
        await using var db = fixture.Create(tenant);
        SeedWarehouse(db, serialEnabled: true);
        if (tree)
        {
            // ProductCd has a global alternate key, so distinct tenants also need distinct seed products.
            var product = "S163" + tenant.ToString("N")[..8];
            db.ProductMasters.Add(new ProductMaster { ProductCd = product, ItemCd = product, TrackingMode = ProductTrackingMode.Serial });
            db.Stocks.Add(new Stock { ProductCd = product, WarehouseCd = "W01", LocationCd = "A-01", LotNo = "", PhysicalQty = 2, AvailableQty = 2 });
            foreach (var name in new[] { "ROOT", "CHILD", "OTHER" })
            {
                db.LogisticsUnits.Add(new LogisticsUnit { LpnNo = name, ContainerType = "BOX", WarehouseCd = "W01",
                    LocationCd = "A-01", ParentLpnNo = name == "CHILD" ? "ROOT" : null });
                db.LpnClosures.Add(new LpnClosure { AncestorLpnNo = name, DescendantLpnNo = name, Depth = 0 });
            }
            db.LpnClosures.Add(new LpnClosure { AncestorLpnNo = "ROOT", DescendantLpnNo = "CHILD", Depth = 1 });
            foreach (var name in new[] { "ROOT", "CHILD" })
            {
                db.LpnContents.Add(new LpnContent { LpnNo = name, ProductCd = product, LotNo = "", SerialNo = "SER-" + name, Qty = 1 });
                db.StockSerials.Add(new StockSerial { ProductCd = product, SerialNo = "SER-" + name, LpnNo = name,
                    WarehouseCd = "W01", LocationCd = "A-01", LotNo = "" });
            }
        }
        if (labelStatus is not null)
        {
            var device = Device("bug163-printer");
            device.Platform = "Windows";
            db.ClientDevices.Add(device);
            db.LabelTemplates.Add(new LabelTemplate { TemplateName = "BUG163", TemplateBody = "^XA^XZ", IsEnabled = true });
            db.LabelJobs.Add(new LabelJob { JobNo = "BUG163-LABEL", OperationId = Guid.NewGuid(), WarehouseCd = "W01",
                TemplateName = "BUG163", Status = labelStatus, RequestedDeviceId = device.DeviceId,
                AttemptCount = labelStatus == LabelJobStatus.Printing ? 2 : 0 });
        }
        await db.SaveChangesAsync();
        return tenant;
    }

    private async Task<string> Bug163StateAsync(Guid tenant)
    {
        await using var db = fixture.Create(tenant);
        return JsonSerializer.Serialize(new
        {
            Units = await db.LogisticsUnits.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant).OrderBy(x => x.LpnNo)
                .Select(x => new { x.LpnNo, x.ParentLpnNo, x.LocationCd, x.Status, x.IsDeleted, x.RowVersion }).ToListAsync(),
            Closures = await db.LpnClosures.AsNoTracking().OrderBy(x => x.AncestorLpnNo).ThenBy(x => x.DescendantLpnNo)
                .Select(x => new { x.AncestorLpnNo, x.DescendantLpnNo, x.Depth }).ToListAsync(),
            Contents = await db.LpnContents.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant).OrderBy(x => x.LpnNo).ThenBy(x => x.SerialNo)
                .Select(x => new { x.LpnNo, x.ProductCd, x.SerialNo, x.Qty, x.IsDeleted, x.RowVersion }).ToListAsync(),
            Serials = await db.StockSerials.AsNoTracking().OrderBy(x => x.SerialNo)
                .Select(x => new { x.SerialNo, x.LpnNo, x.LocationCd, x.Status, x.RowVersion }).ToListAsync(),
            Events = await db.LpnEvents.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.LpnNo, x.OperationId, x.EventType }).ToListAsync(),
            Receipts = await db.TaskCommandReceipts.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.TaskNo, x.OperationId, x.CommandName, x.ResultJson }).ToListAsync(),
            Jobs = await db.LabelJobs.AsNoTracking().OrderBy(x => x.JobNo)
                .Select(x => new { x.JobNo, x.Status, x.AttemptCount, x.RequestedDeviceId, x.CompletedAt, x.ResultMessage, x.RowVersion }).ToListAsync()
        });
    }

    private static LpnService NewLpn(CP6Context db)
        => new(db, new StockMovementService(db, new WmsSequenceService(db)));

    private static LabelJobCommand Bug163LabelCommand(string version)
        => new() { OperationId = Guid.NewGuid(), RowVersion = version, DeviceId = "bug163-printer", ResultMessage = "printed" };

    private static string Bug163RowVersion(byte[]? version)
    {
        var bytes = version ?? throw new Xunit.Sdk.XunitException("Native row version was not generated.");
        Assert.NotEmpty(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static async Task ClaimAndCompleteBug163LabelAsync(CP6Context db)
    {
        var service = new LabelJobService(db);
        var job = await db.LabelJobs.SingleAsync();
        var claimed = await service.ClaimAsync(job.JobNo, Bug163LabelCommand(Bug163RowVersion(job.RowVersion)), "operator");
        var completed = await service.CompleteAsync(job.JobNo, Bug163LabelCommand(claimed.RowVersion), true, "operator");
        Assert.Equal(LabelJobStatus.Completed, completed.Status);
        Assert.Equal(2, await db.TaskCommandReceipts.CountAsync());
    }

    private static TransactionScope Bug163AmbientScope()
        => new(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

    private sealed class WmsBusinessSaveFailure : Exception;

    // The failure occurs after the real database SaveChanges has written business rows.
    private sealed class AfterWmsBusinessSave(Action afterSave) : SaveChangesInterceptor
    {
        public int BusinessSaves { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (++BusinessSaves == 1) afterSave();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class WmsTransactionTrace(Xunit.Abstractions.ITestOutputHelper log) : DbCommandInterceptor
    {
        private readonly HashSet<string> _localPhysicalConnections = [];
        private int _localCommands;
        private bool _ambientObserved;

        private void Record(DbCommand command, CommandEventData eventData)
        {
            var physical = command.Connection switch
            {
                SqlConnection sql => sql.ClientConnectionId.ToString("N"),
                NpgsqlConnection pg => pg.ProcessID.ToString(),
                _ => "unknown"
            };
            var ambient = Transaction.Current is not null;
            var local = eventData.Context?.Database.CurrentTransaction is not null;
            _ambientObserved |= ambient;
            if (local) { _localCommands++; _localPhysicalConnections.Add(physical); }
            log.WriteLine($"BUG163 native command physical={physical} ambient={ambient} local={local}");
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Record(command, eventData); return ValueTask.FromResult(result); }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Record(command, eventData); return ValueTask.FromResult(result); }

        public void AssertStandaloneLocalTransaction()
        {
            Assert.False(_ambientObserved);
            Assert.True(_localCommands > 1);
            Assert.Single(_localPhysicalConnections);
        }
    }
}
