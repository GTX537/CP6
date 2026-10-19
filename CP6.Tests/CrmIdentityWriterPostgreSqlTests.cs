using System.Text.Json;
using System.Text;
using System.Data;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.EntityFramework;
using CP6.Platform.Messaging;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;

namespace CP6.Tests;

[Collection("TenantManagementPostgres")]
public sealed class CrmIdentityWriterPostgreSqlTests(TenantManagementPostgresFixture server)
{
    [CurrentPermissionPostgresFact]
    public async Task Menu_change_cannot_miss_a_concurrent_uncommitted_role_action()
    {
        var tenant = Guid.NewGuid(); var runtime = Runtime(tenant);
        var menuId = 72201; var oldKey = "crm-w22-old"; var newKey = "crm-w22-new";
        await using (var seed = Context(tenant, runtime))
        {
            seed.Sys_Menus.Add(new Sys_Menu { MenuId = menuId, MenuName = "writer fixture", MenuKey = oldKey });
            seed.Sys_Roles.Add(new Sys_Role { RoleId = 71, RoleName = "writer role" });
            await seed.SaveChangesAsync();
        }
        await using var actionWriter = Context(tenant, runtime);
        await using var actionTx = await actionWriter.Database.BeginTransactionAsync();
        actionWriter.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 71, MenuId = menuId, ActionCode = "read" });
        await actionWriter.SaveChangesAsync();
        await using var menuWriter = Context(tenant, runtime);
        (await menuWriter.Sys_Menus.SingleAsync(x => x.MenuId == menuId)).MenuKey = newKey;
        await menuWriter.Database.OpenConnectionAsync();
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var changingMenu = menuWriter.SaveChangesAsync(cancel.Token);
        try
        {
            // A correct fence blocks menu expansion until the first writer commits.
            // On the original implementation this completes first and misses the uncommitted link.
            await CompletedOrWaiting(changingMenu, menuWriter);
            await actionTx.CommitAsync(cancel.Token);
            await changingMenu;
        }
        finally { cancel.Cancel(); try { await changingMenu; } catch (OperationCanceledException) { } }
        await using var observer = Context(tenant);
        var row = await observer.CrmIdentitySnapshots.SingleAsync(x => x.TenantId == tenant && x.AggregateId == "permission:role:71");
        var permission = JsonSerializer.Deserialize<PermissionIdentityData>(row.PayloadJson, IdentityEventContracts.Json)!;
        Assert.Equal(newKey, Assert.Single(permission.Grants).Resource);
        await AssertLatest(tenant, "permission:role:71");
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Menu_first_orders_real_role_service_after_rename_or_delete(bool delete)
    {
        var tenant = Guid.NewGuid(); var runtime = Runtime(tenant); var menuId = delete ? 72203 : 72202;
        await SeedPermission(tenant, runtime, menuId, withAction: false);
        await using var menuWriter = Context(tenant, runtime);
        await using var menuTx = await menuWriter.Database.BeginTransactionAsync();
        var menu = await menuWriter.Sys_Menus.SingleAsync(x => x.MenuId == menuId);
        if (delete) menuWriter.Remove(menu); else menu.MenuKey = "crm-w22-renamed";
        await menuWriter.SaveChangesAsync();
        await using var actionWriter = Context(tenant, runtime);
        await actionWriter.Database.OpenConnectionAsync();
        var service = new RolePermService(actionWriter, Mock.Of<ICurrentPermissionContext>());
        var pending = service.SaveRolePermAsync(71, Permission(menuId), "fixture");
        try
        {
            await CompletedOrWaiting(pending, actionWriter); Assert.False(pending.IsCompleted);
            await menuTx.CommitAsync(); await pending;
        }
        finally { if (!pending.IsCompleted) await menuTx.RollbackAsync(); await pending; }
        await using var observer = Context(tenant);
        var data = await PermissionData(observer, tenant);
        if (delete) Assert.Empty(data.Grants); else Assert.Equal("crm-w22-renamed", Assert.Single(data.Grants).Resource);
        await AssertLatest(tenant, "permission:role:71");
    }

    [TenantManagementPostgresTheory]
    [InlineData(IsolationLevel.RepeatableRead)]
    [InlineData(IsolationLevel.Serializable)]
    public async Task Menu_fanout_rejects_a_caller_snapshot_that_cannot_refresh(IsolationLevel isolation)
    {
        var tenant = Guid.NewGuid(); var runtime = Runtime(tenant); var menuId = isolation == IsolationLevel.RepeatableRead ? 72204 : 72205;
        await SeedPermission(tenant, runtime, menuId, withAction: true);
        await using var writer = Context(tenant, runtime); await using var tx = await writer.Database.BeginTransactionAsync(isolation);
        var menu = await writer.Sys_Menus.SingleAsync(x => x.MenuId == menuId); menu.MenuKey = "crm-must-not-commit";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveChangesAsync());
        Assert.Equal("C02_MENU_FANOUT_REQUIRES_READ_COMMITTED", error.Message);
        Assert.Equal(1, await writer.Database.SqlQuery<int>($"SELECT 1 AS \"Value\"").SingleAsync());
        await tx.CommitAsync();
        await using var observer = Context(tenant);
        Assert.Equal(SeedKey(menuId), (await observer.Sys_Menus.SingleAsync(x => x.MenuId == menuId)).MenuKey);
        Assert.Equal(1, (await PermissionData(observer, tenant)).Version);
    }

    [CurrentPermissionPostgresFact]
    public async Task Cancelled_menu_fence_wait_rolls_back_savepoint_and_releases_waiter()
    {
        var tenant = Guid.NewGuid(); var runtime = Runtime(tenant); const int menuId = 72206;
        await SeedPermission(tenant, runtime, menuId, withAction: true);
        await using var blocker = Context(tenant); await using var blockTx = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlRawAsync("""LOCK TABLE public."Sys_RoleAction" IN ROW EXCLUSIVE MODE""");
        await using var writer = Context(tenant, runtime); await using var caller = await writer.Database.BeginTransactionAsync();
        (await writer.Sys_Menus.SingleAsync(x => x.MenuId == menuId)).MenuKey = "crm-cancelled";
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var pending = writer.SaveChangesAsync(cancel.Token);
        try
        {
            await CompletedOrWaiting(pending, writer); Assert.False(pending.IsCompleted); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Equal(1, await writer.Database.SqlQuery<int>($"SELECT 1 AS \"Value\"").SingleAsync());
            await caller.CommitAsync(); // The caller still owns a usable transaction after savepoint rollback.
        }
        finally { cancel.Cancel(); await blockTx.RollbackAsync(); }
        await using var observer = Context(tenant);
        Assert.Equal(SeedKey(menuId), (await observer.Sys_Menus.SingleAsync(x => x.MenuId == menuId)).MenuKey);
        Assert.Equal(1, (await PermissionData(observer, tenant)).Version);
        await using var retry = Context(tenant, runtime);
        (await retry.Sys_Menus.SingleAsync(x => x.MenuId == menuId)).MenuKey = "crm-recovered"; await retry.SaveChangesAsync();
        Assert.Equal("crm-recovered", Assert.Single((await PermissionData(observer, tenant)).Grants).Resource);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Outbox_failure_rolls_back_menu_snapshot_and_releases_fence(bool callerOwned)
    {
        var tenant = Guid.NewGuid(); var runtime = Runtime(tenant); var menuId = callerOwned ? 72208 : 72207;
        await SeedPermission(tenant, runtime, menuId, withAction: true);
        var constraint = "w22_reject_" + tenant.ToString("N");
        await using var control = Context(tenant);
        await control.Database.ExecuteSqlRawAsync($"ALTER TABLE crm_identity.\"Cp6_OutboxMessage\" ADD CONSTRAINT {constraint} CHECK (\"TenantId\" <> '{tenant:D}'::uuid) NOT VALID");
        try
        {
            await using var writer = Context(tenant, runtime);
            await using var tx = callerOwned ? await writer.Database.BeginTransactionAsync() : null;
            (await writer.Sys_Menus.SingleAsync(x => x.MenuId == menuId)).MenuKey = "crm-rejected";
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => writer.SaveChangesAsync());
            Assert.Equal(constraint, Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
            if (tx is not null)
            {
                Assert.Equal(1, await writer.Database.SqlQuery<int>($"SELECT 1 AS \"Value\"").SingleAsync());
                await tx.CommitAsync();
            }
            Assert.Equal(SeedKey(menuId), (await control.Sys_Menus.AsNoTracking().SingleAsync(x => x.MenuId == menuId)).MenuKey);
            Assert.Equal(1, (await PermissionData(control, tenant)).Version);
            Assert.Single(await control.Set<Cp6OutboxMessage>().Where(x => x.TenantId == tenant).ToArrayAsync());
        }
        finally { await control.Database.ExecuteSqlRawAsync($"ALTER TABLE crm_identity.\"Cp6_OutboxMessage\" DROP CONSTRAINT {constraint}"); }
        await using var retry = Context(tenant, runtime);
        (await retry.Sys_Menus.SingleAsync(x => x.MenuId == menuId)).MenuKey = "crm-after-failure-" + menuId; await retry.SaveChangesAsync();
        Assert.Equal(2, (await PermissionData(control, tenant)).Version); await AssertLatest(tenant, "permission:role:71");
    }

    [CurrentPermissionPostgresFact]
    public async Task Real_membership_and_department_services_publish_final_facts_without_display_only_versions()
    {
        var tenant = Guid.NewGuid(); var runtime = Runtime(tenant);
        await using var writer = Context(tenant, runtime); var current = Mock.Of<ICurrentPermissionContext>();
        var user = new Sys_User { UserName = "writer-user", Password = "fixture-only" };
        writer.Sys_Users.Add(user); writer.Sys_Roles.AddRange(new Sys_Role { RoleId = 71, RoleName = "one" }, new Sys_Role { RoleId = 72, RoleName = "two" });
        await writer.SaveChangesAsync();
        var memberships = new UserRoleService(writer, current); await memberships.SaveAsync(user.Id, [71, 72, 72], 71, "fixture");
        var aggregate = $"user:{user.Id:D}";
        var first = await UserData(writer, tenant, aggregate); Assert.Equal(new[] { "role:71", "role:72" }, first.Roles); Assert.Equal(2, first.Version);
        await memberships.SaveAsync(user.Id, [72], 72, "fixture"); Assert.Equal(new[] { "role:72" }, (await UserData(writer, tenant, aggregate)).Roles);
        var service = new DeptService(writer); var code = tenant.ToString("N");
        var parent = await service.CreateAsync(new() { DeptCode = code + "p", DeptName = "parent" }, null, "fixture");
        var child = await service.CreateAsync(new() { DeptCode = code + "c", DeptName = "child" }, null, "fixture");
        var grandchild = await service.CreateAsync(new() { DeptCode = code + "g", DeptName = "grandchild" }, child, "fixture");
        await service.MoveAsync(child, parent, "fixture");
        var grand = await writer.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.AggregateId == $"department:{grandchild:D}");
        var grandData = JsonSerializer.Deserialize<DepartmentIdentityData>(grand.PayloadJson, IdentityEventContracts.Json)!;
        Assert.Equal($"/{parent:D}/{child:D}/{grandchild:D}/", grandData.Path); Assert.Equal(2, grandData.Version);
        await service.SetUserOrgAsync(user.Id, new() { DeptId = child, ManagerId = user.Id, Email = "fixture@example.test" }, "fixture");
        Assert.Equal(child, (await UserData(writer, tenant, aggregate)).DeptId);
        await service.SetLeaderAsync(child, user.Id, "fixture");
        var beforeDisplay = (await UserData(writer, tenant, aggregate)).Version;
        await service.SetUserOrgAsync(user.Id, new() { DeptId = child, ManagerId = user.Id, Email = "changed@example.test" }, "fixture");
        Assert.Equal(beforeDisplay, (await UserData(writer, tenant, aggregate)).Version);
        await service.DeleteAsync(grandchild);
        var deleted = await writer.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.AggregateId == $"department:{grandchild:D}");
        Assert.True(deleted.IsDeleted); Assert.Equal(3, deleted.Version);
        await AssertLatest(tenant, aggregate); await AssertLatest(tenant, $"department:{grandchild:D}");
    }

    [CurrentPermissionPostgresFact]
    public async Task Real_role_permission_service_preserves_scope_field_and_tenant_boundaries()
    {
        var tenant = Guid.NewGuid(); var foreign = Guid.NewGuid(); var runtime = Runtime(tenant, foreign); const int menuId = 72209;
        await SeedPermission(tenant, runtime, menuId, withAction: false);
        await using (var other = Context(foreign, runtime)) { other.Sys_Roles.Add(new Sys_Role { RoleId = 71, RoleName = "foreign" }); await other.SaveChangesAsync(); }
        await using var writer = Context(tenant, runtime); var service = new RolePermService(writer, Mock.Of<ICurrentPermissionContext>());
        await service.SaveRolePermAsync(71, Permission(menuId), "fixture");
        var dept = Guid.NewGuid();
        await service.SaveRoleDataScopesAsync(71, [new() { ResourceKey = SeedKey(menuId), ScopeType = 4, CustomDeptIds = [dept] }], "fixture");
        FieldRegistry.Register(SeedKey(menuId), new FieldRegistry.Field("cost", "Cost"));
        await service.SaveRoleFieldPermsAsync(71, [new() { ResourceKey = SeedKey(menuId), FieldName = "cost", Access = 3 }], "fixture");
        var data = await PermissionData(writer, tenant); var grant = Assert.Single(data.Grants);
        Assert.Equal(4, grant.Scope); Assert.Equal(new[] { dept }, grant.DeptIds); Assert.Equal(new[] { new IdentityFieldPermission("cost", 3) }, grant.Fields);
        Assert.Equal(1, (await PermissionData(writer, foreign)).Version); Assert.Empty((await PermissionData(writer, foreign)).Grants);
        await service.SaveRolePermAsync(71, new(), "fixture"); Assert.Empty((await PermissionData(writer, tenant)).Grants);
        await AssertLatest(tenant, "permission:role:71");
    }

    private async Task SeedPermission(Guid tenant, CrmIdentityRuntime runtime, int menuId, bool withAction)
    {
        await using var db = Context(tenant, runtime);
        db.Sys_Menus.Add(new Sys_Menu { MenuId = menuId, MenuName = "fixture", MenuKey = SeedKey(menuId) });
        db.Sys_Roles.Add(new Sys_Role { RoleId = 71, RoleName = "fixture" });
        if (withAction) db.Sys_RoleActions.Add(new Sys_RoleAction { RoleId = 71, MenuId = menuId, ActionCode = "read" });
        await db.SaveChangesAsync();
    }
    private static string SeedKey(int menuId) => "crm-w22-seed-" + menuId;
    private static RolePermDto Permission(int menuId) => new() { MenuIds = [menuId], Actions = [new() { MenuId = menuId, ActionCode = "read" }] };
    private static async Task<PermissionIdentityData> PermissionData(CP6Context db, Guid tenant)
        => JsonSerializer.Deserialize<PermissionIdentityData>((await db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.AggregateId == "permission:role:71")).PayloadJson, IdentityEventContracts.Json)!;
    private static async Task<UserIdentityData> UserData(CP6Context db, Guid tenant, string aggregate)
        => JsonSerializer.Deserialize<UserIdentityData>((await db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.AggregateId == aggregate)).PayloadJson, IdentityEventContracts.Json)!;
    private async Task CompletedOrWaiting(Task pending, CP6Context writer)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(8); var pid = ((NpgsqlConnection)writer.Database.GetDbConnection()).ProcessID;
        await using var observer = Context(Guid.NewGuid());
        while (DateTimeOffset.UtcNow < until)
        {
            if (pending.IsCompleted) { await pending; return; }
            if (await observer.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid={pid} AND wait_event_type='Lock') AS \"Value\"").SingleAsync()) return;
            await Task.Delay(20);
        }
        Assert.Fail("Owned writer neither completed nor reached a database lock wait.");
    }
    private async Task AssertLatest(Guid tenant, string aggregate)
    {
        await using var db = Context(tenant); await using var priority = server.PriorityContext();
        var row = await db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.AggregateId == aggregate);
        var messages = (await db.Set<Cp6OutboxMessage>().Where(x => x.TenantId == tenant && x.AggregateId == aggregate).ToArrayAsync())
            .Concat(await priority.Set<Cp6OutboxMessage>().Where(x => x.TenantId == tenant && x.AggregateId == aggregate).ToArrayAsync()).OrderBy(x => x.AggregateVersion).ToArray();
        Assert.Equal(Enumerable.Range(1, row.Version), messages.Select(x => x.AggregateVersion));
        Assert.Equal(IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(row.PayloadJson)), row.PayloadSha256);
        using var envelope = JsonDocument.Parse(messages[^1].Payload); Assert.Equal(row.PayloadJson, envelope.RootElement.GetProperty("data").GetRawText());
        Assert.True(Runtime(tenant).Validator.IsValid(messages[^1].Payload));
    }

    private CP6Context Context(Guid tenant, CrmIdentityRuntime? runtime = null)
        => server.Context(new TenantContext { CurrentTenantId = tenant }, runtime);
    private static CrmIdentityRuntime Runtime(params Guid[] tenants)
    {
        const string issuer = "https://identity-writers.example.test";
        var options = new CrmIdentityOptions { Enabled = true, Issuer = issuer, ProjectionReaderClientIds = ["fixture-reader"] };
        foreach (var tenant in tenants) options.Tenants.Add(tenant, "us");
        return new(options, new IdentityEventValidator(Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory, "contracts/events/platform")), issuer));
    }
}
