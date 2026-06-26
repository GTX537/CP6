using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Controllers.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Platform;

/// <summary>
/// 块④ R10 CrossTenantAuditController 端到端（直构 new CrossTenantAuditController(db) 绕过
/// [RequirePlatformAdmin] 过滤器）。验带外 IgnoreQueryFilters 跨租户可见 + tenantCode/eventType/时间段
/// 过滤 + 分页 clamp。另含种子幂等纯逻辑测（默认租户行 + 首个平台超管，跑两次结果不变）。
/// </summary>
public class CrossTenantAuditControllerTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000A1"); // 默认租户
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-0000000000B2");
    private static readonly Guid TenantC = Guid.Parse("00000000-0000-0000-0000-0000000000C3");

    /// <summary>当前上下文固定在默认租户（证明带外查询不受当前租户限制）。</summary>
    private static CP6Context MakeDb()
    {
        var tenant = new TenantContext { CurrentTenantId = TenantContext.DefaultTenant };
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CP6Context(options, tenant);
    }

    /// <summary>显式置 TenantId（非 Guid.Empty → 盖章逻辑跳过，保留指定值）以铺跨租户行。</summary>
    private static Sys_SecurityLog Log(Guid tenantId, string? reqCode, int eventType, DateTime createdAt,
        string? userName = "u", string? ip = "1.1.1.1")
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestTenantCode = reqCode,
            EventType = eventType,
            CreatedAt = createdAt,
            UserName = userName,
            ClientIp = ip,
        };

    /// <summary>反射取 Ok 匿名对象的 rows 列表与 total。</summary>
    private static (List<object> rows, int total) Unpack(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var val = ok.Value!;
        var rowsObj = val.GetType().GetProperty("rows")!.GetValue(val)!;
        var rows = ((System.Collections.IEnumerable)rowsObj).Cast<object>().ToList();
        var total = (int)val.GetType().GetProperty("total")!.GetValue(val)!;
        return (rows, total);
    }

    private static string? RowTenantCode(object row)
        => (string?)row.GetType().GetProperty("RequestTenantCode")!.GetValue(row);

    private static int RowEventType(object row)
        => (int)row.GetType().GetProperty("EventType")!.GetValue(row)!;

    private static void Seed(CP6Context db)
    {
        db.Sys_SecurityLogs.AddRange(
            // TenantA 行（目标码 AAA / BBB），多事件多日期
            Log(TenantA, "AAA", 1, new DateTime(2026, 1, 10)),
            Log(TenantA, "AAA", 2, new DateTime(2026, 2, 10)),
            Log(TenantA, "BBB", 1, new DateTime(2026, 3, 10)),
            // TenantB 行（不同 TenantId → 当前默认租户行级过滤本应隐藏，带外才可见）
            Log(TenantB, "BBB", 2, new DateTime(2026, 1, 20)),
            Log(TenantB, "CCC", 1, new DateTime(2026, 2, 20)),
            // TenantC 行
            Log(TenantC, "CCC", 3, new DateTime(2026, 3, 20))
        );
        db.SaveChanges();
    }

    [Fact]
    public async Task CrossTenant_AllTenantsVisible_ProvesIgnoreQueryFilters()
    {
        var db = MakeDb();
        Seed(db);
        var ctrl = new CrossTenantAuditController(db);

        // 当前上下文为默认租户(TenantA)；若行级过滤生效只见 TenantA 的 3 行。
        // 带外 IgnoreQueryFilters 应见全部 6 行（含 TenantB/TenantC）。
        var (rows, total) = Unpack(await ctrl.GetList(null, null, null, null, page: 1, pageSize: 200));
        Assert.Equal(6, total);
        Assert.Equal(6, rows.Count);
    }

    [Fact]
    public async Task Filter_ByTenantCode_ReturnsOnlyMatching()
    {
        var db = MakeDb();
        Seed(db);
        var ctrl = new CrossTenantAuditController(db);

        // tenantCode=BBB 对齐 RequestTenantCode：TenantA/BBB 1 行 + TenantB/BBB 1 行 = 2 行（跨租户）。
        var (rows, total) = Unpack(await ctrl.GetList("BBB", null, null, null, 1, 200));
        Assert.Equal(2, total);
        Assert.All(rows, r => Assert.Equal("BBB", RowTenantCode(r)));
    }

    [Fact]
    public async Task Filter_ByEventType_Works()
    {
        var db = MakeDb();
        Seed(db);
        var ctrl = new CrossTenantAuditController(db);

        // eventType=1：TenantA/AAA + TenantA/BBB + TenantB/CCC = 3 行。
        var (rows, total) = Unpack(await ctrl.GetList(null, 1, null, null, 1, 200));
        Assert.Equal(3, total);
        Assert.All(rows, r => Assert.Equal(1, RowEventType(r)));
    }

    [Fact]
    public async Task Filter_ByDateRange_ToIsRightOpenPlusOneDay()
    {
        var db = MakeDb();
        Seed(db);
        var ctrl = new CrossTenantAuditController(db);

        // [2026-02-01, 2026-02-28]：仅 2 月的两行（2026-02-10, 2026-02-20）。
        // to=02-28 应含当日 → 实现为 < 03-01；本数据 2 月最大为 02-20，仍验右开语义正确。
        var (rows, total) = Unpack(await ctrl.GetList(
            null, null, new DateTime(2026, 2, 1), new DateTime(2026, 2, 28), 1, 200));
        Assert.Equal(2, total);
    }

    [Fact]
    public async Task DateRange_To_IncludesSameDayEvents()
    {
        var db = MakeDb();
        Seed(db);
        // 额外铺一行落在 to 当日（00:00 之后任意时刻），验 to 含当日（右开到次日零点）。
        db.Sys_SecurityLogs.Add(Log(TenantA, "AAA", 1, new DateTime(2026, 2, 28, 23, 59, 0)));
        db.SaveChanges();
        var ctrl = new CrossTenantAuditController(db);

        // to=02-28 须包含 02-28 23:59 这条 → [02-01,02-28] 共 2(原) + 1(同日) = 3 行。
        var (_, total) = Unpack(await ctrl.GetList(
            null, null, new DateTime(2026, 2, 1), new DateTime(2026, 2, 28), 1, 200));
        Assert.Equal(3, total);
    }

    [Fact]
    public async Task Pagination_Clamp_Page0To1_PageSize500To200()
    {
        var db = MakeDb();
        // 铺 250 行（超 200 上限），验 pageSize 被 clamp 到 200、page<=0 提为 1。
        for (int i = 0; i < 250; i++)
            db.Sys_SecurityLogs.Add(Log(TenantA, "AAA", 1, new DateTime(2026, 1, 1).AddMinutes(i)));
        db.SaveChanges();
        var ctrl = new CrossTenantAuditController(db);

        var (rows, total) = Unpack(await ctrl.GetList(null, null, null, null, page: 0, pageSize: 500));
        Assert.Equal(250, total);          // total 不受分页影响
        Assert.Equal(200, rows.Count);     // pageSize clamp 500→200；page 0→1 未抛负 Skip 异常
    }

    [Fact]
    public async Task Pagination_SecondPage_ReturnsRemainder()
    {
        var db = MakeDb();
        for (int i = 0; i < 250; i++)
            db.Sys_SecurityLogs.Add(Log(TenantA, "AAA", 1, new DateTime(2026, 1, 1).AddMinutes(i)));
        db.SaveChanges();
        var ctrl = new CrossTenantAuditController(db);

        var (rows, total) = Unpack(await ctrl.GetList(null, null, null, null, page: 2, pageSize: 200));
        Assert.Equal(250, total);
        Assert.Equal(50, rows.Count);      // 第二页余 50 行
    }

    // ── 种子幂等纯逻辑测（复刻 Program.cs T8 种子两段，跑两次断言不变）────────────────
    // Program.cs 的种子非可调用方法，此处用同语义的内联逻辑对 InMemory 验幂等护栏正确。

    private static void RunSeedLogic(CP6Context db)
    {
        // 段1：默认租户行（幂等 by Id）。
        if (!db.Sys_Tenants.Any(t => t.Id == TenantContext.DefaultTenant))
        {
            db.Sys_Tenants.Add(new Sys_Tenant
            {
                Id = TenantContext.DefaultTenant,
                TenantCode = "DEFAULT",
                TenantName = "默认租户",
                Enable = true
            });
            db.SaveChanges();
        }
        // 段2：引导首个平台超管（默认租户 admin → IsPlatformAdmin=true）。
        var seedAdmin = db.Sys_Users.IgnoreQueryFilters()
            .FirstOrDefault(u => u.UserName == "admin" && u.TenantId == TenantContext.DefaultTenant);
        if (seedAdmin != null && !seedAdmin.IsPlatformAdmin)
        {
            seedAdmin.IsPlatformAdmin = true;
            db.SaveChanges();
        }
    }

    [Fact]
    public void Seed_DefaultTenantAndPlatformAdmin_Idempotent()
    {
        var db = MakeDb();
        // 预置默认租户的 admin（未提权）。
        db.Sys_Users.Add(new Sys_User
        {
            Id = Guid.NewGuid(),
            TenantId = TenantContext.DefaultTenant,
            UserName = "admin",
            Password = "x",
            Enable = true,
            IsPlatformAdmin = false
        });
        db.SaveChanges();

        RunSeedLogic(db);
        RunSeedLogic(db);   // 跑两次验幂等

        Assert.Equal(1, db.Sys_Tenants.IgnoreQueryFilters().Count(t => t.Id == TenantContext.DefaultTenant));
        var admin = db.Sys_Users.IgnoreQueryFilters()
            .Single(u => u.UserName == "admin" && u.TenantId == TenantContext.DefaultTenant);
        Assert.True(admin.IsPlatformAdmin);
    }
}
