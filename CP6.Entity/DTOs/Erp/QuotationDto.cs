namespace CP6.Entity.DTOs.Erp;

/// <summary>御見積書 操作种别（MSBBPA030 §2.2）</summary>
public enum QuotationOperationType
{
    /// <summary>登録（新建）</summary>
    New = 10,
    /// <summary>訂正（修改）</summary>
    Edit = 20,
    /// <summary>削除（逻辑删除）</summary>
    Delete = 30,
    /// <summary>参照（查看）</summary>
    View = 40,
    /// <summary>コピー（复制新建）</summary>
    Copy = 50,
    /// <summary>確定登録</summary>
    Confirm = 60,
    /// <summary>確定取消</summary>
    CancelConfirm = 70,
}

/// <summary>
/// 御見積書 详情 DTO（GET / POST / PUT 复用）
/// 扁平化：包含主表 + 關聯見積計算書 + 打印用明细
/// </summary>
public class QuotationDto
{
    // 主键
    public string? QtnNo { get; set; }
    public string? RefQtnNo { get; set; }

    // ヘッダー
    public string BaseCd { get; set; } = string.Empty;
    public string StaffCd { get; set; } = string.Empty;

    // 見積情報
    public string CustomerCd { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? ProjectNoParent { get; set; }
    public string? ProjectNoChild { get; set; }
    public string? ProjectNoMaterial { get; set; }

    // FSC
    public string? FscMgmtNo { get; set; }
    public DateTime? FscChecklistDate { get; set; }

    // 御見積書ヘッダー
    public string? ContactPerson { get; set; }
    public string? DeliveryLocation { get; set; }
    public string? DeliveryDeadline { get; set; }
    public string? Freight { get; set; }
    public string? PaymentCondition { get; set; }
    public string? ValidityPeriod { get; set; }

    // 備考01-15（15 行）
    public string?[] QtnNotes { get; set; } = new string?[15];

    // 提出用
    public string? DimensionPrint { get; set; }
    public string?[] CalcNotes { get; set; } = new string?[8];

    // 発行・金額
    public DateTime? QtnIssueDate { get; set; }
    public DateTime? CalcIssueDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public bool PrintTotalFlg { get; set; } = true;

    // 状态
    public int EstimateCheckFlg { get; set; }
    public DateTime? EstimateCheckDate { get; set; }
    public int MasterConfirmFlg { get; set; }
    public DateTime? MasterConfirmDate { get; set; }

    // メモ
    public string? Memo1 { get; set; }
    public string? Memo2 { get; set; }
    public string? Memo3 { get; set; }

    // 关联
    public List<QuotationCalcDto> Calcs { get; set; } = new();
    public List<QuotationDetailDto> Details { get; set; } = new();

    // 乐观锁
    public byte[]? RowVersion { get; set; }

    // 审计信息
    public DateTime? CreateDate { get; set; }
    public DateTime? ModifyDate { get; set; }
}

/// <summary>御見積書 ⇄ 見積計算書 关联 DTO（中间表 1 行 = 見積計算書一覧中的 1 个「使用」✓ 行）</summary>
public class QuotationCalcDto
{
    public string QtnCalcNo { get; set; } = string.Empty;
    public int EstimateCheckFlg { get; set; }
    public DateTime? EstimateCheckDate { get; set; }
    public int MasterConfirmFlg { get; set; }
    public DateTime? MasterConfirmDate { get; set; }

