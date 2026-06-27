using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CP6.Tests;

/// <summary>
/// WFS Phase A · Task 6 — 乐观并发 / 幂等（RowVersion 写触达 + ActAsync 重试×3）。
///
/// 风险场景：并行两分支近同时办结，到达 parallelJoin 时各自只看到自己那枚 token（脏读 join 计数=1），
/// 双双停泊、无人触发 → "双 1/2 丢失唤醒"死锁（实例永远卡 Running）。
/// 修复：在 <see cref="Wf_FlowInstance"/> 加 <c>[Timestamp] RowVersion</c>，办结末尾写触达 inst 行 →
/// UPDATE 带 <c>WHERE RowVersion=@orig</c> 序列化并行办结；败方抛 <see cref="DbUpdateConcurrencyException"/>，
/// <see cref="FlowEngine.ActAsync"/> 外壳重读全部追踪实体后重试，重算 join 计数即可正确放行。
///
/// 测试基座说明（诚实声明，非伪造）：
///  · InMemory provider 不支持并发令牌，永不抛 <see cref="DbUpdateConcurrencyException"/> → 无法验本任务。
///  · CP6Context 含 <c>nvarchar(max)</c> 列，SQLite <c>EnsureCreated</c> 撞 <c>near "max"</c>（A3/A4/A5 同记录）。
///    本测试改用 <c>GenerateCreateScript()</c> 生成 SQLite DDL → 将 <c>n?varchar(max)</c> 就地替换为 <c>TEXT</c>
///    → 执行建全表，绕开该限制（已验证可建库）。
///  · SQLite 无原生 rowversion，<c>[Timestamp]</c> 列默认不会逐次变更。故用一只 <c>AFTER UPDATE</c> 触发器
///    把 RowVersion 刷成 <c>randomblob(8)</c>，并在测试用子类上 <c>HasTrigger</c> 声明该表带触发器
///    （令 EF Core 8 SQLite 关闭 RETURNING、改用 SELECT 读回触发器写后的新值），使并发令牌在本基座真正生效。
///
/// 两个用例：①直接证明该基座下 RowVersion 令牌会抛真实 <see cref="DbUpdateConcurrencyException"/>（脏写败方）；
///          ②端到端：败方持陈旧 inst 办结 → ActAsync 重试吞冲突、重算 join、实例仍达 Approved 且零残留 Active token。
/// </summary>
public class FlowConcurrencyTests
{
    /// <summary>测试专用子类：声明 Wf_FlowInstance 带 RowVersion 触发器 → EF Core 8 SQLite 关闭 RETURNING，
    /// 改用 SELECT 读回"触发器写后"的 RowVersion，令 SQLite 上的 [Timestamp] 并发令牌真正可用。</summary>
    private sealed class SqliteCP6Context : CP6Context
    {
        public SqliteCP6Context(DbContextOptions<CP6Context> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);
            mb.Entity<Wf_FlowInstance>().ToTable(t => t.HasTrigger("trg_Wf_FlowInstance_RowVersion"));
        }
    }

    private static DbContextOptions<CP6Context> Opts(SqliteConnection c)
        => new DbContextOptionsBuilder<CP6Context>().UseSqlite(c).Options;
    private static SqliteCP6Context Ctx(SqliteConnection c) => new(Opts(c));
    private static FlowEngine Engine(CP6Context db) => new(db, new ApproverResolver(db));

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>建库：EnsureCreated 在 nvarchar(max) 上失败，故生成 SQLite DDL → 替换坏类型 → 执行；
    /// 再建 AFTER UPDATE 触发器逐次刷新 RowVersion。返回一只保持打开的共享 :memory: 连接。</summary>
    private static SqliteConnection NewSqliteWithSchema()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        using (var setup = Ctx(conn))
        {
            var script = Regex.Replace(setup.Database.GenerateCreateScript(),
                                       "n?varchar\\(max\\)", "TEXT", RegexOptions.IgnoreCase);
            Exec(conn, script);
        }
        // SQLite 无原生 rowversion：触发器把 RowVersion 刷成随机 8 字节（recursive_triggers 默认 OFF → 不自递归）
        Exec(conn,
            "CREATE TRIGGER trg_Wf_FlowInstance_RowVersion AFTER UPDATE ON \"Wf_FlowInstance\" " +
            "BEGIN UPDATE \"Wf_FlowInstance\" SET \"RowVersion\" = randomblob(8) WHERE \"Id\" = NEW.\"Id\"; END;");
        return conn;
    }

    // start → split → (a:approval, b:approval) → join → end（同 ParallelGatewayTests 的分叉形）
    private static FlowSchema ForkSchema(Guid ua, Guid ub) => new()
    {
        Start = "s",
        Nodes =
        {
            new FlowNode { Id = "s", Type = "start" },
            new FlowNode { Id = "split", Type = "parallelSplit" },
            new FlowNode { Id = "a", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ua },
            new FlowNode { Id = "b", Type = "approval", ApproverStrategy = "Specified", ApproverUserId = ub },
            new FlowNode { Id = "join", Type = "parallelJoin" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges =
        {
            new FlowEdge { From = "s", To = "split" },
            new FlowEdge { From = "split", To = "a" }, new FlowEdge { From = "split", To = "b" },
            new FlowEdge { From = "a", To = "join" }, new FlowEdge { From = "b", To = "join" },
            new FlowEdge { From = "join", To = "end" },
        },
    };

    private static async Task<(Guid ua, Guid ub)> SeedAndSubmitAsync(SqliteConnection conn)
    {
        var ua = Guid.NewGuid(); var ub = Guid.NewGuid();
        using var db = Ctx(conn);
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = "p", FlowName = "p", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(ForkSchema(ua, ub)), Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("p", Guid.NewGuid(), "{}");
        return (ua, ub);
    }

    /// <summary>证据用例：本 SQLite 基座下 RowVersion 令牌确会抛真实 DbUpdateConcurrencyException。
    /// 两上下文读同一 inst（同 RowVersion）；胜方先存（触发器刷新 RowVersion），败方再存即冲突。</summary>
    [Fact]
    public async Task RowVersion_token_fires_real_concurrency_conflict_on_stale_write()
    {
        using var conn = NewSqliteWithSchema();
        await SeedAndSubmitAsync(conn);

        using var a = Ctx(conn);
        using var b = Ctx(conn);
        var ia = await a.Wf_FlowInstances.FirstAsync();
        var ib = await b.Wf_FlowInstances.FirstAsync();   // 与 a 同一 RowVersion

        ia.ModifyDate = DateTime.Now;
        await a.SaveChangesAsync();                        // 胜方：触发器把 RowVersion 刷新

        ib.ModifyDate = DateTime.Now;                      // 败方仍持旧 RowVersion
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }

    /// <summary>端到端：两分支并行办结，败方在"脏读窗口"持陈旧 inst → ActAsync 重试吞下
    /// DbUpdateConcurrencyException、重读、重算 join 计数 → 实例仍达 Approved，无丢失唤醒死锁。
    /// （若重试外壳缺席，dbB.ActAsync 会把并发异常抛出，本用例即失败 → 证明确走了重试路径。）</summary>
    [Fact]
    public async Task Parallel_branches_with_stale_read_retry_resolves_to_Approved()
    {
        using var conn = NewSqliteWithSchema();
        var (ua, ub) = await SeedAndSubmitAsync(conn);

        Guid taId, tbId;
        using (var q = Ctx(conn))
        {
            taId = (await q.Wf_FlowTasks.FirstAsync(t => t.AssigneeId == ua && t.Status == FlowTaskStatus.Pending)).Id;
            tbId = (await q.Wf_FlowTasks.FirstAsync(t => t.AssigneeId == ub && t.Status == FlowTaskStatus.Pending)).Id;
        }

        // dbB 先把 inst 读进 identity-map → 锁住"办结前"的旧 RowVersion，
        // 等价于"在兄弟分支落库前已读过实例"= 真实近同时办结的脏读窗口。
        using var dbB = Ctx(conn);
        await dbB.Wf_FlowInstances.FirstAsync();

        // dbA 先办结 a 分支并落库 → 触发器把 inst 的 RowVersion 刷新（旧值作废）。
        using (var dbA = Ctx(conn))
            await Engine(dbA).ActAsync(taId, ua, approve: true);

        // dbB 拿陈旧 inst 办结 b 分支：ActOnceAsync 末尾写触达 inst 输掉 RowVersion 竞争 →
        // DbUpdateConcurrencyException → 重试重读 + 重算 join → Approved。
        await Engine(dbB).ActAsync(tbId, ub, approve: true);

        using var check = Ctx(conn);
        var inst = await check.Wf_FlowInstances.AsNoTracking().SingleAsync();
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);                       // 无丢失唤醒 → 最终通过
        Assert.False(await check.Wf_FlowTokens.AsNoTracking().AnyAsync(t => t.Status == FlowTokenStatus.Active));  // 零残留 Active token
    }

    // ─────────────── Fix4：会签"停泊路径"也写触达 inst → 串行化非终票，杜绝丢失唤醒 ───────────────

    // start? 直接 n1(approval "all"，Role 下 2 用户) → end。两会签人均须同意。
    private static FlowSchema AllSignSchema(int roleId) => new()
    {
        Start = "n1",
        Nodes =
        {
            new FlowNode { Id = "n1", Type = "approval", ApproverStrategy = "Role", ApproverRoleId = roleId, Countersign = "all" },
            new FlowNode { Id = "end", Type = "end" },
        },
        Edges = { new FlowEdge { From = "n1", To = "end" } },
    };

    private static async Task<(Guid u1, Guid u2)> SeedAndSubmitAllSignAsync(SqliteConnection conn, int roleId)
    {
        var u1 = Guid.NewGuid(); var u2 = Guid.NewGuid();
        using var db = Ctx(conn);
        db.Sys_Users.AddRange(
            new Sys_User { Id = u1, UserName = $"u{u1:N}", Password = "x", RoleId = roleId, Enable = true },
            new Sys_User { Id = u2, UserName = $"u{u2:N}", Password = "x", RoleId = roleId, Enable = true });
        db.Wf_FlowDefs.Add(new Wf_FlowDef
        {
            Id = Guid.NewGuid(), FlowKey = "cs", FlowName = "cs", FormKey = "f",
            SchemaJson = JsonSerializer.Serialize(AllSignSchema(roleId)), Version = 1, Enable = true,
        });
        await db.SaveChangesAsync();
        await Engine(db).SubmitAsync("cs", Guid.NewGuid(), "{}");
        return (u1, u2);
    }

    /// <summary>
    /// 会签"all"两审批人，两枚非终票近同时落库：败方持"对方仍 Pending"的陈旧视图入停泊路径。
    /// 修复后停泊路径同样写触达 inst.ModifyDate → 输掉 RowVersion 竞争 → DbUpdateConcurrencyException →
    /// ActAsync 重读全部追踪实体（对方现 Approved）、重算会签计数 → 全同意 → 推进 → Approved。
    /// （若写触达仍留在 !decided 早退之后，败方停泊路径不碰 inst → 无冲突 → 双双停泊 → 实例永卡 Running，本用例即失败。）
    /// </summary>
    [Fact]
    public async Task Countersign_parking_path_serialized_resolves_to_Approved()
    {
        using var conn = NewSqliteWithSchema();
        const int roleId = 4242;
        var (u1, u2) = await SeedAndSubmitAllSignAsync(conn, roleId);

        Guid t1Id, t2Id;
        using (var q = Ctx(conn))
        {
            t1Id = (await q.Wf_FlowTasks.FirstAsync(t => t.AssigneeId == u1 && t.Status == FlowTaskStatus.Pending)).Id;
            t2Id = (await q.Wf_FlowTasks.FirstAsync(t => t.AssigneeId == u2 && t.Status == FlowTaskStatus.Pending)).Id;
        }

        // dbB 先把 inst + 两条 Pending 任务读进 identity-map → 锁住"u1 办结前"的旧 RowVersion 与陈旧 Pending 视图，
        // 等价于"两会签人近同时各自读到对方仍 Pending"= 真实丢失唤醒脏读窗口。
        using var dbB = Ctx(conn);
        await dbB.Wf_FlowInstances.FirstAsync();
        await dbB.Wf_FlowTasks.Where(t => t.Status == FlowTaskStatus.Pending).ToListAsync();

        // dbA 先办结 u1（非终票 → 停泊，写触达 inst 行刷新 RowVersion）。
        using (var dbA = Ctx(conn))
            await Engine(dbA).ActAsync(t1Id, u1, approve: true);

        // dbB 持陈旧 inst + 陈旧"u1 Pending"视图办结 u2（亦非终票 → 走停泊路径）。
        await Engine(dbB).ActAsync(t2Id, u2, approve: true);

        using var check = Ctx(conn);
        var inst = await check.Wf_FlowInstances.AsNoTracking().SingleAsync();
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);   // 停泊路径被串行化 → 无丢失唤醒 → 最终通过
        Assert.False(await check.Wf_FlowTokens.AsNoTracking().AnyAsync(t => t.Status == FlowTokenStatus.Active));
    }
}
