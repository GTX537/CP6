using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 成本单（章06 §4）。按工单归集料工费的成本归集单 —— ★差异化卖点：料吃 MES 真实消耗量
/// （<see cref="DomainModels.Mes.WorkOrderMaterial"/>.ActualQty）× BOM 受給単価（ProductMaterial.SupplyPrice），
/// 工费按标准估算（F3-D1，MES 未采集工时）。完工 FG 单位成本 = 实际总成本 / 完工数，喂 AR 成本结转（F3-D3）。
/// 差异 = 实际 − 标准（料用量差异：实际用量 vs 计划用量 × 同单价）。WorkOrderNo 软引用 MES 工单。
/// </summary>
[Table("Fin_CostSheet")]
public class CostSheet : BaseTenantEntity
{
    /// <summary>成本单号（采番 CS-yyyyMM-nnnn）</summary>
    [MaxLength(30)] public string No { get; set; } = string.Empty;

    /// <summary>MES 工单号（软引用 T_WorkOrder.WorkOrderNo；一工单一成本单）</summary>
    [Required, MaxLength(20)] public string WorkOrderNo { get; set; } = string.Empty;

    /// <summary>制品 CD</summary>
    [MaxLength(20)] public string? ProductCd { get; set; }

    /// <summary>成本中心 Id（机台/工序/部门分析维度，可空）</summary>
    public Guid? CostCenterId { get; set; }

    /// <summary>完工数量（FG 单位成本分母，取工单累计良品数）</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal CompletedQty { get; set; }

    /// <summary>实际料成本（Σ 实际用量 × BOM 供给单价，本位币）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal MaterialActual { get; set; }

    /// <summary>标准料成本（Σ 计划用量 × BOM 供给单价，本位币）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal MaterialStandard { get; set; }

    /// <summary>实际直接人工（工时×费率归集，本位币；A2 §3.6）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal LaborActual { get; set; }

    /// <summary>标准直接人工（标准工时×费率，本位币；A2 §3.6）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal LaborStandard { get; set; }

    /// <summary>实际制造费用（机时×费率归集，本位币；A2 §3.6）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal OverheadActual { get; set; }

    /// <summary>标准制造费用（标准机时×费率，本位币；A2 §3.6）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal OverheadStandard { get; set; }

    /// <summary>状态机</summary>
    public CostSheetStatus Status { get; set; } = CostSheetStatus.Draft;

    /// <summary>结转生成的凭证 Id（A-3 完工结转）</summary>
    public Guid? JournalEntryId { get; set; }

    // ── 计算属性（不持久化）──
    /// <summary>实际总成本 = 实际料 + 实际工 + 实际费</summary>
    [NotMapped] public decimal TotalActual => MaterialActual + LaborActual + OverheadActual;
    /// <summary>标准总成本 = 标准料 + 标准工 + 标准费</summary>
    [NotMapped] public decimal StandardCost => MaterialStandard + LaborStandard + OverheadStandard;
    /// <summary>成本差异 = 实际 − 标准（&gt;0 超支）</summary>
    [NotMapped] public decimal Variance => TotalActual - StandardCost;
    /// <summary>
    /// FG 完工单位成本（完工数为 0 时取 0）。拍板②（波C.3）：有标准成本时 FG 按【标准】资本化
    /// （SettleAsync #FG 腿按 StandardCost 过账，差异已单独转 COGS），故单位成本 = 标准总成本 / 完工数
    /// ——出货 AR.Cogs 按此贷 FG 才与结转口径恒等（FG 净零，COGS=出货标准+差异=实际）。
    /// 无标准成本（StandardCost&lt;=0）维持实际口径 = 实际总成本 / 完工数。
    /// </summary>
    [NotMapped] public decimal FgUnitCost => CompletedQty > 0m
        ? Math.Round((StandardCost > 0m ? StandardCost : TotalActual) / CompletedQty, 4, MidpointRounding.AwayFromZero)
        : 0m;

    /// <summary>归集明细行（料/工/费来源可追溯）</summary>
    public List<CostSheetLine> Lines { get; set; } = new();
}

/// <summary>成本归集明细行（章06 §4）。每笔料/工/费的来源、用量、单价、金额，可追溯。</summary>
[Table("Fin_CostSheetLine")]
public class CostSheetLine : BaseTenantEntity
{
    /// <summary>所属成本单 Id</summary>
    public Guid CostSheetId { get; set; }

    /// <summary>行号</summary>
    public int LineNo { get; set; }

    /// <summary>成本要素：料/工/费</summary>
    public CostElement Element { get; set; }

    /// <summary>工序 CD（料行来自 WorkOrderMaterial.ProcessCd）</summary>
    [MaxLength(10)] public string? ProcessCd { get; set; }

    /// <summary>材料 CD（料行）</summary>
    [MaxLength(20)] public string? MaterialCd { get; set; }

    /// <summary>材料名</summary>
    [MaxLength(100)] public string? MaterialName { get; set; }

    /// <summary>计划用量（标准）</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal PlanQty { get; set; }

    /// <summary>实际用量（MES 实绩）</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal ActualQty { get; set; }

    /// <summary>单价（BOM 供给单价）</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal UnitPrice { get; set; }

    /// <summary>实际金额 = 实际用量 × 单价（本位币）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal ActualAmount { get; set; }

    /// <summary>标准金额 = 计划用量 × 单价（本位币）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal StandardAmount { get; set; }

    // ── A2 §3.7 工时追溯字段（料行多为空，工/费行承载）──
    /// <summary>工时（h）。Labor 行=人工工时；Overhead 行=机时；Material 行空。</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal? Hours { get; set; }
    /// <summary>标准工时（h）。Labor 行=标准人工工时；Overhead 行=标准机时。</summary>
    [Column(TypeName = "decimal(21,8)")] public decimal? StandardHours { get; set; }
    /// <summary>工作中心 CD（追溯费率来源）。</summary>
    [MaxLength(10)] public string? WgCd { get; set; }
    /// <summary>作业/任务 CD。</summary>
    [MaxLength(50)] public string? TaskCd { get; set; }
    /// <summary>费率生效日（追溯用了哪版费率）。</summary>
    public DateTime? RateValidFrom { get; set; }
    /// <summary>小时来源：Derived/Manual/StandardFallback/LegacyFallback。</summary>
    [MaxLength(30)] public string? HourSource { get; set; }
    /// <summary>计算/回退/警告说明。</summary>
    [MaxLength(500)] public string? CalcNote { get; set; }
    /// <summary>警告码（无为空）。</summary>
    [MaxLength(50)] public string? WarningCode { get; set; }
}

/// <summary>成本单状态机（章06）。</summary>
public enum CostSheetStatus
{
    /// <summary>草稿</summary>
    Draft = 0,
    /// <summary>已归集（料工费汇总完）</summary>
    Collected = 1,
    /// <summary>已结转（完工生成 WIP→FG 凭证，FG 单位成本定）</summary>
    Settled = 2,
}

/// <summary>成本要素。</summary>
public enum CostElement
{
    /// <summary>直接材料</summary>
    Material = 1,
    /// <summary>直接人工</summary>
    Labor = 2,
    /// <summary>制造费用</summary>
    Overhead = 3,
}
