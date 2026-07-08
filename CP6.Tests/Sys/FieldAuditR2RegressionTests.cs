using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Controllers.Sys;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// 字段审计（#4 T4）R2 回归：RoleController/MenuController.Update 改"先查后改"后，
/// Modified diff 须准确（attach-as-Modified 旧路径 OriginalValues==CurrentValues → diff 为空 → 无 Op=2 行，
/// 这两个测试在旧代码上 FAIL）。同时校验 CreateDate 不被 Update 覆写（无 Creator/Modifier，仅 CreateDate）。
/// 直接 new 控制器 + InMemory 上下文，绕过 [RequirePermission]（与其它控制器测试一致）。
/// </summary>
public class FieldAuditR2RegressionTests
{
    private sealed class FakeUser : ICurrentUserAccessor
    {
        public FakeUser(Guid? id, string? name) { UserId = id; UserName = name; }
        public Guid? UserId { get; }
        public string? UserName { get; }
    }

    private sealed record DiffRow(string Field, string? Old, string? New);

    private static List<DiffRow> ParseChanges(string json)
        => JsonSerializer.Deserialize<List<DiffRow>>(json) ?? new();

    private static readonly Guid _userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static CP6Context Ctx()
        => TestHelper.CreateInMemoryContext(new FakeUser(_userId, "alice"));

    // ── Role：先查后改 → 精准 diff，且 CreateDate 不被覆写 ──────────────────
    [Fact]
    public async Task RoleController_Update_writes_accurate_diff_and_preserves_CreateDate()
    {
        using var db = Ctx();
        var seededDate = new DateTime(2020, 1, 2, 3, 4, 5);
        db.Sys_Roles.Add(new Sys_Role
        {
            RoleId = 7001,
            RoleName = "原角色",
            Description = "desc",
            Enable = true,
            OrderNo = 5,
            CreateDate = seededDate,
        });
        await db.SaveChangesAsync();

        var controller = new RoleController(db);
        // 模拟请求体携来的"断连"实体：新对象 + 相同 PK + 改名（不含 CreateDate）
        var incoming = new Sys_Role
        {
            RoleId = 7001,
            RoleName = "新角色",
            Description = "desc",
            Enable = true,
            OrderNo = 5,
        };
        var result = await controller.Update(incoming);

        Assert.IsType<OkObjectResult>(result);

        // P0-T3：Sys_Role 复合主键 (TenantId, RoleId) → 审计键为 "<TenantId>|<RoleId>"（默认租户上下文）。
        var expectedKey = $"{TenantContext.DefaultTenant}|7001";
        var modified = db.Sys_FieldAuditLogs
            .Where(x => x.Operation == 2 && x.EntityName == nameof(Sys_Role) && x.EntityKey == expectedKey)
            .ToList();
        Assert.Single(modified);
        var diffs = ParseChanges(modified[0].Changes);
        var nameDiff = Assert.Single(diffs, d => d.Field == "RoleName");
        Assert.Equal("原角色", nameDiff.Old);
        Assert.Equal("新角色", nameDiff.New);

        var stored = await db.Sys_Roles.FirstOrDefaultAsync(r => r.RoleId == 7001);
        Assert.NotNull(stored);
        Assert.Equal(seededDate, stored!.CreateDate);   // CreateDate 未被 Update 覆写
        Assert.Equal("新角色", stored.RoleName);
    }

    // ── Menu：先查后改 → 精准 diff，且 CreateDate 不被覆写 ──────────────────
    [Fact]
    public async Task MenuController_Update_writes_accurate_diff_and_preserves_CreateDate()
    {
        using var db = Ctx();
        var seededDate = new DateTime(2019, 6, 7, 8, 9, 10);
        db.Sys_Menus.Add(new Sys_Menu
        {
            MenuId = 7002,
            MenuName = "原菜单",
            RoutePath = "/old",
            OrderNo = 3,
            Enable = true,
            CreateDate = seededDate,
        });
        await db.SaveChangesAsync();

        var controller = new MenuController(db);
        var incoming = new Sys_Menu
        {
            MenuId = 7002,
            MenuName = "新菜单",
            RoutePath = "/old",
            OrderNo = 3,
            Enable = true,
        };
        var result = await controller.Update(incoming);

        Assert.IsType<OkObjectResult>(result);

        var modified = db.Sys_FieldAuditLogs
            .Where(x => x.Operation == 2 && x.EntityName == nameof(Sys_Menu) && x.EntityKey == "7002")
            .ToList();
        Assert.Single(modified);
        var diffs = ParseChanges(modified[0].Changes);
        var nameDiff = Assert.Single(diffs, d => d.Field == "MenuName");
        Assert.Equal("原菜单", nameDiff.Old);
        Assert.Equal("新菜单", nameDiff.New);

        var stored = await db.Sys_Menus.FindAsync(7002);
        Assert.NotNull(stored);
        Assert.Equal(seededDate, stored!.CreateDate);   // CreateDate 未被 Update 覆写
        Assert.Equal("新菜单", stored.MenuName);
    }
}
