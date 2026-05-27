using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// WCS（倉庫制御）サービス（MSBBWM310）
/// </summary>
/// <remarks>
/// 仕様書 §40。実機（AGV/コンベヤ/ASRS）との連携は将来追加。
/// 当面は手動 dispatch + 手動 complete をエミュレート。
/// </remarks>
public interface IWcsService
{
    Task<List<WcsTask>> SearchAsync(WcsTaskSearchQuery q);
    Task<WcsTask?> GetAsync(string taskNo);
    Task<string> CreateAsync(WcsTaskDto dto, string? userName);

    /// <summary>払出：0→1（装置CD 紐付）</summary>
    Task DispatchAsync(string taskNo, string deviceCd, string? userName);
    /// <summary>実行開始：1→2</summary>
    Task StartAsync(string taskNo, string? userName);
    /// <summary>完了：2→3</summary>
    Task CompleteAsync(string taskNo, string? userName);
    /// <summary>失敗：→9（理由付）</summary>
    Task FailAsync(string taskNo, string errorMessage, string? userName);

    Task DeleteAsync(string taskNo, string? userName);
}

public class WcsTaskSearchQuery
{
    public string? TaskNo { get; set; }
    public string? TaskType { get; set; }
    public string? DeviceCd { get; set; }
    public int? Status { get; set; }
    public string? RelatedNo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class WcsTaskDto
{
    public string TaskType { get; set; } = "MOVE";
    public int Priority { get; set; } = 1;
    public string? RelatedNo { get; set; }
    public string? RelatedType { get; set; }
    public string? FromWarehouseCd { get; set; }
    public string? FromLocationCd { get; set; }
    public string? ToWarehouseCd { get; set; }
    public string? ToLocationCd { get; set; }
    public string? ProductCd { get; set; }
    public string? LotNo { get; set; }
    public decimal? Qty { get; set; }
    public string? UnitCd { get; set; }
    public string? Remarks { get; set; }
}
