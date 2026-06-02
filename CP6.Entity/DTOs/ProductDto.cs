namespace CP6.Entity.DTOs;

/// <summary>製品マスタ操作种别（MSBBPA050 §1.3 操作モード）</summary>
public enum ProductOperationType
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
}

/// <summary>
/// 製品マスタ详情 DTO（GET / POST / PUT 共通）
/// 5 表 1 次提交，扁平化为 5 个 List
/// </summary>
public class ProductDto
{
    /// <summary>製品コード（PK、新規時は空、サーバ採番）</summary>
    public string? ProductCd { get; set; }

    public string? ItemCd { get; set; }
    public string? Branch1 { get; set; }
    public string? Branch2 { get; set; }
    public string? Branch3 { get; set; }

    /// <summary>第1页：部材一覧（多行 = 多製品 CD = 1セット内の各部材行）</summary>
    public List<ProductMemberDto> Members { get; set; } = new();

    /// <summary>第2页：基本情報（70+ フィールド）</summary>
    public ProductBasicInfoDto BasicInfo { get; set; } = new();

    /// <summary>第3页：工程情報</summary>
    public List<ProductProcessDto> Processes { get; set; } = new();

    /// <summary>第3页 Popup：連産品（工程ごとの行）</summary>
    public List<ProductCoProductDto> CoProducts { get; set; } = new();

    /// <summary>第4页：材料設定</summary>
    public List<ProductMaterialDto> Materials { get; set; } = new();

    /// <summary>第5页：ロット別単価</summary>
    public List<ProductLotPriceDto> LotPrices { get; set; } = new();

    /// <summary>ステータス：0=承認待ち / 1=承認済み / 9=mc転送済</summary>
    public int Status { get; set; }

    /// <summary>ワークフロー承認FLG</summary>
    public bool WfApprovalFlg { get; set; }

    /// <summary>mcframe7 転送FLG</summary>
    public bool McTransferFlg { get; set; }

    /// <summary>乐观锁（Base64 ROWVERSION）</summary>
    public byte[]? RowVersion { get; set; }
}

/// <summary>第1页 部材一覧の1行</summary>
public class ProductMemberDto
{
    /// <summary>行番（1〜N）</summary>
    public int RowNo { get; set; }
    public string? ProductCd { get; set; }
    public string? EstimateCalcNo { get; set; }
    public string? QuotationNo { get; set; }
    public string? ProjectNoParent { get; set; }
    public string? ProjectNoChild { get; set; }
    public string? ProjectNoMaterial { get; set; }
    public string? CustomerProductName1 { get; set; }
    public string? CustomerProductName2 { get; set; }
    /// <summary>0=未作成 / 1=承認待ち / 9=承認済 or mc転送済</summary>
    public int Status { get; set; }
    public bool WfApproved { get; set; }
    public bool MasterLinked { get; set; }
}

