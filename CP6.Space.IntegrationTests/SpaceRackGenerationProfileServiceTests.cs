using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceRackGenerationProfileServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 8, 19, 15, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Tenant_create_is_idempotent_and_exact_version_is_readable()
    {
        await using var fixture = CreateFixture();
        var request = Request("TENANT-RACK");

        var created = await fixture.Service.CreateAsync(
            request,
            "profile-create-1");
        var replay = await fixture.Service.CreateAsync(
            request,
            "profile-create-1");
        var exact = await fixture.Service.GetVersionAsync(
            created.Profile.LatestVersion.Id);

        Assert.False(created.IdempotentReplay);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(created.Profile.Id, replay.Profile.Id);
        Assert.Equal(created.Profile.LatestVersion.Id, exact.Id);
        Assert.Equal(8, exact.LocationCount);
        Assert.Equal(64, exact.ContentHash.Length);
        Assert.Single(await fixture.Context.RackGenerationProfiles.ToListAsync());
        Assert.Single(await fixture.Context.RackGenerationProfileVersions
            .ToListAsync());
        Assert.Single(await fixture.Context.IdempotencyRecords.ToListAsync());
    }

    [Fact]
    public async Task List_returns_system_and_current_tenant_latest_versions()
    {
        await using var fixture = CreateFixture();
        var system = SpaceRackGenerationProfile.CreateSystem(
            "SYSTEM-RACK",
            "System rack",
            null,
            fixture.Execution.ActorId,
            Now);
        var systemVersion = SpaceRackGenerationProfileVersion.CreateReady(
            system,
            1,
            2400,
            1000,
            5000,
            [new(1, 0, 2200, 4, 2, 600, 500, 100)],
            fixture.Execution.ActorId,
            Now);
        fixture.Context.AddRange(system, systemVersion);
        await fixture.Context.SaveChangesAsync();
        _ = await fixture.Service.CreateAsync(
            Request("TENANT-RACK"),
            "profile-create-list");

        var page = await fixture.Service.GetProfilesAsync(
            scope: null,
            limit: 50,
            cursor: null);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(
            ["System", "Tenant"],
            page.Items.Select(item => item.Scope));
        Assert.All(page.Items, item =>
            Assert.Equal("Ready", item.LatestVersion.Status));
    }

    [Fact]
    public async Task Tenant_api_rejects_system_scope()
    {
        await using var fixture = CreateFixture();

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.CreateAsync(
                Request("SYSTEM-RACK") with { Scope = "System" },
                "profile-system-denied"));

        Assert.Equal(
            SpaceErrorCodes.RackGenerationProfileScopeDenied,
            error.Code);
        Assert.Empty(await fixture.Context.RackGenerationProfiles.ToListAsync());
    }

    private static CreateSpaceRackGenerationProfileRequest Request(
        string code) =>
        new(
            code,
            code,
            2400,
            1000,
            5000,
            [new(1, 0, 2200, 4, 2, 600, 500, 100, 1000)]);

    private static Fixture CreateFixture()
    {
        var execution = new TestExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid());
        var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString("N"),
                    SpaceTestDatabaseRoots.InMemory)
                .Options,
            execution,
            new FixedClock());
        var service = new SpaceRackGenerationProfileService(
            context,
            execution,
            new FixedClock(),
            new TestCursorCodec());
        return new Fixture(context, execution, service);
    }

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId) : ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class TestCursorCodec : ISpaceCursorCodec
    {
        public string Encode(SpaceCursorState state) =>
            $"{state.Resource}:{state.FilterHash}:{state.Offset}";

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash) =>
            throw new NotSupportedException();
    }

    private sealed record Fixture(
        SpaceContext Context,
        TestExecutionContext Execution,
        SpaceRackGenerationProfileService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
