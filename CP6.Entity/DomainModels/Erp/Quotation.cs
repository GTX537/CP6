using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Erp;

/// <summary>
/// 御見積書（正式报价书）主表 - MSBBPA030
/// </summary>
/// <remarks>
/// 对应需求 §2.9.1 T_Quotation
/// 聚合根：一张御見積書聚合 N 张見積計算書（Quotation ⇄ QuotationCalc），
/// 并持有 N 行打印用明细（QuotationDetail）。
/// </remarks>
[Table("T_Quotation")]
public class Quotation : BaseBizEntity
{
    // ───── 业务主键 ─────
    /// <summary>御見積書NO（10 字符，自动采番）</summary>
    [Required]
    [MaxLength(20)]
    public string QtnNo { get; set; } = string.Empty;

    /// <summary>主番（NUMERIC 8）——用于采番排序</summary>
    public int QtnNoMain { get; set; }

    /// <summary>枝番（NUMERIC 2）</summary>
    public int QtnNoBranch { get; set; }

    /// <summary>コピー来源 NO</summary>
    [MaxLength(20)]
    public string? RefQtnNo { get; set; }

    // ───── ヘッダー区 ─────
    /// <summary>拠点 CD</summary>
    [Required, MaxLength(10)]
    public string BaseCd { get; set; } = string.Empty;

    /// <summary>担当者 CD</summary>
    [Required, MaxLength(10)]
    public string StaffCd { get; set; } = string.Empty;

    // ───── 見積情報区 ─────
    /// <summary>得意先 CD</summary>
    [Required, MaxLength(20)]
    public string CustomerCd { get; set; } = string.Empty;

    /// <summary>得意先名（冗余，方便列表渲染）</summary>
    [MaxLength(100)]
    public string? CustomerName { get; set; }

    /// <summary>案件 NO（親）</summary>
    [MaxLength(15)]
    public string? ProjectNoParent { get; set; }

    /// <summary>案件 NO（子）</summary>
    [MaxLength(15)]
    public string? ProjectNoChild { get; set; }

    /// <summary>案件 NO（部材）</summary>
    [MaxLength(15)]
    public string? ProjectNoMaterial { get; set; }

    // ───── FSC 情報 ─────
    /// <summary>FSC 管理 NO（FSC チェックシート画面更新）</summary>
    [MaxLength(20)]
    public string? FscMgmtNo { get; set; }

    /// <summary>FSC チェックシート発行日</summary>
    public DateTime? FscChecklistDate { get; set; }

    // ───── 御見積書ヘッダー区 ─────
    /// <summary>先方担当</summary>
    [MaxLength(100)]
    public string? ContactPerson { get; set; }

    /// <summary>納入場所</summary>
    [MaxLength(100)]
    public string? DeliveryLocation { get; set; }

    /// <summary>納入期限</summary>
    [MaxLength(100)]
    public string? DeliveryDeadline { get; set; }

    /// <summary>別途運賃</summary>
    [MaxLength(100)]
    public string? Freight { get; set; }

    /// <summary>お支払条件</summary>
    [MaxLength(100)]
    public string? PaymentCondition { get; set; }

    /// <summary>見積有効期限</summary>
    [MaxLength(100)]
    public string? ValidityPeriod { get; set; }

    // ───── 御見積書フッター区（備考01-15） ─────
    [MaxLength(200)] public string? QtnNote01 { get; set; }
    [MaxLength(200)] public string? QtnNote02 { get; set; }
    [MaxLength(200)] public string? QtnNote03 { get; set; }
    [MaxLength(200)] public string? QtnNote04 { get; set; }
    [MaxLength(200)] public string? QtnNote05 { get; set; }
    [MaxLength(200)] public string? QtnNote06 { get; set; }
    [MaxLength(200)] public string? QtnNote07 { get; set; }
    [MaxLength(200)] public string? QtnNote08 { get; set; }
    [MaxLength(200)] public string? QtnNote09 { get; set; }
    [MaxLength(200)] public string? QtnNote10 { get; set; }
    [MaxLength(200)] public string? QtnNote11 { get; set; }
    [MaxLength(200)] public string? QtnNote12 { get; set; }
    [MaxLength(200)] public string? QtnNote13 { get; set; }
    [MaxLength(200)] public string? QtnNote14 { get; set; }
    [MaxLength(200)] public string? QtnNote15 { get; set; }

    // ───── 見積計算書(提出用) 備考区 ─────
    /// <summary>寸法印字（定数グループ MAC_SIZE_PURINT_TYP）</summary>
    [MaxLength(4)]
    public string? DimensionPrint { get; set; }

    [MaxLength(200)] public string? CalcNote01 { get; set; }
    [MaxLength(200)] public string? CalcNote02 { get; set; }
    [MaxLength(200)] public string? CalcNote03 { get; set; }
    [MaxLength(200)] public string? CalcNote04 { get; set; }
    [MaxLength(200)] public string? CalcNote05 { get; set; }
    [MaxLength(200)] public string? CalcNote06 { get; set; }
    [MaxLength(200)] public string? CalcNote07 { get; set; }
    [MaxLength(200)] public string? CalcNote08 { get; set; }

    // ───── 発行・金額区 ─────
    /// <summary>御見積書発行日（発行时自動セット）</summary>
    public DateTime? QtnIssueDate { get; set; }

    /// <summary>見積計算書発行日</summary>
    public DateTime? CalcIssueDate { get; set; }

    /// <summary>合計金額（= 明細金額の総和）</summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal? TotalAmount { get; set; }

    /// <summary>合計金額印字（初始☑）</summary>
    public bool PrintTotalFlg { get; set; } = true;

    // ───── 状态区 ─────
    /// <summary>
    /// 見積確認FLG：0=未承認、9=承認済
    /// 对应定数グループ MAC_QTN_CNF_STTS
    /// </summary>
    public int EstimateCheckFlg { get; set; } = 0;

    /// <summary>見積確認日</summary>
    public DateTime? EstimateCheckDate { get; set; }

    /// <summary>
    /// マスタ確定FLG：0=未確定、1=処理中、9=確定済
    /// </summary>
    public int MasterConfirmFlg { get; set; } = 0;

    /// <summary>マスタ確定日</summary>
    public DateTime? MasterConfirmDate { get; set; }

    // ───── メモ ─────
    [MaxLength(100)] public string? Memo1 { get; set; }
    [MaxLength(100)] public string? Memo2 { get; set; }
    [MaxLength(100)] public string? Memo3 { get; set; }

    // ───── 导航属性 ─────
    /// <summary>关联見積計算書（中间表）</summary>
    public List<QuotationCalc> Calcs { get; set; } = new();

    /// <summary>打印用明细（御見積書明細印字用）</summary>
    public List<QuotationDetail> Details { get; set; } = new();
}
