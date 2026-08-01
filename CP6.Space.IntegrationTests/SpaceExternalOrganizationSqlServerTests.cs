using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceExternalOrganizationSqlServerTests
{
    private static readonly DateTime Now =
        new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [SqlServerFact]
    public async Task Sql_constraints_enforce_type_scoped_code_current_member_and_tenant_fk()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            SpaceExternalOrganization customer;
            await using (var seed = CreateContext(connectionString, tenantA))
            {
                customer = SpaceExternalOrganization.Create(
                    tenantA,
                    SpaceExternalOrganizationType.Customer,
                    "partner-001",
                    "Customer");
                var supplier = SpaceExternalOrganization.Create(
                    tenantA,
                    SpaceExternalOrganizationType.Supplier,
                    "PARTNER-001",
                    "Supplier");
                seed.AddRange(customer, supplier);
                await seed.SaveChangesAsync();
            }

            await using (var duplicate = CreateContext(connectionString, tenantA))
            {
                duplicate.ExternalOrganizations.Add(
                    SpaceExternalOrganization.Create(
                        tenantA,
                        SpaceExternalOrganizationType.Customer,
                        "Partner-001",
                        "Duplicate"));
                await Assert.ThrowsAsync<DbUpdateException>(
                    () => duplicate.SaveChangesAsync());
            }

            var userId = Guid.NewGuid();
            SpaceExternalMembership first;
            await using (var current = CreateContext(connectionString, tenantA))
            {
                first = SpaceExternalMembership.Create(
                    tenantA,
                    customer.Id,
                    userId,
                    SpaceExternalMembershipRole.Viewer,
                    Now,
                    null,
                    SpaceExternalMembershipStatus.Invited,
                    Guid.NewGuid(),
                    Now);
                current.ExternalMemberships.Add(first);
                await current.SaveChangesAsync();
            }

            await using (var duplicate = CreateContext(connectionString, tenantA))
            {
                duplicate.ExternalMemberships.Add(
                    SpaceExternalMembership.Create(
                        tenantA,
                        customer.Id,
                        userId,
                        SpaceExternalMembershipRole.OperationsViewer,
                        Now,
                        null,
                        SpaceExternalMembershipStatus.Active,
                        Guid.NewGuid(),
                        Now));
                await Assert.ThrowsAsync<DbUpdateException>(
                    () => duplicate.SaveChangesAsync());
            }

            await using (var revoke = CreateContext(connectionString, tenantA))
            {
                var persisted = await revoke.ExternalMemberships
                    .SingleAsync(item => item.Id == first.Id);
                persisted.Update(
                    persisted.Role,
                    DateTime.SpecifyKind(
                        persisted.ValidFromUtc,
                        DateTimeKind.Utc),
                    persisted.ValidToUtc.HasValue
                        ? DateTime.SpecifyKind(
                            persisted.ValidToUtc.Value,
                            DateTimeKind.Utc)
                        : null,
                    SpaceExternalMembershipStatus.Revoked,
                    Now.AddMinutes(1));
                await revoke.SaveChangesAsync();
            }
            await using (var replacement = CreateContext(connectionString, tenantA))
            {
                replacement.ExternalMemberships.Add(
                    SpaceExternalMembership.Create(
                        tenantA,
                        customer.Id,
                        userId,
                        SpaceExternalMembershipRole.OperationsViewer,
                        Now,
                        null,
                        SpaceExternalMembershipStatus.Active,
                        Guid.NewGuid(),
                        Now.AddMinutes(2)));
                await replacement.SaveChangesAsync();
            }

            await using (var forged = CreateContext(connectionString, tenantB))
            {
                forged.ExternalMemberships.Add(
                    SpaceExternalMembership.Create(
                        tenantB,
                        customer.Id,
                        Guid.NewGuid(),
                        SpaceExternalMembershipRole.Viewer,
                        Now,
                        null,
                        SpaceExternalMembershipStatus.Active,
                        Guid.NewGuid(),
                        Now));
                await Assert.ThrowsAsync<DbUpdateException>(
                    () => forged.SaveChangesAsync());
                Assert.Empty(await forged.ExternalOrganizations.ToListAsync());
            }
        });
    }

    private static async Task WithDatabaseAsync(
        Func<string, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6_Space_E09S01_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var context = CreateContext(
            connectionString,
            Guid.NewGuid());
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

    private static SpaceContext CreateContext(
        string connectionString,
        Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        SpaceContext.MigrationsHistoryTable))
                .Options,
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            new FixedClock());

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }
}
