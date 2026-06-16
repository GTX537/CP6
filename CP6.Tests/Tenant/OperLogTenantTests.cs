using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Tenant;

/// <summary>
/// 章10 §3 审计日志（Sys_OperLog）租户隔离。该表 int Id 非 BaseTenantEntity，TenantId 手加：
/// 验证 SaveChanges 盖章 / 查询按当前租户过滤 / IgnoreQueryFilters 可跨租户（清理用）。
/// </summary>
public class OperLogTenantTests
{
    private static CP6Context DbFor(string dbName, Guid tenant) => new(
        new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options,
        new TenantContext { CurrentTenantId = tenant });

    private static Sys_OperLog NewLog(string url) => new()
    {
        UserName = "u", HttpMethod = "POST", RequestUrl = url, StatusCode = 200, CreateDate = DateTime.Now,
    };

    [Fact]
    public async Task OperLog_StampsCurrentTenant_AndQueryIsScoped()
    {
        var db = Guid.NewGuid().ToString();
        var tA = Guid.NewGuid();
        var tB = Guid.NewGuid();

        using (var ctx = DbFor(db, tA)) { ctx.Sys_OperLogs.Add(NewLog("/a")); await ctx.SaveChangesAsync(); }

        using (var ctx = DbFor(db, tA))
        {
            var logs = await ctx.Sys_OperLogs.ToListAsync();
            Assert.Single(logs);
            Assert.Equal(tA, logs[0].TenantId);   // 盖章为当前租户
        }
        using (var ctx = DbFor(db, tB))
        {
            Assert.Empty(await ctx.Sys_OperLogs.ToListAsync());   // B 看不到 A 的审计日志
        }
    }

    [Fact]
    public async Task OperLog_IgnoreQueryFilters_SeesAllTenants()
    {
        var db = Guid.NewGuid().ToString();
        var tA = Guid.NewGuid();
        var tB = Guid.NewGuid();

        using (var ctx = DbFor(db, tA)) { ctx.Sys_OperLogs.Add(NewLog("/a")); await ctx.SaveChangesAsync(); }
        using (var ctx = DbFor(db, tB)) { ctx.Sys_OperLogs.Add(NewLog("/b")); await ctx.SaveChangesAsync(); }

        // 保留期清理走 IgnoreQueryFilters → 跨租户全见（按时间删，不受当前租户限制）
        using (var ctx = DbFor(db, tA))
        {
            var all = await ctx.Sys_OperLogs.IgnoreQueryFilters().ToListAsync();
            Assert.Equal(2, all.Count);
        }
    }
}
