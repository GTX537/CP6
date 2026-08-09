using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels;
using Microsoft.EntityFrameworkCore;
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
    /// <param name="error">失敗時の詳細。Space は安定した安全コードのみを許可する。</param>
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
        string? operatorUser = null,
        Guid? jobId = null,
        Guid? publishAttemptId = null)
    {
        IntegrationEvent? evt = null;
        try
        {
            var spaceNowUtc = sourceModule == "SPACE"
                ? DateTime.UtcNow
                : (DateTime?)null;
            evt = new IntegrationEvent
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
                    ? (spaceNowUtc ?? DateTime.UtcNow).AddSeconds(60)
                    : (DateTime?)null,
                CorrelationId = correlationId,
                JobId = jobId,
                PublishAttemptId = publishAttemptId,
                PayloadJson = SafeSerialize(payload),
                Creator = string.IsNullOrWhiteSpace(operatorUser) ? "system" : operatorUser,
                CreateDate = spaceNowUtc ?? DateTime.Now,
                OccurredAtUtc = spaceNowUtc,
            };
            Db.IntegrationEvents.Add(evt);
            await Db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // SaveChanges 失败后 Added 实体仍可能留在共享 ChangeTracker；
            // 必须先摘除，避免后续无关业务 Save 意外补写旧 Outbox。
            if (evt is not null)
            {
                try
                {
                    var entry = Db.Entry(evt);
                    if (entry.State != EntityState.Detached)
                        entry.State = EntityState.Detached;
                }
                catch
                {
                    // 清理失败也不得回显第二个异常；继续记录原始失败的安全分类。
                }
            }

            // 持久化自体の失敗は親 hook には伝播させない（ILogger に残すのみ）
            if (sourceModule == "SPACE")
            {
                var safe = SpaceErrorSanitizer.Classify(
                    ex,
                    "SPACE_OUTBOX_PERSIST_FAILED");
                Logger.LogError(
                    "[BridgeHookBase] Space event persistence failed {ReasonCode} {ErrorType} {Fingerprint} {Hook} {SourceNo} {CorrelationId}",
                    safe.ReasonCode,
                    safe.ExceptionType,
                    safe.Fingerprint,
                    hookName,
                    sourceNo,
                    correlationId);
            }
            else
            {
                Logger.LogError(
                    ex,
                    "[BridgeHookBase] IntegrationEvent persistence failed for {Hook} {SourceNo}",
                    hookName,
                    sourceNo);
            }
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
