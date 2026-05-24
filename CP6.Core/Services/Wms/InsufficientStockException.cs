namespace CP6.Core.Services.Wms;

/// <summary>
/// 在庫不足例外。OUT/RSV 時に AvailableQty が要求量を下回り、
/// かつ Warehouse.AllowNegative=false の場合に投げられる。
/// API 層は 400 + "WM-MSG-040" を返す。
/// </summary>
public class InsufficientStockException : InvalidOperationException
{
    public string ProductCd { get; }
    public string LotNo { get; }
    public decimal Requested { get; }
    public decimal Available { get; }

    public InsufficientStockException(string productCd, string lotNo, decimal requested, decimal available)
        : base($"WM-MSG-040: 製品[{productCd}]ロット[{lotNo}]の利用可能在庫が不足しています。必要={requested}, 在庫={available}")
    {
        ProductCd = productCd;
        LotNo = lotNo;
        Requested = requested;
        Available = available;
    }
}