/// <summary>第2页 基本情報（製品基本マスタ ≒ 1:1）</summary>
public class ProductBasicInfoDto
{
    public string CustomerCd { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string SetProductCd { get; set; } = string.Empty;
    public string? SetProductName { get; set; }
    public string ParentChildDiv { get; set; } = "0";

    public string? CustomerItemName1 { get; set; }
    public string? CustomerItemName2 { get; set; }
    public string? CustomerPartNo { get; set; }
    public string? CpItemName1 { get; set; }
    public string? CpItemName2 { get; set; }
    public string? JanCode { get; set; }
    public decimal SetRatio { get; set; } = 1m;

    public string? ProductUsage { get; set; }
    public string? DistributionDiv { get; set; }
    public string? ConfidentialInfo { get; set; }
    public string? SeizureDiv { get; set; }
    public string? ImportanceDiv { get; set; }
    public string? MChange { get; set; }
    public string? QualityDiv { get; set; }
    public string? ShipInspection { get; set; }
    public string? ProductShape { get; set; }
    public string? UnescoMark { get; set; }
    public string? OrigamiMark { get; set; }
    public string? FourMContract { get; set; }
    public string? TkpWrinkleStd { get; set; }
    public string? FoodSafety { get; set; }
    public string? AdShape { get; set; }

    /// <summary>戦略商品区分 01〜10（10 要素 bool 配列）</summary>
    public bool[] StrategicDivs { get; set; } = new bool[10];

    public string? FscProductDiv { get; set; }
    public string? FscMaterialDiv { get; set; }
    public string? FscManagementNo { get; set; }
    public string? RecyclingPayment { get; set; }
    public string? IdMark { get; set; }

    public decimal? PaperUsageG { get; set; }
    public decimal? PlasticUsageG { get; set; }
    public decimal? GlassUsageG { get; set; }
    public decimal? PetUsageG { get; set; }
    public decimal? PackPaperUsageG { get; set; }
    public decimal? PackPlasticUsageG { get; set; }

    public string? DesignProposalNo { get; set; }
    public string? OrderType { get; set; }
    public string? SheetFlute { get; set; }

    public string? PaperCdF { get; set; }
    public string? PrintCdF { get; set; }
    public string? EmbossCdF { get; set; }
    public string? MakerCdF { get; set; }
    public string? PaperCdC { get; set; }
    public string? PrintCdC { get; set; }
    public string? EmbossCdC { get; set; }
    public string? PaperCdB { get; set; }
    public string? PrintCdB { get; set; }
    public string? EmbossCdB { get; set; }
    public string? MakerCdB { get; set; }
    public string? SheetPrint { get; set; }

    public decimal? BladeWidth { get; set; }
    public decimal? BladeFlow { get; set; }
    public decimal? GutterFb { get; set; }
    public decimal? GutterLr { get; set; }
    public decimal? SheetDimW { get; set; }
    public decimal? SheetDimF { get; set; }
    public string? FinalMachineProcess { get; set; }

    public string? PrintNote { get; set; }
    public string? MfgNote { get; set; }
    public string? SlipNote { get; set; }
    public string? DeliveryNote { get; set; }
    public string? ShipNote1 { get; set; }
    public string? ShipNote2 { get; set; }

    public string? ProductCatBig { get; set; }
    public string? ProductCatMid { get; set; }
    public string? ProductCatSml { get; set; }

    public string SalesPriceDiv { get; set; } = "2";
    public string? QtyUnit { get; set; }
    public string? UnitPriceUnit { get; set; }
    public string? PurchaseVendor { get; set; }
    public string FreightBilling { get; set; } = "0";
    public string? FixedShipment { get; set; }
    public decimal? DeliveryReserve { get; set; }
    public decimal? SalesSample { get; set; }

    /// <summary>受注後変更不可（数量単位/単価単位）— サーバ判定で true 時 readOnly</summary>
    public bool IsReadOnlyByOrder { get; set; }
}

/// <summary>第3页 工程行</summary>
public class ProductProcessDto
{
    public string TaskCd { get; set; } = string.Empty;
    public string ProcessCd { get; set; } = string.Empty;
    public string? TopItemCd { get; set; }
    public string? TopBranch1 { get; set; }
    public string? TopBranch2 { get; set; }
    public string? TopBranch3 { get; set; }
    public string? ItemCd { get; set; }
    public string? Branch1 { get; set; }
    public string? Branch2 { get; set; }
    public string? Branch3 { get; set; }
    public string? WgCd { get; set; }
    public string? MachineOrVendor { get; set; }
    public bool MachineFixedFlg { get; set; }
    public string? CpDeliveryDiv { get; set; }

    /// <summary>工程仕様 01〜10（10 要素文字列配列）</summary>
    public string?[] Specs { get; set; } = new string?[10];

    public string? PlateNo1 { get; set; }
    public string? PlateNo2 { get; set; }
    public string? PlateNo3 { get; set; }
    public string? Consumable1 { get; set; }
    public string? Consumable2 { get; set; }
    public string? Consumable3 { get; set; }

    public decimal? PurchasePrice { get; set; }
    public decimal? FixedPrice { get; set; }
    public decimal? LossRate { get; set; }
    public decimal? MachineCount { get; set; }
    public decimal? LeadTime { get; set; }
    public string? ProcessNote1 { get; set; }
    public string? ProcessNote2 { get; set; }
    public string? StorageDest { get; set; }
    public int SortOrder { get; set; }

