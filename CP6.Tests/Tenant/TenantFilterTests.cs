using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Tenant;

/// <summary>
/// 章10 §4 多租户全局过滤 + 写入盖章（C-3）。以 Wf_ 实体（已继承 BaseTenantEntity）验证：
/// 注入租户 A 查询只见 A 的行 / 新增自动盖当前租户 / 跨租户互不可见 / 显式 TenantId 不被覆盖。
/// InMemory 验证盖章 + 过滤逻辑（HasQueryFilter 客户端求值）；真库 SQL 行为同语义。
/// </summary>
public class TenantFilterTests
{
    private static CP6Context DbFor(string dbName, Guid tenant) => new(
        new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options,
        new TenantContext { CurrentTenantId = tenant });

    private static Wf_FlowDef NewDef(string key) => new()
    {
        Id = Guid.NewGuid(), FlowKey = key, FlowName = key, FormKey = "f",
        SchemaJson = "{}", Version = 1, Enable = true,
    };

    [Fact]
    public async Task Insert_StampsCurrentTenant_AndQueryIsScoped()
    {
        var db = Guid.NewGuid().ToString();
        var tA = Guid.NewGuid();
        var tB = Guid.NewGuid();

        using (var ctx = DbFor(db, tA)) { ctx.Wf_FlowDefs.Add(NewDef("a-flow")); await ctx.SaveChangesAsync(); }

        // 租户 A 视角：看得到，且 TenantId 被自动盖为 A
        using (var ctx = DbFor(db, tA))
        {
            var defs = await ctx.Wf_FlowDefs.ToListAsync();
            Assert.Single(defs);
            Assert.Equal(tA, defs[0].TenantId);
        }
        // 租户 B 视角：A 的数据完全不可见（硬墙）
        using (var ctx = DbFor(db, tB))
        {
            Assert.Empty(await ctx.Wf_FlowDefs.ToListAsync());
        }
    }

    [Fact]
    public async Task TwoTenants_SameDb_IsolatedBothWays()
    {
        var db = Guid.NewGuid().ToString();
        var tA = Guid.NewGuid();
        var tB = Guid.NewGuid();

        using (var ctx = DbFor(db, tA)) { ctx.Wf_FlowDefs.Add(NewDef("flow-A")); await ctx.SaveChangesAsync(); }
        using (var ctx = DbFor(db, tB)) { ctx.Wf_FlowDefs.Add(NewDef("flow-B")); await ctx.SaveChangesAsync(); }

        using (var ctx = DbFor(db, tA))
        {
            var defs = await ctx.Wf_FlowDefs.ToListAsync();
            Assert.Single(defs);
            Assert.Equal("flow-A", defs[0].FlowKey);
        }
        using (var ctx = DbFor(db, tB))
        {
            var defs = await ctx.Wf_FlowDefs.ToListAsync();
            Assert.Single(defs);
            Assert.Equal("flow-B", defs[0].FlowKey);
        }
    }

    [Fact]
    public async Task ExplicitTenantId_NotOverwritten()
    {
        var db = Guid.NewGuid().ToString();
        var tA = Guid.NewGuid();
        var tOther = Guid.NewGuid();

        using (var ctx = DbFor(db, tA))
        {
            var def = NewDef("preset");
            def.TenantId = tOther;   // 显式指定 → 盖章不覆盖
            ctx.Wf_FlowDefs.Add(def);
            await ctx.SaveChangesAsync();
        }
        // 当前租户 tA 看不到（它属于 tOther）
        using (var ctx = DbFor(db, tA)) Assert.Empty(await ctx.Wf_FlowDefs.ToListAsync());
        using (var ctx = DbFor(db, tOther)) Assert.Single(await ctx.Wf_FlowDefs.ToListAsync());
    }

    [Fact]
    public async Task DefaultTenant_WhenNoContextInjected()
    {
        var db = Guid.NewGuid().ToString();
        // 单参构造（无注入）→ 默认租户；与显式默认租户上下文互通
        using (var ctx = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
                   .UseInMemoryDatabase(db).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options))
        {
            ctx.Wf_FlowDefs.Add(NewDef("def-flow"));
            await ctx.SaveChangesAsync();
        }
        using (var ctx = DbFor(db, TenantContext.DefaultTenant))
        {
            var defs = await ctx.Wf_FlowDefs.ToListAsync();
            Assert.Single(defs);
            Assert.Equal(TenantContext.DefaultTenant, defs[0].TenantId);
        }
    }
}
