using CP6.Entity.DTOs;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Erp;

/// <summary>
/// MSBBPA140/150 — Web 版型/木型マスタ業務サービス契約
/// </summary>
public interface IPlateMoldService
{
    /// <summary>版型 NO で検索（Rev 指定なし=最新Rev / 指定ありはその Rev）— 仕様書 §4 NO.2</summary>
    Task<PlateMoldDto?> GetByNoAsync(string wdPtnNo, int? rev = null);

    /// <summary>決定見積 NO で検索（基本情報＋構成情報を引入）— 仕様書 §3 NO.1</summary>
    Task<PlateMoldDto?> GetByEstimateNoAsync(string decisionEstimateNo);

    /// <summary>代表製品CD で製品マスタを参照し構成情報（段/原紙/印刷/エンボス/メーカ F・C・B）を引入 — 仕様書 §3 NO.1 第2ブロック（Rev3.0 で木型・版型マスタ→製品マスタ参照に変更）</summary>
    Task<PlateMoldDto?> GetCompositionByProductAsync(string productCd);

    /// <summary>過去履歴一覧（版型 NO の全 Rev）</summary>
    Task<List<PlateMoldHistoryItemDto>> GetHistoryAsync(string wdPtnNo);

    /// <summary>新規登録（Rev=1, 版型 NO 自動採番）— 受注連携 + PE API も実行</summary>
    Task<(string WdPtnNo, int WdRev, PlateMoldOrderLinkResultDto OrderLink, PlateMoldPeApiResultDto PeApi)>
        CreateAsync(PlateMoldDto dto, string? userName);

    /// <summary>改定（最大Rev+1 で新レコード INSERT、前 Rev の適用終了日を新適用開始日 -1 に更新）</summary>
    Task<(int NewRev, PlateMoldOrderLinkResultDto OrderLink, PlateMoldPeApiResultDto PeApi)>
        ReviseAsync(string wdPtnNo, PlateMoldDto dto, string? userName);

    /// <summary>訂正（最新 Rev のみ更新；楽観ロック）</summary>
    Task UpdateAsync(string wdPtnNo, int wdRev, PlateMoldDto dto, string? userName);

    /// <summary>削除（管理者のみ；論理削除）</summary>
    Task DeleteAsync(string wdPtnNo, int wdRev, byte[]? rowVersion, string? userName);

    /// <summary>採番</summary>
    Task<string> NextSequenceAsync();

    // ─── PA150 ───
    Task<(List<PlateMoldListItemDto> Rows, int Total)> SearchAsync(PlateMoldQueryDto query);
    Task<byte[]> ExportListCsvAsync(PlateMoldQueryDto query);
    /// <summary>ラベル発行 CSV（仕様書 §4 ファイル入出力仕様 NO.2）</summary>
    Task<byte[]> IssueLabelCsvAsync(IEnumerable<(string WdPtnNo, int WdRev)> targets);
}

/// <summary>
/// 版型発注書 PE API（仕様書 §8 — 53 項）
/// 環境無し時は NoOp。実環境では HTTP 実装に DI で差し替え。
/// </summary>
public interface IPlateMoldPeApiService
{
    Task<PlateMoldPeApiResultDto> SendAsync(PlateMoldDto dto, CancellationToken ct = default);

    /// <summary>版型発注書の削除連携（削除モード時）。実環境では削除区分付きで PE へ送信。</summary>
    Task<PlateMoldPeApiResultDto> SendDeleteAsync(PlateMoldDto dto, CancellationToken ct = default);
}

public class NoOpPlateMoldPeApiService : IPlateMoldPeApiService
{
    private readonly Microsoft.Extensions.Logging.ILogger<NoOpPlateMoldPeApiService> _logger;
    public NoOpPlateMoldPeApiService(Microsoft.Extensions.Logging.ILogger<NoOpPlateMoldPeApiService> logger) { _logger = logger; }
    public Task<PlateMoldPeApiResultDto> SendAsync(PlateMoldDto dto, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[PE-API stub] 版型発注書 拠点={BaseCd} 得意先={Customer} 発注先={Supplier} 版型NO={WdPtnNo}/Rev{Rev} " +
            "決定価格={Decision} 仕入価格={Purchase} 納期={Delivery}",
            dto.BaseCd, dto.CustomerCd, dto.SupplierCd, dto.WdPtnNo, dto.WdRev,
            dto.DecisionAmount, dto.PurchaseAmount, dto.DeliveryDate);
        return Task.FromResult(new PlateMoldPeApiResultDto { Ok = true, Message = "PE-API stub 送信成功（53 項）" });
    }

    public Task<PlateMoldPeApiResultDto> SendDeleteAsync(PlateMoldDto dto, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[PE-API stub] 版型発注書【削除】 拠点={BaseCd} 得意先={Customer} 版型NO={WdPtnNo}/Rev{Rev}",
            dto.BaseCd, dto.CustomerCd, dto.WdPtnNo, dto.WdRev);
        return Task.FromResult(new PlateMoldPeApiResultDto { Ok = true, Message = "PE-API stub 削除送信成功" });
    }
}
