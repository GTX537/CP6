using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Erp;

/// <summary>
/// 木型・版型管理マスタ（PA140）— ★★★ 最複雑マスタ
/// </summary>
/// <remarks>
/// 仕様書 §9 MAM_木型版型管理マスタ
/// 業務 PK：(WdPtnNo, WdRev) — 1 版型 N Rev で版型履歴管理
/// 改定時は最大 Rev+1 で新レコード追加、前 Rev の適用終了日を新適用開始日 -1 にセット。
/// 登録/改定/訂正時に「版型受注連携」（T_Order + T_OrderDetail 118 項）+ PE API 連携（53 項）を実行。
/// </remarks>
[Table("T_PlateMold")]
public class PlateMold : BaseBizEntity
{
    /// <summary>木型・版型 NO（PK1, 自動採番）</summary>
    [Required, MaxLength(40)]
    public string WdPtnNo { get; set; } = string.Empty;

    /// <summary>Rev（PK2, 登録=1, 改定=最大Rev+1）</summary>
    public int WdRev { get; set; } = 1;

    [Required, MaxLength(20)] public string BaseCd { get; set; } = string.Empty;

    /// <summary>決定見積 NO</summary>
    [Required, MaxLength(20)] public string DecisionEstimateNo { get; set; } = string.Empty;

    /// <summary>版型名</summary>
    [Required, MaxLength(100)] public string VrsnName { get; set; } = string.Empty;

    /// <summary>売上可否区分：1=可 / 2=不可 / 3=商品込 / 4=社内処理 / 5=後報</summary>
    [MaxLength(4)] public string? EarningsCd { get; set; }

    /// <summary>手配 NO（受注連携後に更新）</summary>
    [MaxLength(20)] public string? ArrangeNo { get; set; }

    /// <summary>適用開始日</summary>
    public DateTime StDate { get; set; }
    /// <summary>適用終了日</summary>
    public DateTime EndDate { get; set; }

    /// <summary>得意先 CD（取引先マスタ 得意先 FLG=1）</summary>
    [Required, MaxLength(20)] public string CustomerCd { get; set; } = string.Empty;

    /// <summary>代表製品 CD</summary>
    [Required, MaxLength(20)] public string RepresentativeProductCd { get; set; } = string.Empty;

    [MaxLength(20)] public string? NewVerCd { get; set; }
    /// <summary>工程</summary>
    [MaxLength(20)] public string? ProcessCd { get; set; }
    /// <summary>版型分類（工程→自動セット：0300=ゴム型 / 0450=オフ型 / 0600=木型 / 0650=箔版）</summary>
    [MaxLength(20)] public string? TypeClass { get; set; }
    [MaxLength(20)] public string? EndPlaceCd { get; set; }
    /// <summary>実績日（最終使用日） — 別テーブルから引入のため画面入力なし</summary>
    public DateTime? AchieveDate { get; set; }

    /// <summary>シート段（Rev3.0：製品マスタ参照に変更 — スナップショット）</summary>
    [MaxLength(4)]  public string? SheetFlute { get; set; }

    // 構成情報（製品マスタからスナップショット）
    [MaxLength(20)] public string? PaperCdF { get; set; }
    [MaxLength(20)] public string? PrintCdF { get; set; }
    [MaxLength(20)] public string? EmbossCdF { get; set; }
    [MaxLength(20)] public string? MakerCdF { get; set; }
    [MaxLength(20)] public string? PaperCdC { get; set; }
    [MaxLength(20)] public string? PrintCdC { get; set; }
    [MaxLength(20)] public string? EmbossCdC { get; set; }
    [MaxLength(20)] public string? MakerCdC { get; set; }
    [MaxLength(20)] public string? PaperCdB { get; set; }
    [MaxLength(20)] public string? PrintCdB { get; set; }
    [MaxLength(20)] public string? EmbossCdB { get; set; }
    [MaxLength(20)] public string? MakerCdB { get; set; }

    /// <summary>抜方向</summary>
    [MaxLength(20)] public string? ExtractionDirect { get; set; }
    /// <summary>複版（FLG）</summary>
    public bool DuplicatePlateFlg { get; set; }

    [Column(TypeName = "decimal(15,4)")] public decimal SheetWidth { get; set; }
    [Column(TypeName = "decimal(15,4)")] public decimal SheetFlow { get; set; }
    [Column(TypeName = "decimal(15,4)")] public decimal BladeWidth { get; set; }
    [Column(TypeName = "decimal(15,4)")] public decimal BladeFlow { get; set; }

    /// <summary>付数（面付け）</summary>
    public int CompositionQty { get; set; }
    /// <summary>個数（≥1）</summary>
    public int MfgQty { get; set; } = 1;
    public int ColorQty { get; set; }
    public int BookQty { get; set; }

    [Column(TypeName = "decimal(15,4)")] public decimal? FlexoThick { get; set; }
    [Column(TypeName = "decimal(15,4)")] public decimal? CylinderSize { get; set; }

    /// <summary>発注先 CD（取引先マスタ 発注先 FLG=1）</summary>
    [Required, MaxLength(20)] public string SupplierCd { get; set; } = string.Empty;

    [MaxLength(20)] public string? ProcessDestination { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public DateTime? ArrivalActualDate { get; set; }

    [Column(TypeName = "decimal(15,4)")] public decimal EstimateAmount { get; set; }
    [Column(TypeName = "decimal(15,4)")] public decimal DecisionAmount { get; set; }
    [Column(TypeName = "decimal(15,4)")] public decimal PurchaseAmount { get; set; }

    public DateTime? SalesDate { get; set; }
    [MaxLength(20)] public string? StrippingCd { get; set; }
    [MaxLength(20)] public string? StandingCd { get; set; }
    [MaxLength(20)] public string? HammerCd { get; set; }
    [MaxLength(20)] public string? OversightCd { get; set; }

    /// <summary>場所（システム管理；画面より変更不可）</summary>
    [MaxLength(20)] public string? PlaceCd { get; set; }
    /// <summary>棚・ライン（システム管理）</summary>
    [MaxLength(20)] public string? ShelfLineCd { get; set; }

    /// <summary>通し数（バッチがカウントアップ；画面より変更不可）</summary>
    public int WdQty { get; set; }
    /// <summary>限界通し数</summary>
    public int LimitWdQty { get; set; }
    public DateTime? DispScheduleDate { get; set; }
    public DateTime? ReturnScheduleDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    [MaxLength(300)] public string? ReturnReason { get; set; }

    [MaxLength(300)] public string? Memo { get; set; }
    /// <summary>売上備考</summary>
    [MaxLength(300)] public string? SalesNote { get; set; }
    /// <summary>廃棄実績日（仕様 §4 No.78〜80）</summary>
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

    // 必要物（9 フィールド）
    public int NeedDraft { get; set; }
    public int NeedMylar { get; set; }
    public int NeedGalley { get; set; }
    public int NeedProof { get; set; }
    public int NeedBlueprint { get; set; }
    public int NeedComp { get; set; }
    public int NeedDesignSheet { get; set; }
    [MaxLength(50)] public string? NeedOtherName { get; set; }
    [MaxLength(50)] public string? NeedOther { get; set; }

    // mc 連携キー
    [MaxLength(20)] public string? McOrderNo { get; set; }
    [MaxLength(20)] public string? McOrderDetailNo { get; set; }

    /// <summary>ステータス：0=未転送 / 9=mc 転送済</summary>
    public int Status { get; set; }
    public bool McTransferFlg { get; set; }
}
