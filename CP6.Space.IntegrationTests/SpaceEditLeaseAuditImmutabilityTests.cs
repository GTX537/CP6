using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceEditLeaseAuditImmutabilityTests
{
    private static readonly DateTime Now =
        new(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task SaveChangesAsync_rejects_takeover_audit_mutation(
        EntityState state)
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var audit = CreateAudit(tenantId);
        context.EditLeaseTakeoverAudits.Attach(audit);
        context.Entry(audit).State = state;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.SaveChangesAsync());

        Assert.Equal(
            "Edit lease takeover audit records are immutable.",
            error.Message);
    }

    private static SpaceContext CreateContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options,
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            new FixedClock());

    private static SpaceEditLeaseTakeoverAudit CreateAudit(Guid tenantId) =>
        SpaceEditLeaseTakeoverAudit.Create(
            tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Resolve abandoned editing session",
            Guid.NewGuid(),
            "127.0.0.1 | integration-test",
            Now);

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId) : ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }
}
