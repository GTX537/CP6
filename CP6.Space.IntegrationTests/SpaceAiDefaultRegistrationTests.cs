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
        services.AddScoped<ISpaceExecutionContext>(_ =>
            new TestExecutionContext(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
        await using var provider = services.BuildServiceProvider();

        var policySource =
            provider.GetRequiredService<ISpaceAiTenantPolicySource>();
        var quota =
            provider.GetRequiredService<ISpaceAiQuotaLeaseManager>();
        var registry = provider.GetRequiredService<
            IWarehouseGenerationProviderRegistry>();
        var outputValidator = provider.GetRequiredService<
            IWarehouseGenerationOutputValidator>();
        var tenantId =
            Guid.Parse("11111111-1111-1111-1111-111111111111");

        var lease = await quota.TryAcquireAsync(tenantId, 3);

        Assert.IsType<SpaceAiAdministrationService>(policySource);
        Assert.Same(
            policySource,
            provider.GetRequiredService<ISpaceAiAdministrationService>());
        Assert.IsType<ClosedSpaceAiQuotaLeaseManager>(quota);
        Assert.IsType<WarehouseGenerationProviderRegistry>(registry);
        Assert.IsType<WarehouseGenerationOutputValidator>(outputValidator);
        Assert.Null(lease);
        Assert.False(registry.TryGet("external-v1", out _));
        Assert.Empty(registry.Registrations);
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;
}
