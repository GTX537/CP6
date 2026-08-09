using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceExternalGrantServiceTests
{
    [Fact]
    public async Task Create_and_update_grant_validate_published_scope_and_version()
    {
        await using var fixture = await CreateFixtureAsync();
        var service = new SpaceExternalGrantService(
            fixture.Context,
            fixture.Execution,
            fixture.Clock);

        var created = await service.CreateGrantAsync(
            fixture.Organization.Id,
            new CreateSpaceExternalGrantRequest(
                fixture.SiteId,
                [fixture.FloorA],
                [fixture.ZoneA],
                [" owner-a "],
                [new SpaceExternalGrantObjectRequest(" task ", " pick-1 ")],
                CanExport: true,
                ValidFromUtc: fixture.Now));

        Assert.Equal(1, created.GrantVersion);
        Assert.Equal([fixture.FloorA], created.FloorLogicalIds);
        Assert.Equal([fixture.ZoneA], created.ZoneLogicalIds);
        Assert.Equal(["owner-a"], created.OwnerIds);
        Assert.Equal("task", Assert.Single(created.Objects).BusinessObjectType);
        Assert.Equal(2, fixture.Organization.SecurityStamp);

        var updated = await service.UpdateGrantAsync(
            fixture.Organization.Id,
            created.Id,
            new UpdateSpaceExternalGrantRequest(
                fixture.SiteId,
                [fixture.FloorB],
                [fixture.ZoneB],
                ["OWNER-B"],
                [new SpaceExternalGrantObjectRequest("Task", "Pick-2")],
                null,
                false,
                fixture.Now,
                null,
                "Suspended"));

        Assert.Equal(2, updated.GrantVersion);
        Assert.Equal("Suspended", updated.Status);
        Assert.Equal([fixture.FloorB], updated.FloorLogicalIds);
        Assert.Equal([fixture.ZoneB], updated.ZoneLogicalIds);
        Assert.Equal(3, fixture.Organization.SecurityStamp);
        Assert.Equal(
            4,
            await fixture.Context.ExternalGrantFloors
                .IgnoreQueryFilters()
                .CountAsync(item => item.GrantId == created.Id) +
            await fixture.Context.ExternalGrantZones
                .IgnoreQueryFilters()
                .CountAsync(item => item.GrantId == created.Id));
        Assert.Single(await fixture.Context.ExternalGrantFloors
            .Where(item => item.GrantId == created.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task Zone_outside_selected_floor_is_rejected()
    {
        await using var fixture = await CreateFixtureAsync();
        var service = new SpaceExternalGrantService(
            fixture.Context,
            fixture.Execution,
            fixture.Clock);

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.CreateGrantAsync(
                fixture.Organization.Id,
                new CreateSpaceExternalGrantRequest(
                    fixture.SiteId,
                    [fixture.FloorA],
                    [fixture.ZoneB])));

        Assert.Equal(SpaceErrorCodes.ExternalGrantScopeInvalid, error.Code);
        Assert.Equal(422, error.StatusCode);
    }

    [Fact]
    public async Task Unknown_site_and_invalid_field_policy_fail_closed()
    {
        await using var fixture = await CreateFixtureAsync();
        var service = new SpaceExternalGrantService(
            fixture.Context,
            fixture.Execution,
            fixture.Clock);

        var unknownSite = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.CreateGrantAsync(
                fixture.Organization.Id,
                new CreateSpaceExternalGrantRequest(Guid.NewGuid())));
        var missingPolicy = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.CreateGrantAsync(
                fixture.Organization.Id,
                new CreateSpaceExternalGrantRequest(
                    fixture.SiteId,
                    FieldPolicyId: Guid.NewGuid())));
        var vendorPolicy = SpaceFieldPolicy.Create(
            fixture.TenantId,
            "Vendor portal",
            SpaceExternalOrganizationType.Supplier,
            false);
        fixture.Context.FieldPolicies.Add(vendorPolicy);
        await fixture.Context.SaveChangesAsync();
        var wrongAudience = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.CreateGrantAsync(
                fixture.Organization.Id,
                new CreateSpaceExternalGrantRequest(
                    fixture.SiteId,
                    FieldPolicyId: vendorPolicy.Id)));

        Assert.Equal(404, unknownSite.StatusCode);
        Assert.Equal(SpaceErrorCodes.ExternalGrantScopeInvalid, unknownSite.Code);
        Assert.Equal(SpaceErrorCodes.FieldPolicyDenied, missingPolicy.Code);
        Assert.Equal(422, missingPolicy.StatusCode);
        Assert.Equal(SpaceErrorCodes.FieldPolicyDenied, wrongAudience.Code);
        Assert.Equal(422, wrongAudience.StatusCode);
    }

    [Fact]
    public async Task Active_matching_field_policy_can_be_attached_to_a_grant()
    {
        await using var fixture = await CreateFixtureAsync();
        var policy = SpaceFieldPolicy.Create(
            fixture.TenantId,
            "Customer portal",
            SpaceExternalOrganizationType.Customer,
            false);
        fixture.Context.FieldPolicies.Add(policy);
        await fixture.Context.SaveChangesAsync();
        var service = new SpaceExternalGrantService(
            fixture.Context,
            fixture.Execution,
            fixture.Clock);

        var created = await service.CreateGrantAsync(
            fixture.Organization.Id,
            new CreateSpaceExternalGrantRequest(
                fixture.SiteId,
                FieldPolicyId: policy.Id));

        Assert.Equal(policy.Id, created.FieldPolicyId);
    }

    [Fact]
    public async Task Ef_model_freezes_grant_tables_and_tenant_foreign_keys()
    {
        await using var fixture = await CreateFixtureAsync();
        var model = fixture.Context.Model;
        var grant = model.FindEntityType(typeof(SpaceExternalGrant))!;
        var floor = model.FindEntityType(typeof(SpaceExternalGrantFloor))!;
        var owner = model.FindEntityType(typeof(SpaceExternalGrantOwner))!;

        Assert.Equal("Space_ExternalGrant", grant.GetTableName());
        Assert.Equal("Space_ExternalGrantFloor", floor.GetTableName());
        Assert.Equal("Space_ExternalGrantOwner", owner.GetTableName());
        Assert.Contains(
            grant.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Select(item => item.Name)
                .SequenceEqual(["TenantId", "OrganizationId"]));
        Assert.Contains(
            floor.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Select(item => item.Name)
                .SequenceEqual(["TenantId", "GrantId"]));
        Assert.True(grant.FindProperty(nameof(SpaceExternalGrant.RowVersion))!
            .IsConcurrencyToken);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var execution = new TestExecutionContext(tenantId, actorId);
        var clock = new TestClock(now);
        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseInMemoryDatabase(
                Guid.NewGuid().ToString("N"),
                SpaceTestDatabaseRoots.InMemory)
            .Options;
        var context = new SpaceContext(options, execution, clock);
        var organization = SpaceExternalOrganization.Create(
            tenantId,
            SpaceExternalOrganizationType.Customer,
            "CUST-A",
            "Customer A");
        var model = SpaceModel.Create(tenantId, siteId);
        var version = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "Published scope");
        var floorA = Guid.NewGuid();
        var floorB = Guid.NewGuid();
        var zoneA = Guid.NewGuid();
        var zoneB = Guid.NewGuid();
        context.AddRange(
            organization,
            model,
            version,
            SpaceFloorRevision.Create(
                tenantId,
                version.Id,
                floorA,
                siteId,
                1,
                "F1",
                "Floor 1"),
            SpaceFloorRevision.Create(
                tenantId,
                version.Id,
                floorB,
                siteId,
                2,
                "F2",
                "Floor 2"),
            SpaceZoneRevision.Create(
                tenantId,
                version.Id,
                zoneA,
                floorA,
                "ZA",
                0),
            SpaceZoneRevision.Create(
                tenantId,
                version.Id,
                zoneB,
                floorB,
                "ZB",
                0));
        await context.SaveChangesAsync();

        var hash = new string('a', 64);
        version.BeginValidation();
        version.MarkReady(hash, "rules-v1", hash);
        version.BeginPublishing();
        version.MarkPublished(actorId, now);
        model.SetPublishedVersion(version, hash);
        await context.SaveChangesAsync();

        return new Fixture(
            context,
            execution,
            clock,
            organization,
            tenantId,
            siteId,
            floorA,
            floorB,
            zoneA,
            zoneB,
            now);
    }

    private sealed record Fixture(
        SpaceContext Context,
        TestExecutionContext Execution,
        TestClock Clock,
        SpaceExternalOrganization Organization,
        Guid TenantId,
        Guid SiteId,
        Guid FloorA,
        Guid FloorB,
        Guid ZoneA,
        Guid ZoneB,
        DateTime Now) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed class TestClock(DateTime now) : ISpaceClock
    {
        public DateTime UtcNow { get; } = now;
    }
}
