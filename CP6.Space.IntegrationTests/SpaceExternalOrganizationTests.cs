using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceExternalOrganizationTests
{
    private static readonly DateTime Now =
        new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Same_code_is_isolated_by_organization_type()
    {
        await using var context = NewSpaceContext(Guid.NewGuid());
        var service = NewService(context);

        var customer = await service.CreateOrganizationAsync(
            new CreateSpaceExternalOrganizationRequest(
                "Customer",
                "partner-001",
                "Customer"));
        var supplier = await service.CreateOrganizationAsync(
            new CreateSpaceExternalOrganizationRequest(
                "Supplier",
                "PARTNER-001",
                "Supplier"));

        Assert.NotEqual(customer.Id, supplier.Id);
        Assert.Equal(2, (await service.GetOrganizationsAsync(null, null)).Count);
        var conflict = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.CreateOrganizationAsync(
                new CreateSpaceExternalOrganizationRequest(
                    "Customer",
                    "Partner-001",
                    "Duplicate Customer")));
        Assert.Equal(
            SpaceErrorCodes.ExternalOrganizationConflict,
            conflict.Code);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task Membership_requires_one_current_row_but_allows_reinvite_after_revoke()
    {
        await using var context = NewSpaceContext(Guid.NewGuid());
        var service = NewService(context);
        var organization = await service.CreateOrganizationAsync(
            new CreateSpaceExternalOrganizationRequest(
                "ThirdPartyLogistics",
                "3pl-a",
                "3PL A"));
        var userId = Guid.NewGuid();
        var first = await service.CreateMembershipAsync(
            organization.Id,
            new CreateSpaceExternalMembershipRequest(
                userId,
                "Viewer"));

        var conflict = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            service.CreateMembershipAsync(
                organization.Id,
                new CreateSpaceExternalMembershipRequest(
                    userId,
                    "Viewer")));
        Assert.Equal(SpaceErrorCodes.ExternalMembershipConflict, conflict.Code);

        await service.UpdateMembershipAsync(
            organization.Id,
            first.Id,
            new UpdateSpaceExternalMembershipRequest(
                "Viewer",
                first.ValidFromUtc,
                first.ValidToUtc,
                "Revoked"));
        var replacement = await service.CreateMembershipAsync(
            organization.Id,
            new CreateSpaceExternalMembershipRequest(
                userId,
                "OperationsViewer",
                Status: "Active"));

        Assert.NotEqual(first.Id, replacement.Id);
        Assert.Equal(
            2,
            (await service.GetMembershipsAsync(
                organization.Id,
                null)).Count);
        var refreshed = await service.GetOrganizationAsync(organization.Id);
        Assert.Equal(4, refreshed.SecurityStamp);
    }

    [Fact]
    public async Task Query_filters_hide_organizations_and_memberships_cross_tenant()
    {
        var root = new InMemoryDatabaseRoot();
        var database = Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var organizationId = Guid.Empty;

        await using (var contextA = NewSpaceContext(tenantA, root, database))
        {
            var organization = SpaceExternalOrganization.Create(
                tenantA,
                SpaceExternalOrganizationType.Customer,
                "customer-a",
                "Customer A");
            var membership = SpaceExternalMembership.Create(
                tenantA,
                organization.Id,
                Guid.NewGuid(),
                SpaceExternalMembershipRole.Viewer,
                Now,
                null,
                SpaceExternalMembershipStatus.Active,
                Guid.NewGuid(),
                Now);
            organizationId = organization.Id;
            contextA.AddRange(organization, membership);
            await contextA.SaveChangesAsync();
        }

        await using var contextB = NewSpaceContext(tenantB, root, database);
        Assert.Empty(await contextB.ExternalOrganizations.ToListAsync());
        Assert.Empty(await contextB.ExternalMemberships.ToListAsync());
        Assert.Single(
            await contextB.ExternalOrganizations
                .IgnoreQueryFilters()
                .Where(item => item.Id == organizationId)
                .ToListAsync());
        Assert.Single(
            await contextB.ExternalMemberships
                .IgnoreQueryFilters()
                .ToListAsync());
    }

    [Fact]
    public void Ef_model_has_composite_tenant_constraints_and_filters()
    {
        using var context = NewSpaceContext(Guid.NewGuid());
        var organization = context.Model.FindEntityType(
            typeof(SpaceExternalOrganization))!;
        var membership = context.Model.FindEntityType(
            typeof(SpaceExternalMembership))!;

        Assert.Equal("Space_ExternalOrganization", organization.GetTableName());
        Assert.Equal("Space_ExternalMembership", membership.GetTableName());
        Assert.NotNull(organization.GetQueryFilter());
        Assert.NotNull(membership.GetQueryFilter());
        Assert.Contains(
            organization.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        "TenantId",
                        "Type",
                        "NormalizedCode",
                    ]));
        Assert.Contains(
            membership.GetIndexes(),
            index =>
                index.IsUnique &&
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        "TenantId",
                        "OrganizationId",
                        "UserId",
                    ]));
        Assert.Contains(
            membership.GetForeignKeys(),
            foreignKey =>
                foreignKey.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        "TenantId",
                        "OrganizationId",
                    ]));
        Assert.True(
            organization.FindProperty(nameof(SpaceExternalOrganization.RowVersion))!
                .IsConcurrencyToken);
        Assert.True(
            membership.FindProperty(nameof(SpaceExternalMembership.RowVersion))!
                .IsConcurrencyToken);
    }

    [Fact]
    public async Task Cp6_reference_validator_fails_closed_for_cross_tenant_user()
    {
        var root = new InMemoryDatabaseRoot();
        var database = Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userA = NewUser(tenantA, "user-a");
        var userB = NewUser(tenantB, "user-b");

        await using (var dbA = NewLegacyContext(tenantA, root, database))
        {
            dbA.Sys_Users.Add(userA);
            await dbA.SaveChangesAsync();
        }
        await using (var dbB = NewLegacyContext(tenantB, root, database))
        {
            dbB.Sys_Users.Add(userB);
            await dbB.SaveChangesAsync();
        }

        await using var current = NewLegacyContext(tenantA, root, database);
        var validator = new Cp6SpaceExternalReferenceValidator(
            current,
            new TestExecutionContext(tenantA, Guid.NewGuid()));
        await validator.EnsureUserAsync(tenantA, userA.Id);
        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            validator.EnsureUserAsync(tenantA, userB.Id));
        Assert.Equal(SpaceErrorCodes.ExternalReferenceNotFound, error.Code);
        Assert.Equal(404, error.StatusCode);
    }

    private static SpaceExternalOrganizationService NewService(
        SpaceContext context) =>
        new(
            context,
            new TestExecutionContext(
                context.CurrentTenantId,
                Guid.NewGuid()),
            new FixedClock(),
            new AcceptingReferenceValidator());

    private static SpaceContext NewSpaceContext(
        Guid tenantId,
        InMemoryDatabaseRoot? root = null,
        string? database = null) =>
        new(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(
                    database ?? Guid.NewGuid().ToString("N"),
                    root ?? new InMemoryDatabaseRoot())
                .Options,
            new TestExecutionContext(tenantId, Guid.NewGuid()),
            new FixedClock());

    private static CP6Context NewLegacyContext(
        Guid tenantId,
        InMemoryDatabaseRoot root,
        string database) =>
        new(
            new DbContextOptionsBuilder<CP6Context>()
                .UseInMemoryDatabase(database, root)
                .Options,
            new TenantContext { CurrentTenantId = tenantId });

    private static Sys_User NewUser(Guid tenantId, string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserName = name,
            Password = "test-only",
            Enable = true,
        };

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class AcceptingReferenceValidator :
        ISpaceExternalReferenceValidator
    {
        public Task EnsureUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task EnsureBusinessPartnerAsync(
            Guid tenantId,
            SpaceExternalOrganizationType organizationType,
            string businessPartnerType,
            Guid businessPartnerId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
