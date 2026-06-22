using System.Collections;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Controllers.Sys;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// 安全日志分页查询（S 类认证加固 T8）。直接调 GetList（[RequirePermission] 授权过滤器在 MVC 管线
/// 才运行，单测直调不触发）；返回为匿名对象 { rows, total }，用反射读出断言。
/// </summary>
public class SecurityLogControllerTests
{
    private static void Seed(CP6Context db, SecurityEventType type, string? userName, DateTime at)
    {
        db.Sys_SecurityLogs.Add(new Sys_SecurityLog
        {
            EventType = (int)type,
            UserName = userName,
            CreatedAt = at
        });
    }

    private static (int total, int rowCount) Unwrap(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var v = ok.Value!;
        var total = (int)v.GetType().GetProperty("total")!.GetValue(v)!;
        var rows = (IEnumerable)v.GetType().GetProperty("rows")!.GetValue(v)!;
        return (total, rows.Cast<object>().Count());
    }

    [Fact]
    public async Task Filters_by_event_type()
    {
        using var db = TestHelper.CreateInMemoryContext();
        Seed(db, SecurityEventType.LoginFailed, "alice", DateTime.Now);
        Seed(db, SecurityEventType.LoginFailed, "bob", DateTime.Now);
        Seed(db, SecurityEventType.LoginSuccess, "alice", DateTime.Now);
        db.SaveChanges();

        var (total, _) = Unwrap(await new SecurityLogController(db)
            .GetList(eventType: (int)SecurityEventType.LoginFailed, userName: null, from: null, to: null));

        Assert.Equal(2, total);   // 仅 2 条 LoginFailed
    }

    [Fact]
    public async Task Filters_by_username_substring()
    {
        using var db = TestHelper.CreateInMemoryContext();
        Seed(db, SecurityEventType.LoginSuccess, "alice", DateTime.Now);
        Seed(db, SecurityEventType.LoginSuccess, "alicia", DateTime.Now);
        Seed(db, SecurityEventType.LoginSuccess, "bob", DateTime.Now);
        db.SaveChanges();

        var (total, _) = Unwrap(await new SecurityLogController(db)
            .GetList(eventType: null, userName: "ali", from: null, to: null));

        Assert.Equal(2, total);   // alice + alicia 含 "ali"
    }

    [Fact]
    public async Task Filters_by_date_range_inclusive_of_to_day()
    {
        using var db = TestHelper.CreateInMemoryContext();
        Seed(db, SecurityEventType.LoginSuccess, "x", new DateTime(2026, 6, 1, 10, 0, 0));
        Seed(db, SecurityEventType.LoginSuccess, "x", new DateTime(2026, 6, 15, 23, 0, 0));   // to 当日晚间须命中
        Seed(db, SecurityEventType.LoginSuccess, "x", new DateTime(2026, 6, 20, 10, 0, 0));
        db.SaveChanges();

        var (total, _) = Unwrap(await new SecurityLogController(db)
            .GetList(eventType: null, userName: null,
                     from: new DateTime(2026, 6, 10), to: new DateTime(2026, 6, 15)));

        Assert.Equal(1, total);   // 仅 6/15 那条（6/1 早于 from，6/20 晚于 to）
    }

    [Fact]
    public async Task Paginates_and_returns_total()
    {
        using var db = TestHelper.CreateInMemoryContext();
        for (int i = 0; i < 5; i++)
            Seed(db, SecurityEventType.LoginSuccess, $"u{i}", DateTime.Now.AddMinutes(-i));
        db.SaveChanges();

        var (total, rowCount) = Unwrap(await new SecurityLogController(db)
            .GetList(eventType: null, userName: null, from: null, to: null, page: 1, pageSize: 2));

        Assert.Equal(5, total);       // 总数为全量
        Assert.Equal(2, rowCount);    // 当页仅 2 条
    }

    [Fact]
    public async Task Page_zero_is_clamped_to_first_page_without_throwing()
    {
        using var db = TestHelper.CreateInMemoryContext();
        for (int i = 0; i < 3; i++)
            Seed(db, SecurityEventType.LoginSuccess, $"u{i}", DateTime.Now.AddMinutes(-i));
        db.SaveChanges();

        // page=0 不应抛（负数 Skip），应 clamp 到第 1 页
        var (total, rowCount) = Unwrap(await new SecurityLogController(db)
            .GetList(eventType: null, userName: null, from: null, to: null, page: 0, pageSize: 2));

        Assert.Equal(3, total);
        Assert.Equal(2, rowCount);
    }
}
