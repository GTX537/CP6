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
    public const int PartialAllocated = 5; // 材料不足バックフローあり
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

/// <summary>
/// キット組立指示 方向（§24 / WM140）
/// </summary>
public static class KitOrderDirection
{
    public const string Assemble = "ASSEMBLE";       // 組立：部品 OUT + キット品 IN
    public const string Disassemble = "DISASSEMBLE"; // バラシ：キット品 OUT + 部品 IN
}

/// <summary>
/// キット組立指示 ステータス
/// </summary>
public static class KitOrderStatus
{
    public const int Draft = 0;       // 下書き
    public const int Executed = 1;    // 実行済
    public const int Cancelled = 9;   // 取消
}

/// <summary>
/// クロスドック ステータス（§23 / WM130）
/// </summary>
public static class CrossDockStatus
{
    public const int Planned = 0;    // 計画
    public const int Executed = 1;   // 実行済
    public const int Cancelled = 9;  // 取消
}

/// <summary>
/// 補充指示 ステータス（§22 / WM120）
/// </summary>
public static class ReplenishStatus
{
    public const int Pending = 0;    // 未実行
    public const int Executed = 1;   // 実行済
    public const int Cancelled = 9;  // 取消
}

/// <summary>
/// 補充トリガ種別
/// </summary>
public static class ReplenishTrigger
{
    public const string Batch = "BATCH";   // 日次バッチ自動
    public const string Manual = "MANUAL"; // 手動起票
    public const string Alert = "ALERT";   // リアルタイム警報
}

/// <summary>
/// スロッティング ステータス（§21 / WM110）
/// </summary>
public static class SlottingStatus
{
    public const int Analyzing = 0;     // 分析中
    public const int Recommended = 1;   // 推奨完了
    public const int Approved = 2;      // 承認済
    public const int Cancelled = 9;     // 取消
}

/// <summary>
/// ABC ランク
/// </summary>
public static class AbcRank
{
    public const string A = "A"; // 上位 80%（高頻度）→ 前棚/ピッキング棚
    public const string B = "B"; // 80~95%（中頻度）→ 中段
    public const string C = "C"; // 95~100%（低頻度）→ 上段・奥
}

/// <summary>
/// 原紙ロール 状态（§29 / WM200）
/// </summary>
public static class PaperRollStatus
{
    public const int InStock = 0;    // 在庫
    public const int InUse = 1;      // 使用中
    public const int Remnant = 2;    // 残米
    public const int Disposed = 3;   // 廃棄
}

/// <summary>
/// 原紙 流れ目
/// </summary>
public static class PaperGrainDirection
{
    public const string T = "T"; // T 目
    public const string Y = "Y"; // Y 目
}

/// <summary>
/// インキ 種別（§32 / WM230）
/// </summary>
public static class InkType
{
    public const string Offset = "OFFSET";   // オフセット
    public const string Flexo = "FLEXO";     // フレキソ
    public const string UV = "UV";           // UV
    public const string Other = "OTHER";
}

/// <summary>
/// インキ 開封状態
/// </summary>
public static class InkOpenStatus
{
    public const string Unopened = "UNOPENED";
    public const string Opened = "OPENED";
}

/// <summary>
/// パレット 状态（§33 / WM240）
/// </summary>
public static class PalletStatus
{
    public const int Building = 0;       // 組成中
    public const int InStock = 1;        // 保管中
    public const int WaitingShip = 2;    // 出荷待機
    public const int Shipped = 3;        // 出荷済
}

/// <summary>
/// 残材 状态（§30 / WM210）
/// </summary>
public static class RemnantStatus
{
    public const int Available = 0;   // 利用可
    public const int Reserved = 1;    // 予約済
    public const int Used = 2;        // 使用済
    public const int Disposed = 3;    // 廃棄
}

/// <summary>
/// 残材 素材区分
/// </summary>
public static class RemnantMaterialType
{
    public const string Paper = "PAPER";
    public const string Film = "FILM";
    public const string Other = "OTHER";
}

