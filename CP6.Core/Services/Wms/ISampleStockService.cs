using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// サンプル品 在庫・貸出サービス（MSBBWM260）
/// </summary>
/// <remarks>
/// 仕様書 §34。試作・色見本等の貸出/返却管理。
/// 状态遷移：0=保管中 → 1=貸出中 → 2=返却済 ; 3=失効/破棄
/// </remarks>
public interface ISampleStockService
{
    Task<List<SampleStock>> SearchAsync(SampleSearchQuery q);
    Task<SampleStock?> GetAsync(string sampleNo);
    Task<string> CreateAsync(SampleDto dto, string? userName);
    Task UpdateAsync(string sampleNo, SampleDto dto, string? userName);

    /// <summary>貸出：0→1</summary>
    Task LendAsync(string sampleNo, string lentTo, DateTime? expectedReturnDate, string? userName);

    /// <summary>返却：1→2</summary>
    Task ReturnAsync(string sampleNo, string? userName);

    /// <summary>失効/破棄：→3</summary>
    Task ExpireAsync(string sampleNo, string? userName);

    /// <summary>未返却（期限超）一覧</summary>
    Task<List<SampleStock>> OverdueAsync();

    Task DeleteAsync(string sampleNo, string? userName);
}

public class SampleSearchQuery
{
    public string? SampleNo { get; set; }
    public string? SampleType { get; set; }
    public string? CustomerCd { get; set; }
    public string? ProductCd { get; set; }
    public int? Status { get; set; }
    public bool? OverdueOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class SampleDto
{
    public string SampleType { get; set; } = "PROTO";
    public string? CustomerCd { get; set; }
    public string? CustomerName { get; set; }
    public string? ProductCd { get; set; }
    public string? ProductName { get; set; }
    public decimal Quantity { get; set; } = 1m;
    public string UnitCd { get; set; } = "PCS";
    public string? WarehouseCd { get; set; }
    public string? LocationCd { get; set; }
    public string? Remarks { get; set; }
}
