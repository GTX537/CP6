using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.BackgroundServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests.Tenant;

/// <summary>
/// 章10 §5 后台任务按租户循环（TenantScopeRunner）。验证：每个启用租户各开一次 scope 且 ITenantContext/CP6Context
/// 在 body 内被设为该租户；停用租户跳过；单租户异常不影响其余租户。
/// </summary>
public class TenantScopeRunnerTests
{
    private static ServiceProvider BuildProvider(string dbName)
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var services = new ServiceCollection();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ITenantEnumerator, TenantEnumerator>();
        // 工厂读取 scope 的 ITenantContext → CP6Context 的过滤/盖章与运行时一致
        services.AddScoped(sp => new CP6Context(options, sp.GetRequiredService<ITenantContext>()));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ForEachTenant_VisitsEachEnabledTenant_WithScopedContext()
    {
        var provider = BuildProvider(Guid.NewGuid().ToString());
        var tA = Guid.NewGuid();
        var tB = Guid.NewGuid();
        var disabled = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
            db.Sys_Tenants.AddRange(
                new Sys_Tenant { Id = tA, TenantCode = "A", TenantName = "a", Enable = true },
                new Sys_Tenant { Id = tB, TenantCode = "B", TenantName = "b", Enable = true },
                new Sys_Tenant { Id = disabled, TenantCode = "C", TenantName = "c", Enable = false });
            await db.SaveChangesAsync();
        }

        var visited = new List<Guid>();
        var contextSeen = new List<Guid>();
        await TenantScopeRunner.ForEachTenantAsync(
            provider.GetRequiredService<IServiceScopeFactory>(),
            (sp, tenantId, ct) =>
            {
                visited.Add(tenantId);
                contextSeen.Add(sp.GetRequiredService<CP6Context>().CurrentTenantId);
                return Task.CompletedTask;
            });

        Assert.Equal(2, visited.Count);
        Assert.Contains(tA, visited);
        Assert.Contains(tB, visited);
        Assert.DoesNotContain(disabled, visited);
        // body 内 CP6Context 当前租户 == 正在处理的租户（作用域正确传播）
        Assert.Equal(visited.OrderBy(x => x), contextSeen.OrderBy(x => x));
    }

    [Fact]
    public async Task ForEachTenant_OneTenantThrows_OthersStillRun()
    {
        var provider = BuildProvider(Guid.NewGuid().ToString());
        var tA = Guid.NewGuid();
        var tB = Guid.NewGuid();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
            db.Sys_Tenants.AddRange(
                new Sys_Tenant { Id = tA, TenantCode = "A", TenantName = "a", Enable = true },
                new Sys_Tenant { Id = tB, TenantCode = "B", TenantName = "b", Enable = true });
            await db.SaveChangesAsync();
        }

        var completed = new List<Guid>();
        await TenantScopeRunner.ForEachTenantAsync(
            provider.GetRequiredService<IServiceScopeFactory>(),
            (sp, tenantId, ct) =>
            {
                if (tenantId == tA) throw new InvalidOperationException("boom");
                completed.Add(tenantId);
                return Task.CompletedTask;
            });

        Assert.Equal(new[] { tB }, completed);
    }
}