    // 只读展示字段（来自 JOIN EstimateCalc，方便前端渲染一覧）
    public DateTime? QtnCalcDate { get; set; }
    public string? CustomerProductName1 { get; set; }
    public string? CustomerProductName2 { get; set; }
    public decimal? EstimateQty { get; set; }
    public decimal? ConfirmedUnitPrice { get; set; }
    public string? Unit { get; set; }
    public decimal? Amount { get; set; }
    public string? QtnDiv { get; set; }
}

/// <summary>御見積書 打印用明细 DTO（可编辑）</summary>
public class QuotationDetailDto
{
    public int DetailNo { get; set; }
    public string? ItemName1 { get; set; }
    public string? ItemName2 { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? Unit { get; set; }
    public decimal? Amount { get; set; }
    public bool PrintTotalFlg { get; set; } = true;

    /// <summary>关联の見積計算書NO（使用✓自动追加时设置，手工追加时为空）</summary>
    public string? QtnCalcNo { get; set; }
}

/// <summary>御見積書 分页查询参数（MSBBPA040 调用）</summary>
public class QuotationQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? BaseCd { get; set; }
    public string? StaffCd { get; set; }
    public DateTime? IssueDateFrom { get; set; }
    public DateTime? IssueDateTo { get; set; }
    public string? QtnNoFrom { get; set; }
    public string? QtnNoTo { get; set; }
    public string? CustomerCd { get; set; }
    public string? ProjectNoParent { get; set; }
    public string? ProjectNoChild { get; set; }
    public string? ProjectNoMaterial { get; set; }
    public string? CustomerProductName1 { get; set; }
    public string? CustomerProductName2 { get; set; }

    /// <summary>ステータス：任意组合 ["0","9","C"]，对应 未承認/承認済/見積確定済</summary>
    public List<string>? Statuses { get; set; }

    /// <summary>排序列（前端 el-table prop，白名单内才生效）</summary>
    public string? SortField { get; set; }
    /// <summary>排序方向：asc / desc</summary>
    public string? SortOrder { get; set; }
}

/// <summary>御見積書一覧行（MSBBPA040 返回）</summary>
public class QuotationListItem
{
    public string QtnNo { get; set; } = string.Empty;
    public DateTime? QtnIssueDate { get; set; }
    public string? BaseCd { get; set; }
    public string? StaffCd { get; set; }
    public string? StaffName { get; set; }
    public string? CustomerCd { get; set; }
    public string? CustomerName { get; set; }
    public string? ProjectNoParent { get; set; }
    public string? ProjectNoChild { get; set; }
    public string? ProjectNoMaterial { get; set; }

    /// <summary>第 1 行明细の品名1</summary>
    public string? ItemName1 { get; set; }
    /// <summary>第 1 行明细の品名2</summary>
    public string? ItemName2 { get; set; }
    /// <summary>第 1 行明细の数量</summary>
    public decimal? FirstQuantity { get; set; }
    /// <summary>第 1 行明细の単価</summary>
    public decimal? FirstUnitPrice { get; set; }
    /// <summary>第 1 行明细の金額</summary>
    public decimal? FirstAmount { get; set; }

    public decimal? TotalAmount { get; set; }
    public int EstimateCheckFlg { get; set; }
    public int MasterConfirmFlg { get; set; }

    /// <summary>综合状态（展示用）：未承認/承認済/見積確定済</summary>
    public string? Status { get; set; }
}

/// <summary>関連見積計算書 候选项（案件NO設定後联动刷新用，§2.6.1）</summary>
public class QuotationCalcCandidate
{
    public string QtnCalcNo { get; set; } = string.Empty;
    public DateTime? QtnCalcDate { get; set; }
    public string? CustomerProductName1 { get; set; }
    public string? CustomerProductName2 { get; set; }
    public decimal? EstimateQty { get; set; }
    public decimal? ConfirmedUnitPrice { get; set; }
    public string? Unit { get; set; }
    public decimal? Amount { get; set; }
    public string? QtnDiv { get; set; }
    /// <summary>若已在 QuotationCalc 中存在（即当前 Quotation 已关联），前端默认勾选「使用」</summary>
    public bool IsLinked { get; set; }
}

/// <summary>確定登録请求（§2.6.5）</summary>
public class ConfirmRequest
{
    /// <summary>勾选「確定」的 見積計算書NO 列表</summary>
    public List<string> QtnCalcNos { get; set; } = new();
    public byte[]? RowVersion { get; set; }
}

/// <summary>発行帳票请求（§2.6.4 Step6）</summary>
public class IssueRequest
{
    public bool IssueQuotation { get; set; } = true;
    public bool IssueSubmitCalc { get; set; } = true;
    public bool IssueCalc { get; set; } = true;
}
