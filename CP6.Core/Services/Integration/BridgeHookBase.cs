using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Integration;

/// <summary>
/// Bridge Hook 基底クラス（Phase 6 持久化基盤）
/// </summary>
/// <remarks>
/// 設計：
///   - 既存 hook の try/catch 構造（業務例外 → Skipped、技術例外 → Failed）は保持
///   - 各分岐の末尾で <see cref="PersistEventAsync"/> を呼び <see cref="IntegrationEvent"/> を書き残す
///   - 失敗時は <see cref="IntegrationEvent.NextRetryAt"/> を設定 → Worker が自動リトライ
///
/// 既存外部インタフェース（<see cref="IWmsBridgeHook"/> 等）は変更しない。
/// </remarks>
public abstract class BridgeHookBase
{
    protected readonly CP6Context Db;
    protected readonly ILogger Logger;

    protected BridgeHookBase(CP6Context db, ILogger logger)
    {
        Db = db;
        Logger = logger;
    }

    /// <summary>
    /// IntegrationEvent を書き残す（Phase 6）
    /// </summary>
    /// <param name="sourceModule">ERP / MES / WMS</param>
    /// <param name="targetModule">ERP / MES / WMS</param>
    /// <param name="hookName">呼び出し元メソッド名（nameof 推奨）</param>
    /// <param name="sourceNo">源業務番号</param>
    /// <param name="targetNo">目標業務番号（Success 時のみ）</param>
    /// <param name="status"><see cref="IntegrationEventStatus"/> 取値</param>
    /// <param name="error">失敗時の詳細（Skipped 時は理由、Failed 時は ToString()）</param>
    /// <param name="correlationId">端到端 trace 用 GUID</param>
    /// <param name="payload">入力 payload（重試時に Dispatcher が反序列化）</param>
    /// <param name="operatorUser">操作者（審計 T4 / spec §8）：呼び出し元の userName を透過して <see cref="IntegrationEvent.Creator"/> に落とす。
    /// null/空白なら "system" に退避（operator を持たない存量桥の後方互換）。</param>
    /// <remarks>
    /// 失敗時のリトライ間隔は固定 60s で初期化（Worker が指数退避で更新）。
    /// Persistence 自体が失敗しても親 hook には伝播させない（best-effort 強化）。
    /// </remarks>
    protected async Task PersistEventAsync(
        string sourceModule,
        string targetModule,
        string hookName,
        string sourceNo,
        string? targetNo,
        string status,
        string? error,
        Guid correlationId,
        object payload,
        string? operatorUser = null)
    {
        try
        {
            var evt = new IntegrationEvent
            {
                Id = Guid.NewGuid(),
                SourceModule = sourceModule,
                TargetModule = targetModule,
                HookName = hookName,
                SourceNo = sourceNo,
                TargetNo = targetNo,
                Status = status,
                Attempts = 1,
                LastError = error,
                NextRetryAt = status == IntegrationEventStatus.Failed
                    ? DateTime.UtcNow.AddSeconds(60)
                    : (DateTime?)null,
                CorrelationId = correlationId,
                PayloadJson = SafeSerialize(payload),
                Creator = string.IsNullOrWhiteSpace(operatorUser) ? "system" : operatorUser,
                CreateDate = DateTime.Now,
            };
            Db.IntegrationEvents.Add(evt);
            await Db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // 持久化自体の失敗は親 hook には伝播させない（ILogger に残すのみ）
            Logger.LogError(ex,
                "[BridgeHookBase] IntegrationEvent persistence failed for {Hook} {SourceNo}",
                hookName, sourceNo);
        }
    }

    private static string SafeSerialize(object payload)
    {
        try
        {
            return JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = false,
                MaxDepth = 8,
            });
        }
        catch
        {
            return "{\"_error\":\"serialization_failed\"}";
        }
    }
}
