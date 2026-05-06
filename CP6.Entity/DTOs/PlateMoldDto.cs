namespace CP6.Entity.DTOs;

/// <summary>PA140 操作種別（5 種）</summary>
public enum PlateMoldOperationType
{
    /// <summary>登録（Rev=1, 新規版型 NO 自動採番）</summary>
    Register = 10,
    /// <summary>改定（Rev=最大Rev+1, 前 Rev の適用終了日を自動更新）</summary>
    Revise = 20,
    /// <summary>訂正（最新 Rev のみ更新）</summary>
    Edit = 30,
    /// <summary>削除（管理者のみ；論理削除）</summary>
    Delete = 40,
    /// <summary>参照</summary>
    View = 50,
}

/// <summary>PA140 版型・木型 1 件 DTO</summary>
public class PlateMoldDto
{
    public string? WdPtnNo { get; set; }
    public int WdRev { get; set; } = 1;

    public string BaseCd { get; set; } = string.Empty;
    public string? BaseName { get; set; }
    public string DecisionEstimateNo { get; set; } = string.Empty;
    public string VrsnName { get; set; } = string.Empty;
    public string? EarningsCd { get; set; }
    public string? ArrangeNo { get; set; }
    public DateTime StDate { get; set; }
    public DateTime EndDate { get; set; }
    public string CustomerCd { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string RepresentativeProductCd { get; set; } = string.Empty;
    public string? RepresentativeProductName1 { get; set; }
    public string? RepresentativeProductName2 { get; set; }
    public string? NewVerCd { get; set; }
    public string? ProcessCd { get; set; }
    public string? TypeClass { get; set; }
    public string? EndPlaceCd { get; set; }
    public DateTime? AchieveDate { get; set; }

    // 構成情報スナップショット
    public string? SheetFlute { get; set; }
    public string? PaperCdF { get; set; }
    public string? PrintCdF { get; set; }
    public string? EmbossCdF { get; set; }
    public string? MakerCdF { get; set; }
    public string? PaperCdC { get; set; }
    public string? PrintCdC { get; set; }
    public string? EmbossCdC { get; set; }
    public string? MakerCdC { get; set; }
    public string? PaperCdB { get; set; }
    public string? PrintCdB { get; set; }
    public string? EmbossCdB { get; set; }
    public string? MakerCdB { get; set; }

    public string? ExtractionDirect { get; set; }
    public bool DuplicatePlateFlg { get; set; }

    public decimal SheetWidth { get; set; }
    public decimal SheetFlow { get; set; }
    public decimal BladeWidth { get; set; }
    public decimal BladeFlow { get; set; }

    public int CompositionQty { get; set; }
    public int MfgQty { get; set; } = 1;
    public int ColorQty { get; set; }
    public int BookQty { get; set; }

    public decimal? FlexoThick { get; set; }
    public decimal? CylinderSize { get; set; }

    public string SupplierCd { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public string? ProcessDestination { get; set; }
    public string? ProcessDestinationName { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public DateTime? ArrivalActualDate { get; set; }

    public decimal EstimateAmount { get; set; }
    public decimal DecisionAmount { get; set; }
    public decimal PurchaseAmount { get; set; }

    public DateTime? SalesDate { get; set; }
    public string? StrippingCd { get; set; }
    public string? StandingCd { get; set; }
    public string? HammerCd { get; set; }
    public string? OversightCd { get; set; }

    public string? PlaceCd { get; set; }
    public string? ShelfLineCd { get; set; }
    public int WdQty { get; set; }
    public int LimitWdQty { get; set; }
    public DateTime? DispScheduleDate { get; set; }
    public DateTime? ReturnScheduleDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string? ReturnReason { get; set; }
    public string? Memo { get; set; }
    public string? SalesNote { get; set; }
    public DateTime? DispActualDate { get; set; }

    // 添付情報（8 種 CheckBox）
    public bool AtachInfoSheetFront { get; set; }
    public bool AtachInfoSheetBack { get; set; }
    public bool AtachInfoActual { get; set; }
    public bool AtachInfoBaseplate { get; set; }
    public bool AtachInfoPositive { get; set; }
    public bool AtachInfoNegative { get; set; }
    public bool AtachInfoMo { get; set; }
    public bool AtachInfoFd { get; set; }

    // 必要物
    public int NeedDraft { get; set; }
    public int NeedMylar { get; set; }
    public int NeedGalley { get; set; }
    public int NeedProof { get; set; }
    public int NeedBlueprint { get; set; }
    public int NeedComp { get; set; }
    public int NeedDesignSheet { get; set; }
    public string? NeedOtherName { get; set; }
    public string? NeedOther { get; set; }

    public int Status { get; set; }
    public bool McTransferFlg { get; set; }
    public string? McOrderNo { get; set; }
    public string? McOrderDetailNo { get; set; }

    public byte[]? RowVersion { get; set; }
}

public class PlateMoldHistoryItemDto
{
    public string WdPtnNo { get; set; } = string.Empty;
    public int WdRev { get; set; }
    public DateTime StDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? SupplierCd { get; set; }
    public string? SupplierName { get; set; }
    public int WdQty { get; set; }
    public DateTime? DispScheduleDate { get; set; }
    public DateTime? ReturnScheduleDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string? ReturnReason { get; set; }
}

// ─────────────── PA150 一覧 ───────────────

public class PlateMoldQueryDto
{
    public string? BaseCd { get; set; }
    public string? StaffCd { get; set; }
    public DateTime? StDateFrom { get; set; }
    public DateTime? StDateTo { get; set; }
    public string? ArrangeNo { get; set; }
    public string? ProjectNo { get; set; }
    public string? WdPtnNoFrom { get; set; }
    public string? WdPtnNoTo { get; set; }
    public string? VrsnName { get; set; }
    public string? TypeClass { get; set; }
    public string? NewVerCd { get; set; }
    public string? CustomerCd { get; set; }
    public string? RepresentativeProductCd { get; set; }
    public string? ProcessCd { get; set; }
    public int? WdQtyFrom { get; set; }
    public int? WdQtyTo { get; set; }
    public string? SupplierCd { get; set; }
    public int? LimitWdQtyFrom { get; set; }
    public int? LimitWdQtyTo { get; set; }
    public string? PlaceCd { get; set; }
    public string? ShelfLineCd { get; set; }
    public DateTime? AchieveDateFrom { get; set; }
    public DateTime? AchieveDateTo { get; set; }
    public DateTime? ArrivalDateFrom { get; set; }
    public DateTime? ArrivalDateTo { get; set; }
    public DateTime? DispScheduleFrom { get; set; }
    public DateTime? DispScheduleTo { get; set; }
    public DateTime? ReturnScheduleFrom { get; set; }
    public DateTime? ReturnScheduleTo { get; set; }
    public DateTime? ReturnDateFrom { get; set; }
    public DateTime? ReturnDateTo { get; set; }
    /// <summary>true=最新Rev のみ表示（既定）</summary>
    public bool? OnlyLatestRev { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public int? MaxRows { get; set; }
}

public class PlateMoldListItemDto
{
    public int RowNo { get; set; }
    /// <summary>UI: 発行☑</summary>
    public bool Issue { get; set; }
    public string WdPtnNo { get; set; } = string.Empty;
    public string? VrsnName { get; set; }
    public int WdRev { get; set; }
    public string? TypeClass { get; set; }
    public string? CustomerCd { get; set; }
    public string? CustomerName { get; set; }
    public string? RepresentativeProductCd { get; set; }
    public string? RepresentativeProductName { get; set; }
    public int WdQty { get; set; }
    public int LimitWdQty { get; set; }
    public DateTime? AchieveDate { get; set; }
    public string? SupplierCd { get; set; }
    public string? SupplierName { get; set; }
    public DateTime? ArrivalActualDate { get; set; }
    public DateTime? DispScheduleDate { get; set; }
    public DateTime? ReturnScheduleDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string? ReturnReason { get; set; }
    public string? ProjectNo { get; set; }
    public string? ProjectName { get; set; }
    public string? ProcessCd { get; set; }
    public string? ProcessName { get; set; }
    public string? PlaceCd { get; set; }
    public string? PlaceName { get; set; }
    public string? ShelfLineCd { get; set; }
    public string? ShelfLineName { get; set; }
    public string? NewVerCd { get; set; }
    public string? EndPlaceCd { get; set; }
    public string? EndPlaceName { get; set; }
    public decimal? BladeWidth { get; set; }
    public decimal? BladeFlow { get; set; }
    public int? CompositionQty { get; set; }
    public int? MfgQty { get; set; }
    public int? ColorQty { get; set; }
    public string? StrippingCd { get; set; }
    public string? StandingCd { get; set; }
    public string? HammerCd { get; set; }
    public string? OversightCd { get; set; }
    public string? Memo { get; set; }
}

/// <summary>PA140 §7 版型受注連携結果（118 項）</summary>
public class PlateMoldOrderLinkResultDto
{
    public string WebOrderNo { get; set; } = string.Empty;
    public int WebOrderDetailNo { get; set; }
    public string ArrangeNo { get; set; } = string.Empty;
}

/// <summary>PA140 §8 PE API 連携結果（53 項）</summary>
public class PlateMoldPeApiResultDto
{
    public bool Ok { get; set; }
    public string? Message { get; set; }
}
