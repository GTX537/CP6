using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 记账规则种子（章05 §6）。播种自动凭证引擎要用的标准规则——AP 发票/付款/预付/红字、AR 收入/成本结转/收款/预收/销售红字。
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

    /// <summary>标准规则的定义（纯内存，便于单测断言）。</summary>
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

        // AP 预付款：借 预付账款（带供应商）/ 贷 银行存款（账户来自事件头）
        yield return new PostingRule
        {
            EventType = "AP.Prepayment", Name = "应付预付款", VoucherSource = VoucherSource.AP, IsActive = true,
            Lines =
            {
                Role(1, PostingSide.Debit, "AP_PREPAYMENT", "Amount", carryPartner: true),
                Header(2, PostingSide.Credit, "BankGlAccountId", "Amount"),
            },
        };

        // AP 供应商红字：借 应付（带供应商）/ 贷 各费用/原材料（透传）/ 贷 进项税转出（与发票过账镜像）
        yield return new PostingRule
        {
            EventType = "AP.CreditMemo", Name = "应付供应商红字", VoucherSource = VoucherSource.AP, IsActive = true,
            Lines =
            {
                Role(1, PostingSide.Debit, "AP_CONTROL", "GrossAmount", carryPartner: true),
                Doc(2, PostingSide.Credit, "ExpenseAccountId", "Amount", carryCostCenter: true),
                Role(3, PostingSide.Credit, "TAX_INPUT", "TaxAmount"),
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

        // AR 收款：借 银行存款（账户来自事件头 BankGlAccountId）/ 贷 应收（带客户）。与 AP.Payment 镜像反向
        yield return new PostingRule
        {
            EventType = "AR.Receipt", Name = "应收收款", VoucherSource = VoucherSource.AR, IsActive = true,
            Lines =
            {
                Header(1, PostingSide.Debit, "BankGlAccountId", "Amount"),
                Role(2, PostingSide.Credit, "AR_CONTROL", "Amount", carryPartner: true),
            },
        };

        // AR 预收款：借 银行存款（事件头）/ 贷 预收账款（带客户）。与 AP.Prepayment 镜像反向
        yield return new PostingRule
        {
            EventType = "AR.Advance", Name = "应收预收款", VoucherSource = VoucherSource.AR, IsActive = true,
            Lines =
            {
                Header(1, PostingSide.Debit, "BankGlAccountId", "Amount"),
                Role(2, PostingSide.Credit, "AR_ADVANCE", "Amount", carryPartner: true),
            },
        };

        // AR 销售红字（退货冲收入）：借 主营收入 / 借 销项税（0额跳过）/ 贷 应收（带客户）。与 AR.Revenue 镜像反向
        yield return new PostingRule
        {
            EventType = "AR.CreditMemo", Name = "应收销售红字", VoucherSource = VoucherSource.AR, IsActive = true,
            Lines =
            {
                Role(1, PostingSide.Debit, "REVENUE", "NetAmount"),
                Role(2, PostingSide.Debit, "TAX_OUTPUT", "TaxAmount"),
                Role(3, PostingSide.Credit, "AR_CONTROL", "GrossAmount", carryPartner: true),
            },
        };

        // AR 销售红字成本回冲（货退回入库）：借 库存商品 FG / 贷 主营成本（来源 Cost，与 AR.Cogs 镜像反向）
        yield return new PostingRule
        {
            EventType = "AR.CogsReversal", Name = "应收成本回冲", VoucherSource = VoucherSource.Cost, IsActive = true,
            Lines =
            {
                Role(1, PostingSide.Debit, "FG", "Amount"),
                Role(2, PostingSide.Credit, "COGS", "Amount"),
            },
        };

        // 外注成品成本入账（采购章07 §5）：借 库存商品 FG / 贷 原材料 INVENTORY（料+加工费从原材料结转入成品）。
        // 加工费的应付由三单匹配（FinApServiceAdapter 借 INVENTORY 贷 AP_CONTROL）独立记，此处只做结转、不碰应付 → 零重复。来源 Cost。
        yield return new PostingRule
        {
            EventType = "Subcontract.CostPosted", Name = "外注成品成本入账", VoucherSource = VoucherSource.Cost, IsActive = true,
            Lines =
            {
                Role(1, PostingSide.Debit, "FG", "Amount"),
                Role(2, PostingSide.Credit, "INVENTORY", "Amount"),
            },
        };

        // ── 波A 库存移动自动凭证（VoucherSource.Inventory）──
        // 采购入库暂估：借 原材料 INVENTORY / 贷 暂估应付账款 GRNI（收货未开票，发票到票后再冲销）。金额=入库 Qty×UnitPrice
        yield return new PostingRule
        {
            EventType = "Inventory.Received", Name = "库存入库暂估", VoucherSource = VoucherSource.Inventory, IsActive = true,
            Lines =
            {
                Role(1, PostingSide.Debit, "INVENTORY", "Amount"),
                Role(2, PostingSide.Credit, "GRNI", "Amount"),
            },
        };

        // 盘盈：借 原材料 INVENTORY / 贷 营业外收入 NON_OP_INCOME（盘盈利得）
        yield return new PostingRule
        {
            EventType = "Inventory.AdjustGain", Name = "库存盘盈", VoucherSource = VoucherSource.Inventory, IsActive = true,
            Lines =
            {
                Role(1, PostingSide.Debit, "INVENTORY", "Amount"),
                Role(2, PostingSide.Credit, "NON_OP_INCOME", "Amount"),
            },
        };

        // 盘亏：借 待处理财产损溢 PENDING_PROPERTY_LOSS / 贷 原材料 INVENTORY
        yield return new PostingRule
        {
            EventType = "Inventory.AdjustLoss", Name = "库存盘亏", VoucherSource = VoucherSource.Inventory, IsActive = true,
            Lines =
            {
                Role(1, PostingSide.Debit, "PENDING_PROPERTY_LOSS", "Amount"),
                Role(2, PostingSide.Credit, "INVENTORY", "Amount"),
            },
        };

        // 报废：借 营业外支出 NON_OP_EXPENSE（报废损失）/ 贷 原材料 INVENTORY
        yield return new PostingRule
        {
            EventType = "Inventory.Scrapped", Name = "库存报废", VoucherSource = VoucherSource.Inventory, IsActive = true,
            Lines =
            {
                Role(1, PostingSide.Debit, "NON_OP_EXPENSE", "Amount"),
                Role(2, PostingSide.Credit, "INVENTORY", "Amount"),
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
