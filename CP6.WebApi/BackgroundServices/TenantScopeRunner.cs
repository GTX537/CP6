using CP6.Core.Services.Common;

namespace CP6.WebApi.BackgroundServices;

/// <summary>
/// 后台任务的"按租户循环"助手（OA 章10 §5）。后台 BackgroundService 无 HttpContext → 无 JWT → ITenantContext
/// 保持默认租户，直接跑只会处理默认租户的数据。本助手枚举全部启用租户，**逐租户开独立 scope 并设当前租户**，
/// 让每租户的扫描/对账/重试都正确作用域（全局查询过滤）+ 写入盖章到本租户。
///
/// 关键：先开一个 scope 取租户清单（<see cref="ITenantEnumerator"/>），再为每个租户开**新** scope 设
/// <see cref="ITenantContext.CurrentTenantId"/>——同一 scope 内 ITenantContext 与 CP6Context 是同一份，
/// CP6Context 查询/盖章时按本租户求值。单租户期清单仅默认租户，行为与改造前一致。
/// 某租户异常吞掉记日志，不影响其余租户，也不拖垮宿主（取消信号照常向上抛）。
/// </summary>
public static class TenantScopeRunner
{
    public static async Task ForEachTenantAsync(
        IServiceScopeFactory scopeFactory,
        Func<IServiceProvider, Guid, CancellationToken, Task> body,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        IReadOnlyList<Guid> tenants;
        using (var enumScope = scopeFactory.CreateScope())
        {
            tenants = await enumScope.ServiceProvider
                .GetRequiredService<ITenantEnumerator>().ListActiveAsync(ct);
        }
        ct.ThrowIfCancellationRequested();

        foreach (var tenantId in tenants)
        {
            ct.ThrowIfCancellationRequested();

            using var scope = scopeFactory.CreateScope();
            // 在跑 body（及其解析的 CP6Context）之前设当前租户——同 scope 内同一份 ITenantContext
            scope.ServiceProvider.GetRequiredService<ITenantContext>().CurrentTenantId = tenantId;
            try
            {
                ct.ThrowIfCancellationRequested();
                await body(scope.ServiceProvider, tenantId, ct);
                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                ct.ThrowIfCancellationRequested();
                logger?.LogError(ex, "后台任务在租户 {Tenant} 执行异常（跳过该租户，继续其余）", tenantId);
            }
        }
        ct.ThrowIfCancellationRequested();
    }
}
