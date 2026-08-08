using CP6.Entity.DomainModels;
using CP6.WebApi.Controllers.Sys;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

public class MenuControllerTests
{
    private static MenuController CreateController(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        return new MenuController(db);
    }

    private static async Task SeedAsync(CP6.Core.EFDbContext.CP6Context db)
    {
        db.Sys_Menus.AddRange(
            new Sys_Menu { MenuId = 100, MenuName = "系统管理", OrderNo = 0 },
            new Sys_Menu { MenuId = 101, MenuName = "角色管理", ParentId = 100, OrderNo = 0 },
            new Sys_Menu { MenuId = 102, MenuName = "菜单管理", ParentId = 100, OrderNo = 1 },
            new Sys_Menu { MenuId = 200, MenuName = "销售管理", OrderNo = 1 }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateTree_UpdatesParentAndSiblingOrder()
    {
        var controller = CreateController(out var db);
        await SeedAsync(db);

        var result = await controller.UpdateTree([
            new MenuTreePosition(100, null, 0),
            new MenuTreePosition(102, 100, 0),
            new MenuTreePosition(101, 100, 1),
            new MenuTreePosition(200, null, 1)
        ]);

        Assert.IsType<OkObjectResult>(result);
        var role = await db.Sys_Menus.SingleAsync(x => x.MenuId == 101);
        var menu = await db.Sys_Menus.SingleAsync(x => x.MenuId == 102);
        Assert.Equal(1, role.OrderNo);
        Assert.Equal(0, menu.OrderNo);

        var moveResult = await controller.UpdateTree([
            new MenuTreePosition(102, 200, 0)
        ]);

        Assert.IsType<OkObjectResult>(moveResult);
        Assert.Equal(200, (await db.Sys_Menus.SingleAsync(x => x.MenuId == 102)).ParentId);
    }

    [Fact]
    public async Task UpdateTree_RejectsCyclesWithoutPersistingChanges()
    {
        var controller = CreateController(out var db);
        await SeedAsync(db);

        var result = await controller.UpdateTree([
            new MenuTreePosition(100, 101, 0),
            new MenuTreePosition(101, 100, 0)
        ]);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null((await db.Sys_Menus.SingleAsync(x => x.MenuId == 100)).ParentId);
        Assert.Equal(100, (await db.Sys_Menus.SingleAsync(x => x.MenuId == 101)).ParentId);
    }

    [Fact]
    public async Task UpdateTree_RejectsUnknownParent()
    {
        var controller = CreateController(out var db);
        await SeedAsync(db);

        var result = await controller.UpdateTree([
            new MenuTreePosition(101, 999, 0)
        ]);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(100, (await db.Sys_Menus.SingleAsync(x => x.MenuId == 101)).ParentId);
    }

    [Fact]
    public async Task UpdateTree_RejectsNonPositiveMenuId()
    {
        var controller = CreateController(out var db);

        var result = await controller.UpdateTree([
            new MenuTreePosition(0, null, 0)
        ]);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(db.Sys_Menus);
    }
}
