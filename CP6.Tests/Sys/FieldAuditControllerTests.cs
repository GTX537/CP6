using System.Collections;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Controllers.Sys;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// 字段审计查询（#4 字段审计 T5）。直接 new FieldAuditController(db) 调方法（[RequirePermission]
/// 授权过滤器在 MVC 管线才运行，单测直调不触发——移交 T8 gstack）；返回为匿名对象，用反射读出断言。
/// 列表端点返 changeCount 摘要不返完整 changes（防大负载）；record 端点按 ChangedAt 正序返完整 changes。
/// </summary>
public class FieldAuditControllerTests
{
    private static void Seed(CP6Context db, string entityName, string entityKey, int operation,
        string changes, Guid? userId = null, string? userName = null, DateTime? at = null)
    {
        db.Sys_FieldAuditLogs.Add(new Sys_FieldAuditLog
        {
            EntityName = entityName,
            EntityKey = entityKey,
            Operation = operation,
            Changes = changes,
            UserId = userId,
            UserName = userName,
            ChangedAt = at ?? DateTime.Now
        });
    }

    private static (int total, List<object> rows) Unwrap(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var v = ok.Value!;
        var total = (int)v.GetType().GetProperty("total")!.GetValue(v)!;
        var rows = (IEnumerable)v.GetType().GetProperty("rows")!.GetValue(v)!;
        return (total, rows.Cast<object>().ToList());
    }

    private static List<object> UnwrapRows(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var v = ok.Value!;
        var rows = (IEnumerable)v.GetType().GetProperty("rows")!.GetValue(v)!;
        return rows.Cast<object>().ToList();
    }

    private static T Prop<T>(object row, string name)
    {
        var p = row.GetType().GetProperty(name);
        Assert.NotNull(p);
        return (T)p!.GetValue(row)!;
    }

    private const string TwoChanges = "[{\"field\":\"Name\",\"old\":\"a\",\"new\":\"b\"}," +
                                      "{\"field\":\"Age\",\"old\":\"1\",\"new\":\"2\"}]";

    [Fact]
    public async Task Filters_by_entity_name()
    {
        using var db = TestHelper.CreateInMemoryContext();
        Seed(db, "Sys_User", "u1", 2, TwoChanges);
        Seed(db, "Sys_User", "u2", 2, TwoChanges);
        Seed(db, "Sys_Role", "r1", 2, TwoChanges);
        db.SaveChanges();

        var (total, _) = Unwrap(await new FieldAuditController(db)
            .GetList(entityName: "Sys_User", entityKey: null, userId: null, from: null, to: null));

        Assert.Equal(2, total);
    }

    [Fact]
    public async Task Filters_by_user_id()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var target = Guid.NewGuid();
        Seed(db, "Sys_User", "u1", 2, TwoChanges, userId: target);
        Seed(db, "Sys_User", "u2", 2, TwoChanges, userId: target);
        Seed(db, "Sys_User", "u3", 2, TwoChanges, userId: Guid.NewGuid());
        db.SaveChanges();

        var (total, _) = Unwrap(await new FieldAuditController(db)
            .GetList(entityName: null, entityKey: null, userId: target, from: null, to: null));

