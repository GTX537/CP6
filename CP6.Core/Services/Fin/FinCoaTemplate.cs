using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 默认科目表模板包（章01 §3）。多套模板包结构一致（五大类+借贷方向+控制科目），只换编码/命名；
/// 部署时按 <see cref="StandardScheme"/> 选一套一键导入。控制科目的 <see cref="CoaSeedRow.Role"/>
/// 角色锚点跨模板恒定——自动凭证引擎（Plan 2）只认 Role，换模板包零改动。
/// </summary>
public static class FinCoaTemplate
{
    public const string CnGaap = "CN-GAAP";
    public const string Intl = "INTL";

    /// <summary>模板包中的一行科目定义。NormalSide 显式给（备抵科目方向与大类相反）。</summary>
    public record CoaSeedRow(
        string Code,
        string Name,
        AccountType Type,
        AccountSide NormalSide,
        bool IsLeaf,
        int Level = 1,
        bool IsControl = false,
        string? SubLedgerType = null,
        bool RequirePartner = false,
        string? Role = null,
        string? ParentCode = null);

    /// <summary>按方案取模板；未知方案抛 E-FIN-010。</summary>
    public static IReadOnlyList<CoaSeedRow> Get(string scheme) => scheme switch
    {
        CnGaap => CnGaapRows,
        Intl => IntlRows,
        _ => throw new InvalidOperationException("E-FIN-010"),   // 未知模板方案
    };

    // 借/贷便捷常量
    private const AccountSide Dr = AccountSide.Debit;
    private const AccountSide Cr = AccountSide.Credit;

