using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs.Mes;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Mes;

/// <summary>
/// <see cref="IMesBridgeHook"/> の標準実装。ERP 受注作成 → MES 製造指図 自動展開。
/// </summary>
/// <remarks>
/// Phase 6: <see cref="BridgeHookBase"/> 継承で IntegrationEvent 持久化を追加。
/// 受注全明細を <see cref="IWorkOrderService.ExpandFromOrderAsync"/> で指図展開する。
/// 既に指図がある等の業務エラー（ME-MSG-005 等）は Skipped に変換し、受注作成は失敗させない。
/// </remarks>
public class MesBridgeHook : BridgeHookBase, IMesBridgeHook
{
    private readonly IWorkOrderService _woService;

    public MesBridgeHook(CP6Context db, IWorkOrderService woService, ILogger<MesBridgeHook> logger)
        : base(db, logger)
    {
        _woService = woService;
    }

    public async Task<MesBridgeResult> OnOrderCreatedAsync(string webOrderNo, string? userName)
    {
        var corrId = Guid.NewGuid();
        var payload = new { webOrderNo, userName };
        try
        {
            var nos = await _woService.ExpandFromOrderAsync(
                new ExpandFromOrderRequest { WebOrderNo = webOrderNo }, userName);
            Logger.LogInformation("[MES-Bridge] 受注 {Order} → 製造指図 {Count} 件自動展開（{Nos}）",
                webOrderNo, nos.Count, string.Join(",", nos));
            await PersistEventAsync("ERP", "MES", nameof(OnOrderCreatedAsync),
                webOrderNo, nos.Count > 0 ? nos[0] : null,
                IntegrationEventStatus.Success, null, corrId, payload);
            return MesBridgeResult.Ok(nos);
        }
        catch (InvalidOperationException ex)
        {
            // 既に指図あり／対象明細なし等の業務エラー
            Logger.LogWarning("[MES-Bridge] 受注 {Order} 指図展開スキップ: {Msg}", webOrderNo, ex.Message);
            await PersistEventAsync("ERP", "MES", nameof(OnOrderCreatedAsync),
                webOrderNo, null, IntegrationEventStatus.Skipped, ex.Message, corrId, payload);
            return MesBridgeResult.Skipped(ex.Message);
        }
        catch (Exception ex)
        {
            // 想定外（DB 接続不可等）— 親の受注作成は失敗させない
            Logger.LogError(ex, "[MES-Bridge] 受注 {Order} 指図展開で予期せぬエラー", webOrderNo);
            await PersistEventAsync("ERP", "MES", nameof(OnOrderCreatedAsync),
                webOrderNo, null, IntegrationEventStatus.Failed, ex.ToString(), corrId, payload);
            return MesBridgeResult.Failed(ex.Message);
        }
    }
}
