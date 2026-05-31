using CP6.Entity.DTOs.Mes;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Mes;

/// <summary>
/// <see cref="IMesBridgeHook"/> の標準実装。ERP 受注作成 → MES 製造指図 自動展開。
/// </summary>
/// <remarks>
/// 受注全明細を <see cref="IWorkOrderService.ExpandFromOrderAsync"/> で指図展開する。
/// 既に指図がある等の業務エラー（ME-MSG-005 等）は Skipped に変換し、受注作成は失敗させない。
/// </remarks>
public class MesBridgeHook : IMesBridgeHook
{
    private readonly IWorkOrderService _woService;
    private readonly ILogger<MesBridgeHook> _logger;

    public MesBridgeHook(IWorkOrderService woService, ILogger<MesBridgeHook> logger)
    {
        _woService = woService;
        _logger = logger;
    }

    public async Task<MesBridgeResult> OnOrderCreatedAsync(string webOrderNo, string? userName)
    {
        try
        {
            var nos = await _woService.ExpandFromOrderAsync(
                new ExpandFromOrderRequest { WebOrderNo = webOrderNo }, userName);
            _logger.LogInformation("[MES-Bridge] 受注 {Order} → 製造指図 {Count} 件自動展開（{Nos}）",
                webOrderNo, nos.Count, string.Join(",", nos));
            return MesBridgeResult.Ok(nos);
        }
        catch (InvalidOperationException ex)
        {
            // 既に指図あり／対象明細なし等の業務エラー
            _logger.LogWarning("[MES-Bridge] 受注 {Order} 指図展開スキップ: {Msg}", webOrderNo, ex.Message);
            return MesBridgeResult.Skipped(ex.Message);
        }
        catch (Exception ex)
        {
            // 想定外（DB 接続不可等）— 親の受注作成は失敗させない
            _logger.LogError(ex, "[MES-Bridge] 受注 {Order} 指図展開で予期せぬエラー", webOrderNo);
            return MesBridgeResult.Failed(ex.Message);
        }
    }
}
