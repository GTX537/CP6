using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 在庫移動サービス。<see cref="Stock"/> テーブルへの全変動はここを経由する。
/// </summary>
/// <remarks>
/// 仕様書 §13.3 StockMovementService 中核メソッド。
/// 直接 _db.Stocks.Update() / Add() を呼ぶことは禁止。
/// </remarks>
public interface IStockMovementService
{
    /// <summary>
    /// 在庫変動 1 件を適用する（自動採番 + 在庫更新 + Txn 発行を 1 トランザクションで）。
    /// </summary>
    /// <param name="req">変動要求</param>
    /// <param name="ct">キャンセル</param>
    /// <returns>発行された TxnNo</returns>
    /// <exception cref="InsufficientStockException">利用可能在庫が不足し、マイナス許可なし</exception>
    Task<string> ApplyAsync(StockMovementRequest req, CancellationToken ct = default);

    /// <summary>
    /// 棚移動（MOVE）— 源 OUT と 先 IN を 1 ペアで発行。
    /// </summary>
    /// <returns>発行された TxnNo 2 件（[0]=源 OUT, [1]=先 IN）</returns>
    Task<(string OutTxnNo, string InTxnNo)> MoveAsync(StockMoveRequest req, CancellationToken ct = default);
}

/// <summary>
/// 在庫変動要求 DTO
/// </summary>
public class StockMovementRequest
{
    public string TxnType { get; set; } = string.Empty; // IN/OUT/ADJ/RSV/UNRSV
    public string WarehouseCd { get; set; } = string.Empty;
    public string LocationCd { get; set; } = string.Empty;
    public string ProductCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = ""; // ロット管理なしは空文字
    public decimal Qty { get; set; } // IN/OUT は正数、ADJ は符号付き差分
    public string? UnitCd { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? RelatedNo { get; set; }
    public string? RelatedType { get; set; }
    public string? OperatorCd { get; set; }
    public string? Remark { get; set; }
    public DateTime? ExpiryDate { get; set; } // IN 時のみ参照
    public DateTime? ReceiveDate { get; set; }
    public string OwnerType { get; set; } = StockOwnerType.Self;
    public string? OwnerCd { get; set; }
    public string? PaperRollNo { get; set; }

    /// <summary>
    /// マイナス在庫ガード上書き（既定 false）。true の場合、倉庫の <c>AllowNegative</c> 設定に関わらず
    /// マイナス在庫記帳を許可する。完工反冲（拍板①：不足でも報工を止めず負在庫＋告警）専用の逃がし弁。
    /// この標志はガード可否のみに作用し、他の在庫更新セマンティクスは一切変えない。
    /// </summary>
    public bool AllowNegativeOverride { get; set; } = false;
}

/// <summary>
/// 棚移動要求 DTO
/// </summary>
public class StockMoveRequest
{
    public string WarehouseCd { get; set; } = string.Empty;
    public string FromLocationCd { get; set; } = string.Empty;
    public string ToLocationCd { get; set; } = string.Empty;
    public string ProductCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = "";
    public decimal Qty { get; set; }
    public string? OperatorCd { get; set; }
    public string? Remark { get; set; }
}
