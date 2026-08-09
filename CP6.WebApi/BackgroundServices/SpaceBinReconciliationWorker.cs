using System.Diagnostics;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Space;
using CP6.Core.Services.Space.Observability;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// Space↔WMS 库位对账后台服务（波5）。每日扫描已发布库位（Status=1）与其 WMS 消费 bin 的
/// 停用漂移（bin.IsActive=false），只读不自愈，仅记录每租户的脱敏摘要供人工核查。
/// 照 <see cref="FinReconciliationWorker"/> 同构：启动后延迟首跑，之后每 24h 一次，
/// 经 <see cref="TenantScopeRunner"/> 逐租户作用域扫描。
/// </summary>
public class SpaceBinReconciliationWorker : BackgroundService
{
    private const string WorkerActor = "space-worker:bin-reconciliation";
    private const string ReconciliationAction = "space.reconciliation.scan";
    private const string ReconciliationResourceType = "SpaceBin";
    private const string AuditFailureReason =
        "SPACE_RECONCILIATION_AUDIT_WRITE_FAILED";
    private const string ScanFailureReason =
        "SPACE_RECONCILIATION_SCAN_FAILED";
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly SpaceSafeError AuditRejectedError =
        SpaceErrorSanitizer.Classify(
            new AuditWriteRejectedException(),
            AuditFailureReason);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SpaceBinReconciliationWorker> _logger;

    public SpaceBinReconciliationWorker(IServiceScopeFactory scopeFactory, ILogger<SpaceBinReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Space 库位对账 worker 启动");
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await ProcessOnceAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _logger.LogInformation("Space 库位对账 worker 停止");
        }
    }

    /// <summary>跑一次对账并记录结果——按租户循环：每租户独立作用域扫描其 Space↔WMS 漂移。
    /// 单租户扫描异常在本 Worker 内审计并脱敏记录，不影响其余租户；宿主取消仍向上传播。</summary>
    public async Task ProcessOnceAsync(CancellationToken ct = default)
    {
        await TenantScopeRunner.ForEachTenantAsync(_scopeFactory, async (sp, tenantId, c) =>
        {
            var parentTraceId = ActivityTraceId.CreateRandom();
            var parentSpanId = ActivitySpanId.CreateRandom();
            using var activity = new Activity("Space.BinReconciliation")
                .SetIdFormat(ActivityIdFormat.W3C)
                .SetParentId(
                    parentTraceId,
                    parentSpanId,
                    ActivityTraceFlags.None)
                .Start();
            var context = SpaceExecutionContext.ForSystem(
                tenantId,
                WorkerActor,
                Guid.NewGuid(),
                activity.TraceId.ToHexString(),
                jobId: Guid.NewGuid(),
                runId: Guid.NewGuid());
            var manager =
                sp.GetRequiredService<ISpaceExecutionContextManager>();
            var writer = sp.GetRequiredService<ISpaceAuditWriter>();
            var db = sp.GetRequiredService<CP6Context>();
            using var execution = manager.Push(context);
            using var logScope = _logger.BeginScope(
                new Dictionary<string, object?>
                {
                    ["TenantId"] = context.TenantId,
                    ["ActorType"] = context.ActorType,
                    ["ActorId"] = context.ActorId,
                    ["CorrelationId"] = context.CorrelationId,
                    ["TraceId"] = context.TraceId,
                    ["JobId"] = context.JobId,
                    ["RunId"] = context.RunId,
                });

            await TryAppendAuditAsync(
                writer,
                CreateAudit(SpaceAuditOutcome.Started),
                context,
                c);

            List<SpaceBinDriftScanner.SpaceBinDrift> drifts;
            try
            {
                drifts = await SpaceBinDriftScanner.ScanAsync(db, c);
            }
            catch (OperationCanceledException) when (c.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                c.ThrowIfCancellationRequested();
                var safe = SpaceErrorSanitizer.Classify(
                    ex,
                    ScanFailureReason);
                await TryAppendAuditAsync(
                    writer,
                    CreateAudit(
                        SpaceAuditOutcome.Failed,
                        safe.ReasonCode,
                        new SpaceAuditEvidence(
                            Status: "Failed",
                            ExceptionType: safe.ExceptionType,
                            ErrorFingerprint: safe.Fingerprint)),
                    context,
                    c);
                _logger.LogError(
                    "[SpaceBinDrift] tenant={TenantId} correlation={CorrelationId} reason={ReasonCode} type={ErrorType} fingerprint={ErrorFingerprint}",
                    tenantId,
                    context.CorrelationId,
                    safe.ReasonCode,
                    safe.ExceptionType,
                    safe.Fingerprint);
                return;
            }

            await TryAppendAuditAsync(
                writer,
                CreateAudit(
                    SpaceAuditOutcome.Succeeded,
                    evidence: new SpaceAuditEvidence(
                        ItemCount: drifts.Count,
                        Status: "Completed")),
                context,
                c);
            if (drifts.Count == 0)
            {
                _logger.LogInformation(
                    "[SpaceBinDrift] tenant={TenantId} correlation={CorrelationId} driftCount={DriftCount}",
                    tenantId,
                    context.CorrelationId,
                    drifts.Count);
            }
            else
            {
                _logger.LogWarning(
                    "[SpaceBinDrift] tenant={TenantId} correlation={CorrelationId} driftCount={DriftCount}",
                    tenantId,
                    context.CorrelationId,
                    drifts.Count);
            }
        }, _logger, ct);
    }

    private static SpaceAuditEventInput CreateAudit(
        string outcome,
        string? reasonCode = null,
        SpaceAuditEvidence? evidence = null)
        => new(
            ReconciliationAction,
            ReconciliationResourceType,
            null,
            outcome,
            ReasonCode: reasonCode,
            Evidence: evidence,
            ClientType: "Worker");

    private async Task<bool> TryAppendAuditAsync(
        ISpaceAuditWriter writer,
        SpaceAuditEventInput input,
        ISpaceExecutionContext context,
        CancellationToken ct)
    {
        try
        {
            var appended = await writer.TryAppendAsync(input, ct);
            ct.ThrowIfCancellationRequested();
            if (appended)
                return true;

            LogAuditFailure(AuditRejectedError, input, context);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ct.ThrowIfCancellationRequested();
            var safe = SpaceErrorSanitizer.Classify(
                ex,
                AuditFailureReason);
            LogAuditFailure(safe, input, context);
            return false;
        }
    }

    private void LogAuditFailure(
        SpaceSafeError safe,
        SpaceAuditEventInput input,
        ISpaceExecutionContext context)
    {
        _logger.LogWarning(
            "[SpaceReconciliationAudit] reason={ReasonCode} type={ErrorType} fingerprint={ErrorFingerprint} tenant={TenantId} correlation={CorrelationId} outcome={AuditOutcome}",
            safe.ReasonCode,
            safe.ExceptionType,
            safe.Fingerprint,
            context.TenantId,
            context.CorrelationId,
            input.Outcome);
    }

    private sealed class AuditWriteRejectedException : Exception
    {
    }
}
