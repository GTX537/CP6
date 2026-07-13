using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Space;
using CP6.Tests.Infra;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

/// <summary>
/// Space 真库（SQL Server）集成测试（ch04 D-9）——由 <c>CP6_TEST_SQLSERVER</c> 环境变量门控。
///
/// SQLite 无法覆盖以下三条真库语义，故这里改用真实 SQL Server（缺环境变量则 Skip，CI 恒绿）：
///   · 过滤唯一索引：Space_Location 的 (TenantId, LocationCode) UNIQUE WHERE [LocationCode] IS NOT NULL
///     ——非空码租户内唯一，草稿期多行 NULL 码不互撞（CP6Context OnModelCreating HasFilter）。
///   · 两阶段换码：草稿→发布重排时经 NULL 中转规避唯一冲突（ch00 §4.6 / ch03 §7）。
///   · RowVersion 乐观锁：SQL Server 原生 rowversion，并发第二写抛 DbUpdateConcurrencyException。
///
/// 每个测试实例建一个唯一名临时库（CP6Test_{Guid:N}），EnsureCreated 建 schema，
/// Dispose 时 EnsureDeleted 清理（try/catch 兜底，不在真库留垃圾）。
/// 租户由 CP6Context 默认盖章（TenantContext.DefaultTenant），故两行同码即同租户冲突。
/// </summary>
public sealed class SpaceSqlIntegrationTests : IDisposable
{
    private readonly string? _connString;

    public SpaceSqlIntegrationTests()
    {
        var baseConn = Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvVar);
        if (string.IsNullOrEmpty(baseConn))
            return;   // 无环境变量：测试全被 [SqlServerFact] Skip，构造函数无需建库

        // 从传入连接串派生唯一名临时库（Database 段被覆盖）
        _connString = new SqlConnectionStringBuilder(baseConn)
        {
            InitialCatalog = $"CP6Test_{Guid.NewGuid():N}"
        }.ConnectionString;

        using var ctx = NewContext();
        ctx.Database.EnsureCreated();   // 建全模型 schema（含 Space_Location 过滤唯一索引 + rowversion）
    }

    private CP6Context NewContext()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseSqlServer(_connString!)
            .Options;
        return new CP6Context(options);
    }

    public void Dispose()
    {
        if (_connString == null) return;
        try
        {
            using var ctx = NewContext();
            ctx.Database.EnsureDeleted();   // 删临时库
        }
        catch
        {
            // 兜底：清理失败不掩盖测试结果（临时库名含 Guid，不复用）
        }
    }

    // ── D-9.1: 过滤唯一索引 ──────────────────────────────────────────────

    /// <summary>同租户内两行相同非空 LocationCode → 第二次写入触发唯一索引冲突（DbUpdateException）。</summary>
    [SqlServerFact]
    public void UniqueIndex_SameNonNullCode_SecondInsertThrows()
    {
        using (var ctx = NewContext())
        {
            ctx.Space_Locations.Add(new Space_Location { LocationCode = "DUP-001", Status = 1 });
            ctx.SaveChanges();
        }

        using var ctx2 = NewContext();
        ctx2.Space_Locations.Add(new Space_Location { LocationCode = "DUP-001", Status = 1 });

        var ex = Assert.Throws<DbUpdateException>(() => ctx2.SaveChanges());
        // 内层为 SqlException 2601/2627（唯一键/唯一索引冲突）
        Assert.IsType<SqlException>(ex.GetBaseException());
    }

    /// <summary>过滤索引 HasFilter([LocationCode] IS NOT NULL)：多行 NULL 码不互撞，可共存。</summary>
    [SqlServerFact]
    public void UniqueIndex_TwoNullCodes_BothCoexist()
    {
        using (var ctx = NewContext())
        {
            ctx.Space_Locations.Add(new Space_Location { LocationCode = null, Status = 0 });
            ctx.Space_Locations.Add(new Space_Location { LocationCode = null, Status = 0 });
            ctx.SaveChanges();   // 不应抛：NULL 被过滤索引排除
        }

        using var ctx2 = NewContext();
        Assert.Equal(2, ctx2.Space_Locations.Count(x => x.LocationCode == null));
    }

    // ── D-9.2: 两阶段重排 ────────────────────────────────────────────────

    /// <summary>两阶段换码：直接互换会撞唯一索引，经 NULL 中转（腾空→占用→回填）可成功交换两码。</summary>
    [SqlServerFact]
    public void TwoPhaseReorder_SwapCodes_NullIntermediate_Succeeds()
    {
        Guid id1, id2;
        using (var ctx = NewContext())
        {
            var l1 = new Space_Location { LocationCode = "S-A", Status = 1 };
            var l2 = new Space_Location { LocationCode = "S-B", Status = 1 };
            ctx.Space_Locations.AddRange(l1, l2);
            ctx.SaveChanges();
            id1 = l1.Id;
            id2 = l2.Id;
        }

        // 目标：l1 ← "S-B"，l2 ← "S-A"。经 NULL 中转规避 "S-A"/"S-B" 唯一冲突。
        using (var ctx = NewContext())
        {
            var l1 = ctx.Space_Locations.Single(x => x.Id == id1);
            l1.LocationCode = null;          // 阶段一：腾空 "S-A"
            ctx.SaveChanges();

            var l2 = ctx.Space_Locations.Single(x => x.Id == id2);
            l2.LocationCode = "S-A";         // 阶段二：l2 占用刚腾空的 "S-A"（原 "S-B" 释放）
            ctx.SaveChanges();

            l1.LocationCode = "S-B";         // 阶段三：l1 回填 "S-B"
            ctx.SaveChanges();
        }

        using var verify = NewContext();
        Assert.Equal("S-B", verify.Space_Locations.Single(x => x.Id == id1).LocationCode);
        Assert.Equal("S-A", verify.Space_Locations.Single(x => x.Id == id2).LocationCode);
    }

    // ── D-9.3: RowVersion 并发测试 ────────────────────────────────────────

    /// <summary>两上下文并发改同一行：先写者提交后 rowversion 改变，后写者 WHERE RowVersion 命中 0 行 → DbUpdateConcurrencyException。</summary>
    [SqlServerFact]
    public void RowVersion_ConcurrentUpdate_SecondThrows()
    {
        Guid id;
        using (var seed = NewContext())
        {
            var loc = new Space_Location { LocationCode = "RV-001", Status = 1 };
            seed.Space_Locations.Add(loc);
            seed.SaveChanges();
            id = loc.Id;
        }

        // 两个独立上下文各自加载同一行（各持相同的初始 RowVersion 快照）
        using var ctxA = NewContext();
        using var ctxB = NewContext();
        var a = ctxA.Space_Locations.Single(x => x.Id == id);
        var b = ctxB.Space_Locations.Single(x => x.Id == id);

        a.Status = 2;
        ctxA.SaveChanges();   // 先写者成功，DB rowversion 递增

        b.Status = 0;
        // 后写者 UPDATE ... WHERE Id=@id AND RowVersion=@stale → 影响 0 行
        Assert.Throws<DbUpdateConcurrencyException>(() => ctxB.SaveChanges());
    }
}
