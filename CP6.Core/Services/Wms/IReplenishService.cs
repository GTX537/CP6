using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// 補充指示サービス（MSBBWM120）
/// </summary>
/// <remarks>
/// 仕様書 §22。
/// GenerateAsync：指定倉庫の "ピッキング棚（接頭辞 PIK-）" 在庫を一覧取得、
/// MinQty を割っているものに対して "保管棚（接頭辞 RES-）" から補充する指示を一括生成。
/// 第一版は接頭辞ベースの簡易実装（後で T_Location に LocationKind 列を追加して厳密化可）。
/// ExecuteAsync：保管棚 OUT + ピッキング棚 IN（MOVE ペア）。
/// </remarks>
public interface IReplenishService
{
    Task<List<ReplenishOrder>> SearchAsync(ReplenishSearchQuery q);
    Task<ReplenishOrder?> GetAsync(string replenishNo);
    Task<string> CreateAsync(ReplenishOrderDto dto, string? userName);

    /// <summary>
    /// 指定倉庫の補充必要 SKU を一括生成（バッチ）。
    /// 第一版：LocationCd LIKE 'PIK-%' を ピッキング棚、'RES-%' を 保管棚 とみなす。
    /// minQty 未満 + 同 ProductCd で RES- に在庫あり の組合せで指示作成。
    /// </summary>
    Task<int> GenerateBatchAsync(string warehouseCd, decimal minQty, string? userName);

    Task ExecuteAsync(string replenishNo, string? userName);
    Task CancelAsync(string replenishNo, string? userName);
}

public class ReplenishSearchQuery
{
    public string? ReplenishNo { get; set; }
    public string? ProductCd { get; set; }
    public string? WarehouseCd { get; set; }
    public int? Status { get; set; }
    public int? Priority { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class ReplenishOrderDto
{
    public int Priority { get; set; } = 2;
    public string ProductCd { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public string FromLocationCd { get; set; } = string.Empty;
    public string ToLocationCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public string? UnitCd { get; set; }
    public string TriggerType { get; set; } = "MANUAL";
    public string? OperatorCd { get; set; }
    public string? Remarks { get; set; }
}