/// <summary>
/// 印版・木型 状态（§31 / WM220）
/// </summary>
public static class PlateMoldStatus
{
    public const int Usable = 0;         // 使用可
    public const int Maintenance = 1;    // メンテ中
    public const int LifeReached = 2;    // 寿命到達
    public const int Discarded = 3;      // 廃版
}

/// <summary>
/// 印版・木型 区分
/// </summary>
public static class PlateMoldType
{
    public const string Plate = "PLATE";       // 印版
    public const string Mold = "MOLD";         // 木型
    public const string Cylinder = "CYL";      // シリンダ
    public const string Other = "OTHER";
}

/// <summary>
/// サンプル品 状态（§34 / WM260）
/// </summary>
public static class SampleStatus
{
    public const int InStock = 0;     // 保管中
    public const int LentOut = 1;     // 貸出中
    public const int Returned = 2;    // 返却済
    public const int Expired = 3;     // 失効・破棄
}

/// <summary>
/// サンプル品 種別
/// </summary>
public static class SampleType
{
    public const string Prototype = "PROTO";   // 試作
    public const string ColorSwatch = "COLOR"; // 色見本
    public const string Dummy = "DUMMY";       // ダミー
    public const string Other = "OTHER";
}

/// <summary>
/// WCS タスク 状态（§40 / WM310）
/// </summary>
public static class WcsTaskStatus
{
    public const int Created    = 0;  // 作成
    public const int Dispatched = 1;  // 装置へ払出
    public const int Executing  = 2;  // 実行中
    public const int Completed  = 3;  // 完了
    public const int Failed     = 9;  // 失敗
}

/// <summary>WCS タスク種別</summary>
public static class WcsTaskType
{
    public const string Move  = "MOVE";   // 棚移動
    public const string Pick  = "PICK";   // ピッキング
    public const string Put   = "PUT";    // 棚入れ
    public const string Count = "COUNT";  // 棚卸
}

/// <summary>
/// 配送業者 シップメント 状态（§41 / WM320）
/// </summary>
public static class CarrierShipmentStatus
{
    public const int Created   = 0;  // 作成済
    public const int PickedUp  = 1;  // 集荷済
    public const int InTransit = 2;  // 配送中
    public const int Delivered = 3;  // 配達完了
    public const int Failed    = 9;  // 配達失敗
}

/// <summary>配送業者 区分</summary>
public static class CarrierCode
{
    public const string Yamato = "YAMATO";
    public const string Sagawa = "SAGAWA";
    public const string JpPost = "JP";
    public const string Self   = "SELF";
    public const string Other  = "OTHER";
}

/// <summary>
/// IoT センサ 種別（§42 / WM330）
/// </summary>
public static class IotSensorType
{
    public const string Temperature = "TEMP";   // 温度 ℃
    public const string Humidity    = "HUMID";  // 湿度 %
    public const string Shock       = "SHOCK";  // 衝撃 G
    public const string Shelf       = "SHELF";  // 棚センサ ON/OFF
}

/// <summary>
/// モバイル作業指示 ステータス（§37 / WM300）
/// </summary>
public static class MobileTaskStatus
{
    public const int Pending    = 0;  // 未着手
    public const int InProgress = 1;  // 進行中
    public const int Completed  = 2;  // 完了
    public const int Cancelled  = 9;  // 取消
}

/// <summary>モバイル作業指示 種別</summary>
public static class MobileTaskType
{
    public const string Receive = "RECEIVE";   // 入庫検品スキャン
    public const string Putaway = "PUTAWAY";   // 棚配置（製品+棚位 2スキャン）
    public const string Pick    = "PICK";      // ピッキング
    public const string Count   = "COUNT";     // 棚卸カウント
    public const string Move    = "MOVE";      // 棚移動
    public const string Label   = "LABEL";     // ラベル発行
}

/// <summary>モバイル スキャン解決種別（barcode resolve 結果）</summary>
public static class MobileScanKind
{
    public const string Location = "LOCATION";  // ロケーションCD と一致
    public const string Product  = "PRODUCT";   // 製品CD（在庫）と一致
    public const string Unknown  = "UNKNOWN";   // 解決不能
}
