using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;

namespace CP6.Tests;

public class DictServiceTests
{
    private static CP6Context Db()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static DictService Svc(CP6Context db) => new(db, new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task Translate_ReturnsLabel_UnknownReturnsValue_NullStaysNull()
    {
        using var db = Db();
        db.Sys_DictDatas.Add(new Sys_DictData { TypeCode = "order_status", Value = "1", Label = "已确认", Enable = true });
        await db.SaveChangesAsync();
        var svc = Svc(db);

        Assert.Equal("已确认", await svc.TranslateAsync("order_status", "1"));   // 命中
        Assert.Equal("9", await svc.TranslateAsync("order_status", "9"));        // 未命中 → 原值
        Assert.Null(await svc.TranslateAsync("order_status", null));             // null
    }

    [Fact]
    public async Task GetItems_OnlyEnabled_OrderedByOrderNo()
    {
        using var db = Db();
        db.Sys_DictDatas.AddRange(
            new Sys_DictData { TypeCode = "t", Value = "b", Label = "B", OrderNo = 2, Enable = true },
            new Sys_DictData { TypeCode = "t", Value = "a", Label = "A", OrderNo = 1, Enable = true },
            new Sys_DictData { TypeCode = "t", Value = "x", Label = "X", OrderNo = 0, Enable = false });   // 禁用
        await db.SaveChangesAsync();

        var items = await Svc(db).GetItemsAsync("t");
        Assert.Equal(new[] { "a", "b" }, items.Select(i => i.Value));   // 仅启用 + 按 OrderNo
    }

    [Fact]
    public async Task GetItems_Caches_UntilInvalidate()
    {
        using var db = Db();
        var svc = Svc(db);
        Assert.Empty(await svc.GetItemsAsync("t"));   // 缓存空结果

        db.Sys_DictDatas.Add(new Sys_DictData { TypeCode = "t", Value = "1", Label = "L", Enable = true });
        await db.SaveChangesAsync();
        Assert.Empty(await svc.GetItemsAsync("t"));   // 仍命中旧缓存

        svc.InvalidateType("t");
        Assert.Single(await svc.GetItemsAsync("t"));   // 失效后重查
    }
}
