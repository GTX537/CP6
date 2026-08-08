using CP6.Space.Application;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.IntegrationTests;

[Collection(SpaceSqlServerCollection.Name)]
public sealed class SpaceExternalTenantIsolationTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Collaboration_graph_filters_same_business_ids_in_memory()
    {
        var root = new InMemoryDatabaseRoot();
        var database = Guid.NewGuid().ToString("N");

        await AssertIsolationAsync(tenantId => new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(database, root)
                .Options,
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            new FixedClock()));
    }

    [SqlServerFact]
    public async Task Collaboration_graph_filters_same_business_ids_in_sql_server()
    {
        var baseConnection = Environment.GetEnvironmentVariable(
            SqlServerFactAttribute.EnvVar)!;
        var connectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"CP6_Space_E09S04_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using var setup = CreateSqlContext(
            connectionString,
            Guid.NewGuid());
        try
        {
            await setup.Database.MigrateAsync();
            await AssertIsolationAsync(tenantId =>
                CreateSqlContext(connectionString, tenantId));
        }
        finally
        {
            await setup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task AssertIsolationAsync(
        Func<Guid, SpaceContext> createContext)
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var sharedUserId = Guid.NewGuid();
        var sharedSiteId = Guid.NewGuid();
        var sharedFloorId = Guid.NewGuid();
        var sharedZoneId = Guid.NewGuid();

        var graphA = await SeedAsync(
            createContext,
            tenantA,
            sharedUserId,
            sharedSiteId,
            sharedFloorId,
            sharedZoneId,
            "Tenant A secret");
        var graphB = await SeedAsync(
            createContext,
            tenantB,
            sharedUserId,
            sharedSiteId,
            sharedFloorId,
            sharedZoneId,
            "Tenant B secret");

        await AssertVisibleGraphAsync(createContext, tenantA, graphA, graphB);
        await AssertVisibleGraphAsync(createContext, tenantB, graphB, graphA);

        await using var audit = createContext(tenantA);
        Assert.Equal(
            2,
            await audit.ExternalOrganizations.IgnoreQueryFilters().CountAsync());
        Assert.Equal(
            2,
            await audit.ExternalMemberships.IgnoreQueryFilters().CountAsync());
        Assert.Equal(
            2,
            await audit.ExternalGrants.IgnoreQueryFilters().CountAsync());
        Assert.Equal(
            2,
            await audit.ExternalGrantFloors.IgnoreQueryFilters().CountAsync());
        Assert.Equal(
            2,
            await audit.ExternalGrantZones.IgnoreQueryFilters().CountAsync());
        Assert.Equal(
            2,
            await audit.ExternalGrantOwners.IgnoreQueryFilters().CountAsync());
        Assert.Equal(
            2,
            await audit.ExternalGrantObjects.IgnoreQueryFilters().CountAsync());
        Assert.Equal(
            2,
            await audit.FieldPolicies.IgnoreQueryFilters().CountAsync());
        Assert.Equal(
            2,
            await audit.FieldPolicyFields.IgnoreQueryFilters().CountAsync());
    }

    private static async Task<GraphIds> SeedAsync(
        Func<Guid, SpaceContext> createContext,
        Guid tenantId,
        Guid userId,
        Guid siteId,
        Guid floorId,
        Guid zoneId,
        string organizationName)
    {
        await using var context = createContext(tenantId);
        var organization = SpaceExternalOrganization.Create(
            tenantId,
            SpaceExternalOrganizationType.Customer,
            "SHARED-PARTNER",
            organizationName);
        var membership = SpaceExternalMembership.Create(
            tenantId,
            organization.Id,
            userId,
            SpaceExternalMembershipRole.Viewer,
            Now.AddDays(-1),
            null,
            SpaceExternalMembershipStatus.Active,
            userId,
            Now);
        var policy = SpaceFieldPolicy.Create(
            tenantId,
            "Shared portal policy",
            SpaceExternalOrganizationType.Customer,
            canExport: true);
        var field = SpaceFieldPolicyField.Create(
            tenantId,
            policy.Id,
            SpaceFieldPolicyResourceType.Stock,
            "materialNumber",
            SpaceFieldMaskingRule.Partial);
        var grant = SpaceExternalGrant.Create(
            tenantId,
            organization.Id,
            siteId,
            policy.Id,
            canExport: true,
            Now.AddDays(-1),
            null,
            SpaceExternalGrantStatus.Active);
        var grantFloor = SpaceExternalGrantFloor.Create(
            tenantId,
            grant.Id,
            floorId);
        var grantZone = SpaceExternalGrantZone.Create(
            tenantId,
            grant.Id,
            zoneId);
        var grantOwner = SpaceExternalGrantOwner.Create(
            tenantId,
            grant.Id,
            "SHARED-OWNER");
        var grantObject = SpaceExternalGrantObject.Create(
            tenantId,
            grant.Id,
            "task",
            "SHARED-TASK");

        context.AddRange(
            organization,
            membership,
            policy,
            field,
            grant,
            grantFloor,
            grantZone,
            grantOwner,
            grantObject);
        await context.SaveChangesAsync();
        return new GraphIds(
            organization.Id,
            membership.Id,
            grant.Id,
            grantFloor.Id,
            grantZone.Id,
            grantOwner.Id,
            grantObject.Id,
            policy.Id,
            field.Id);
    }

    private static async Task AssertVisibleGraphAsync(
        Func<Guid, SpaceContext> createContext,
        Guid tenantId,
        GraphIds own,
        GraphIds other)
    {
        await using var context = createContext(tenantId);
        Assert.Equal(
            own.OrganizationId,
            Assert.Single(await context.ExternalOrganizations.ToListAsync()).Id);
        Assert.Equal(
            own.MembershipId,
            Assert.Single(await context.ExternalMemberships.ToListAsync()).Id);
        Assert.Equal(
            own.GrantId,
            Assert.Single(await context.ExternalGrants.ToListAsync()).Id);
        Assert.Equal(
            own.GrantFloorId,
            Assert.Single(await context.ExternalGrantFloors.ToListAsync()).Id);
        Assert.Equal(
            own.GrantZoneId,
            Assert.Single(await context.ExternalGrantZones.ToListAsync()).Id);
        Assert.Equal(
            own.GrantOwnerId,
            Assert.Single(await context.ExternalGrantOwners.ToListAsync()).Id);
        Assert.Equal(
            own.GrantObjectId,
            Assert.Single(await context.ExternalGrantObjects.ToListAsync()).Id);
        Assert.Equal(
            own.PolicyId,
            Assert.Single(await context.FieldPolicies.ToListAsync()).Id);
        Assert.Equal(
            own.PolicyFieldId,
            Assert.Single(await context.FieldPolicyFields.ToListAsync()).Id);
        Assert.DoesNotContain(
            await context.ExternalOrganizations.ToListAsync(),
            item => item.Id == other.OrganizationId);
        Assert.DoesNotContain(
            await context.ExternalGrants.ToListAsync(),
            item => item.Id == other.GrantId);
        Assert.DoesNotContain(
            await context.FieldPolicies.ToListAsync(),
            item => item.Id == other.PolicyId);
    }

    private static SpaceContext CreateSqlContext(
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

    private sealed record GraphIds(
        Guid OrganizationId,
        Guid MembershipId,
        Guid GrantId,
        Guid GrantFloorId,
        Guid GrantZoneId,
        Guid GrantOwnerId,
        Guid GrantObjectId,
        Guid PolicyId,
        Guid PolicyFieldId);

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }
}
