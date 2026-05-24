namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// 在庫トランザクション種別
/// </summary>
/// <remarks>
/// 仕様書 §1.4 在庫トランザクション種別。
/// IN/OUT/MOVE/ADJ は物理在庫を増減、RSV/UNRSV は引当数のみ動かす。
/// </remarks>
public static class WmsTxnType
{
    /// <summary>入庫</summary>
    public const string IN = "IN";
    /// <summary>出庫</summary>
    public const string OUT = "OUT";
    /// <summary>棚移動（源 OUT + 先 IN を1ペアで発行）</summary>
    public const string MOVE = "MOVE";
    /// <summary>調整（棚卸差異等）</summary>
    public const string ADJ = "ADJ";
    /// <summary>引当（AvailableQty を減らす、PhysicalQty 不変）</summary>
    public const string RSV = "RSV";
    /// <summary>引当解除</summary>
    public const string UNRSV = "UNRSV";
}

/// <summary>
/// 倉庫区分（仕様書 §2.3）
/// </summary>
public static class WarehouseType
{
    public const int RawMaterial = 1;   // 原材料
    public const int WorkInProcess = 2; // 半製品
    public const int Finished = 3;      // 完成品
    public const int Defective = 4;     // 不良品
    public const int External = 5;      // 外注
}

/// <summary>
/// 在庫所有者区分（VMI 対応、§28.4）
/// </summary>
public static class StockOwnerType
{
    public const string Self = "SELF";
    public const string Customer = "CUSTOMER";
}

/// <summary>
/// 入庫予定ステータス（§4 / WM030）
/// </summary>
public static class InboundOrderStatus
{
    public const int Draft = 0;        // 下書き
    public const int Confirmed = 1;    // 確定済
    public const int PartialReceived = 2; // 入庫中（部分入庫）
    public const int Completed = 3;    // 完了（全数入庫）
    public const int Cancelled = 9;    // 取消
}

/// <summary>
/// 入庫実績ステータス（§5 / WM040）
/// </summary>
public static class InboundReceiptStatus
{
    public const int Draft = 0;       // 下書き（StockTxn 未発行）
    public const int Confirmed = 1;   // 確定済（StockTxn 発行済）
    public const int Cancelled = 9;   // 取消（逆仕訳済）
}

/// <summary>
/// 入庫実績 入庫元区分
/// </summary>
public static class InboundSourceType
{
    public const string Purchase = "PURCHASE";     // 購買入庫
    public const string Production = "PRODUCTION"; // MES完成品（WM060）
    public const string Rma = "RMA";               // 返品受入
    public const string Manual = "MANUAL";         // 直入（予定なし）
}

/// <summary>
/// 出庫指示ステータス（§6 / WM050 + WM070 共有）
/// </summary>
public static class OutboundOrderStatus
{
    public const int Draft = 0;       // 下書き
    public const int Confirmed = 1;   // 確定済
    public const int Allocated = 2;   // 引当済
    public const int Picking = 3;     // ピッキング中
    public const int Completed = 4;   // 出庫完了
    public const int Cancelled = 9;   // 取消
}

/// <summary>
/// 出庫区分
/// </summary>
public static class OutboundType
{
    public const int Material = 1;     // 材料出庫（MES 製造指図向け）
    public const int Shipping = 2;     // 出荷（客先向け）
    public const int InternalTransfer = 3; // 社内振替
    public const int Other = 9;
}

/// <summary>
/// 棚卸ステータス（§10 / WM090）
/// </summary>
public static class StockTakeStatus
{
    public const int Planned = 0;        // 計画（スナップショット済、カウント未着手）
    public const int Counting = 1;       // カウント中
    public const int DiffReview = 2;     // 差異確認中
    public const int AwaitingApproval = 3; // 承認待ち（差異あり且つ閾値超）
    public const int Completed = 4;      // 完了（ADJ 反映済）
    public const int Cancelled = 9;      // 取消
}

/// <summary>
/// 棚卸区分
/// </summary>
public static class StockTakeType
{
    public const int Full = 1;       // 全棚卸
    public const int Cycle = 2;      // サイクル棚卸（一部範囲）
    public const int AdHoc = 3;      // 臨時
}

/// <summary>
/// 棚卸明細 承認ステータス
/// </summary>
public static class StockTakeDetailApproval
{
    public const int Pending = 0;     // 未承認
    public const int AutoApproved = 1; // 自動承認（差異 0）
    public const int Approved = 2;     // 承認済
    public const int Rejected = 9;     // 却下
}

/// <summary>
/// 入荷検品 ステータス（§20 / WM100）
/// </summary>
public static class QcInspectionStatus
{
    public const int Created = 0;     // 作成（明細入力中）
    public const int Inspecting = 1;  // 検品中
    public const int Judged = 2;      // 判定済（後続処理完了）
    public const int Cancelled = 9;   // 取消
}

/// <summary>
/// 入荷検品 最終判定
/// </summary>
public static class QcInspectionJudgement
{
    public const string Pass = "PASS";              // 合格 → 正規倉庫へ自動入庫
    public const string Conditional = "CONDITIONAL"; // 条件付合格 → 保留倉庫
    public const string Hold = "HOLD";              // 保留 → QC 保留倉庫
    public const string Fail = "FAIL";              // 不合格 → 不良品倉庫
    public const string Return = "RETURN";          // 即返品（書類のみ）
}

/// <summary>
/// RMA 返品 ステータス（§25 / WM150）
/// </summary>
public static class RmaStatus
{
    public const int Applied = 0;           // 申請受付
    public const int Authorized = 1;        // RMA番号発行済
    public const int Received = 2;          // 返品入庫済
    public const int Inspecting = 3;        // 検査中
    public const int Judged = 4;            // 判定済
    public const int Closed = 5;            // 後処理完了
    public const int Cancelled = 9;         // 取消
}

/// <summary>
/// RMA 商品状態
/// </summary>
public static class RmaCondition
{
    public const string New = "NEW";        // 新品未使用
    public const string Open = "OPEN";      // 開封済
    public const string Damaged = "DAMAGED"; // 破損
}

/// <summary>
/// RMA 判定（明細単位の振分）
/// </summary>
public static class RmaJudgement
{
    public const string Resell = "RESELL";                 // 再販可 → 正規倉庫へ
    public const string Repair = "REPAIR";                 // 要修理 → 修理倉庫へ
    public const string Scrap = "SCRAP";                   // 廃棄 → ADJ で除去
    public const string SupplierReturn = "SUPPLIER_RETURN"; // 仕入先返品
}
