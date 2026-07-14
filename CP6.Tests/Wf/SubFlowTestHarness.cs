// CP6.Tests/Wf/SubFlowTestHarness.cs —— 共享基座：GenerateCreateScript + TEXT 替换建库 +
// AFTER UPDATE 触发器模拟 rowversion（Wf_FlowInstance=恰一次恢复闸；Wf_ServiceJob=fast path/worker 抢 job）
using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Wf;

internal static class SubFlowTestHarness
{
    /// <summary>测试专用子类：两表声明 rowversion 触发器（EF Core 8 SQLite 关 RETURNING 改 SELECT 读回，
    /// 令 [Timestamp] 并发令牌在 SQLite 基座真正生效——照 FlowConcurrencyTests 口径）。</summary>
    internal sealed class SqliteCP6Context : CP6Context
    {
        public SqliteCP6Context(DbContextOptions<CP6Context> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<Wf_FlowInstance>().ToTable(t => t.HasTrigger("trg_Wf_FlowInstance_RowVersion"));
            mb.Entity<Wf_ServiceJob>().ToTable(t => t.HasTrigger("trg_Wf_ServiceJob_RowVersion"));
        }
    }

    public static SqliteCP6Context Ctx(SqliteConnection c)
        => new(new DbContextOptionsBuilder<CP6Context>().UseSqlite(c).Options);

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
        Exec(conn,
            "CREATE TRIGGER trg_Wf_ServiceJob_RowVersion AFTER UPDATE ON \"Wf_ServiceJob\" " +
            "BEGIN UPDATE \"Wf_ServiceJob\" SET \"RowVersion\" = randomblob(8) WHERE \"Id\" = NEW.\"Id\"; END;");
        return conn;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>子流程：cs → ca(指定审批人) → ce。</summary>
    public static FlowSchema ChildSchema(Guid approver) => new()
    {
        Start = "cs",
        Nodes =
        {
            new FlowNode { Id = "cs", Type = "start" },
            new FlowNode { Id = "ca", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = approver },
            new FlowNode { Id = "ce", Type = "end" },
        },
        Edges = { new FlowEdge { From = "cs", To = "ca" }, new FlowEdge { From = "ca", To = "ce" } },
    };

    /// <summary>秒批子流程：cs → ce（起即终态，测 fast path 即时收敛）。</summary>
    public static FlowSchema InstantChildSchema() => new()
    {
        Start = "cs",
        Nodes = { new FlowNode { Id = "cs", Type = "start" }, new FlowNode { Id = "ce", Type = "end" } },
        Edges = { new FlowEdge { From = "cs", To = "ce" } },
    };

    /// <summary>父流程：ps → sub(subFlow) → pa(父审批,证明恢复推进) → pe；errorEdge=true 时另挂 sub→err(IsError)→ee。</summary>
    public static FlowSchema ParentSchema(Guid parentApprover, string subFlowKey,
        string? collectionVar = null, string? policy = null, string? varsIn = null, string? varsOut = null,
        bool errorEdge = false, Guid? errApprover = null)
    {
        var s = new FlowSchema
        {
            Start = "ps",
            Nodes =
            {
                new FlowNode { Id = "ps", Type = "start" },
                new FlowNode { Id = "sub", Type = "subFlow", SubFlowKey = subFlowKey, SubCollectionVar = collectionVar,
                               SubCompletionPolicy = policy, SubVarsInJson = varsIn, SubVarsOutJson = varsOut },
                new FlowNode { Id = "pa", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = parentApprover },
                new FlowNode { Id = "pe", Type = "end" },
            },
            Edges =
            {
                new FlowEdge { From = "ps", To = "sub" },
                new FlowEdge { From = "sub", To = "pa" },
                new FlowEdge { From = "pa", To = "pe" },
            },
        };
        if (errorEdge)
        {
            s.Nodes.Add(new FlowNode { Id = "err", Type = "approval", ApproverStrategy = "Specified",
                                       ApproverUserId = errApprover ?? parentApprover });
            s.Nodes.Add(new FlowNode { Id = "ee", Type = "end" });
            s.Edges.Add(new FlowEdge { From = "sub", To = "err", IsError = true });
            s.Edges.Add(new FlowEdge { From = "err", To = "ee" });
        }
        return s;
    }

    public static void SeedDef(CP6Context db, string flowKey, FlowSchema schema, bool enable = true)
        => db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowKey, FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(schema), Version = 1, Enable = enable,
        });
}
