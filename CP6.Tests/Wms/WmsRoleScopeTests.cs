using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Wms;

public sealed class WmsRoleScopeTests
{
    [Fact]
    public async Task TaskReadsAndAnalytics_AreRestrictedToGrantedWarehouseArea()
    {
        await using var db = NewDb();
        db.MobileTasks.AddRange(
            Mobile("T-A1", "W01", "A1", MobileTaskStatus.Completed),
            Mobile("T-A2", "W01", "A2", MobileTaskStatus.Exception),
            Mobile("T-W2", "W02", "A1", MobileTaskStatus.Pending));
        await db.SaveChangesAsync();

        var service = MoveService(
            db,
            new WmsAccessScope(false, [new WmsScopeGrant("W01", "A1")]));

        var page = await service.GetTasksAsync(new MobileTaskV2Query());
        var analytics = await service.GetAnalyticsAsync(new TaskAnalyticsQuery());

        Assert.Equal(["T-A1"], page.Items.Select(x => x.TaskNo));
        Assert.Equal(1, page.Total);
        Assert.NotNull(await service.GetAsync("T-A1"));
        Assert.Null(await service.GetAsync("T-A2"));
        Assert.Null(await service.GetAsync("T-W2"));
        Assert.Equal(1, analytics.Created);
        Assert.Equal(1, analytics.Completed);
        Assert.Equal(0, analytics.Exceptions);
    }

    [Fact]
    public async Task TaskCommands_HideOutOfScopeTaskBeforeIdempotencyReplay()
    {
        await using var db = NewDb();
        var task = Mobile("T-A2", "W01", "A2", MobileTaskStatus.Pending);
        db.MobileTasks.Add(task);
        db.TaskCommandReceipts.Add(new TaskCommandReceipt
        {
            OperationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TaskNo = task.MobileTaskNo,
            CommandName = "assign",
            ResultJson = """{"taskNo":"T-A2","warehouseCd":"W01","areaCd":"A2"}"""
        });
        await db.SaveChangesAsync();

        var service = MoveService(
            db,
            new WmsAccessScope(false, [new WmsScopeGrant("W01", "A1")]));

        await Assert.ThrowsAsync<MobileTaskNotFoundException>(() =>
            service.AssignAsync(task.MobileTaskNo, new AssignTaskV2Request
            {
                OperationId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                RowVersion = Convert.ToBase64String([1]),
                AssignedTo = "operator"
            }, "dispatcher"));
    }

    [Fact]
    public async Task Create_UsesTargetLocationAreaAndRejectsCrossAreaAccess()
    {
        await using var db = NewDb();
        SeedWarehouses(db);
        await db.SaveChangesAsync();
        var service = MoveService(
            db,
            new WmsAccessScope(false, [new WmsScopeGrant("W01", "A1")]));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(Move("A1"), "dispatcher"));

