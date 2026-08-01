using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceExternalGrantSqlServerTests
{
    private static readonly DateTime Now =
        new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [SqlServerFact]
    public async Task Sql_constraints_enforce_grant_scope_uniqueness_validity_and_tenant_fk()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            SpaceExternalOrganization organization;
            SpaceExternalGrant grant;
            var floorId = Guid.NewGuid();
            await using (var seed = CreateContext(connectionString, tenantA))
            {
                organization = SpaceExternalOrganization.Create(
                    tenantA,
                    SpaceExternalOrganizationType.Customer,
                    "CUST-A",
                    "Customer A");
                grant = SpaceExternalGrant.Create(
                    tenantA,
                    organization.Id,
                    Guid.NewGuid(),
                    null,
                    false,
                    Now,
                    null,
                    SpaceExternalGrantStatus.Active);
                seed.AddRange(
                    organization,
                    grant,
                    SpaceExternalGrantFloor.Create(
                        tenantA,
                        grant.Id,
                        floorId));
                await seed.SaveChangesAsync();
            }

            await using (var duplicate = CreateContext(connectionString, tenantA))
            {
                duplicate.ExternalGrantFloors.Add(
                    SpaceExternalGrantFloor.Create(
                        tenantA,
                        grant.Id,
                        floorId));
                await Assert.ThrowsAsync<DbUpdateException>(
                    () => duplicate.SaveChangesAsync());
            }

            await using (var forged = CreateContext(connectionString, tenantB))
            {
                forged.ExternalGrantFloors.Add(
                    SpaceExternalGrantFloor.Create(
                        tenantB,
                        grant.Id,
                        Guid.NewGuid()));
                await Assert.ThrowsAsync<DbUpdateException>(
                    () => forged.SaveChangesAsync());
                Assert.Empty(await forged.ExternalGrants.ToListAsync());
            }

            await using (var invalid = CreateContext(connectionString, tenantA))
            {
                var error = await Assert.ThrowsAsync<SqlException>(() =>
                    invalid.Database.ExecuteSqlInterpolatedAsync(
                        $$"""
                        INSERT INTO [Space_ExternalGrant]
                            ([Id], [OrganizationId], [SiteId], [FieldPolicyId],
                             [CanExport], [ValidFromUtc], [ValidToUtc], [Status],
                             [GrantVersion], [TenantId], [CreatedAtUtc],
                             [CreatedBy], [ModifiedAtUtc], [ModifiedBy],
                             [IsDeleted])
                        VALUES
                            ({{Guid.NewGuid()}}, {{organization.Id}},
                             {{Guid.NewGuid()}}, NULL, 0, {{Now}}, {{Now}}, 0,
                             1, {{tenantA}}, {{Now}}, NULL, NULL, NULL, 0);
                        """));
                Assert.Equal(547, error.Number);
            }
        });
    }

    [SqlServerFact]
    public async Task Grant_update_can_replace_the_same_scopes_atomically()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var tenantId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var siteId = Guid.NewGuid();
            var floorId = Guid.NewGuid();
            var execution = new TestExecutionContext(tenantId, actorId);
            await using var context = CreateContext(connectionString, execution);
            var organization = SpaceExternalOrganization.Create(
                tenantId,
                SpaceExternalOrganizationType.Customer,
                "CUST-UPDATE",
                "Customer Update");
            var model = SpaceModel.Create(tenantId, siteId);
            var version = SpaceModelVersion.CreateDraft(
                tenantId,
                model.Id,
                1,
                "Published scope");
            context.AddRange(
                organization,
                model,
                version,
                SpaceFloorRevision.Create(
                    tenantId,
                    version.Id,
                    floorId,
                    siteId,
                    1,
                    "F1",
                    "Floor 1"));
            await context.SaveChangesAsync();

            var hash = new string('a', 64);
            version.BeginValidation();
            version.MarkReady(hash, "rules-v1", hash);
            version.BeginPublishing();
            version.MarkPublished(actorId, Now);
            model.SetPublishedVersion(version, hash);
            await context.SaveChangesAsync();

            var service = new SpaceExternalGrantService(
                context,
                execution,
                new FixedClock());
            var created = await service.CreateGrantAsync(
                organization.Id,
                new(
                    siteId,
                    [floorId],
                    OwnerIds: ["owner-a"],
                    Objects: [new("task", "pick-1")],
                    ValidFromUtc: Now));
            var updated = await service.UpdateGrantAsync(
                organization.Id,
                created.Id,
                new(
                    siteId,
                    [floorId],
                    [],
                    ["owner-a"],
                    [new("task", "pick-1")],
                    null,
                    false,
                    Now,
                    null,
                    "Active"));

            Assert.Equal(2, updated.GrantVersion);
            Assert.Equal([floorId], updated.FloorLogicalIds);
            Assert.Equal(2, await context.ExternalGrantFloors
                .IgnoreQueryFilters()
                .CountAsync(item => item.GrantId == created.Id));
            Assert.Single(await context.ExternalGrantFloors
                .Where(item => item.GrantId == created.Id)
                .ToListAsync());
        });
    }

    private static async Task WithDatabaseAsync(Func<string, Task> action)
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6_Space_E09S02_{Guid.NewGuid():N}",
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
        CreateContext(
            connectionString,
            new TestExecutionContext(tenantId, Guid.NewGuid()));

    private static SpaceContext CreateContext(
        string connectionString,
        TestExecutionContext execution) =>
        new(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseSqlServer(
                    connectionString,
                    sql => sql.MigrationsHistoryTable(
                        SpaceContext.MigrationsHistoryTable))
                .Options,
            execution,
            new FixedClock());

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }
}