    /// <summary>中国企业会计准则（CN-GAAP）全量模板（章01 §3.2）。</summary>
    public static readonly IReadOnlyList<CoaSeedRow> CnGaapRows = new List<CoaSeedRow>
    {
        // 1 资产类（借方增）
        new("1000", "流动资产",   AccountType.Asset, Dr, IsLeaf: false),
        new("1001", "库存现金",   AccountType.Asset, Dr, true, 2, ParentCode: "1000"),
        new("1002", "银行存款",   AccountType.Asset, Dr, true, 2, ParentCode: "1000"),
        new("1012", "其他货币资金", AccountType.Asset, Dr, true, 2, ParentCode: "1000"),
        new("1101", "交易性金融资产", AccountType.Asset, Dr, true, 2, ParentCode: "1000"),
        new("1122", "应收账款",   AccountType.Asset, Dr, true, 2, IsControl: true, SubLedgerType: "AR", RequirePartner: true, Role: "AR_CONTROL", ParentCode: "1000"),
        new("1123", "预付账款",   AccountType.Asset, Dr, true, 2, RequirePartner: true, Role: "AP_PREPAYMENT", ParentCode: "1000"),
        new("1131", "应收票据",   AccountType.Asset, Dr, true, 2, ParentCode: "1000"),
        new("1221", "其他应收款", AccountType.Asset, Dr, true, 2, ParentCode: "1000"),
        new("1231", "坏账准备",   AccountType.Asset, Cr, true, 2, ParentCode: "1000"),   // 备抵，贷方余额
        new("1401", "原材料",     AccountType.Asset, Dr, true, 2, IsControl: true, SubLedgerType: "INV", Role: "INVENTORY", ParentCode: "1000"),
        new("1402", "在途物资",   AccountType.Asset, Dr, true, 2, ParentCode: "1000"),
        new("1403", "周转材料",   AccountType.Asset, Dr, true, 2, ParentCode: "1000"),
        new("1411", "在制品 WIP", AccountType.Asset, Dr, true, 2, IsControl: true, SubLedgerType: "COST", Role: "WIP", ParentCode: "1000"),
        new("1412", "库存商品 FG", AccountType.Asset, Dr, true, 2, IsControl: true, SubLedgerType: "INV", Role: "FG", ParentCode: "1000"),
        new("1471", "存货跌价准备", AccountType.Asset, Cr, true, 2, ParentCode: "1000"),   // 备抵
        new("1601", "固定资产",   AccountType.Asset, Dr, true, Role: "FIXED_ASSET"),
        new("1602", "累计折旧",   AccountType.Asset, Cr, true, Role: "ACCUM_DEPREC"),   // 备抵
        new("1606", "固定资产清理", AccountType.Asset, Dr, true, Role: "ASSET_CLEARING"),   // A3 §4 处置清理科目
        new("1701", "无形资产",   AccountType.Asset, Dr, true),
        new("1901", "待处理财产损溢", AccountType.Asset, Dr, true, Role: "PENDING_PROPERTY_LOSS"),   // A3 §4 盘亏清理

        // 2 负债类（贷方增）
        new("2000", "流动负债",   AccountType.Liability, Cr, IsLeaf: false),
        new("2001", "短期借款",   AccountType.Liability, Cr, true, 2, ParentCode: "2000"),
        new("2202", "应付账款",   AccountType.Liability, Cr, true, 2, IsControl: true, SubLedgerType: "AP", RequirePartner: true, Role: "AP_CONTROL", ParentCode: "2000"),
        new("2203", "预收账款",   AccountType.Liability, Cr, true, 2, RequirePartner: true, Role: "AR_ADVANCE", ParentCode: "2000"),
        new("2204", "暂估应付账款", AccountType.Liability, Cr, true, 2, Role: "GRNI", ParentCode: "2000"),   // 波A 收货未开票暂估（2202 已被 AP_CONTROL 占用，另取邻码）
        new("2211", "应付职工薪酬", AccountType.Liability, Cr, true, 2, ParentCode: "2000"),
        new("2221", "应交税费",   AccountType.Liability, Cr, IsLeaf: false, Level: 2, ParentCode: "2000"),
        new("2221.01", "应交税费—进项税", AccountType.Liability, Cr, true, 3, Role: "TAX_INPUT", ParentCode: "2221"),
        new("2221.02", "应交税费—销项税", AccountType.Liability, Cr, true, 3, Role: "TAX_OUTPUT", ParentCode: "2221"),
        new("2221.03", "应交税费—应交所得税", AccountType.Liability, Cr, true, 3, ParentCode: "2221"),
        new("2231", "应付票据",   AccountType.Liability, Cr, true, 2, ParentCode: "2000"),
        new("2241", "其他应付款", AccountType.Liability, Cr, true, 2, ParentCode: "2000"),
        new("2501", "长期借款",   AccountType.Liability, Cr, true),

        // 3 权益类（贷方增）
        new("3001", "实收资本",   AccountType.Equity, Cr, true, Role: "EQUITY_CAPITAL"),
        new("3002", "资本公积",   AccountType.Equity, Cr, true),
        new("3101", "盈余公积",   AccountType.Equity, Cr, true),
        new("3103", "本年利润",   AccountType.Equity, Cr, true),
        new("3104", "利润分配—未分配利润", AccountType.Equity, Cr, true, Role: "RETAINED_EARNINGS"),

        // 4 收入类（贷方增）
        new("4001", "主营业务收入", AccountType.Revenue, Cr, true, Role: "REVENUE"),
        new("4051", "其他业务收入", AccountType.Revenue, Cr, true),
        new("4301", "营业外收入", AccountType.Revenue, Cr, true, Role: "NON_OP_INCOME"),
        new("4401", "汇兑收益",   AccountType.Revenue, Cr, true, Role: "FX_GAIN"),

        // 5 成本类（借方增）
        new("5001", "主营业务成本", AccountType.Expense, Dr, true, Role: "COGS"),
        new("5051", "其他业务成本", AccountType.Expense, Dr, true),
        new("5101", "制造费用",   AccountType.Expense, Dr, IsLeaf: false),
        new("5101.01", "制造费用—折旧", AccountType.Expense, Dr, true, 2, ParentCode: "5101"),
        new("5101.02", "制造费用—水电", AccountType.Expense, Dr, true, 2, ParentCode: "5101"),
        new("5101.03", "制造费用—间接人工", AccountType.Expense, Dr, true, 2, ParentCode: "5101"),
        new("5201", "生产成本—直接材料", AccountType.Expense, Dr, true, Role: "DIRECT_MATERIAL"),
        new("5202", "生产成本—直接人工", AccountType.Expense, Dr, true, Role: "DIRECT_LABOR"),
        new("5203", "生产成本—制造费用", AccountType.Expense, Dr, true, Role: "MFG_OVERHEAD"),

        // 6 费用类（借方增）
        new("6001", "销售费用",   AccountType.Expense, Dr, true),
        new("6002", "管理费用",   AccountType.Expense, Dr, true),
        new("6003", "财务费用",   AccountType.Expense, Dr, true),
        new("6004", "财务费用—汇兑损失", AccountType.Expense, Dr, true, Role: "FX_LOSS"),
        new("6115", "资产处置损益", AccountType.Expense, Dr, true, Role: "ASSET_DISPOSAL_PL"),   // A3 §4 出售/转让损益
        new("6601", "研发费用",   AccountType.Expense, Dr, true),
        new("6711", "营业外支出", AccountType.Expense, Dr, true, Role: "NON_OP_EXPENSE"),   // A3 §4 报废净损/盘亏
        new("6801", "所得税费用", AccountType.Expense, Dr, true),
    };

