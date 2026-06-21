using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Pur.Contracts;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Pur;

/// <summary>
/// 外注财务成本入账适配器单测（采购 章07，P2-D2 接桩→真实）。
/// 把 <see cref="IFinCostService"/> 桩换成委托真实自动凭证引擎的适配器：外注成品成本
/// 借 库存商品 FG / 贷 原材料 INVENTORY（料+加工费从原材料结转入成品；加工费应付由三单匹配独立记，此处零重复）。
/// </summary>
public class FinCostServiceAdapterTests
{
    /// <summary>InMemory + 真实 CoA 模板(CnGaap，含 FG/INVENTORY 角色) + 记账规则种子(含 Subcontract.CostPosted)。</summary>
    private static async Task<CP6Context> SeedFinAsync()
    {
        var db = TestHelper.CreateInMemoryContext();
        await new GlAccountService(db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "seed");
        await PostingRuleSeed.EnsureSeededAsync(db);
        return db;
    }

    private static FinCostServiceAdapter Adapter(CP6Context db) =>
        new(new AutoVoucherEngine(db, new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db))), db);

    private static SubcontractCostDto Dto(decimal processing = 300m, decimal consign = 600m) => new()
    {
        PoNo = "PO1", LineNo = 1, FinishedItemId = "BOX-A", FinishedQty = 100m,
        ProcessingFee = processing, ConsignCost = consign, FinishedCost = processing + consign,
    };

    [Fact]
    public async Task Post_GeneratesCostVoucher_DebitFgCreditInventory()
    {
        using var db = await SeedFinAsync();

        var r = await Adapter(db).PostSubcontractCostAsync(Dto(), "u1");

        Assert.True(r.Ok, r.Code);
        Assert.False(string.IsNullOrEmpty(r.CostVoucherNo));        // 真实凭证号

        var entry = await db.JournalEntries.Include(x => x.Lines)
            .SingleAsync(j => j.Source == VoucherSource.Cost && j.SourceDocNo == "SC-PO1-1");
        Assert.Equal(JournalStatus.Posted, entry.Status);
        Assert.Equal(entry.No, r.CostVoucherNo);

        var fg = await db.GlAccounts.FirstAsync(a => a.Role == "FG");
        var inv = await db.GlAccounts.FirstAsync(a => a.Role == "INVENTORY");
        var debit = entry.Lines.Single(l => l.Debit > 0);
        var credit = entry.Lines.Single(l => l.Credit > 0);
        Assert.Equal(fg.Id, debit.AccountId);                      // 借 库存商品 FG
        Assert.Equal(900m, debit.Debit);                           // = FinishedCost (300+600)
        Assert.Equal(inv.Id, credit.AccountId);                    // 贷 原材料 INVENTORY
        Assert.Equal(900m, credit.Credit);
    }

    [Fact]
    public async Task Post_Idempotent_SameVoucherNo_NoDuplicate()
    {
        using var db = await SeedFinAsync();
        var adapter = Adapter(db);

        var r1 = await adapter.PostSubcontractCostAsync(Dto(), "u1");
        var r2 = await adapter.PostSubcontractCostAsync(Dto(), "u1");   // 重放同 PO-Line

        Assert.True(r1.Ok, r1.Code);
        Assert.True(r2.Ok, r2.Code);
        Assert.Equal(r1.CostVoucherNo, r2.CostVoucherNo);              // 幂等返回同一凭证号
        Assert.Equal(1, await db.JournalEntries.CountAsync(j => j.SourceDocNo == "SC-PO1-1"));  // 不重复生成
    }

    [Fact]
    public void SeedRule_SubcontractCost_DebitFgCreditInventory()
    {
        var rule = PostingRuleSeed.BuildRules().Single(r => r.EventType == "Subcontract.CostPosted");

        Assert.Equal(VoucherSource.Cost, rule.VoucherSource);
        Assert.True(rule.IsActive);
        var debit = rule.Lines.Single(l => l.Side == PostingSide.Debit);
        Assert.Equal("FG", debit.AccountRole);                        // 借 库存商品
        Assert.Equal("Amount", debit.AmountField);
        var credit = rule.Lines.Single(l => l.Side == PostingSide.Credit);
        Assert.Equal("INVENTORY", credit.AccountRole);                // 贷 原材料（不碰应付）
        Assert.Equal("Amount", credit.AmountField);
    }
}
