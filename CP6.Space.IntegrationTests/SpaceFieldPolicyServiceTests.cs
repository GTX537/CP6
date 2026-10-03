using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceFieldPolicyServiceTests
{
    [Theory]
    [InlineData("23505", true)]
    [InlineData("23503", false)]
    [InlineData("23514", false)]
    public async Task Injected_postgres_save_failure_preserves_policy_unique_conflict_and_other_errors(
        string sqlState, bool mapsToBusinessConflict)
    {
        // This InMemory/public-provider exception fixture verifies the catch boundary only.
        var interceptor = new SaveFailureInterceptor();
        await using var fixture = CreateFixture(interceptor);
        var failure = new DbUpdateException("Injected PostgreSQL save failure",
            new PostgresException("Injected provider condition", "ERROR", "ERROR", sqlState,
                constraintName: "AnyPolicyIntegrityName"));
        interceptor.Failure = failure;
        var service = new SpaceFieldPolicyService(fixture.Context, fixture.Execution);

        var caught = await Record.ExceptionAsync(() => service.CreatePolicyAsync(new(
            "Caller regression", "Customer", [new("Stock", "materialNumber")])));

        Assert.Equal(1, interceptor.FailedSaveCalls);
        if (mapsToBusinessConflict)
        {
            var conflict = Assert.IsType<SpaceProblemException>(caught);
            Assert.Equal(SpaceErrorCodes.FieldPolicyConflict, conflict.Code);
            Assert.Equal(409, conflict.StatusCode);
            Assert.Equal("The field policy conflicts with current data.", conflict.Title);
            Assert.Equal("reload-field-policies", conflict.RecoveryAction);
            Assert.False(conflict.Retryable);
        }
        else
            Assert.Same(failure, caught);
    }

    [Fact]
    public async Task Create_and_update_policy_replace_fields_and_bump_authorization_stamp()
    {
        await using var fixture = CreateFixture();
        var service = new SpaceFieldPolicyService(
            fixture.Context,
            fixture.Execution);
        var created = await service.CreatePolicyAsync(new(
            "Customer portal",
            "Customer",
            [
                new("Stock", "materialNumber"),
                new("Stock", "lotNumber", "Partial"),
            ]));
        var grant = SpaceExternalGrant.Create(
            fixture.TenantId,
            fixture.Organization.Id,
            Guid.NewGuid(),
            created.Id,
            false,
            fixture.Now,
            null,
            SpaceExternalGrantStatus.Active);
        fixture.Context.ExternalGrants.Add(grant);
        await fixture.Context.SaveChangesAsync();

        var updated = await service.UpdatePolicyAsync(
            created.Id,
            new(
                "Customer portal v2",
                [new("Task", "taskId", "Hash")],
                true,
                "Active"));

        Assert.Equal(2, updated.PolicyVersion);
        Assert.True(updated.CanExport);
        Assert.Equal("taskId", Assert.Single(updated.Fields).FieldName);
        Assert.Equal(2, fixture.Organization.SecurityStamp);
        Assert.Equal(
            3,
            await fixture.Context.FieldPolicyFields
                .IgnoreQueryFilters()
                .CountAsync(item => item.PolicyId == created.Id));
        Assert.Single(await fixture.Context.FieldPolicyFields
            .Where(item => item.PolicyId == created.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task Unknown_duplicate_and_scalar_mask_fields_fail_closed()
    {
        await using var fixture = CreateFixture();
        var service = new SpaceFieldPolicyService(
            fixture.Context,
            fixture.Execution);

        var unknown = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.CreatePolicyAsync(new(
                "bad",
                "Customer",
                [new("Stock", "internalCost")])));
        var duplicate = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.CreatePolicyAsync(new(
                "duplicate",
                "Customer",
                [
                    new("Stock", "materialNumber"),
                    new("stock", "MATERIALNUMBER"),
                ])));
        var scalar = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.CreatePolicyAsync(new(
                "scalar",
                "Customer",
                [new("Stock", "physicalQuantity", "Hash")])));

        Assert.Equal(SpaceErrorCodes.FieldPolicyInvalid, unknown.Code);
        Assert.Equal(422, duplicate.StatusCode);
        Assert.Equal(422, scalar.StatusCode);
    }

    [Fact]
    public async Task Ef_model_freezes_policy_tables_and_tenant_foreign_keys()
    {
        await using var fixture = CreateFixture();
        var model = fixture.Context.Model;
        var policy = model.FindEntityType(typeof(SpaceFieldPolicy))!;
        var field = model.FindEntityType(typeof(SpaceFieldPolicyField))!;
        var grant = model.FindEntityType(typeof(SpaceExternalGrant))!;

        Assert.Equal("Space_FieldPolicy", policy.GetTableName());
        Assert.Equal("Space_FieldPolicyField", field.GetTableName());
        Assert.True(policy.FindProperty(nameof(SpaceFieldPolicy.RowVersion))!
            .IsConcurrencyToken);
        Assert.Contains(
            field.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Select(item => item.Name)
                .SequenceEqual(["TenantId", "PolicyId"]));
        Assert.Contains(
            grant.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Select(item => item.Name)
                .SequenceEqual(["TenantId", "FieldPolicyId"]));
    }

    private static Fixture CreateFixture(params IInterceptor[] interceptors)
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 1, 15, 0, 0, DateTimeKind.Utc);
        var execution = new TestExecutionContext(tenantId, actorId);
        var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString("N"),
                    SpaceTestDatabaseRoots.InMemory)
                .AddInterceptors(interceptors)
                .Options,
            execution,
            new FixedClock(now));
        var organization = SpaceExternalOrganization.Create(
            tenantId,
            SpaceExternalOrganizationType.Customer,
            "CUST-A",
            "Customer A");
        context.ExternalOrganizations.Add(organization);
        context.SaveChanges();
        return new Fixture(
            context,
            execution,
            tenantId,
            organization,
            now);
    }

    private sealed record Fixture(
        SpaceContext Context,
        TestExecutionContext Execution,
        Guid TenantId,
        SpaceExternalOrganization Organization,
        DateTime Now) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class SaveFailureInterceptor : SaveChangesInterceptor
    {
        public DbUpdateException? Failure { get; set; }
        public int FailedSaveCalls { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Failure is null) return base.SavingChangesAsync(eventData, result, cancellationToken);
            FailedSaveCalls++;
            throw Failure;
        }
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed class FixedClock(DateTime now) : ISpaceClock
    {
        public DateTime UtcNow { get; } = now;
    }
}
