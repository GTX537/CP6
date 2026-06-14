using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章01 A-1：会计科目 / 成本中心实体落库往返。
/// （服务层 CRUD + 模板导入在 A-2 的 GlAccountServiceTests 覆盖。）
/// </summary>
public class GlAccountTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    [Fact]
    public async Task GlAccount_RoundTrip_PersistsAllFields()
    {
        using var db = NewDb();
        var ar = new GlAccount
        {
            Code = "1122",
            Name = "应收账款",
            Type = AccountType.Asset,
            NormalSide = AccountSide.Debit,
            Level = 2,
            IsLeaf = true,
            IsControl = true,
            SubLedgerType = "AR",
            RequirePartner = true,
            Role = "AR_CONTROL",
            StandardScheme = "CN-GAAP",
        };
        db.GlAccounts.Add(ar);
        await db.SaveChangesAsync();

        var loaded = await db.GlAccounts.SingleAsync(x => x.Code == "1122");
        Assert.NotEqual(Guid.Empty, loaded.Id);
        Assert.Equal("应收账款", loaded.Name);
        Assert.Equal(AccountType.Asset, loaded.Type);
        Assert.Equal(AccountSide.Debit, loaded.NormalSide);
        Assert.True(loaded.IsControl);
        Assert.True(loaded.RequirePartner);
        Assert.Equal("AR", loaded.SubLedgerType);
        Assert.Equal("AR_CONTROL", loaded.Role);
        Assert.True(loaded.IsActive);   // 默认启用
    }

    [Fact]
    public async Task GlAccount_TreeParent_Links()
    {
        using var db = NewDb();
        var parent = new GlAccount { Code = "1000", Name = "流动资产", Type = AccountType.Asset, NormalSide = AccountSide.Debit, Level = 1, IsLeaf = false };
        db.GlAccounts.Add(parent);
        await db.SaveChangesAsync();

        var child = new GlAccount { Code = "1001", Name = "库存现金", Type = AccountType.Asset, NormalSide = AccountSide.Debit, Level = 2, IsLeaf = true, ParentId = parent.Id };
        db.GlAccounts.Add(child);
        await db.SaveChangesAsync();

        var loaded = await db.GlAccounts.SingleAsync(x => x.Code == "1001");
        Assert.Equal(parent.Id, loaded.ParentId);
        Assert.True(loaded.IsLeaf);
    }

    [Fact]
    public async Task CostCenter_RoundTrip_WithMachineLink()
    {
        using var db = NewDb();
        var cc = new CostCenter
        {
            Code = "PRT-01",
            Name = "印刷机1号",
            Type = CostCenterType.Machine,
            LinkMachineId = "M-PRT-01",
        };
        db.CostCenters.Add(cc);
        await db.SaveChangesAsync();

        var loaded = await db.CostCenters.SingleAsync(x => x.Code == "PRT-01");
        Assert.Equal(CostCenterType.Machine, loaded.Type);
        Assert.Equal("M-PRT-01", loaded.LinkMachineId);
        Assert.True(loaded.IsActive);
    }
}