        Assert.Equal(2, total);
    }

    [Fact]
    public async Task Filters_by_date_range_inclusive_of_to_day()
    {
        using var db = TestHelper.CreateInMemoryContext();
        Seed(db, "Sys_User", "u1", 2, TwoChanges, at: new DateTime(2026, 6, 1, 10, 0, 0));
        Seed(db, "Sys_User", "u2", 2, TwoChanges, at: new DateTime(2026, 6, 15, 23, 0, 0)); // to 当日晚间须命中
        Seed(db, "Sys_User", "u3", 2, TwoChanges, at: new DateTime(2026, 6, 20, 10, 0, 0));
        db.SaveChanges();

        var (total, _) = Unwrap(await new FieldAuditController(db)
            .GetList(entityName: null, entityKey: null, userId: null,
                     from: new DateTime(2026, 6, 10), to: new DateTime(2026, 6, 15)));

        Assert.Equal(1, total);
    }

    [Fact]
    public async Task Paginates_and_returns_total()
    {
        using var db = TestHelper.CreateInMemoryContext();
        for (int i = 0; i < 5; i++)
            Seed(db, "Sys_User", $"u{i}", 2, TwoChanges, at: DateTime.Now.AddMinutes(-i));
        db.SaveChanges();

        var (total, rows) = Unwrap(await new FieldAuditController(db)
            .GetList(entityName: null, entityKey: null, userId: null, from: null, to: null,
                     page: 1, pageSize: 2));

        Assert.Equal(5, total);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task Page_zero_is_clamped_to_first_page_without_throwing()
    {
        using var db = TestHelper.CreateInMemoryContext();
        for (int i = 0; i < 3; i++)
            Seed(db, "Sys_User", $"u{i}", 2, TwoChanges, at: DateTime.Now.AddMinutes(-i));
        db.SaveChanges();

        var (total, rows) = Unwrap(await new FieldAuditController(db)
            .GetList(entityName: null, entityKey: null, userId: null, from: null, to: null,
                     page: 0, pageSize: 2));

        Assert.Equal(3, total);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task PageSize_over_max_is_clamped_to_200()
    {
        using var db = TestHelper.CreateInMemoryContext();
        for (int i = 0; i < 250; i++)
            Seed(db, "Sys_User", $"u{i}", 2, TwoChanges, at: DateTime.Now.AddSeconds(-i));
        db.SaveChanges();

        var (total, rows) = Unwrap(await new FieldAuditController(db)
            .GetList(entityName: null, entityKey: null, userId: null, from: null, to: null,
                     page: 1, pageSize: 500));

        Assert.Equal(250, total);
        Assert.Equal(200, rows.Count); // clamp 到 200
    }

    [Fact]
    public async Task List_row_exposes_changeCount_not_full_changes()
    {
        using var db = TestHelper.CreateInMemoryContext();
        Seed(db, "Sys_User", "u1", 2, TwoChanges);
        db.SaveChanges();

        var rows = UnwrapRows(await new FieldAuditController(db)
            .GetList(entityName: "Sys_User", entityKey: null, userId: null, from: null, to: null));

        var row = Assert.Single(rows);

        // changeCount 可反射读到，且 == 该行 Changes JSON 反序列化长度（2）
        var changeCount = Prop<int>(row, "changeCount");
        var expected = JsonSerializer.Deserialize<List<JsonElement>>(TwoChanges)!.Count;
        Assert.Equal(expected, changeCount);
        Assert.Equal(2, changeCount);

        // 列表行不暴露完整 changes 串（防大负载）
        Assert.Null(row.GetType().GetProperty("changes"));
        Assert.Null(row.GetType().GetProperty("Changes"));
    }

    [Fact]
    public async Task Record_returns_changes_ascending_by_changedAt_with_full_payload()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var oneChange = "[{\"field\":\"Name\",\"old\":\"a\",\"new\":\"b\"}]";
        // 乱序插入，校验输出按 ChangedAt 正序
        Seed(db, "Sys_User", "u1", 2, TwoChanges, at: new DateTime(2026, 6, 3, 10, 0, 0));
        Seed(db, "Sys_User", "u1", 1, oneChange, at: new DateTime(2026, 6, 1, 10, 0, 0));
        Seed(db, "Sys_User", "u1", 2, oneChange, at: new DateTime(2026, 6, 2, 10, 0, 0));
        Seed(db, "Sys_User", "other", 2, TwoChanges, at: new DateTime(2026, 6, 5, 10, 0, 0)); // 不同 key，不命中
        db.SaveChanges();

        var rows = UnwrapRows(await new FieldAuditController(db)
            .GetRecord(entityName: "Sys_User", entityKey: "u1"));

        Assert.Equal(3, rows.Count);

        // 正序：6/1 → 6/2 → 6/3
        var times = rows.Select(r => Prop<DateTime>(r, "ChangedAt")).ToList();
        Assert.Equal(new DateTime(2026, 6, 1, 10, 0, 0), times[0]);
        Assert.Equal(new DateTime(2026, 6, 2, 10, 0, 0), times[1]);
        Assert.Equal(new DateTime(2026, 6, 3, 10, 0, 0), times[2]);

        // record 端点返完整 changes 原 JSON 串
        Assert.Equal(oneChange, Prop<string>(rows[0], "Changes"));
        Assert.Equal(oneChange, Prop<string>(rows[1], "Changes"));
        Assert.Equal(TwoChanges, Prop<string>(rows[2], "Changes"));
    }
}
