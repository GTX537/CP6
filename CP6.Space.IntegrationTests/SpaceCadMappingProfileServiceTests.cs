using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceCadMappingProfileServiceTests
{
    [Fact]
    public async Task System_profile_can_be_copied_and_new_versions_are_immutable()
    {
        await using var fixture = CreateFixture();
        var system = await fixture.Service.GetProfileAsync(
            StandardSpaceCadMappingProfileCatalog.SystemProfile.ProfileId);
        var request = new SaveSpaceCadMappingProfileRequest(
            null,
            "Tenant warehouse profile",
            IsEnabled: true,
            system.Rules,
            CopyFromProfileId: system.Id,
            CopyFromVersion: system.Version);

        var created = await fixture.Service.SaveProfileAsync(request, "cad-profile-v1");
        var replay = await fixture.Service.SaveProfileAsync(request, "cad-profile-v1");
        var updatedRules = created.Profile.Rules.Select(rule =>
            rule.RuleId == "rack-layer"
                ? rule with { ConfidenceWeight = .88m }
                : rule).ToArray();
        var updated = await fixture.Service.SaveProfileAsync(
            new(
                created.Profile.Id,
                "Tenant warehouse profile",
                IsEnabled: true,
                updatedRules,
                created.Profile.RowVersion),
            "cad-profile-v2");

        Assert.True(created.Created);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(created.Profile.Id, replay.Profile.Id);
        Assert.False(updated.Created);
        Assert.Equal(2, updated.Profile.Version);
        Assert.Equal(system.Id, updated.Profile.BasedOnProfileId);
        Assert.Equal(system.Version, updated.Profile.BasedOnVersion);
        Assert.Equal(
            .90m,
            (await fixture.Service.GetProfileAsync(created.Profile.Id, 1))
                .Rules.Single(rule => rule.RuleId == "rack-layer")
                .ConfidenceWeight);
        Assert.Equal(
            .88m,
            (await fixture.Service.GetProfileAsync(created.Profile.Id, 2))
                .Rules.Single(rule => rule.RuleId == "rack-layer")
                .ConfidenceWeight);

        var storedV1 = await fixture.Context.CadMappingProfileVersions
            .SingleAsync(item => item.ProfileId == created.Profile.Id &&
                item.Version == 1);
        fixture.Context.Entry(storedV1).State = EntityState.Deleted;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task Catalog_returns_current_tenant_profile_and_historical_versions()
    {
        await using var fixture = CreateFixture();
        var system = StandardSpaceCadMappingProfileCatalog.SystemProfile;
        var created = await fixture.Service.SaveProfileAsync(
            new(
                null,
                "Disabled draft profile",
                IsEnabled: false,
                system.Rules,
                CopyFromProfileId: system.ProfileId,
                CopyFromVersion: system.Version),
            "disabled-v1");
        var updated = await fixture.Service.SaveProfileAsync(
            new(
                created.Profile.Id,
                "Enabled profile",
                IsEnabled: true,
                created.Profile.Rules,
                created.Profile.RowVersion),
            "enabled-v2");

        var current = await fixture.Service.ListAsync();
        var historical = await fixture.Service.FindAsync(created.Profile.Id, 1);

        Assert.Equal(2, current.Count);
        Assert.Contains(current, item => item.Scope == SpaceCadMappingScope.System);
        Assert.Contains(
            current,
            item => item.ProfileId == created.Profile.Id &&
                item.Version == 2 && item.IsEnabled);
        Assert.NotNull(historical);
        Assert.False(historical!.IsEnabled);
        Assert.Equal(updated.Profile.DefinitionSha256,
            current.Single(item => item.ProfileId == created.Profile.Id)
                .DefinitionSha256);
    }

    [Fact]
    public async Task System_profile_is_read_only_and_updates_require_rowversion()
    {
        await using var fixture = CreateFixture();
        var system = StandardSpaceCadMappingProfileCatalog.SystemProfile;
        var readOnly = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.SaveProfileAsync(
                new(
                    system.ProfileId,
                    system.Name,
                    system.IsEnabled,
                    system.Rules),
                "edit-system"));
        Assert.Equal(SpaceErrorCodes.CadMappingProfileReadOnly, readOnly.Code);

        var created = await fixture.Service.SaveProfileAsync(
            new(null, "Private", true, system.Rules),
            "private-v1");
        var conflict = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.SaveProfileAsync(
                new(created.Profile.Id, "Private", true, system.Rules),
                "private-v2"));
        Assert.Equal(SpaceErrorCodes.CadMappingProfileConflict, conflict.Code);
    }

    [Fact]
    public async Task Tenant_profiles_are_not_visible_or_copyable_across_tenants()
    {
        var database = Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var fixtureA = CreateFixture(database, tenantA);
        var system = StandardSpaceCadMappingProfileCatalog.SystemProfile;
        var saved = await fixtureA.Service.SaveProfileAsync(
            new(null, "Tenant A", true, system.Rules),
            "tenant-a-v1");

        await using var fixtureB = CreateFixture(database, tenantB);
        var profiles = await fixtureB.Service.GetProfilesAsync();
        var notFound = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixtureB.Service.GetProfileAsync(saved.Profile.Id));
        var copyDenied = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixtureB.Service.SaveProfileAsync(
                new(
                    null,
                    "Cross tenant copy",
                    true,
                    system.Rules,
                    CopyFromProfileId: saved.Profile.Id,
                    CopyFromVersion: 1),
                "cross-tenant-copy"));

        Assert.Single(profiles);
        Assert.Equal(SpaceCadMappingScope.System, profiles[0].Scope);
        Assert.Equal(SpaceErrorCodes.CadMappingProfileNotFound, notFound.Code);
        Assert.Equal(SpaceErrorCodes.CadMappingProfileNotFound, copyDenied.Code);
        Assert.Empty(await fixtureB.Context.CadMappingProfiles.ToListAsync());
        Assert.Empty(await fixtureB.Context.CadMappingProfileVersions.ToListAsync());
        Assert.Equal(
            1,
            await fixtureB.Context.CadMappingProfileVersions
                .IgnoreQueryFilters()
                .CountAsync());
    }

    [Fact]
    public async Task Ef_model_freezes_tenant_relationships_and_append_only_versions()
    {
        await using var fixture = CreateFixture();
        var profile = fixture.Context.Model.FindEntityType(
            typeof(SpaceCadMappingProfile))!;
        var version = fixture.Context.Model.FindEntityType(
            typeof(SpaceCadMappingProfileVersion))!;

        Assert.Equal("Space_LayerMappingProfile", profile.GetTableName());
        Assert.Equal("Space_LayerMappingProfileVersion", version.GetTableName());
        Assert.True(profile.FindProperty(nameof(SpaceCadMappingProfile.RowVersion))!
            .IsConcurrencyToken);
        Assert.NotNull(profile.GetQueryFilter());
        Assert.NotNull(version.GetQueryFilter());
        Assert.Contains(
            version.GetForeignKeys(),
            foreignKey => foreignKey.Properties.Select(property => property.Name)
                .SequenceEqual(["TenantId", "ProfileId"]));
        Assert.Contains(
            version.GetIndexes(),
            index => index.IsUnique && index.Properties
                .Select(property => property.Name)
                .SequenceEqual(["TenantId", "ProfileId", "Version"]));
    }

    private static Fixture CreateFixture(
        string? database = null,
        Guid? tenantId = null)
    {
        var tenant = tenantId ?? Guid.NewGuid();
        var execution = new TestExecutionContext(tenant, Guid.NewGuid());
        var clock = new FixedClock();
        var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(
                    database ?? Guid.NewGuid().ToString("N"),
                    SpaceTestDatabaseRoots.InMemory)
                .Options,
            execution,
            clock);
        return new Fixture(
            context,
            new SpaceCadMappingProfileService(context, execution, clock));
    }

    private sealed record Fixture(
        SpaceContext Context,
        SpaceCadMappingProfileService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow { get; } =
            new(2026, 8, 16, 14, 0, 0, DateTimeKind.Utc);
    }
}
