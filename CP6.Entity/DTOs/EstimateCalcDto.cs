namespace CP6.Entity.DTOs;

/// <summary>操作种别</summary>
public enum OperationType
{
    /// <summary>登録（新建）</summary>
    New = 10,
    /// <summary>訂正（修改）</summary>
    Edit = 20,
    /// <summary>削除（删除/软删）</summary>
    Delete = 30,
    /// <summary>参照（查看）</summary>
    View = 40,
    /// <summary>コピー（复制新建）</summary>
    Copy = 50,
}

/// <summary>
/// 見積計算書 详情 DTO（GET 返回 + POST/PUT 请求体共用）
/// 扁平化：包含主表 + 工程明细
/// </summary>
public class EstimateCalcDto
{
    // 主键
    public string? QtnCalcNo { get; set; }

    // 基本信息
    public string QtnBaseCd { get; set; } = string.Empty;
    public string OrderBaseCd { get; set; } = string.Empty;
    public string StaffCd { get; set; } = string.Empty;
    public string? CustomerCd { get; set; }
    public string? ProjectNoParent { get; set; }
    public string? ProjectNoChild { get; set; }
    public string? ProjectNoMaterial { get; set; }
    public string? OrderType { get; set; }
    public string? ProductCategoryBig { get; set; }
    public string? ProductCategoryMid { get; set; }
    public string? ProductCategorySml { get; set; }
    public string? CustomerProductName1 { get; set; }
    public string? CustomerProductName2 { get; set; }
    public decimal? OrderQty { get; set; }
    public string? OrderYm { get; set; }
    public string? ParentChildDiv { get; set; }
    public string? FscProductDiv { get; set; }
    public string? FscMaterialDiv { get; set; }

    // 构成信息
    public string? SheetFlute { get; set; }
    public string? PaperCdF { get; set; }
    public string? PrintCdF { get; set; }
    public string? EmbossCdF { get; set; }
    public decimal? PatternCntF { get; set; }
    public string? PaperCdC { get; set; }
    public string? PrintCdC { get; set; }
    public string? EmbossCdC { get; set; }
    public string? PaperCdB { get; set; }
    public string? PrintCdB { get; set; }
    public string? EmbossCdB { get; set; }
    public decimal? PatternCntB { get; set; }
    public string? SheetPrint { get; set; }
    public decimal? BladeWidth { get; set; }
    public decimal? BladeFlow { get; set; }
    public decimal? GutterFb { get; set; }
    public decimal? GutterLr { get; set; }
    public decimal? SheetDimW { get; set; }
    public decimal? SheetDimF { get; set; }
    public string? FinalMachineProc { get; set; }
    public string? ProductShape1 { get; set; }
    public string? ProductShape2 { get; set; }
    public string? DistDiv { get; set; }
    public string? RecyclePayment { get; set; }
    public string? IdMark { get; set; }
    public string? AdShape { get; set; }

    // 战略商品区分
    public bool[] StrategicDivs { get; set; } = new bool[10];

    // 备注
    public string? PrintNote { get; set; }
    public string? MfgNote { get; set; }
    public string? SlipNote { get; set; }
    public string? DeliveryNote { get; set; }
    public string? ShipNote1 { get; set; }
    public string? ShipNote2 { get; set; }

    // 报价数量
    public decimal?[] EstimateQtys { get; set; } = new decimal?[8];
    public decimal?[] PalletCnts { get; set; } = new decimal?[8];
    public decimal? ProposalLot1 { get; set; }
    public decimal? ProposalLot2 { get; set; }
    public string? Unit { get; set; }
    public decimal? DecidedQty { get; set; }

    // 計算結果
    public string? QtnDiv { get; set; }
    public decimal? EstimateSqm { get; set; }
    public decimal? StandardUnitPrice { get; set; }
    public decimal? EstimateUnitPrice { get; set; }
    public decimal? ConfirmedUnitPrice { get; set; }

    // 工程明细
    public List<EstimateCalcProcessDto> Processes { get; set; } = new();

    // 乐观锁（base64 或 byte[]）
    public byte[]? RowVersion { get; set; }

    // 只读展示信息
    public DateTime? QtnDate { get; set; }
    public string? RefQtnCalcNo { get; set; }
    public DateTime? CreateDate { get; set; }
    public DateTime? ModifyDate { get; set; }
}

public class EstimateCalcProcessDto
{
    public int SeqNo { get; set; }
    public string? ProcessCd { get; set; }
    public string? ProcessName { get; set; }
    public string? TaskCd { get; set; }
    public string? TaskName { get; set; }
    public string? WgCd { get; set; }
    public string? MfgLocation { get; set; }
    public string? Spec1Label { get; set; }
    public string? Spec1Val { get; set; }
    public string? Spec2Label { get; set; }
    public string? Spec2Val { get; set; }
    public string? Spec3Label { get; set; }
    public string? Spec3Val { get; set; }
    public string? Spec4Label { get; set; }
    public string? Spec4Val { get; set; }
    public string? Spec5Label { get; set; }
    public string? Spec5Val { get; set; }
    public string? Spec6Label { get; set; }
    public string? Spec6Val { get; set; }
    public string? Spec7Label { get; set; }
    public string? Spec7Val { get; set; }
    public string? PlateNo { get; set; }
    public string? ProcNote1 { get; set; }
    public string? ProcNote2 { get; set; }
}

/// <summary>分页查询请求</summary>
public class EstimateCalcQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? QtnCalcNo { get; set; }
    public string? CustomerCd { get; set; }
    public string? BaseCd { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
}

/// <summary>計算結果（Calculate 接口响应体）</summary>
public class EstimateCalcResult
{
    /// <summary>面積(m²)：シート寸W × シート寸F × 数量 / 1,000,000</summary>
    public decimal EstimateSqm { get; set; }
    /// <summary>標準原価単価（円/枚）</summary>
    public decimal StandardUnitPrice { get; set; }
    /// <summary>見積単価（円/枚）= StandardUnitPrice × (1 + 利益率)</summary>
    public decimal EstimateUnitPrice { get; set; }
    /// <summary>確定単価（円/枚）＝ 与 EstimateUnitPrice 同值，用户可手改</summary>
    public decimal ConfirmedUnitPrice { get; set; }
    /// <summary>明细：各数量段对应合计金额</summary>
    public List<EstimateCalcAmountRow> AmountRows { get; set; } = new();
    /// <summary>過程説明（返给前端展示計算ロジック）</summary>
    public List<string> Notes { get; set; } = new();
}

/// <summary>見積数量 × 単価 的合计行</summary>
public class EstimateCalcAmountRow
{
    public int Index { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>分页响应行</summary>
public class EstimateCalcListItem
{
    public string QtnCalcNo { get; set; } = string.Empty;
    public DateTime QtnDate { get; set; }
    public string? CustomerCd { get; set; }
    public string? CustomerProductName1 { get; set; }
    public decimal? OrderQty { get; set; }
    public string? QtnBaseCd { get; set; }
    public string? StaffCd { get; set; }
    public string? QtnDiv { get; set; }
    public decimal? EstimateUnitPrice { get; set; }
    public DateTime CreateDate { get; set; }
    public DateTime? ModifyDate { get; set; }
}
