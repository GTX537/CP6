using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

public sealed class PurReconcileMenuSeedTests
{
    [Fact]
    public void EnsureSeeded_FirstInitializationCreatesMenuWithResourceKeyAndAdminGrant()
    {
        using var db = TestHelper.CreateInMemoryContext();
        BackfillMissingMenuKeys(db);

        PurReconcileMenuSeed.EnsureSeeded(db);
        db.ChangeTracker.Clear();

        var menu = Assert.Single(db.Sys_Menus.AsNoTracking());
        Assert.Equal(708, menu.MenuId);
        Assert.Equal("采购对账", menu.MenuName);
        Assert.Equal("/pur/reconcile", menu.RoutePath);
        Assert.Equal("pur-reconcile", menu.MenuKey);
        Assert.Equal("Finished", menu.Icon);
        Assert.Equal(700, menu.ParentId);
        Assert.Equal(708, menu.OrderNo);
        Assert.True(menu.Enable);
        var grant = Assert.Single(db.Sys_RoleMenus.AsNoTracking());
        Assert.Equal(1, grant.RoleId);
        Assert.Equal(708, grant.MenuId);

        var audit = Assert.Single(db.Sys_FieldAuditLogs.AsNoTracking());
        Assert.Equal(nameof(Sys_Menu), audit.EntityName);
        Assert.Equal("708", audit.EntityKey);
        Assert.Equal(1, audit.Operation);
        using var changes = JsonDocument.Parse(audit.Changes);
        var keyChange = Assert.Single(changes.RootElement.EnumerateArray(),
            change => change.GetProperty("Field").GetString() == nameof(Sys_Menu.MenuKey));
        Assert.Equal("pur-reconcile", keyChange.GetProperty("New").GetString());
    }

    [Fact]
    public void EnsureSeeded_RepeatedInitializationDoesNotChangeRowsOrAddFieldAudit()
    {
        using var db = TestHelper.CreateInMemoryContext();
        BackfillMissingMenuKeys(db);
        PurReconcileMenuSeed.EnsureSeeded(db);
        db.ChangeTracker.Clear();
        var before = db.Sys_Menus.AsNoTracking().Single();
        var grantId = db.Sys_RoleMenus.AsNoTracking().Single().Id;
        var auditId = db.Sys_FieldAuditLogs.AsNoTracking().Single().Id;

        // Program performs the global key backfill before this seed on every initialization.
        BackfillMissingMenuKeys(db);
        PurReconcileMenuSeed.EnsureSeeded(db);
        db.ChangeTracker.Clear();

        var after = Assert.Single(db.Sys_Menus.AsNoTracking());
        Assert.Equal(before.MenuKey, after.MenuKey);
        Assert.Equal(before.CreateDate, after.CreateDate);
        Assert.Equal(grantId, Assert.Single(db.Sys_RoleMenus.AsNoTracking()).Id);
        Assert.Equal(auditId, Assert.Single(db.Sys_FieldAuditLogs.AsNoTracking()).Id);
    }

    [Fact]
    public void EnsureSeeded_ExistingMenuPreservesOverridesAndRoleGrants()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var createdAt = new DateTime(2026, 1, 2, 3, 4, 5);
        db.Sys_Menus.Add(new Sys_Menu
        {
            MenuId = 708,
            MenuName = "自定义采购对账",
            RoutePath = "/custom/reconcile",
            MenuKey = "custom-reconcile",
            Icon = "CustomIcon",
            ParentId = 900,
            OrderNo = 12,
            Enable = false,
            CreateDate = createdAt,
        });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 2, MenuId = 708 });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        var grantId = db.Sys_RoleMenus.AsNoTracking().Single().Id;
        var auditId = db.Sys_FieldAuditLogs.AsNoTracking().Single().Id;

        BackfillMissingMenuKeys(db);
        PurReconcileMenuSeed.EnsureSeeded(db);
        db.ChangeTracker.Clear();

        var menu = Assert.Single(db.Sys_Menus.AsNoTracking());
        Assert.Equal("自定义采购对账", menu.MenuName);
        Assert.Equal("/custom/reconcile", menu.RoutePath);
        Assert.Equal("custom-reconcile", menu.MenuKey);
        Assert.Equal("CustomIcon", menu.Icon);
        Assert.Equal(900, menu.ParentId);
        Assert.Equal(12, menu.OrderNo);
        Assert.False(menu.Enable);
        Assert.Equal(createdAt, menu.CreateDate);
        var grant = Assert.Single(db.Sys_RoleMenus.AsNoTracking());
        Assert.Equal(grantId, grant.Id);
        Assert.Equal(2, grant.RoleId);
        Assert.Equal(auditId, Assert.Single(db.Sys_FieldAuditLogs.AsNoTracking()).Id);
    }

    private static void BackfillMissingMenuKeys(CP6Context db)
    {
        foreach (var menu in db.Sys_Menus.Where(m => m.MenuKey == null && m.RoutePath != null).ToList())
            menu.MenuKey = menu.RoutePath!.Trim('/').Replace('/', '-');
        db.SaveChanges();
    }
}
