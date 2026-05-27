using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// クロスドッキング サービス（MSBBWM130）
/// </summary>
/// <remarks>
/// 仕様書 §23。
/// 1. Plan：到着品を仮置きロケーションに置く前提で予定を作成
/// 2. Execute：IN（仮置きロケ）→ OUT（同ロケ、出荷ドック）を 1 トランザクションで発行
///    在庫の滞留時間≒0 となる（IN と OUT のタイムスタンプが連続）
/// </remarks>
public interface ICrossDockService
{
    Task<List<CrossDockOrder>> SearchAsync(CrossDockSearchQuery q);
    Task<CrossDockOrder?> GetAsync(string xdockNo);
    Task<string> CreateAsync(CrossDockOrderDto dto, string? userName);
    Task ExecuteAsync(string xdockNo, string? userName);
    Task CancelAsync(string xdockNo, string? userName);
}

public class CrossDockSearchQuery
{
    public string? XDockNo { get; set; }
    public string? ProductCd { get; set; }
    public string? InboundNo { get; set; }
    public string? OutboundNo { get; set; }
    public int? Status { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class CrossDockOrderDto
{
    public string? InboundNo { get; set; }
    public string? OutboundNo { get; set; }
    public string ProductCd { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public decimal Qty { get; set; }
    public string? SupplierCd { get; set; }
    public string? CustomerCd { get; set; }
    public string? FromDock { get; set; }
    public string? ToDock { get; set; }
    public string WarehouseCd { get; set; } = string.Empty;
    public string TempLocationCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public string? OperatorCd { get; set; }
    public string? Remarks { get; set; }
}