    /// <summary>製造順優先項目 1〜8（生産並び順、サーバ自動計算）</summary>
    public string?[] ManufOrderPrios { get; set; } = new string?[8];
}

/// <summary>第4页 材料行</summary>
public class ProductMaterialDto
{
    public string ProcessCd { get; set; } = string.Empty;
    public string MaterialCd { get; set; } = string.Empty;
    public string MaterialTypeDiv { get; set; } = "1";
    public string? ItemCd { get; set; }
    public string? Branch1 { get; set; }
    public string? Branch2 { get; set; }
    public string? Branch3 { get; set; }
    public string? SupplyDiv { get; set; }
    public decimal? SupplyPrice { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>第5页 ロット別単価行</summary>
public class ProductLotPriceDto
{
    public int DetailNo { get; set; }
    public string? ItemCd { get; set; }
    public string? Branch1 { get; set; }
    public string? Branch2 { get; set; }
    public string? Branch3 { get; set; }
    public DateTime? CurrentPriceDate { get; set; }
    public DateTime? NewPriceDate { get; set; }
    public DateTime? CurrentBranchDate { get; set; }
    public DateTime? NewBranchDate { get; set; }
    public decimal LotQty { get; set; }
    public decimal? CurrentSetPrice { get; set; }
    public decimal? NewSetPrice { get; set; }
    public decimal? CurrentUnitPrice { get; set; }
    public decimal? NewUnitPrice { get; set; }
    public decimal? CurrentBranchPrice { get; set; }
    public decimal? NewBranchPrice { get; set; }
    public decimal? PurchasePrice { get; set; }
    public string? PriceApplyBasis { get; set; }
}

/// <summary>第3页 Popup 連産品行</summary>
public class ProductCoProductDto
{
    public string ProcessCd { get; set; } = string.Empty;
    public int RowNo { get; set; }
    public string? CoProductName { get; set; }
    public decimal QtyRatio { get; set; }
    public string? NextProcessCd { get; set; }
}

/// <summary>MSBBPA060 製品マスタ一覧 用 DTO（軽量）</summary>
public class ProductListItemDto
{
    public string ProductCd { get; set; } = string.Empty;
    public string? SetProductCd { get; set; }
    public string? SetProductName { get; set; }
    public string? CustomerCd { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerItemName1 { get; set; }
    public string? CustomerItemName2 { get; set; }
    public string? ProjectNoParent { get; set; }
    public string? ProjectNoChild { get; set; }
    public string? QuotationNo { get; set; }
    public string? EstimateCalcNo { get; set; }
    public int Status { get; set; }
    public bool WfApprovalFlg { get; set; }
    public bool McTransferFlg { get; set; }
    public DateTime? ModifyDate { get; set; }
}

/// <summary>製品マスタ検索条件</summary>
public class ProductQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? ProductCdFrom { get; set; }
    public string? ProductCdTo { get; set; }
    public string? CustomerCd { get; set; }
    public string? SetProductCd { get; set; }
    public string? ProjectNoParent { get; set; }
    public string? ProjectNoChild { get; set; }
    public string? QuotationNo { get; set; }
    public string? EstimateCalcNo { get; set; }
    public string? CustomerItemName1 { get; set; }
    public string? CustomerItemName2 { get; set; }
    public string? DesignProposalNo { get; set; }
    public DateTime? ModifyDateFrom { get; set; }
    public DateTime? ModifyDateTo { get; set; }
    public List<int>? Statuses { get; set; }

    /// <summary>排序列（前端 el-table prop，白名单内才生效）</summary>
    public string? SortField { get; set; }
    /// <summary>排序方向：asc / desc</summary>
    public string? SortOrder { get; set; }
}

/// <summary>仕掛チェック結果（10章）</summary>
public class WipCheckResultDto
{
    /// <summary>0=未登録/問題なし / 1=警告（続行可） / 2=エラー（生産予定確定）/ 3=エラー（指図済）</summary>
    public int Level { get; set; }

    /// <summary>関連手配NO一覧（メッセージ埋め込み用）</summary>
    public List<string> WipHandleNumbers { get; set; } = new();

    /// <summary>メッセージID（MSG-1/MSG-2/MSG-3）</summary>
    public string? MsgId { get; set; }

    public string? Message { get; set; }
}
