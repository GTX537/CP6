using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpacePersonnelEventServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Batch_is_normalized_projected_and_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = Request(
            Position(" position-1 ", Now.AddMinutes(-2), 1),
            Work(" work-1 ", Now.AddMinutes(-1), "Busy", 2));

        var accepted = await fixture.Service.IngestAsync(fixture.SiteId, request);
        var replay = await fixture.Service.IngestAsync(fixture.SiteId, request);

        Assert.Equal(2, accepted.AcceptedCount);
        Assert.Equal(0, accepted.DuplicateCount);
        Assert.Equal("PDA-01", accepted.SourceId);
        Assert.Equal("Real", accepted.SourceKind);
        Assert.Equal(0, replay.AcceptedCount);
        Assert.Equal(2, replay.DuplicateCount);
        Assert.All(replay.Receipts, value => Assert.Equal("Duplicate", value.Outcome));
        Assert.Equal(2, await fixture.Context.PersonnelEvents.CountAsync());
        var state = await fixture.Context.PersonnelStates.SingleAsync();
        Assert.Equal("PERSON-01", state.PersonExternalId);
        Assert.Equal(SpacePersonnelWorkState.Busy, state.WorkState);
        Assert.Equal(10m, state.XMillimeters);
        Assert.NotNull(state.PositionEventId);
        Assert.NotNull(state.WorkStateEventId);
    }

    [Fact]
    public async Task Reused_source_event_identity_with_other_content_is_rejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.IngestAsync(
            fixture.SiteId,
            Request(Position("position-1", Now.AddMinutes(-2), 1)));

        var exception = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.IngestAsync(
                fixture.SiteId,
                Request(Position("position-1", Now.AddMinutes(-1), 2))));

        Assert.Equal(SpaceErrorCodes.PersonnelEventConflict, exception.Code);
        Assert.Single(await fixture.Context.PersonnelEvents.ToListAsync());
    }

    [Fact]
    public async Task Source_identity_cannot_switch_between_real_and_simulated()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.IngestAsync(
            fixture.SiteId,
            Request(Position("position-1", Now.AddMinutes(-2), 1)));
        var simulated = Request(
            Position("position-2", Now.AddMinutes(-1), 2)) with
        {
            SourceKind = "Simulated",
        };

        var exception = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.IngestAsync(fixture.SiteId, simulated));

        Assert.Equal(SpaceErrorCodes.PersonnelEventConflict, exception.Code);
        Assert.Single(await fixture.Context.PersonnelEvents.ToListAsync());
    }

    [Fact]
    public async Task Stale_position_is_ledgered_without_regressing_current_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.IngestAsync(
            fixture.SiteId,
            Request(Position("position-2", Now.AddMinutes(-1), 2)));

        var response = await fixture.Service.IngestAsync(
            fixture.SiteId,
            Request(Position("position-1", Now.AddMinutes(-2), 1)));

        var receipt = Assert.Single(response.Receipts);
        Assert.Equal("AcceptedStale", receipt.Outcome);
        Assert.False(receipt.ProjectionApplied);
        Assert.Equal(2, await fixture.Context.PersonnelEvents.CountAsync());
        var state = await fixture.Context.PersonnelStates.SingleAsync();
        Assert.Equal("POSITION-2", state.PositionSourceEventId);
    }

    [Fact]
    public async Task Invalid_shape_future_clock_and_external_principal_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var invalid = Request(Work(
            "work-1",
            Now.AddMinutes(6),
            "Busy",
            1));

        var shape = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.IngestAsync(fixture.SiteId, invalid));
        Assert.Equal(SpaceErrorCodes.PersonnelEventInvalid, shape.Code);

        await using var external = await Fixture.CreateAsync(isExternal: true);
        var denied = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            external.Service.IngestAsync(
                external.SiteId,
                Request(Work("work-1", Now.AddMinutes(-1), "Idle", 1))));
        Assert.Equal(SpaceErrorCodes.ExternalSubjectDenied, denied.Code);
        Assert.Empty(await external.Context.PersonnelEvents.ToListAsync());
    }

    [Fact]
    public async Task Missing_or_cross_tenant_site_is_not_visible()
    {
        await using var fixture = await Fixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.IngestAsync(
                Guid.NewGuid(),
                Request(Work("work-1", Now.AddMinutes(-1), "Idle", 1))));

        Assert.Equal(SpaceErrorCodes.PersonnelSiteNotFound, exception.Code);
        Assert.Empty(await fixture.Context.PersonnelEvents.ToListAsync());
    }

    [Fact]
    public async Task Ef_model_has_append_ledger_and_unique_current_projection_keys()
    {
        await using var fixture = await Fixture.CreateAsync();
        var eventType = fixture.Context.Model.FindEntityType(
            typeof(SpacePersonnelEvent))!;
        var stateType = fixture.Context.Model.FindEntityType(
            typeof(SpacePersonnelCurrentState))!;

        Assert.Equal("Space_PersonnelEvent", eventType.GetTableName());
        Assert.Contains(eventType.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(value => value.Name)
                .SequenceEqual(new[]
                {
                    "TenantId", "SiteId", "SourceId", "SourceEventId",
                }));
        Assert.Equal("Space_PersonnelState", stateType.GetTableName());
        Assert.Contains(stateType.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(value => value.Name)
                .SequenceEqual(new[]
                {
                    "TenantId", "SiteId", "SourceId", "PersonExternalId",
                }));
    }

    private static IngestSpacePersonnelEventsRequest Request(
        params SpacePersonnelEventInput[] events) =>
        new(
            SpacePersonnelEventContract.Version,
            " pda-01 ",
            "Real",
            events);

    private static SpacePersonnelEventInput Position(
        string id,
        DateTime occurredAtUtc,
        long sequence) =>
        new(
            id,
            " person-01 ",
            null,
            "PositionObserved",
            null,
            Guid.NewGuid(),
            null,
            10m,
            20m,
            0m,
            50m,
            sequence,
            new DateTimeOffset(occurredAtUtc));

    private static SpacePersonnelEventInput Work(
        string id,
        DateTime occurredAtUtc,
        string state,
        long sequence) =>
        new(
            id,
            " person-01 ",
            null,
            "WorkStateChanged",
            state,
            null,
            null,
            null,
            null,
            null,
            null,
            sequence,
            new DateTimeOffset(occurredAtUtc));

    private sealed record Fixture(
        SpaceContext Context,
        SpacePersonnelEventService Service,
        Guid SiteId) : IAsyncDisposable
    {
        public static async Task<Fixture> CreateAsync(bool isExternal = false)
        {
            var tenantId = Guid.NewGuid();
            var execution = new TestExecutionContext(
                tenantId,
                Guid.NewGuid(),
                isExternal);
            var context = new SpaceContext(
                new DbContextOptionsBuilder<SpaceContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                    .Options,
                execution,
                new FixedClock());
            var siteId = Guid.NewGuid();
            context.Models.Add(SpaceModel.Create(tenantId, siteId));
            await context.SaveChangesAsync();
            var service = new SpacePersonnelEventService(
                context,
                execution,
                new FixedClock(),
                new AllowAccess());
            return new Fixture(context, service, siteId);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId,
        bool IsExternal) : ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class AllowAccess : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
        }
    }
}
