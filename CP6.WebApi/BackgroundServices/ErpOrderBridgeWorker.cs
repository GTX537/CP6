using System.Data;
using CP6.Core.Services.Common;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.BackgroundServices;

/// <summary>Hands committed orders to existing idempotent ERP→WMS/MES hooks with bounded durable retries.</summary>
public sealed class ErpOrderBridgeWorker(IDbContextFactory<ErpIntegrationContext> factory, IServiceScopeFactory scopes,
    ErpIntegrationRuntime runtime, ILogger<ErpOrderBridgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var queue = await factory.CreateDbContextAsync(stoppingToken);
                var now = runtime.Clock.GetUtcNow();
                var candidates = await queue.OrderBridges.AsNoTracking().Where(x => x.CompletedAtUtc == null &&
                    x.AttemptCount < 10 && x.AvailableAtUtc <= now).OrderBy(x => x.AvailableAtUtc).Take(8)
                    .Select(x => new { x.TenantId, x.OrderKey }).ToListAsync(stoppingToken);
                foreach (var candidate in candidates)
                    await DispatchOneAsync(candidate.TenantId, candidate.OrderKey, stoppingToken);
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                logger.LogWarning("C03 post-commit order bridge is temporarily unavailable.");
                try { await Task.Delay(2000, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    public async Task DispatchOneAsync(Guid tenant, string key, CancellationToken ct)
    {
        if (!runtime.Options.Tenants.ContainsKey(tenant)) return;
        await using var queue = await factory.CreateDbContextAsync(ct);
        if (!(queue.Database.IsSqlServer() || queue.Database.IsNpgsql()))
            throw new InvalidOperationException("C03_REQUIRES_SQL_TRANSACTION");
        using var scope = scopes.CreateScope();
        var ops = scope.ServiceProvider.GetService<ErpIntegrationOpsV1>();
        var manual = ops is not null && await ErpIntegrationOpsV1.HasManualBridgeAsync(queue, tenant, key, ct);
        // 原有自动路径持有 dispatch 锁。受控续行先取得 IAM 根，并在同一事务内暂存新效果。
        // This prevents a second worker entering even if a wall-clock lease would expire.
        await using var transaction = await queue.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using var business = manual ? await IntegrationOpsAuthorityV1.BindAsync(queue, tenant, ct) : null;
        if (business is not null) await ErpIntegrationOpsV1.HoldWorkerRootAsync(business, ct);
        await ErpDeliveryReplayService.LockBridgeAsync(queue, tenant, key, ct);
        var dispatchQuery = queue.Database.IsNpgsql()
            ? queue.OrderBridges.FromSqlInterpolated($"""SELECT * FROM erp_integration."OrderBridgeDispatch" WHERE "TenantId"={tenant} AND "OrderKey"=CAST({key} AS bpchar) FOR UPDATE""")
            : queue.OrderBridges.FromSqlInterpolated($"SELECT * FROM erp_integration.OrderBridgeDispatch WITH (UPDLOCK,HOLDLOCK) WHERE TenantId={tenant} AND OrderKey={key}");
        var dispatch = await dispatchQuery.SingleOrDefaultAsync(ct);
        var now = runtime.Clock.GetUtcNow();
        if (dispatch is null || dispatch.CompletedAtUtc is not null || dispatch.AttemptCount >= 10 ||
            dispatch.AvailableAtUtc > now || dispatch.LeaseExpiresAtUtc > now ||
            (dispatch.LeaseOwner is not null && dispatch.LeaseExpiresAtUtc is null)) return;
        if (ops is not null && await ErpIntegrationOpsV1.HasManualBridgeAsync(queue, tenant, key, ct))
        {
            if (business is null) return;
            if (await ops.ExecuteBridgeAsync(queue, business, dispatch, ct))
            {
                dispatch.CompletedAtUtc = now;
                dispatch.AttemptCount++;
                dispatch.LastErrorCode = null;
            }
            else
            {
                dispatch.AttemptCount = 10;
                dispatch.LastErrorCode = ErpIntegrationOpsV1.Blocked;
            }
            await queue.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return;
        }
        dispatch.AttemptCount++;
        var completed = false;
        try
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().CurrentTenantId = tenant;
            var wms = await scope.ServiceProvider.GetRequiredService<IWmsBridgeHook>().OnOrderCreatedAsync(key, "crm-integration");
            var mes = await scope.ServiceProvider.GetRequiredService<IMesBridgeHook>().OnOrderCreatedAsync(key, "crm-integration");
            // The established hook contract makes Skipped a business/no-op outcome; technical failures remain retryable.
            completed = (wms.Success || wms.Message?.StartsWith("SKIPPED:", StringComparison.Ordinal) == true) &&
                (mes.Success || mes.Message?.StartsWith("SKIPPED:", StringComparison.Ordinal) == true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { /* Record the content-safe retry below; no hook exception/body is persisted here. */ }
        if (completed) { dispatch.CompletedAtUtc = runtime.Clock.GetUtcNow(); dispatch.LastErrorCode = null; }
        else
        {
            dispatch.LastErrorCode = dispatch.AttemptCount >= 10 ? "C03_BRIDGE_REPLAY_REQUIRED" : "C03_BRIDGE_UNAVAILABLE";
            dispatch.AvailableAtUtc = runtime.Clock.GetUtcNow().AddSeconds(Math.Min(300, Math.Pow(2, dispatch.AttemptCount)));
            if (dispatch.AttemptCount >= 10) logger.LogError("C03 post-commit bridge requires operator replay.");
        }
        await queue.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
