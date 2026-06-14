using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>财务章01 A-2：科目服务 CRUD（不删只停用）+ 多国别模板包导入（Role 锚点跨模板恒定）。</summary>
public class GlAccountServiceTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    [Fact]
    public async Task ImportTemplate_CnGaap_LoadsAllWithRoleAnchors()
    {
        using var db = NewDb();
        var svc = new GlAccountService(db);

        var n = await svc.ImportTemplateAsync(FinCoaTemplate.CnGaap, "tester");

        Assert.Equal(FinCoaTemplate.CnGaapRows.Count, n);
        Assert.Equal(n, await db.GlAccounts.CountAsync());

        // Role 锚点 → CN-GAAP 编码
        Assert.Equal("2202", (await svc.GetByRoleAsync("AP_CONTROL"))!.Code);
        Assert.Equal("1122", (await svc.GetByRoleAsync("AR_CONTROL"))!.Code);
        Assert.Equal("4001", (await svc.GetByRoleAsync("REVENUE"))!.Code);
        Assert.Equal("5001", (await svc.GetByRoleAsync("COGS"))!.Code);

        // 控制科目标记 + 往来单位强制
        var ap = await svc.GetByCodeAsync("2202");
        Assert.True(ap!.IsControl);
        Assert.Equal("AP", ap.SubLedgerType);
        Assert.True(ap.RequirePartner);

        // ParentId 解析：应收账款挂在"流动资产"下
        var ar = await svc.GetByCodeAsync("1122");
        var current = await svc.GetByCodeAsync("1000");
        Assert.Equal(current!.Id, ar!.ParentId);
    }

    [Fact]
    public async Task ImportTemplate_Intl_SameRoleDifferentCode()
    {
        using var db = NewDb();
        var svc = new GlAccountService(db);

        await svc.ImportTemplateAsync(FinCoaTemplate.Intl, null);

        // 同 Role（AP_CONTROL）不同 Code（INTL=2100 vs CN=2202）
        Assert.Equal("2100", (await svc.GetByRoleAsync("AP_CONTROL"))!.Code);
        Assert.Equal("1100", (await svc.GetByRoleAsync("AR_CONTROL"))!.Code);
    }

    [Fact]
    public async Task ImportTemplate_Twice_Throws()
    {
        using var db = NewDb();
        var svc = new GlAccountService(db);
        await svc.ImportTemplateAsync(FinCoaTemplate.CnGaap, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ImportTemplateAsync(FinCoaTemplate.CnGaap, null));
        Assert.Equal("E-FIN-002", ex.Message);
    }

    [Fact]
    public async Task Create_DuplicateCode_Throws()
    {
        using var db = NewDb();
        var svc = new GlAccountService(db);
        await svc.CreateAsync(new GlAccount { Code = "9001", Name = "测试", Type = AccountType.Asset, NormalSide = AccountSide.Debit }, "u1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAsync(new GlAccount { Code = "9001", Name = "重复", Type = AccountType.Asset, NormalSide = AccountSide.Debit }, "u1"));
        Assert.Equal("E-FIN-001", ex.Message);
    }

    [Fact]
    public async Task Deactivate_DisablesNotDeletes()
    {
        using var db = NewDb();
        var svc = new GlAccountService(db);
        var id = await svc.CreateAsync(new GlAccount { Code = "9002", Name = "待停用", Type = AccountType.Expense, NormalSide = AccountSide.Debit }, "u1");

        await svc.DeactivateAsync(id, "u2");

        var e = await svc.GetAsync(id);
        Assert.NotNull(e);                       // 仍在库（没删）
        Assert.False(e!.IsActive);               // 只是停用
        Assert.Empty(await svc.ListAsync());     // 默认列表不含停用
        Assert.Single(await svc.ListAsync(includeInactive: true));
    }

    [Fact]
    public async Task GetByRole_OnlyReturnsActive()
    {
        using var db = NewDb();
        var svc = new GlAccountService(db);
        var id = await svc.CreateAsync(new GlAccount { Code = "4001", Name = "主营业务收入", Type = AccountType.Revenue, NormalSide = AccountSide.Credit, Role = "REVENUE" }, null);
        Assert.NotNull(await svc.GetByRoleAsync("REVENUE"));

        await svc.DeactivateAsync(id, null);
        Assert.Null(await svc.GetByRoleAsync("REVENUE"));   // 停用后角色锚点取不到
    }
}
