using CP6.Entity.DomainModels;
using CP6.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests;

/// <summary>
/// DictController 集成测试（用 InMemory DB 测试完整 CRUD 流程）
///
/// 面试要点：
/// 1. 集成测试 vs 单元测试：
///    - 单元测试：只测一个方法，Mock 掉所有依赖
///    - 集成测试：测完整流程，用 InMemory DB 替代真实 DB
/// 2. 每个测试方法独立数据库（Guid），互不干扰
/// 3. 测试 Controller 返回的 IActionResult 类型（OkObjectResult / BadRequestObjectResult）
/// </summary>
public class DictControllerTests
{
    private DictController CreateController()
    {
        var context = TestHelper.CreateInMemoryContext();
        var cache = TestHelper.CreateCacheService();
        return new DictController(context, cache);
    }

    [Fact]
    public async Task AddType_ShouldReturnOk()
    {
        // Arrange
        var controller = CreateController();
        var entity = new Sys_DictType
        {
            TypeCode = "test_type",
            TypeName = "Test Type",
            Enable = true,
            OrderNo = 1
        };

        // Act
        var result = await controller.AddType(entity);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var created = Assert.IsType<Sys_DictType>(okResult.Value);
        Assert.Equal("test_type", created.TypeCode);
        Assert.True(created.Id > 0); // 自动生成 ID
    }

    [Fact]
    public async Task AddType_DuplicateCode_ShouldReturnBadRequest()
    {
        // Arrange
        var controller = CreateController();
        var entity1 = new Sys_DictType { TypeCode = "dup", TypeName = "First", OrderNo = 1 };
        var entity2 = new Sys_DictType { TypeCode = "dup", TypeName = "Second", OrderNo = 2 };

        // Act
        await controller.AddType(entity1);
        var result = await controller.AddType(entity2); // 重复

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetTypes_ShouldReturnPaginatedData()
    {
        // Arrange
        var controller = CreateController();
        await controller.AddType(new Sys_DictType { TypeCode = "a", TypeName = "AA", OrderNo = 1 });
        await controller.AddType(new Sys_DictType { TypeCode = "b", TypeName = "BB", OrderNo = 2 });
        await controller.AddType(new Sys_DictType { TypeCode = "c", TypeName = "CC", OrderNo = 3 });

        // Act：查第 1 页，每页 2 条
        var result = await controller.GetTypes(page: 1, pageSize: 2);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var data = okResult.Value!;
        // 用反射获取匿名对象属性
        var total = (int)data.GetType().GetProperty("total")!.GetValue(data)!;
        Assert.Equal(3, total); // 总共 3 条
    }

    [Fact]
    public async Task UpdateType_ShouldModifyData()
    {
        // Arrange
        var controller = CreateController();
        await controller.AddType(new Sys_DictType { TypeCode = "upd", TypeName = "Before", OrderNo = 1 });

        // Act
        var result = await controller.UpdateType(new Sys_DictType
        {
            Id = 1,
            TypeCode = "upd",
            TypeName = "After",
            OrderNo = 2
        });

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var updated = Assert.IsType<Sys_DictType>(okResult.Value);
        Assert.Equal("After", updated.TypeName);
        Assert.Equal(2, updated.OrderNo);
    }

    [Fact]
    public async Task UpdateType_NotFound_ShouldReturn404()
    {
        // Arrange
        var controller = CreateController();

        // Act：更新不存在的记录
        var result = await controller.UpdateType(new Sys_DictType { Id = 999 });

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteTypes_ShouldRemoveData()
    {
        // Arrange
        var controller = CreateController();
        await controller.AddType(new Sys_DictType { TypeCode = "del1", TypeName = "D1", OrderNo = 1 });
        await controller.AddType(new Sys_DictType { TypeCode = "del2", TypeName = "D2", OrderNo = 2 });

        // Act：删除第一条
        var result = await controller.DeleteTypes(new[] { 1 });

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        // 确认剩余 1 条
        var listResult = await controller.GetTypes(page: 1, pageSize: 10);
        var data = ((OkObjectResult)listResult).Value!;
        var total = (int)data.GetType().GetProperty("total")!.GetValue(data)!;
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task GetOptions_ShouldReturnEnabledOnly()
    {
        // Arrange
        var context = TestHelper.CreateInMemoryContext();
        var cache = TestHelper.CreateCacheService();
        var controller = new DictController(context, cache);

        context.Sys_DictDatas.AddRange(
            new Sys_DictData { TypeCode = "gender", Value = "1", Label = "Male", Enable = true, OrderNo = 1 },
            new Sys_DictData { TypeCode = "gender", Value = "2", Label = "Female", Enable = true, OrderNo = 2 },
            new Sys_DictData { TypeCode = "gender", Value = "3", Label = "Disabled", Enable = false, OrderNo = 3 }
        );
        await context.SaveChangesAsync();

        // Act
        var result = await controller.GetOptions("gender");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var options = Assert.IsAssignableFrom<IEnumerable<object>>(okResult.Value);
        Assert.Equal(2, options.Count()); // Enable=false 的被过滤
    }

    [Fact]
    public async Task GetOptions_ShouldBeCached()
    {
        // Arrange
        var context = TestHelper.CreateInMemoryContext();
        var cache = TestHelper.CreateCacheService();
        var controller = new DictController(context, cache);

        context.Sys_DictDatas.Add(
            new Sys_DictData { TypeCode = "cached", Value = "1", Label = "Test", Enable = true, OrderNo = 1 }
        );
        await context.SaveChangesAsync();

        // Act：第一次调用（写入缓存）
        await controller.GetOptions("cached");

        // 直接从 DB 删除数据（模拟数据变化但缓存未失效）
        context.Sys_DictDatas.RemoveRange(context.Sys_DictDatas);
        await context.SaveChangesAsync();

        // 第二次调用（应该从缓存返回，不查 DB）
        var result = await controller.GetOptions("cached");

        // Assert：虽然 DB 已清空，但缓存还有数据
        var okResult = Assert.IsType<OkObjectResult>(result);
        var options = Assert.IsAssignableFrom<IEnumerable<object>>(okResult.Value);
        Assert.Single(options); // 缓存命中，返回旧数据
    }
}
