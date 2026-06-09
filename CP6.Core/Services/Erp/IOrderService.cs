using CP6.Entity.DTOs;

namespace CP6.Core.Services.Erp;

/// <summary>
/// MSBBPA070 / 080 / 090 — Web 受注 業務サービス契約
/// </summary>
/// <remarks>
/// 5 表 1 次提交（T_Order / T_OrderDetail / T_OrderProcess / T_OrderProcessNote / T_OrderMaterial）
/// 業務 PK：WebOrderNo（シーケンス自動採番）
/// </remarks>
public interface IOrderService
{
    // ───── PA070 CRUD ─────

    /// <summary>受注ヘッダー＋全明細＋全工程＋全材料を 1 個の DTO で取得</summary>
    Task<OrderDto?> GetByWebOrderNoAsync(string webOrderNo, bool includeDeleted = false);

    /// <summary>登録（POST）— 採番後 5 表一括書込。返回新 WebOrderNo</summary>
    Task<string> CreateAsync(OrderDto dto, string? userName);

    /// <summary>訂正（PUT）— 5 表 diff 同期、楽観的ロック</summary>
    Task UpdateAsync(string webOrderNo, OrderDto dto, string? userName);

    /// <summary>削除（DELETE）— 軟削除</summary>
    Task DeleteAsync(string webOrderNo, byte[]? rowVersion, string? userName);

    /// <summary>
    /// 受注取消（Phase 6）— 関連 MES 指図 / WMS 出庫指示の反向級联含む。
    /// </summary>
    /// <param name="webOrderNo">受注NO</param>
    /// <param name="reason">取消理由（必須、監査用）</param>
    /// <param name="force">
    /// false=二段確認モード：半路状態あれば NeedsDecision を返し DB 未変更。
    /// true=強制実施モード：可能な全 WO/Outbound を取消（OrderCancelBridgeHook 経由）。
    /// </param>
    /// <param name="userName">操作者</param>
    /// <returns>状態機判定結果（Cancelled / NeedsDecision / Rejected / PartiallyCancelled）</returns>
    Task<OrderCancelResult> CancelAsync(string webOrderNo, string reason, bool force, string? userName);

    /// <summary>採番 — 次の WebOrderNo を発行</summary>
    Task<string> NextSequenceAsync();

    // ───── PA070 検索引入（仕様書 §5.x） ─────

    /// <summary>NO.1 製品CD検索 — セット製品CDの部材一覧を取得</summary>
    Task<List<OrderDetailDto>> LookupBySetProductCdAsync(string setProductCd);

    /// <summary>NO.2 手配NO検索 — 既存受注を 13+18 フィールドで取得</summary>
    Task<OrderDto?> LookupByHaibaiNoAsync(string? haibaiNo1, string? haibaiNo2, string? haibaiNo3);

    /// <summary>NO.3 製品基本マスタから第2画面 63項目引入（新規時）</summary>
    Task<OrderDetailDto?> LookupProductMasterForDetailAsync(string productCd);

    /// <summary>NO.4 製品加工工程マスタから第3画面 35項目引入（新規時）</summary>
    Task<List<OrderProcessDto>> LookupProductProcessesAsync(string productCd);

    /// <summary>NO.5 製品加工材料マスタから材料設定引入</summary>
    Task<List<OrderMaterialDto>> LookupProductMaterialsAsync(string productCd);

    // ───── PA070 業務ルール ─────

    /// <summary>isEditable 判定（受注画面制御マスタ：受注区分 × 製品区分 × 品目採番）</summary>
    Task<IsEditableResultDto> CheckIsEditableAsync(string orderType, string? productCatBig, string? productCd);

    /// <summary>仕掛チェック（L2/L3）— 訂正時のみ</summary>
    Task<OrderWipCheckResultDto> CheckWipAsync(string webOrderNo, int webOrderDetailNo);

    /// <summary>与信限度額チェック</summary>
    Task<CreditCheckResultDto> CheckCreditAsync(string customerCd, decimal newOrderAmount);

    /// <summary>預り売上数チェック（Rev4：預り売上数 ≤ 受注残数）</summary>
    Task<bool> CheckConsignedSalesQtyAsync(string webOrderNo, int webOrderDetailNo, decimal consignedSalesQty);

    /// <summary>加工予定日計算（出荷日時から各工程 LT を営業日逆算）</summary>
    Task<List<DateTime>> CalcLeadTimeAsync(string shipDateTime, IEnumerable<int> leadTimeDays, string? wgCd);

    // ───── PA080 受注一覧照会 ─────

    /// <summary>多条件検索（40+ パラメータ）</summary>
    Task<(List<OrderListItemDto> Rows, int Total)> SearchOrdersAsync(OrderQueryDto query);

    /// <summary>CSV 出力（現在の検索条件全件）</summary>
    Task<byte[]> ExportListCsvAsync(OrderQueryDto query);

    // ───── PA090 単価訂正 ─────

    /// <summary>単価訂正対象一覧</summary>
    Task<(List<OrderPriceCorrectionItemDto> Rows, int Total)> SearchPriceCorrectionsAsync(OrderPriceCorrectionQueryDto query);

    /// <summary>単価訂正バッチ更新（POWER EGG WF 起票も実行）</summary>
    Task<OrderPriceCorrectionBatchResultDto> BatchUpdatePriceAsync(OrderPriceCorrectionBatchUpdateDto request, string? userName);

    /// <summary>金額リアルタイム計算（PA090 行内編集用）</summary>
    Task<decimal> CalcAmountAsync(string webOrderNo, int detailNo, decimal? newIndPrice, decimal? newSetPrice);

    // ───── PA070 共通処理（仕様書 §処理詳細） ─────

    /// <summary>製品区分自動設定（共通処理「製品区分設定」）— 工程の最終機械から判定</summary>
    Task<(string? CatBig, string? CatMid, string? CatSml)> CalcProductCategoryAsync(string productCd, IEnumerable<OrderProcessDto> processes);

    /// <summary>材料設定計算（共通処理「材料設定」）— BOM 展開して受注材料に変換</summary>
    Task<List<OrderMaterialDto>> CalcMaterialsAsync(string productCd, IEnumerable<OrderProcessDto> processes);

    /// <summary>客先納期 vs 工程 LT 合計 チェック（仕様書 §7.1 0-1）</summary>
    Task<(bool Ok, int LtSumDays, string? Message)> CheckDeliveryLeadTimeAsync(
        string productCd, DateTime? orderDate, DateTime? deliveryDate);

    // ───── PA080 帳票 ─────

    /// <summary>受注伝票 PDF 発行（仕様書 §PA080 API）</summary>
    Task<byte[]> ExportOrderReportPdfAsync(IEnumerable<string> webOrderDetailKeys);
}
