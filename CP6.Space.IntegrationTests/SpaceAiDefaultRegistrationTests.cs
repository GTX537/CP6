using CP6.Space.Application;
using CP6.Space.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceAiDefaultRegistrationTests
{
    [Fact]
    public async Task Infrastructure_defaults_are_disabled_closed_and_empty()
    {
        var services = new ServiceCollection();
        services.AddSpaceDesignV1Persistence(
            "Server=(localdb)\\mssqllocaldb;Database=cp6-space-ai-test;");
        await using var provider = services.BuildServiceProvider();

        var policySource =
            provider.GetRequiredService<ISpaceAiTenantPolicySource>();
        var quota =
            provider.GetRequiredService<ISpaceAiQuotaLeaseManager>();
        var registry = provider.GetRequiredService<
            IWarehouseGenerationProviderRegistry>();
        var tenantId =
            Guid.Parse("11111111-1111-1111-1111-111111111111");

        var policy = await policySource.GetPolicyAsync(tenantId);
        var lease = await quota.TryAcquireAsync(tenantId, 3);

        Assert.IsType<DisabledSpaceAiTenantPolicySource>(policySource);
        Assert.IsType<ClosedSpaceAiQuotaLeaseManager>(quota);
        Assert.IsType<WarehouseGenerationProviderRegistry>(registry);
        Assert.False(policy.IsEnabled);
        Assert.Null(lease);
        Assert.False(registry.TryGet("external-v1", out _));
    }
}
