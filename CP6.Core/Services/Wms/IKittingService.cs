using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// キット組立・バラシ サービス（MSBBWM140）
/// </summary>
/// <remarks>
/// 仕様書 §24 キッティング・組立。
/// マスタ：KitSku + 構成部品 BOM。
/// 指示：ASSEMBLE（部品 OUT × N + キット品 IN × 1）
///       DISASSEMBLE（キット品 OUT × 1 + 部品 IN × N）
/// 1 指示の実行は全ステップ成功 or 全失敗（StockMovementService の例外で巻き戻し）。
/// </remarks>
public interface IKittingService
{
    // ─── マスタ ───
    Task<List<KitMaster>> SearchMastersAsync(string? keyword);
    Task<KitMasterDto?> GetMasterAsync(string kitSku);
    Task CreateMasterAsync(KitMasterDto dto, string? userName);
    Task UpdateMasterAsync(string kitSku, KitMasterDto dto, string? userName);
    Task DeleteMasterAsync(string kitSku, string? userName);

    // ─── 指示 ───
    Task<List<KitOrder>> SearchOrdersAsync(KitOrderSearchQuery q);
    Task<KitOrder?> GetOrderAsync(string kitOrderNo);
    Task<string> CreateOrderAsync(KitOrderDto dto, string? userName);

    /// <summary>
    /// 指示実行（ASSEMBLE/DISASSEMBLE）。
    /// ASSEMBLE：部品 OUT × N + キット品 IN × 1。
    /// DISASSEMBLE：キット品 OUT × 1 + 部品 IN × N。
    /// 部品 OUT は FIFO 引当（在庫不足は InsufficientStockException）。
    /// </summary>
    Task ExecuteAsync(string kitOrderNo, string? userName);

    Task CancelAsync(string kitOrderNo, string? userName);
}

public class KitMasterDto
{
    public string KitSku { get; set; } = string.Empty;
    public string KitName { get; set; } = string.Empty;
    public string? DefaultWarehouseCd { get; set; }
    public string? Remarks { get; set; }
    public bool ActiveFlg { get; set; } = true;
    public List<KitMasterComponentDto> Components { get; set; } = new();
}

public class KitMasterComponentDto
{
    public int LineNo { get; set; }
    public string ComponentProductCd { get; set; } = string.Empty;
    public string? ComponentName { get; set; }
    public decimal RequiredQty { get; set; }
    public string? UnitCd { get; set; }
    public string? Remarks { get; set; }
}

public class KitOrderDto
{
    public string KitSku { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public string Direction { get; set; } = KitOrderDirection.Assemble;
    public string WarehouseCd { get; set; } = string.Empty;
    public string KitLocationCd { get; set; } = string.Empty;
    public string? KitLotNo { get; set; }
    public string? OperatorCd { get; set; }
    public string? Remarks { get; set; }
}

public class KitOrderSearchQuery
{
    public string? KitOrderNo { get; set; }
    public string? KitSku { get; set; }
    public string? Direction { get; set; }
    public int? Status { get; set; }
    public DateTime? ExecutedFrom { get; set; }
    public DateTime? ExecutedTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