    /// <summary>
    /// 国际通用区间码（INTL）关键科目模板（章01 §3.3）。纯区间码不绑准则；同 Role 不同 Code。
    /// 全量按同一规则铺开，此处给关键/控制科目（含 Role 锚点），足够支撑自动凭证与 MVP。
    /// </summary>
    public static readonly IReadOnlyList<CoaSeedRow> IntlRows = new List<CoaSeedRow>
    {
        new("1010", "Cash on Hand",            AccountType.Asset, Dr, true),
        new("1020", "Bank",                    AccountType.Asset, Dr, true),
        new("1100", "Accounts Receivable",     AccountType.Asset, Dr, true, IsControl: true, SubLedgerType: "AR", RequirePartner: true, Role: "AR_CONTROL"),
        new("1300", "Raw Materials",           AccountType.Asset, Dr, true, IsControl: true, SubLedgerType: "INV", Role: "INVENTORY"),
        new("1340", "Work in Progress",        AccountType.Asset, Dr, true, IsControl: true, SubLedgerType: "COST", Role: "WIP"),
        new("1350", "Finished Goods",          AccountType.Asset, Dr, true, IsControl: true, SubLedgerType: "INV", Role: "FG"),
        new("1900", "Pending Property Loss",   AccountType.Asset, Dr, true, Role: "PENDING_PROPERTY_LOSS"),   // 波A 盘亏清理
        new("2100", "Accounts Payable",        AccountType.Liability, Cr, true, IsControl: true, SubLedgerType: "AP", RequirePartner: true, Role: "AP_CONTROL"),
        new("2110", "Goods Received Not Invoiced", AccountType.Liability, Cr, true, Role: "GRNI"),             // 波A 收货未开票暂估（2150 已被 AR_ADVANCE 占用）
        new("2150", "Advance from Customers",  AccountType.Liability, Cr, true, RequirePartner: true, Role: "AR_ADVANCE"),
        new("2210", "Input Tax (VAT recoverable)", AccountType.Liability, Cr, true, Role: "TAX_INPUT"),
        new("2220", "Output Tax (VAT payable)",    AccountType.Liability, Cr, true, Role: "TAX_OUTPUT"),
        new("3000", "Share Capital",           AccountType.Equity, Cr, true, Role: "EQUITY_CAPITAL"),
        new("3300", "Retained Earnings",       AccountType.Equity, Cr, true, Role: "RETAINED_EARNINGS"),
        new("4000", "Sales Revenue",           AccountType.Revenue, Cr, true, Role: "REVENUE"),
        new("4800", "Non-Operating Income",    AccountType.Revenue, Cr, true, Role: "NON_OP_INCOME"),        // 波A 盘盈利得
        new("4900", "FX Gain",                 AccountType.Revenue, Cr, true, Role: "FX_GAIN"),
        new("5000", "Cost of Goods Sold",      AccountType.Expense, Dr, true, Role: "COGS"),
        new("5100", "Direct Material",         AccountType.Expense, Dr, true, Role: "DIRECT_MATERIAL"),
        new("5200", "Direct Labor",            AccountType.Expense, Dr, true, Role: "DIRECT_LABOR"),
        new("5300", "Manufacturing Overhead",  AccountType.Expense, Dr, true, Role: "MFG_OVERHEAD"),
        new("6800", "Non-Operating Expense",   AccountType.Expense, Dr, true, Role: "NON_OP_EXPENSE"),       // 波A 报废净损
        new("6900", "FX Loss",                 AccountType.Expense, Dr, true, Role: "FX_LOSS"),
    };
}
