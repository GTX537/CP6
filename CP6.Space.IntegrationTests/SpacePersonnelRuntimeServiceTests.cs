using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpacePersonnelRuntimeServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Current_state_exposes_freshness_provenance_filters_and_cursor()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Ingest.IngestAsync(
            fixture.SiteId,
            Request(
                "pda-01",
                "person-01",
                Position("position-1", Now.AddMinutes(-2), 10m, 1),
                Work("work-1", Now.AddMinutes(-1), "Busy", 2)));
        await fixture.Ingest.IngestAsync(
            fixture.SiteId,
            Request(
                "pda-01",
                "person-02",
                Position("position-2", Now.AddMinutes(-10), 20m, 1),
                Work("work-2", Now.AddMinutes(-10), "Idle", 2)));

        var first = await fixture.Runtime.GetCurrentAsync(
            fixture.SiteId,
            "real",
            null,
            null,
            1,
            null);
        var second = await fixture.Runtime.GetCurrentAsync(
            fixture.SiteId,
            "real",
            null,
            null,
            1,
            first.NextCursor);
        var busy = await fixture.Runtime.GetCurrentAsync(
            fixture.SiteId,
            null,
            "busy",
            null,
            100,
            null);

        Assert.Equal(300, first.FreshnessThresholdSeconds);
        Assert.NotNull(first.NextCursor);
        var fresh = Assert.Single(first.Items);
        Assert.Equal("PERSON-01", fresh.PersonExternalId);
        Assert.Equal("POSITION-1", fresh.PositionSourceEventId);
        Assert.NotNull(fresh.PositionEventId);
        Assert.False(fresh.PositionIsStale);
        Assert.False(fresh.WorkStateIsStale);
        Assert.False(fresh.IsSimulated);
        var stale = Assert.Single(second.Items);
        Assert.Equal("PERSON-02", stale.PersonExternalId);
        Assert.True(stale.PositionIsStale);
        Assert.True(stale.WorkStateIsStale);
        Assert.Null(second.NextCursor);
        Assert.Equal("PERSON-01", Assert.Single(busy.Items).PersonExternalId);
    }

    [Fact]
    public async Task Trajectory_is_authoritative_ordered_retained_and_paginated()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Ingest.IngestAsync(
            fixture.SiteId,
            Request(
                "pda-01",
                "person-01",
                Position("position-2", Now.AddMinutes(-2), 20m, 2),
                Position("position-1", Now.AddMinutes(-3), 10m, 1),
                Work("work-1", Now.AddMinutes(-1), "Busy", 3)));

        var first = await fixture.Runtime.GetTrajectoryAsync(
            fixture.SiteId,
            " pda-01 ",
            " person-01 ",
            new DateTimeOffset(Now.AddHours(-1)),
            new DateTimeOffset(Now),
            1,
            null);
        var second = await fixture.Runtime.GetTrajectoryAsync(
            fixture.SiteId,
            "PDA-01",
            "PERSON-01",
            first.FromUtc,
            first.ToUtc,
            1,
            first.NextCursor);

        Assert.Equal("Real", first.SourceKind);
        Assert.Equal(new DateTimeOffset(Now.AddDays(-30)), first.RetentionCutoffUtc);
        Assert.Equal("POSITION-1", Assert.Single(first.Items).SourceEventId);
        Assert.NotNull(first.NextCursor);
        Assert.Equal("POSITION-2", Assert.Single(second.Items).SourceEventId);
        Assert.Null(second.NextCursor);
        Assert.All(first.Items.Concat(second.Items), value =>
        {
            Assert.NotEqual(Guid.Empty, value.EventId);
            Assert.True(value.IngestDelayMilliseconds >= 0);
        });
    }

    [Fact]
    public async Task Trajectory_rejects_out_of_retention_and_oversized_windows()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Ingest.IngestAsync(
            fixture.SiteId,
            Request(
                "pda-01",
                "person-01",
                Position("position-1", Now.AddMinutes(-2), 10m, 1)));

        var expired = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Runtime.GetTrajectoryAsync(
                fixture.SiteId,
                "PDA-01",
                "PERSON-01",
                new DateTimeOffset(Now.AddDays(-31)),
                new DateTimeOffset(Now.AddDays(-31).AddHours(1)),
                100,
                null));
        var oversized = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Runtime.GetTrajectoryAsync(
                fixture.SiteId,
                "PDA-01",
                "PERSON-01",
                new DateTimeOffset(Now.AddHours(-25)),
                new DateTimeOffset(Now),
                100,
                null));

        Assert.Equal(SpaceErrorCodes.PersonnelQueryInvalid, expired.Code);
        Assert.Equal(SpaceErrorCodes.PersonnelQueryInvalid, oversized.Code);
    }

    [Fact]
    public async Task Current_filters_reject_numeric_enum_aliases()
    {
        await using var fixture = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Runtime.GetCurrentAsync(
                fixture.SiteId,
                "0",
                null,
                null,
                100,
                null));

        Assert.Equal(SpaceErrorCodes.PersonnelQueryInvalid, exception.Code);
    }

    [Fact]
    public async Task External_and_cross_tenant_reads_fail_closed()
    {
        await using var external = await Fixture.CreateAsync(isExternal: true);
        var denied = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            external.Runtime.GetCurrentAsync(
                external.SiteId,
                null,
                null,
                null,
                100,
                null));
        Assert.Equal(SpaceErrorCodes.ExternalSubjectDenied, denied.Code);

        await using var internalFixture = await Fixture.CreateAsync();
        var hidden = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            internalFixture.Runtime.GetCurrentAsync(
                Guid.NewGuid(),
                null,
                null,
                null,
                100,
                null));
        Assert.Equal(SpaceErrorCodes.PersonnelSiteNotFound, hidden.Code);
    }

    private static IngestSpacePersonnelEventsRequest Request(
        string sourceId,
        string personExternalId,
        params SpacePersonnelEventInput[] events) =>
        new(
            SpacePersonnelEventContract.Version,
            sourceId,
            "Real",
            events.Select(value => value with
            {
                PersonExternalId = personExternalId,
            }).ToArray());

    private static SpacePersonnelEventInput Position(
        string id,
        DateTime occurredAtUtc,
        decimal x,
        long sequence) =>
        new(
            id,
            "placeholder",
            null,
            "PositionObserved",
            null,
            Guid.NewGuid(),
            null,
            x,
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
            "placeholder",
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
        SpacePersonnelEventService Ingest,
        SpacePersonnelRuntimeService Runtime,
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
            var access = new AllowAccess();
            var clock = new FixedClock();
            return new Fixture(
                context,
                new SpacePersonnelEventService(
                    context,
                    execution,
                    clock,
                    access),
                new SpacePersonnelRuntimeService(
                    context,
                    execution,
                    clock,
                    access,
                    new TestCursorCodec(),
                    new SpacePersonnelRuntimeOptions()),
                siteId);
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

    private sealed class TestCursorCodec : ISpaceCursorCodec
    {
        public string Encode(SpaceCursorState state) =>
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state)));

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash)
        {
            var state = JsonSerializer.Deserialize<SpaceCursorState>(
                Encoding.UTF8.GetString(Convert.FromBase64String(cursor)))
                ?? throw new FormatException();
            if (state.Resource != expectedResource ||
                state.FilterHash != expectedFilterHash)
            {
                throw new FormatException();
            }
            return state;
        }
    }
}
