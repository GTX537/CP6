using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务波A 地基（Task 0.1 + 0.1b）：库存过账规则种子
/// （Inventory.Received / AdjustGain / AdjustLoss / Scrapped，VoucherSource=Inventory）
/// + 两 COA 模板（CN-GAAP / INTL）均含所需 Role（INVENTORY/GRNI/NON_OP_INCOME/
/// PENDING_PROPERTY_LOSS/NON_OP_EXPENSE）。规则只配角色锚点+金额字段，借贷两边取同一
/// "Amount" 字段 → ValidateBalance 意义上天然平衡。
/// </summary>
public class InventoryPostingRuleSeedTests
{
    private static PostingRule Rule(string eventType) =>
        PostingRuleSeed.BuildRules().Single(r => r.EventType == eventType);

    [Fact]
    public void BuildRules_IncludesFourInventoryEventTypes()
    {
        var ets = PostingRuleSeed.BuildRules().Select(r => r.EventType).ToHashSet();
        Assert.Contains("Inventory.Received", ets);
        Assert.Contains("Inventory.AdjustGain", ets);
        Assert.Contains("Inventory.AdjustLoss", ets);
        Assert.Contains("Inventory.Scrapped", ets);
    }

    [Theory]
    [InlineData("Inventory.Received", "INVENTORY", "GRNI")]                     // 采购入库暂估
    [InlineData("Inventory.AdjustGain", "INVENTORY", "NON_OP_INCOME")]         // 盘盈
    [InlineData("Inventory.AdjustLoss", "PENDING_PROPERTY_LOSS", "INVENTORY")] // 盘亏
    [InlineData("Inventory.Scrapped", "NON_OP_EXPENSE", "INVENTORY")]          // 报废
    public void InventoryRule_HasExpectedDebitCreditRoles_AndBalancedAmountField(
        string eventType, string debitRole, string creditRole)
    {
        var rule = Rule(eventType);

        Assert.Equal(VoucherSource.Inventory, rule.VoucherSource);
        Assert.True(rule.IsActive);
        Assert.Equal(2, rule.Lines.Count);

        var debit = rule.Lines.Single(l => l.Side == PostingSide.Debit);
        var credit = rule.Lines.Single(l => l.Side == PostingSide.Credit);

        // 两行均按 Role 锚点解析科目（FixedRole）
        Assert.Equal(RuleLineSource.FixedRole, debit.Source);
        Assert.Equal(RuleLineSource.FixedRole, credit.Source);
        Assert.Equal(debitRole, debit.AccountRole);
        Assert.Equal(creditRole, credit.AccountRole);

        // ★ 借贷两边金额字段一致 = "Amount"：单边同字段 → 借贷天然相等
        Assert.Equal("Amount", debit.AmountField);
        Assert.Equal("Amount", credit.AmountField);
    }

    [Theory]
    [InlineData(FinCoaTemplate.CnGaap)]
    [InlineData(FinCoaTemplate.Intl)]
    public void BothCoaTemplates_ContainEveryRoleUsedByInventoryRules(string scheme)
    {
        var roles = FinCoaTemplate.Get(scheme)
            .Where(r => r.Role != null).Select(r => r.Role!).ToHashSet();

        Assert.Contains("INVENTORY", roles);
        Assert.Contains("GRNI", roles);
        Assert.Contains("NON_OP_INCOME", roles);
        Assert.Contains("PENDING_PROPERTY_LOSS", roles);
        Assert.Contains("NON_OP_EXPENSE", roles);
    }

    [Theory]
    [InlineData(FinCoaTemplate.CnGaap)]
    [InlineData(FinCoaTemplate.Intl)]
    public void GrniAccount_IsLiabilityCredit_AndDoesNotRequirePartner(string scheme)
    {
        var grni = FinCoaTemplate.Get(scheme).Single(r => r.Role == "GRNI");
        Assert.Equal(AccountType.Liability, grni.Type);
        Assert.Equal(AccountSide.Credit, grni.NormalSide);
        // Inventory.Received 贷 GRNI 时不带往来单位；若科目 RequirePartner=true 会触发 E-FIN-106
        Assert.False(grni.RequirePartner);
    }
}
