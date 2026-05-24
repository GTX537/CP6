using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// RMA 返品管理サービス（MSBBWM150）
/// </summary>
/// <remarks>
/// 仕様書 §25。
/// ライフサイクル：0=申請 → 1=RMA発行 → 2=返品入庫 → 3=検査中 → 4=判定済 → 5=後処理完了 → 9=取消
/// </remarks>
public interface IRmaService
{
    Task<List<RmaHeader>> SearchAsync(RmaSearchQuery q);
    Task<RmaDto?> GetAsync(string rmaNo);

    /// <summary>RMA 申請受付（status=0→1 同時に番号発行）</summary>
    Task<string> CreateAsync(RmaDto dto, string? userName);

    /// <summary>返品入庫実行（status=1→2、各明細の IN トランザクション発行）</summary>
    Task ReceiveAsync(string rmaNo, string? userName);

    /// <summary>検査開始（status=2→3）</summary>
    Task StartInspectionAsync(string rmaNo, string? userName);

    /// <summary>明細ごとに判定をセット + 振分処理（status=3→4）</summary>
    Task JudgeAndDisposeAsync(string rmaNo, List<RmaDispositionInput> inputs, string? userName);

    /// <summary>後処理完了（status=4→5）</summary>
    Task CloseAsync(string rmaNo, string? userName);

    /// <summary>取消（status<5 のみ）</summary>
    Task CancelAsync(string rmaNo, string? userName);
}

public class RmaSearchQuery
{
    public string? RmaNo { get; set; }
    public string? CustomerCd { get; set; }
    public string? OriginalShippingNo { get; set; }
    public int? Status { get; set; }
    public DateTime? AppliedFrom { get; set; }
    public DateTime? AppliedTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class RmaDto
{
    public string? RmaNo { get; set; }
    public string CustomerCd { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? OriginalShippingNo { get; set; }
    public string? ReturnReason { get; set; }
    public DateTime AppliedDate { get; set; } = DateTime.Today;
    public string WarehouseCd { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? OperatorCd { get; set; }
    public string? Remarks { get; set; }
    public List<RmaDetailDto> Details { get; set; } = new();
}

public class RmaDetailDto
{
    public int LineNo { get; set; }
    public string ProductCd { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public string LotNo { get; set; } = string.Empty;
    public decimal Qty { get; set; }
    public string? UnitCd { get; set; }
    public string ConditionLevel { get; set; } = "OPEN";
    public string? Judgement { get; set; }
    public string? DestLocationCd { get; set; }
    public string? InboundTxnNo { get; set; }
    public string? DispositionTxnNo { get; set; }
    public string? Remarks { get; set; }
}

public class RmaDispositionInput
{
    public int LineNo { get; set; }
    /// <summary>RESELL / REPAIR / SCRAP / SUPPLIER_RETURN</summary>
    public string Judgement { get; set; } = string.Empty;
    /// <summary>振分先ロケーション（RESELL/REPAIR 時必須）</summary>
    public string? DestLocationCd { get; set; }
}
