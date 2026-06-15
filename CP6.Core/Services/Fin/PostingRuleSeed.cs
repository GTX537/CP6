using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 记账规则种子（章05 §6）。播种自动凭证引擎要用的标准规则——AP 发票/付款、AR 收入/成本结转。
/// 幂等：按 EventType 判存，已有不重播；规则只引用 <see cref="GlAccount.Role"/> 角色锚点，不绑具体科目，
/// 故与 COA 模板包（CN-GAAP/INTL）解耦，换模板包零改动。汇差用现成 FX_GAIN/FX_LOSS 角色（核销/重估时挂）。
/// </summary>
public static class PostingRuleSeed
{
    /// <summary>幂等播种四条标准规则。返回新增规则数。</summary>
    public static int EnsureSeeded(CP6Context db)
    {
        var existing = db.PostingRules.Select(r => r.EventType).ToHashSet();
        var added = 0;

        foreach (var rule in BuildRules())
        {
            if (existing.Contains(rule.EventType)) continue;
            db.PostingRules.Add(rule);
            added++;
        }

        if (added > 0) db.SaveChanges();
        return added;
    }

    /// <summary>同 <see cref="EnsureSeeded"/> 的异步版。</summary>
    public static async Task<int> EnsureSeededAsync(CP6Context db)
    {
        var existing = (await db.PostingRules.Select(r => r.EventType).ToListAsync()).ToHashSet();
        var added = 0;
        foreach (var rule in BuildRules())
        {
            if (existing.Contains(rule.EventType)) continue;
            db.PostingRules.Add(rule);
            added++;
        }
        if (added > 0) await db.SaveChangesAsync();
        return added;
    }

    /// <summary>四条标准规则的定义（纯内存，便于单测断言）。</summary>
    public static IEnumerable<PostingRule> BuildRules()
    {
        // AP 发票过账：借 各费用/原材料（单据行透传）+ 借 进项税（0额跳过=不可抵扣）/ 贷 应付（带供应商）
        yield return new PostingRule
        {
            EventType = "AP.InvoicePosted", Name = "应付发票过账", VoucherSource = VoucherSource.AP, IsActive = true,
            Lines =
            {
                Doc(1, PostingSide.Debit, "ExpenseAccountId", "Amount", carryCostCenter: true),
                Role(2, PostingSide.Debit, "TAX_INPUT", "TaxAmount"),
                Role(3, PostingSide.Credit, "AP_CONTROL", "GrossAmount", carryPartner: true),
            },
        };

        // AP 付款：借 应付（带供应商）/ 贷 银行存款（账户来自事件头 BankGlAccountId）
        yield return new PostingRule
        {
            EventType = "AP.Payment", Name = "应付付款", VoucherSource = VoucherSource.AP, IsActive = true,
            Lines =
            {
                Role(1, PostingSide.Debit, "AP_CONTROL", "Amount", carryPartner: true),
                Header(2, PostingSide.Credit, "BankGlAccountId", "Amount"),
            },
        };

        // AR 收入确认：借 应收（带客户）/ 贷 主营收入 / 贷 销项税（0额跳过）
        yield return new PostingRule
        {
            EventType = "AR.Revenue", Name = "应收收入确认", VoucherSource = VoucherSource.AR, IsActive = true,
            Lines =
            {
                Role(1, PostingSide.Debit, "AR_CONTROL", "GrossAmount", carryPartner: true),
                Role(2, PostingSide.Credit, "REVENUE", "NetAmount"),
                Role(3, PostingSide.Credit, "TAX_OUTPUT", "TaxAmount"),
            },
        };

        // AR 成本结转：借 主营成本 / 贷 库存商品 FG（来源 Cost，与收入凭证分开幂等）
        yield return new PostingRule
        {
            EventType = "AR.Cogs", Name = "应收成本结转", VoucherSource = VoucherSource.Cost, IsActive = true,
            Lines =
            {
                Role(1, PostingSide.Debit, "COGS", "Amount"),
                Role(2, PostingSide.Credit, "FG", "Amount"),
            },
        };
    }

    private static PostingRuleLine Role(int no, PostingSide side, string role, string amountField, bool carryPartner = false, bool carryCostCenter = false) =>
        new()
        {
            LineNo = no, Side = side, Source = RuleLineSource.FixedRole,
            AccountRole = role, AmountField = amountField, CarryPartner = carryPartner, CarryCostCenter = carryCostCenter,
        };

    private static PostingRuleLine Doc(int no, PostingSide side, string accField, string amtField, bool carryCostCenter = false) =>
        new()
        {
            LineNo = no, Side = side, Source = RuleLineSource.DocumentLines,
            LineAccountField = accField, LineAmountField = amtField, CarryCostCenter = carryCostCenter,
        };

    private static PostingRuleLine Header(int no, PostingSide side, string headerGuidField, string amountField) =>
        new()
        {
            LineNo = no, Side = side, Source = RuleLineSource.HeaderAccount,
            LineAccountField = headerGuidField, AmountField = amountField,
        };
}
