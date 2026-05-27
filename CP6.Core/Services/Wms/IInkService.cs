using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// インキ・接着剤サービス（MSBBWM230）
/// </summary>
/// <remarks>
/// 仕様書 §32。
/// 混合：旧 2 ロット OUT + 新「混合ロット」1 IN（賞味期限はより早い方を継承）
/// </remarks>
public interface IInkService
{
    Task<List<InkLot>> SearchLotsAsync(InkLotSearchQuery q);
    Task<InkLot?> GetLotAsync(string inkLotNo);
    Task<string> CreateLotAsync(InkLotDto dto, string? userName);

    /// <summary>開封：UNOPENED → OPENED、開封時にこの ExpiryDate を上書き可（例：30日後）</summary>
    Task OpenLotAsync(string inkLotNo, DateTime? newExpiryDate, string? userName);

    /// <summary>ロット混合：2 ロットを統合し新ロット作成（賞味期限は最早を継承）</summary>
    Task<string> MixLotsAsync(MixInkRequest req, string? userName);

    /// <summary>色合せ履歴 — 客先×色番号で検索</summary>
    Task<List<InkColorMatchHistory>> SearchMatchesAsync(string? customerCd, string? colorCode);

    /// <summary>色合せ実績登録</summary>
    Task<string> RecordMatchAsync(InkColorMatchDto dto, string? userName);
}

public class InkLotSearchQuery
{
    public string? InkLotNo { get; set; }
    public string? ColorCode { get; set; }
    public string? InkType { get; set; }
    public string? OpenStatus { get; set; }
    public bool? ExpiringWithin30Days { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class InkLotDto
{
    public string ColorCode { get; set; } = string.Empty;
    public string InkType { get; set; } = "OFFSET";
    public DateTime ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public string UnitCd { get; set; } = "kg";
    public decimal? ViscosityCp { get; set; }
    public decimal? SolidContent { get; set; }
    public string? LocationCd { get; set; }
    public string? SupplierCd { get; set; }
    public string? Remarks { get; set; }
}

public class MixInkRequest
{
    public string ParentLotNoA { get; set; } = string.Empty;
    public decimal ParentQtyA { get; set; }
    public string ParentLotNoB { get; set; } = string.Empty;
    public decimal ParentQtyB { get; set; }
    public string? NewColorCode { get; set; } // 空なら ParentA を継承
    public string? NewLocationCd { get; set; }
    public string? Remarks { get; set; }
}

public class InkColorMatchDto
{
    public string CustomerCd { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string ColorCode { get; set; } = string.Empty;
    public string? FormulaJson { get; set; }
    public decimal ConsumedQty { get; set; }
    public string? OperatorCd { get; set; }
    public string? Remarks { get; set; }
}
