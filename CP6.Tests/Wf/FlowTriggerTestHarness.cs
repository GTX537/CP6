// CP6.Tests/Wf/FlowTriggerTestHarness.cs —— 共享基座：GenerateCreateScript + TEXT 替换建库 +
// AFTER UPDATE 触发器模拟 rowversion（本波额外给 Wf_FlowTrigger 建同款触发器，B-T2 双 worker 抢占用）
using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

internal static class FlowTriggerTestHarness
{
    /// <summary>测试专用子类：声明两表带 rowversion 触发器（EF Core 8 SQLite 关 RETURNING 改 SELECT 读回，
    /// 令 [Timestamp] 并发令牌在 SQLite 基座真正生效——照 FlowConcurrencyTests 口径）。</summary>
    internal sealed class SqliteCP6Context : CP6Context
    {
        public SqliteCP6Context(DbContextOptions<CP6Context> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<Wf_FlowInstance>().ToTable(t => t.HasTrigger("trg_Wf_FlowInstance_RowVersion"));
            mb.Entity<Wf_FlowTrigger>().ToTable(t => t.HasTrigger("trg_Wf_FlowTrigger_RowVersion"));
        }
    }

    public static SqliteCP6Context Ctx(SqliteConnection c)
        => new(new DbContextOptionsBuilder<CP6Context>().UseSqlite(c).Options);

    public static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    public static FlowTriggerService Service(CP6Context db) => new(db, Engine(db));

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
        Exec(conn,
            "CREATE TRIGGER trg_Wf_FlowTrigger_RowVersion AFTER UPDATE ON \"Wf_FlowTrigger\" " +
            "BEGIN UPDATE \"Wf_FlowTrigger\" SET \"RowVersion\" = randomblob(8) WHERE \"Id\" = NEW.\"Id\"; END;");
        return conn;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>最小 schema：start → approval(指定人) → end（形状照 FlowConcurrencyTests.ForkSchema）。</summary>
    public static string MinimalSchemaJson(Guid approver) => JsonSerializer.Serialize(new FlowSchema
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges = { new FlowEdge { From = "s", To = "a" }, new FlowEdge { From = "a", To = "end" } },
    });

    /// <summary>seed：一个流程定义（默认 enabled，flowKey 默认 "fk-trig"）+ 发起人 + 审批人。</summary>
    public static async Task<(Guid StarterId, Guid ApproverId)> SeedFlowAndUsersAsync(
        SqliteConnection conn, string flowKey = "fk-trig", bool flowEnabled = true, bool starterEnabled = true)
    {
        var starter = Guid.NewGuid();
        var approver = Guid.NewGuid();
        using var db = Ctx(conn);
        db.Sys_Users.AddRange(
            new Sys_User { Id = starter, UserName = $"st{starter:N}", Password = "x", RoleId = 1, Enable = starterEnabled },
            new Sys_User { Id = approver, UserName = $"ap{approver:N}", Password = "x", RoleId = 1, Enable = true });
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = "f",
            SchemaJson = MinimalSchemaJson(approver), Version = 1, Enable = flowEnabled,
        });
        await db.SaveChangesAsync();
        return (starter, approver);
    }

    public static Wf_FlowTrigger NewTrigger(string flowKey, int type, Guid starterId,
        bool enabled = true, string configJson = "{}", string? eventKey = null)
        => new()
        {
            FlowKey = flowKey, TriggerType = type, StarterUserId = starterId,
            Enabled = enabled, ConfigJson = configJson, EventKey = eventKey,
        };
}
