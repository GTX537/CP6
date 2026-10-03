using CP6.Space.Application;
using CP6.Core.Persistence;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceFieldPolicySqlServerTests(SpaceRelationalFixture database, ITestOutputHelper output)
{
    private static readonly DateTime Now =
        new(2026, 8, 1, 19, 0, 0, DateTimeKind.Utc);

    [SqlServerFact]
    public async Task Policy_update_can_replace_the_same_field_under_filtered_index()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var tenantId = Guid.NewGuid();
            var execution = new TestExecutionContext(tenantId, Guid.NewGuid());
            await using var context = CreateContext(connectionString, execution);
            var service = new SpaceFieldPolicyService(context, execution);
            var created = await service.CreatePolicyAsync(new(
                "Customer portal",
                "Customer",
                [new("Stock", "materialNumber")]));

            var updated = await service.UpdatePolicyAsync(
                created.Id,
                new(
                    "Customer portal",
                    [new("Stock", "materialNumber", "Partial")],
                    false,
                    "Active"));

            Assert.Equal(2, updated.PolicyVersion);
            Assert.Equal("Partial", Assert.Single(updated.Fields).MaskingRule);
            Assert.Equal(
                2,
                await context.FieldPolicyFields
                    .IgnoreQueryFilters()
                    .CountAsync(item => item.PolicyId == created.Id));
            Assert.Single(await context.FieldPolicyFields
                .Where(item => item.PolicyId == created.Id)
                .ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task Policy_constraints_enforce_current_uniqueness_and_tenant_fk()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var policy = SpaceFieldPolicy.Create(
                tenantA,
                "Portal",
                SpaceExternalOrganizationType.Customer,
                false);
            await using (var seed = CreateContext(connectionString, tenantA))
            {
                seed.AddRange(
                    policy,
                    SpaceFieldPolicyField.Create(
                        tenantA,
                        policy.Id,
                        SpaceFieldPolicyResourceType.Stock,
                        "materialNumber",
                        SpaceFieldMaskingRule.None));
                await seed.SaveChangesAsync();
            }

            await using (var duplicate = CreateContext(connectionString, tenantA))
            {
                duplicate.FieldPolicies.Add(SpaceFieldPolicy.Create(
                    tenantA,
                    "portal",
                    SpaceExternalOrganizationType.Customer,
                    false));
                var error = await Assert.ThrowsAsync<DbUpdateException>(
                    () => duplicate.SaveChangesAsync());
                var failure = DatabaseFailureClassifier.Classify(error);
                Assert.Equal(DatabaseFailureKind.UniqueConstraint, failure.Kind);
                Assert.Equal("UX_Space_FieldPolicy_CurrentName", failure.ConstraintName);
            }

            await using (var forged = CreateContext(connectionString, tenantB))
            {
                forged.FieldPolicyFields.Add(SpaceFieldPolicyField.Create(
                    tenantB,
                    policy.Id,
                    SpaceFieldPolicyResourceType.Stock,
                    "ownerId",
                    SpaceFieldMaskingRule.None));
                var error = await Assert.ThrowsAsync<DbUpdateException>(
                    () => forged.SaveChangesAsync());
                var failure = DatabaseFailureClassifier.Classify(error);
                Assert.Equal(DatabaseFailureKind.ForeignKey, failure.Kind);
                Assert.Equal("FK_Space_FieldPolicyField_Policy_Tenant", failure.ConstraintName);
                Assert.Empty(await forged.FieldPolicies.ToListAsync());
            }
        });
    }

    private async Task WithDatabaseAsync(Func<string, Task> action)
    {
        if (SpaceRelationalFixture.IsSelected)
        {
            database.WriteSetupEvidence(output);
            await action(database.ConnectionString);
            return;
        }

        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6_Space_E09S03_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var context = CreateContext(connectionString, Guid.NewGuid());
        try
        {
            await context.Database.MigrateAsync();
            await action(connectionString);
        }
        finally
        {
            await context.Database.EnsureDeletedAsync();
        }
    }

    private SpaceContext CreateContext(
        string connectionString,
        Guid tenantId) =>
        CreateContext(
            connectionString,
            new TestExecutionContext(tenantId, Guid.NewGuid()));

    private SpaceContext CreateContext(
        string connectionString,
        TestExecutionContext execution)
    {
        if (SpaceRelationalFixture.IsSelected)
        {
            Assert.True(string.Equals(database.ConnectionString, connectionString, StringComparison.Ordinal),
                "Selected Space contexts must use the fixture-owned connection.");
            return database.CreateSpaceContext(execution, new FixedClock(),
                new SpaceNativeFailureObserver(database.Database.Provider, output.WriteLine, "field-policy"));
        }
        return new(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        SpaceContext.MigrationsHistoryTable))
                .Options,
            execution,
            new FixedClock());
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }
}
