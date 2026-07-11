using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// F1 波G G.1：存量租户 COA 缺行逐租户回填（<see cref="FinCoaBackfillSeed"/>）。
/// 断言：存量租户缺 GRNI/本年利润 → 回填后有，且属性与模板一致；已全者零变动；只对已导入 scheme 的租户回填。
/// </summary>
public class FinCoaBackfillSeedTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static void AddTenant(CP6Context db, Guid tid, string code)
    {
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tid, TenantCode = code, TenantName = code, Enable = true });
        db.SaveChanges();
    }

    /// <summary>用模板为某租户建 COA（显式 TenantId），可排除若干 Code 以模拟旧模板缺行。</summary>
    private static void SeedTenantCoa(CP6Context db, Guid tid, string scheme, params string[] excludeCodes)
    {
        var rows = FinCoaTemplate.Get(scheme);
        var byCode = rows.ToDictionary(r => r.Code, _ => Guid.NewGuid());
        var excl = excludeCodes.ToHashSet();
        foreach (var r in rows)
        {
            if (excl.Contains(r.Code)) continue;
            db.GlAccounts.Add(new GlAccount
            {
                Id = byCode[r.Code],
                TenantId = tid,
                Code = r.Code,
                Name = r.Name,
                Type = r.Type,
                NormalSide = r.NormalSide,
                Level = r.Level,
                IsLeaf = r.IsLeaf,
                IsControl = r.IsControl,
                SubLedgerType = r.SubLedgerType,
                RequirePartner = r.RequirePartner,
                Role = r.Role,
                StandardScheme = scheme,
                IsActive = true,
                ParentId = r.ParentCode != null && byCode.TryGetValue(r.ParentCode, out var pid) ? pid : null,
            });
        }
        db.SaveChanges();
    }

    private static GlAccount? Acct(CP6Context db, Guid tid, string scheme, string code) =>
        db.GlAccounts.IgnoreQueryFilters()
            .FirstOrDefault(a => a.TenantId == tid && a.StandardScheme == scheme && a.Code == code);

    [Fact]
    public void Backfill_CnTenantMissingGrni_InsertsGrniMatchingTemplate()
    {
        using var db = NewDb();
        var tid = Guid.NewGuid();
        AddTenant(db, tid, "T1");
        SeedTenantCoa(db, tid, FinCoaTemplate.CnGaap, excludeCodes: "2204");

        Assert.Null(Acct(db, tid, FinCoaTemplate.CnGaap, "2204"));   // 前置：缺行

        FinCoaBackfillSeed.EnsureSeeded(db);

        var grni = Acct(db, tid, FinCoaTemplate.CnGaap, "2204");
        Assert.NotNull(grni);
        // 金额科目属性与模板一致
        var tmpl = FinCoaTemplate.CnGaapRows.Single(r => r.Code == "2204");
        Assert.Equal(tmpl.Type, grni!.Type);
        Assert.Equal(tmpl.NormalSide, grni.NormalSide);
        Assert.Equal("GRNI", grni.Role);
        Assert.False(grni.RequirePartner);
        Assert.True(grni.IsActive);
        // ParentCode 2000 → ParentId 解析到同租户既有 2000
        var parent = Acct(db, tid, FinCoaTemplate.CnGaap, "2000");
        Assert.Equal(parent!.Id, grni.ParentId);
    }

    [Fact]
    public void Backfill_IntlTenantMissingRows_InsertsAllFiveIncludingCurrentYearEarnings()
    {
        using var db = NewDb();
        var tid = Guid.NewGuid();
        AddTenant(db, tid, "T1");
        SeedTenantCoa(db, tid, FinCoaTemplate.Intl, "2110", "1900", "4800", "6800", "3103");

        FinCoaBackfillSeed.EnsureSeeded(db);

        // 波A 四行 + 波G 本年利润 3103 全部回填
        Assert.Equal("GRNI", Acct(db, tid, FinCoaTemplate.Intl, "2110")!.Role);
        Assert.Equal("PENDING_PROPERTY_LOSS", Acct(db, tid, FinCoaTemplate.Intl, "1900")!.Role);
        Assert.Equal("NON_OP_INCOME", Acct(db, tid, FinCoaTemplate.Intl, "4800")!.Role);
        Assert.Equal("NON_OP_EXPENSE", Acct(db, tid, FinCoaTemplate.Intl, "6800")!.Role);

        var cye = Acct(db, tid, FinCoaTemplate.Intl, "3103");
        Assert.NotNull(cye);
        Assert.Equal(AccountType.Equity, cye!.Type);
        Assert.Equal(AccountSide.Credit, cye.NormalSide);
        Assert.Null(cye.Role);   // 本年利润无 Role（按编码 3103 定位）
    }

    [Fact]
    public void Backfill_TenantAlreadyComplete_NoChange()
    {
        using var db = NewDb();
        var tid = Guid.NewGuid();
        AddTenant(db, tid, "T1");
        SeedTenantCoa(db, tid, FinCoaTemplate.CnGaap);   // 全量含 2204

        var before = db.GlAccounts.IgnoreQueryFilters().Count(a => a.TenantId == tid);
        FinCoaBackfillSeed.EnsureSeeded(db);
        var after = db.GlAccounts.IgnoreQueryFilters().Count(a => a.TenantId == tid);

        Assert.Equal(before, after);   // 已全者零变动，无重复插
    }

    [Fact]
    public void Backfill_MultiTenant_OnlyMissingTenantChanges()
    {
        using var db = NewDb();
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        AddTenant(db, t1, "T1");
        AddTenant(db, t2, "T2");
        SeedTenantCoa(db, t1, FinCoaTemplate.CnGaap, excludeCodes: "2204");   // 缺
        SeedTenantCoa(db, t2, FinCoaTemplate.CnGaap);                          // 全

        var t2Before = db.GlAccounts.IgnoreQueryFilters().Count(a => a.TenantId == t2);
        FinCoaBackfillSeed.EnsureSeeded(db);

        Assert.NotNull(Acct(db, t1, FinCoaTemplate.CnGaap, "2204"));           // T1 补上
        var t2After = db.GlAccounts.IgnoreQueryFilters().Count(a => a.TenantId == t2);
        Assert.Equal(t2Before, t2After);                                       // T2 不动（无重复）
        // T1 恰好只多了 1 行（2204），未误插其它
        Assert.Single(db.GlAccounts.IgnoreQueryFilters().Where(a => a.TenantId == t1 && a.Code == "2204"));
    }

    [Fact]
    public void Backfill_TenantWithoutScheme_Skipped()
    {
        using var db = NewDb();
        var tid = Guid.NewGuid();
        AddTenant(db, tid, "T1");   // 有租户但未导入任何 COA

        FinCoaBackfillSeed.EnsureSeeded(db);

        Assert.Empty(db.GlAccounts.IgnoreQueryFilters().Where(a => a.TenantId == tid));   // 不凭空建 COA
    }
}
