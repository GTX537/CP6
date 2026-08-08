using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceAiAdministrationServiceTests
{
    [Fact]
    public async Task Missing_policy_is_disabled_and_does_not_expose_provider_secrets()
    {
        await using var fixture = CreateFixture();

        var policy = await fixture.Service.GetPolicyAsync();
        var runtime = await ((ISpaceAiTenantPolicySource)fixture.Service)
            .GetPolicyAsync(fixture.Execution.TenantId);

        Assert.Equal(0, policy.Version);
        Assert.Equal("Disabled", policy.DataPolicy);
        Assert.False(runtime.IsEnabled);
        Assert.Equal(["local-approved"],
            policy.ApprovedProviders.Select(item => item.Alias));
        var publicNames = typeof(UpdateSpaceAiPolicyRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        Assert.DoesNotContain(publicNames, name =>
            name.Contains("Key", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Url", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Endpoint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Update_is_versioned_idempotent_and_runtime_readable()
    {
        await using var fixture = CreateFixture();
        var siteId = Guid.NewGuid();
        var request = new UpdateSpaceAiPolicyRequest(
            0,
            "MetadataOnly",
            [siteId],
            ["local-approved"],
            2,
            false,
            1_000,
            10_000,
            "usd");

        var first = await fixture.Service.UpdatePolicyAsync(request, "request-1");
        var replay = await fixture.Service.UpdatePolicyAsync(request, "request-1");
        var runtime = await ((ISpaceAiTenantPolicySource)fixture.Service)
            .GetPolicyAsync(fixture.Execution.TenantId);

        Assert.Equal(1, first.Policy.Version);
        Assert.False(first.IdempotentReplay);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal("USD", replay.Policy.Currency);
        Assert.True(runtime.IsEnabled);
        Assert.True(runtime.AllowsSite(siteId));
        Assert.True(runtime.AllowsProvider("local-approved"));
        Assert.Single(await fixture.Context.AiTenantPolicies.ToArrayAsync());
    }

    [Fact]
    public async Task Unknown_provider_alias_is_rejected_fail_closed()
    {
        await using var fixture = CreateFixture();
        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.UpdatePolicyAsync(
                new UpdateSpaceAiPolicyRequest(
                    0,
                    "MetadataOnly",
                    [Guid.NewGuid()],
                    ["not-approved"],
                    1,
                    false,
                    null,
                    null,
                    null),
                "request-2"));

        Assert.Equal(SpaceErrorCodes.AiProviderAliasNotApproved, error.Code);
        Assert.Empty(await fixture.Context.AiTenantPolicies.ToArrayAsync());
    }

    [Fact]
    public async Task External_principal_is_hard_denied_even_before_data_access()
    {
        await using var fixture = CreateFixture(isExternal: true);

        var error = await Assert.ThrowsAsync<SpaceProblemException>(
            () => fixture.Service.GetPolicyAsync());

        Assert.Equal(SpaceErrorCodes.ExternalSubjectDenied, error.Code);
        Assert.Equal(403, error.StatusCode);
    }

    [Fact]
    public async Task Usage_reports_units_unpriced_state_and_current_budget_balances()
    {
        await using var fixture = CreateFixture();
        await fixture.Service.UpdatePolicyAsync(
            new UpdateSpaceAiPolicyRequest(
                0,
                "Disabled",
                [],
                [],
                3,
                false,
                100,
                1_000,
                "USD"),
            "budget-policy");
        fixture.Context.AiUsageRecords.Add(SpaceAiUsageRecord.Create(
            fixture.Execution.TenantId,
            Guid.NewGuid(),
            "local-approved",
            "model-v1",
            new string('a', 64),
            12,
            4,
            0,
            null,
            null,
            250,
            SpaceAiUsageOutcome.Succeeded,
            new DateTime(2026, 8, 2, 15, 0, 0, DateTimeKind.Utc)));
        fixture.Context.AiBudgetReservations.Add(
            SpaceAiBudgetReservation.Create(
                fixture.Execution.TenantId,
                Guid.NewGuid(),
                new string('b', 64),
                new DateOnly(2026, 8, 2),
                202608,
                20,
                "USD",
                new DateTime(2026, 8, 2, 16, 15, 0, DateTimeKind.Utc)));
        await fixture.Context.SaveChangesAsync();

        var page = await fixture.Service.GetUsageAsync(
            new SpaceAiUsageQuery(
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Single(page.Items);
        Assert.Equal(12, page.Summary.InputUnits);
        Assert.Equal(4, page.Summary.OutputUnits);
        Assert.True(page.Summary.HasUnpricedUsage);
        Assert.Equal(20, page.Summary.DailyBudget.ConsumedMinor);
        Assert.Equal(80, page.Summary.DailyBudget.RemainingMinor);
        Assert.Equal(980, page.Summary.MonthlyBudget.RemainingMinor);
    }

    private static Fixture CreateFixture(bool isExternal = false)
    {
        var execution = new TestExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            isExternal);
        var clock = new FixedClock(
            new DateTime(2026, 8, 2, 16, 0, 0, DateTimeKind.Utc));
        var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            execution,
            clock);
        var registry = new WarehouseGenerationProviderRegistry(
        [
            new WarehouseGenerationProviderRegistration(
                "local-approved",
                WarehouseGenerationProviderKind.Local,
                new FakeProvider()),
        ]);
        return new Fixture(
            context,
            execution,
            new SpaceAiAdministrationService(
                context,
                execution,
                clock,
                registry));
    }

    private sealed record Fixture(
        SpaceContext Context,
        TestExecutionContext Execution,
        SpaceAiAdministrationService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId,
        bool IsExternal) : ISpaceExecutionContext;

    private sealed class FixedClock(DateTime now) : ISpaceClock
    {
        public DateTime UtcNow { get; } = now;
    }

    private sealed class FakeProvider : IWarehouseGenerationProvider
    {
        public Task<WarehouseGenerationResult> GenerateAsync(
            WarehouseGenerationInput input,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
