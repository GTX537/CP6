using CP6.Entity;
using CP6.Entity.DomainModels.Sys;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// C-4 数据权限真库集成测（SQLite in-memory，真实关系型 provider）。
/// 验证章03 "本部门及下级" 的 Path.StartsWith 子树过滤能翻译为真实 SQL（LIKE）并正确过滤——
/// InMemory provider 不校验 SQL 翻译，故用 SQLite 兜底这条风险。
/// </summary>
public class DataScopeSqlIntegrationTests
{
    /// <summary>仅含 Sys_Dept + 一个 IDataScoped 测试实体的最小上下文（避免全 CP6 模型在 SQLite 的方言差异）。</summary>
    private sealed class ScopeSqlContext : DbContext
    {
        public ScopeSqlContext(DbContextOptions<ScopeSqlContext> o) : base(o) { }
        public DbSet<Sys_Dept> Sys_Depts => Set<Sys_Dept>();
        public DbSet<ScopedDoc> Docs => Set<ScopedDoc>();
    }

    private sealed class ScopedDoc : IDataScoped
    {
        public int Id { get; set; }
        public string? Creator { get; set; }
        public Guid? DeptId { get; set; }
    }

    [Fact]
    public void Subtree_PathStartsWith_TranslatesToSql_AndFilters()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<ScopeSqlContext>().UseSqlite(conn).Options;
        using var ctx = new ScopeSqlContext(options);
        ctx.Database.EnsureCreated();

        var d1 = Guid.NewGuid();   // 根 A
        var d2 = Guid.NewGuid();   // A 的子 A1
        var outside = Guid.NewGuid();
        ctx.Sys_Depts.AddRange(
            new Sys_Dept { Id = d1, DeptCode = "A", DeptName = "A", Path = $"/{d1}/" },
            new Sys_Dept { Id = d2, DeptCode = "A1", DeptName = "A1", Path = $"/{d1}/{d2}/" },
            new Sys_Dept { Id = outside, DeptCode = "B", DeptName = "B", Path = $"/{outside}/" });
        ctx.Docs.AddRange(
            new ScopedDoc { DeptId = d2 },        // A 子树内
            new ScopedDoc { DeptId = outside });  // A 子树外
        ctx.SaveChanges();

        // 复刻 DataScopeFilter case 3：记录部门 Path 以 ctx.DeptPath 为前缀（真实 SQL：EXISTS + LIKE）
        var path = $"/{d1}/";
        var query = ctx.Docs.Where(x => x.DeptId != null &&
            ctx.Sys_Depts.Any(d => d.Id == x.DeptId && d.Path.StartsWith(path)));

        // 确认确实下推为 SQL（含 LIKE），而非客户端求值
        var sql = query.ToQueryString();
        Assert.Contains("LIKE", sql, StringComparison.OrdinalIgnoreCase);

        var result = query.ToList();
        Assert.Single(result);             // 只 d2（在 A 子树内）
        Assert.Equal(d2, result[0].DeptId);
    }
}
