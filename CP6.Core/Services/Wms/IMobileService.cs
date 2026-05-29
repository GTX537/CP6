using CP6.Entity.DomainModels.Wms;

namespace CP6.Core.Services.Wms;

/// <summary>
/// モバイル作業指示サービス（MSBBWM300）
/// </summary>
/// <remarks>
/// 仕様書 §37 RFハンディ・モバイルWMS。
/// 現場端末向けの 3 プリミティブ：作業一覧取得 / バーコードスキャン解決 / 作業完了。
/// スキャンはロケーションCD or 製品CD（在庫）を解決し、現品コンテキストを返す。
/// MOVE 種別の完了時のみ <see cref="IStockMovementService"/> 経由で実在庫を動かす。
/// </remarks>
public interface IMobileService
{
    /// <summary>作業者の指示一覧（未着手・進行中を優先）</summary>
    Task<List<MobileTask>> GetTasksAsync(MobileTaskQuery q);

    Task<MobileTask?> GetAsync(string mobileTaskNo);

    /// <summary>作業指示を新規発行（PC 側 or バッチからの配信）</summary>
    Task<string> CreateAsync(MobileTaskDto dto, string? userName);

    /// <summary>作業開始（status=0→1、StartedAt 記録）</summary>
    Task StartAsync(string mobileTaskNo, string? userName);

    /// <summary>
    /// バーコード 1 件を解決する。ロケーションCD/バーコード一致 → LOCATION、
    /// 製品CD/在庫一致 → PRODUCT、それ以外 → UNKNOWN。
    /// taskNo を渡すと、その指示の期待値との照合結果（matched）も返す。
    /// </summary>
    Task<MobileScanResult> ScanAsync(MobileScanRequest req);

    /// <summary>作業完了。MOVE は MOVE トランザクション一対を発行、他は状態のみ更新。</summary>
    Task CompleteAsync(string mobileTaskNo, MobileCompleteRequest req, string? userName);

    Task CancelAsync(string mobileTaskNo, string? userName);
}

public class MobileTaskQuery
{
    public string? AssignedTo { get; set; }
    public string? TaskType { get; set; }
    public int? Status { get; set; }
    /// <summary>未完了（未着手 + 進行中）のみ</summary>
    public bool OpenOnly { get; set; } = false;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class MobileTaskDto
{
    public string TaskType { get; set; } = MobileTaskType.Pick;
    public string? AssignedTo { get; set; }
    public int Priority { get; set; } = 2;
    public string? RelatedNo { get; set; }
    public string? RelatedType { get; set; }
    public string? ProductCd { get; set; }
    public string? ProductName { get; set; }
    public string? LotNo { get; set; }
    public string? WarehouseCd { get; set; }
    public string? FromLocationCd { get; set; }
    public string? ToLocationCd { get; set; }
    public decimal Qty { get; set; }
    public string? UnitCd { get; set; }
    public string? Instruction { get; set; }
    public string? Remarks { get; set; }
}

public class MobileScanRequest
{
    /// <summary>スキャンした生バーコード文字列</summary>
    public string Barcode { get; set; } = string.Empty;
    /// <summary>照合対象の作業指示NO（任意）</summary>
    public string? TaskNo { get; set; }
    /// <summary>絞り込み倉庫（任意）</summary>
    public string? WarehouseCd { get; set; }
}

public class MobileScanResult
{
    /// <summary>LOCATION / PRODUCT / UNKNOWN</summary>
    public string Kind { get; set; } = MobileScanKind.Unknown;
    public string Barcode { get; set; } = string.Empty;

    // LOCATION 解決時
    public string? LocationCd { get; set; }
    public string? LocationName { get; set; }
    public bool? IsBlocked { get; set; }

    // PRODUCT 解決時
    public string? ProductCd { get; set; }

    /// <summary>解決対象の現在在庫（製品×ロケ×ロット 単位、上位 20 件）</summary>
    public List<MobileStockLine> Stocks { get; set; } = new();

    /// <summary>taskNo 照合時：期待値と一致したか</summary>
    public bool? Matched { get; set; }
    public string? Message { get; set; }
}

public class MobileStockLine
{
    public string WarehouseCd { get; set; } = string.Empty;
    public string LocationCd { get; set; } = string.Empty;
    public string ProductCd { get; set; } = string.Empty;
    public string LotNo { get; set; } = string.Empty;
    public decimal PhysicalQty { get; set; }
    public decimal AvailableQty { get; set; }
    public string? UnitCd { get; set; }
    public DateTime? ExpiryDate { get; set; }
}

public class MobileCompleteRequest
{
    /// <summary>実績数量（カウント結果 / 実ピック数 等）。未指定なら指示数量を使用。</summary>
    public decimal? ScannedQty { get; set; }
    /// <summary>PUTAWAY/MOVE の確定先（指示の ToLocationCd を上書き）</summary>
    public string? ToLocationCd { get; set; }
    public string? Remarks { get; set; }
}