        var denied = await Assert.ThrowsAsync<WmsAccessDeniedException>(() =>
            service.CreateAsync(Move(null), "dispatcher"));
        Assert.Equal("WM-V2-SCOPE-DENIED", denied.Message);
        Assert.Empty(db.MobileTasks);
        Assert.Empty(db.StockTransactions);
    }

    [Fact]
    public async Task ReplenishmentReadsAndWrites_UseTargetLocationArea()
    {
        await using var db = NewDb();
        SeedWarehouses(db);
        db.ReplenishOrders.AddRange(
            Replenishment("R-A1", "W01", "A1-FROM"),
            Replenishment("R-A2", "W01", "A2-TO"),
            Replenishment("R-W2", "W02", "W2-A1"));
        await db.SaveChangesAsync();

        var scope = new WmsAccessScope(
            false, [new WmsScopeGrant("W01", "A1")]);
        var service = ReplenishmentService(db, scope);

        var visible = await service.SearchAsync(
            new ReplenishSearchQuery());

        Assert.Equal(["R-A1"], visible.Select(x => x.ReplenishNo));
        Assert.NotNull(await service.GetAsync("R-A1"));
        Assert.Null(await service.GetAsync("R-A2"));
        Assert.Null(await service.GetAsync("R-W2"));

        var denied = await Assert.ThrowsAsync<WmsAccessDeniedException>(() =>
            service.CreateAsync(new ReplenishOrderDto
            {
                WarehouseCd = "W01",
                FromLocationCd = "A1-FROM",
                ToLocationCd = "A2-TO",
                ProductCd = "P1",
                Qty = 1
            }, "dispatcher"));
        Assert.Equal("WM-V2-SCOPE-DENIED", denied.Message);
    }

    [Fact]
    public async Task SlottingPlans_RequireWarehouseWideGrant()
    {
        await using var db = NewDb();
        SeedWarehouses(db);
        db.SlottingPlans.AddRange(
            Slotting("S-W1", "W01"),
            Slotting("S-W2", "W02"));
        await db.SaveChangesAsync();

        var areaService = SlottingService(
            db,
            new WmsAccessScope(
                false, [new WmsScopeGrant("W01", "A1")]));

        Assert.Empty(await areaService.SearchAsync(null, null));
        Assert.Null(await areaService.GetAsync("S-W1"));
        var denied = await Assert.ThrowsAsync<WmsAccessDeniedException>(() =>
            areaService.AnalyzeAsync("W01", 90, "supervisor"));
        Assert.Equal("WM-V2-SCOPE-DENIED", denied.Message);

        var warehouseService = SlottingService(
            db,
            new WmsAccessScope(
                false, [new WmsScopeGrant("W01", null)]));
        var visible = await warehouseService.SearchAsync(null, null);

        Assert.Equal(["S-W1"], visible.Select(x => x.SlottingPlanNo));
        Assert.NotNull(await warehouseService.GetAsync("S-W1"));
        Assert.StartsWith(
            "SLP",
            await warehouseService.AnalyzeAsync(
                "W01", 90, "supervisor"));
    }

    [Fact]
    public async Task RoleScopeManagement_NormalizesAndProviderUnionsRoleGrants()
    {
        await using var db = NewDb();
        SeedWarehouses(db);
        db.Sys_Roles.AddRange(
            Role(20, "Supervisor"),
            Role(21, "Dispatcher"));
        await db.SaveChangesAsync();
        var service = new WmsRoleScopeService(db);

        var saved = await service.ReplaceAsync(20, new ReplaceWmsRoleScopesRequest
        {
            Scopes =
            [
                new WmsRoleScopeItem { WarehouseCd = "w01", AreaCd = "a1" },
                new WmsRoleScopeItem { WarehouseCd = "W01", AreaCd = "A1" },
                new WmsRoleScopeItem { WarehouseCd = "w02" }
            ]
        }, "admin");

        Assert.Equal(2, saved.Count);
        var provider = new WmsAccessScopeProvider(
            db,
            new FixedPermissionContext(new UserPermissionContext
            {
                RoleIds = [20, 21]
            }));
        var scope = await provider.GetCurrentAsync();
        Assert.True(scope.Allows("W01", "A1"));
        Assert.False(scope.Allows("W01", "A2"));
        Assert.True(scope.Allows("W02", "ANY"));
        Assert.False(scope.Allows("W03", "A1"));
    }

    [Fact]
    public async Task AdminIsAlwaysAllScope_AndUnconfiguredRoleIsFailClosed()
    {
        await using var db = NewDb();
        var admin = new WmsAccessScopeProvider(
            db,
            new FixedPermissionContext(new UserPermissionContext { RoleIds = [1] }));
        var unconfigured = new WmsAccessScopeProvider(
            db,
            new FixedPermissionContext(new UserPermissionContext { RoleIds = [22] }));

        Assert.True((await admin.GetCurrentAsync()).Allows("ANY", "ANY"));
        Assert.False((await unconfigured.GetCurrentAsync()).Allows("W01", "A1"));
    }

    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options, new TenantContext
        {
            CurrentTenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")
        });
    }

    private static MobileTaskV2Service MoveService(
        CP6Context db,
        WmsAccessScope scope)
    {
        var sequence = new WmsSequenceService(db);
        return new MobileTaskV2Service(
            db,
            sequence,
            new StockMovementService(db, sequence),
            new FixedWmsAccessScopeProvider(scope));
    }

    private static ReplenishService ReplenishmentService(
        CP6Context db,
        WmsAccessScope scope)
    {
        var sequence = new WmsSequenceService(db);
        var provider = new FixedWmsAccessScopeProvider(scope);
        return new ReplenishService(
            db,
            sequence,
            new MobileTaskV2Service(
                db,
                sequence,
                new StockMovementService(db, sequence),
                provider),
            provider);
    }

    private static SlottingService SlottingService(
        CP6Context db,
        WmsAccessScope scope)
    {
        var sequence = new WmsSequenceService(db);
        var provider = new FixedWmsAccessScopeProvider(scope);
        return new SlottingService(
            db,
            sequence,
            new MobileTaskV2Service(
                db,
                sequence,
                new StockMovementService(db, sequence),
                provider),
            provider);
    }

    private static MobileTask Mobile(
        string taskNo,
        string warehouse,
        string area,
        int status)
        => new()
        {
            MobileTaskNo = taskNo,
            ContractVersion = 2,
            TaskType = MobileTaskType.Move,
            Status = status,
            WarehouseCd = warehouse,
            AreaCd = area,
            FromLocationCd = "FROM",
            ToLocationCd = "TO",
            ProductCd = "P1",
            Qty = 1
        };

    private static CreateMoveTaskV2Request Move(string? claimedArea)
        => new()
        {
            OperationId = Guid.NewGuid(),
            WarehouseCd = "W01",
            AreaCd = claimedArea,
            FromLocationCd = "A1-FROM",
            ToLocationCd = "A2-TO",
            ProductCd = "P1",
            Qty = 1
        };

    private static ReplenishOrder Replenishment(
        string replenishNo,
        string warehouse,
        string targetLocation)
        => new()
        {
            ReplenishNo = replenishNo,
            WarehouseCd = warehouse,
            FromLocationCd = "RES-01",
            ToLocationCd = targetLocation,
            ProductCd = "P1",
            Qty = 1
        };

    private static SlottingPlan Slotting(
        string planNo,
        string warehouse)
        => new()
        {
            SlottingPlanNo = planNo,
            WarehouseCd = warehouse,
            Status = SlottingStatus.Recommended,
            AnalyzedAt = DateTime.Now
        };

    private static void SeedWarehouses(CP6Context db)
    {
        db.Warehouses.AddRange(
            new Warehouse { WarehouseCd = "W01", WarehouseName = "Warehouse 1" },
            new Warehouse { WarehouseCd = "W02", WarehouseName = "Warehouse 2" });
        db.Locations.AddRange(
            new Location
            {
                WarehouseCd = "W01",
                LocationCd = "A1-FROM",
                AreaCd = "A1"
            },
            new Location
            {
                WarehouseCd = "W01",
                LocationCd = "A2-TO",
                AreaCd = "A2"
            },
            new Location
            {
                WarehouseCd = "W02",
                LocationCd = "W2-A1",
                AreaCd = "A1"
            });
    }

    private static Sys_Role Role(int id, string name)
        => new()
        {
            RoleId = id,
            RoleName = name,
            Enable = true
        };

    private sealed class FixedPermissionContext(UserPermissionContext context)
        : ICurrentPermissionContext
    {
        public Task<UserPermissionContext> GetAsync() => Task.FromResult(context);
        public Task<UserPermissionContext> PrewarmAsync(Guid userId)
            => Task.FromResult(context);
        public void Invalidate(Guid userId) { }
        public void InvalidateByRole(int roleId) { }
    }
}
