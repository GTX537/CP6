using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceAccessEvaluatorTests
{
    [Fact]
    public async Task Multiple_grants_remain_complete_or_clauses()
    {
        var fixture = await CreateFixtureAsync();
        await using var context = fixture.Context;
        var floorA = Guid.NewGuid();
        var floorB = Guid.NewGuid();
        var grantA = AddGrant(
            context,
            fixture,
            fixture.SiteA,
            floorA,
            "OWNER-A");
        var grantB = AddGrant(
            context,
            fixture,
            fixture.SiteA,
            floorB,
            "OWNER-B");
        await context.SaveChangesAsync();
        var evaluator = CreateEvaluator(context, fixture);

        var allowed = await evaluator.EvaluateAsync(
            fixture.Principal,
            SpaceAccessAction.Read,
            new SpaceResource(
                fixture.TenantId,
                SpaceResourceType.Stock,
                fixture.SiteA,
                floorA,
                OwnerId: "owner-a"));
        var cartesianMix = await evaluator.EvaluateAsync(
            fixture.Principal,
            SpaceAccessAction.Read,
            new SpaceResource(
                fixture.TenantId,
                SpaceResourceType.Stock,
                fixture.SiteA,
                floorA,
                OwnerId: "owner-b"));

        Assert.True(allowed.Allowed);
        Assert.Equal([grantA.Id], allowed.MatchedGrantIds);
        Assert.False(cartesianMix.Allowed);
        Assert.Equal(
            SpaceErrorCodes.ExternalScopeDenied,
            cartesianMix.ReasonCode);
        Assert.Equal(2, cartesianMix.Scope.Clauses.Count);
        Assert.Contains(
            cartesianMix.Scope.Clauses,
            clause => clause.GrantId == grantB.Id);
    }

    [Fact]
    public async Task Organization_context_is_required_and_cannot_mix_grants()
    {
        var fixture = await CreateFixtureAsync();
        await using var context = fixture.Context;
        AddGrant(context, fixture, fixture.SiteA, Guid.NewGuid(), "OWNER-A");
        var otherOrganization = SpaceExternalOrganization.Create(
            fixture.TenantId,
            SpaceExternalOrganizationType.Supplier,
            "SUP-B",
            "Supplier B");
        context.ExternalOrganizations.Add(otherOrganization);
        context.ExternalMemberships.Add(SpaceExternalMembership.Create(
            fixture.TenantId,
            otherOrganization.Id,
            fixture.UserId,
            SpaceExternalMembershipRole.Viewer,
            fixture.Now.AddDays(-1),
            null,
            SpaceExternalMembershipStatus.Active,
            fixture.UserId,
            fixture.Now));
        var otherGrant = SpaceExternalGrant.Create(
            fixture.TenantId,
            otherOrganization.Id,
            fixture.SiteB,
            null,
            false,
            fixture.Now.AddDays(-1),
            null,
            SpaceExternalGrantStatus.Active);
        context.ExternalGrants.Add(otherGrant);
        await context.SaveChangesAsync();
        var evaluator = CreateEvaluator(context, fixture);

        var missingContext = await evaluator.BuildQueryScopeAsync(
            fixture.Principal with { OrganizationContextId = null },
            SpaceResourceType.Stock,
            null);
        var wrongSite = await evaluator.EvaluateAsync(
            fixture.Principal,
            SpaceAccessAction.Read,
            new SpaceResource(
                fixture.TenantId,
                SpaceResourceType.Stock,
                fixture.SiteB));

        Assert.False(missingContext.Allowed);
        Assert.Equal(
            SpaceErrorCodes.ExternalOrganizationContextRequired,
            missingContext.ReasonCode);
        Assert.False(wrongSite.Allowed);
        Assert.DoesNotContain(
            wrongSite.Scope.Clauses,
            clause => clause.GrantId == otherGrant.Id);
    }

    [Fact]
    public async Task Expired_membership_and_suspended_grant_fail_closed()
    {
        var fixture = await CreateFixtureAsync(
            membershipValidToUtc: new DateTime(
                2026,
                8,
                1,
                11,
                0,
                0,
                DateTimeKind.Utc));
        await using var context = fixture.Context;
        AddGrant(context, fixture, fixture.SiteA, Guid.NewGuid(), "OWNER-A");
        await context.SaveChangesAsync();
        var evaluator = CreateEvaluator(context, fixture);

        var expired = await evaluator.BuildQueryScopeAsync(
            fixture.Principal,
            SpaceResourceType.Stock,
            new SpaceOrganizationContext(fixture.Organization.Id));

        Assert.False(expired.Allowed);
        Assert.Equal(
            SpaceErrorCodes.ExternalMembershipInactive,
            expired.ReasonCode);
    }

    [Fact]
    public async Task Export_requires_matching_clause_with_export_enabled()
    {
        var fixture = await CreateFixtureAsync();
        await using var context = fixture.Context;
        var grant = AddGrant(
            context,
            fixture,
            fixture.SiteA,
            Guid.NewGuid(),
            "OWNER-A",
            canExport: false);
        await context.SaveChangesAsync();
        var evaluator = CreateEvaluator(context, fixture);
        var resource = new SpaceResource(
            fixture.TenantId,
            SpaceResourceType.Stock,
            fixture.SiteA,
            grantFloor(context, grant.Id),
            OwnerId: "OWNER-A");

        var read = await evaluator.EvaluateAsync(
            fixture.Principal,
            SpaceAccessAction.Read,
            resource);
        var export = await evaluator.EvaluateAsync(
            fixture.Principal,
            SpaceAccessAction.Export,
            resource);

        Assert.True(read.Allowed);
        Assert.False(export.Allowed);
    }

    [Fact]
    public async Task Export_requires_both_grant_and_active_policy_capability()
    {
        var fixture = await CreateFixtureAsync();
        await using var context = fixture.Context;
        var policy = SpaceFieldPolicy.Create(
            fixture.TenantId,
            "Customer stock export",
            SpaceExternalOrganizationType.Customer,
            canExport: false);
        context.FieldPolicies.Add(policy);
        var grant = AddGrant(
            context,
            fixture,
            fixture.SiteA,
            Guid.NewGuid(),
            "OWNER-A",
            canExport: true,
            fieldPolicyId: policy.Id);
        await context.SaveChangesAsync();
        var evaluator = CreateEvaluator(context, fixture);
        var resource = new SpaceResource(
            fixture.TenantId,
            SpaceResourceType.Stock,
            fixture.SiteA,
            grantFloor(context, grant.Id),
            OwnerId: "OWNER-A");

        var denied = await evaluator.EvaluateAsync(
            fixture.Principal,
            SpaceAccessAction.Export,
            resource);
        policy.Update(policy.Name, true, SpaceFieldPolicyStatus.Active);
        await context.SaveChangesAsync();
        var allowed = await evaluator.EvaluateAsync(
            fixture.Principal,
            SpaceAccessAction.Export,
            resource);

        Assert.False(denied.Allowed);
        Assert.True(allowed.Allowed);
        Assert.Equal([policy.Id], allowed.FieldPolicyIds);
    }

    [Fact]
    public async Task Authorization_version_is_bound_to_resource_scope()
    {
        var fixture = await CreateFixtureAsync();
        await using var context = fixture.Context;
        AddGrant(
            context,
            fixture,
            fixture.SiteA,
            Guid.NewGuid(),
            "OWNER-A");
        await context.SaveChangesAsync();
        var evaluator = CreateEvaluator(context, fixture);
        var organization = new SpaceOrganizationContext(
            fixture.Organization.Id);

        var scene = await evaluator.BuildQueryScopeAsync(
            fixture.Principal,
            SpaceResourceType.PublishedScene,
            organization);
        var stock = await evaluator.BuildQueryScopeAsync(
            fixture.Principal,
            SpaceResourceType.Stock,
            organization);
        var task = await evaluator.BuildQueryScopeAsync(
            fixture.Principal,
            SpaceResourceType.Task,
            organization);

        Assert.Equal(64, scene.AuthorizationVersion.Length);
        Assert.Equal(3, new[]
        {
            scene.AuthorizationVersion,
            stock.AuthorizationVersion,
            task.AuthorizationVersion,
        }.Distinct(StringComparer.Ordinal).Count());
    }

    private static Guid grantFloor(SpaceContext context, Guid grantId) =>
        context.ExternalGrantFloors.Local
            .Single(item => item.GrantId == grantId)
            .FloorLogicalId;

    private static SpaceExternalGrant AddGrant(
        SpaceContext context,
        Fixture fixture,
        Guid siteId,
        Guid floorId,
        string ownerId,
        bool canExport = false,
        Guid? fieldPolicyId = null)
    {
        var grant = SpaceExternalGrant.Create(
            fixture.TenantId,
            fixture.Organization.Id,
            siteId,
            fieldPolicyId,
            canExport,
            fixture.Now.AddDays(-1),
            null,
            SpaceExternalGrantStatus.Active);
        context.ExternalGrants.Add(grant);
        context.ExternalGrantFloors.Add(SpaceExternalGrantFloor.Create(
            fixture.TenantId,
            grant.Id,
            floorId));
        context.ExternalGrantOwners.Add(SpaceExternalGrantOwner.Create(
            fixture.TenantId,
            grant.Id,
            ownerId));
        return grant;
    }

    private static SpaceAccessEvaluator CreateEvaluator(
        SpaceContext context,
        Fixture fixture) =>
        new(
            context,
            fixture.Execution,
            new TestClock(fixture.Now));

    private static async Task<Fixture> CreateFixtureAsync(
        DateTime? membershipValidToUtc = null)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var execution = new TestExecutionContext(tenantId, userId);
        var options = new DbContextOptionsBuilder<SpaceContext>()
            .UseInMemoryDatabase(
                Guid.NewGuid().ToString("N"),
                SpaceTestDatabaseRoots.InMemory)
            .Options;
        var context = new SpaceContext(options, execution, new TestClock(now));
        var organization = SpaceExternalOrganization.Create(
            tenantId,
            SpaceExternalOrganizationType.Customer,
            "CUST-A",
            "Customer A");
        context.ExternalOrganizations.Add(organization);
        context.ExternalMemberships.Add(SpaceExternalMembership.Create(
            tenantId,
            organization.Id,
            userId,
            SpaceExternalMembershipRole.Viewer,
            now.AddDays(-2),
            membershipValidToUtc,
            SpaceExternalMembershipStatus.Active,
            userId,
            now));
        await context.SaveChangesAsync();
        return new Fixture(
            tenantId,
            userId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            now,
            execution,
            organization,
            new SpacePrincipal(tenantId, userId, true, organization.Id),
            context);
    }

    private sealed record Fixture(
        Guid TenantId,
        Guid UserId,
        Guid SiteA,
        Guid SiteB,
        DateTime Now,
        TestExecutionContext Execution,
        SpaceExternalOrganization Organization,
        SpacePrincipal Principal,
        SpaceContext Context);

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed class TestClock(DateTime now) : ISpaceClock
    {
        public DateTime UtcNow { get; } = now;
    }
}
