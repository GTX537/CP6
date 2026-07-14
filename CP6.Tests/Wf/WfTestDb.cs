using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// Wf 引擎测试基座（B-T1 抽出，与 <see cref="WfsInfraTestHarness"/> 并存，职责=Wf 引擎实例基座）。
/// 复用 <c>FlowConcurrencyTests</c> 的 SQLite 建库口径：<c>GenerateCreateScript()</c> +
/// <c>n?varchar(max)→TEXT</c> 替换 + <c>Wf_FlowInstance</c> 的 <c>[Timestamp] RowVersion</c> AFTER UPDATE 触发器
/// （EF Core 8 SQLite 需 <c>HasTrigger</c> 声明改用 SELECT 读回，令 RowVersion 并发令牌在此基座真正生效）。
/// </summary>
internal static class WfTestDb
{
    /// <summary>测试专用子类：声明 Wf_FlowInstance 带 RowVersion 触发器。</summary>
    internal sealed class SqliteCP6Context : CP6Context
    {
        public SqliteCP6Context(DbContextOptions<CP6Context> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<Wf_FlowInstance>().ToTable(t => t.HasTrigger("trg_Wf_FlowInstance_RowVersion"));
        }
    }

    public static SqliteCP6Context Ctx(SqliteConnection c)
        => new(new DbContextOptionsBuilder<CP6Context>().UseSqlite(c).Options);

    /// <summary>无租户感知的纯引擎（内部 ApproverResolver）。同 FlowConcurrencyTests.Engine。</summary>
    public static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    public static SqliteConnection NewSqliteWithSchema()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var setup = Ctx(conn))
        {
            var script = Regex.Replace(setup.Database.GenerateCreateScript(),
                                       "n?varchar\\(max\\)", "TEXT", RegexOptions.IgnoreCase);
            Exec(conn, script);
        }
        Exec(conn,
            "CREATE TRIGGER trg_Wf_FlowInstance_RowVersion AFTER UPDATE ON \"Wf_FlowInstance\" " +
            "BEGIN UPDATE \"Wf_FlowInstance\" SET \"RowVersion\" = randomblob(8) WHERE \"Id\" = NEW.\"Id\"; END;");
        return conn;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
