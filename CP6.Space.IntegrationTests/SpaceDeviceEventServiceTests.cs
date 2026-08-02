using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceDeviceEventServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Mapping_is_normalized_listed_and_concurrency_updated()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateMappingAsync(
            fixture.SiteId,
            new CreateSpaceDeviceMappingRequest(
                " wcs-01 ",
                "Real",
                " agv-01 ",
                "Agv",
                fixture.DeviceElementId));

        Assert.Equal("WCS-01", created.SourceId);
        Assert.Equal("AGV-01", created.DeviceExternalId);
        Assert.Equal("Device", created.ElementType);
        Assert.Equal(fixture.PublishedVersionId, created.ValidatedModelVersionId);

        var page = await fixture.Service.GetMappingsAsync(
            fixture.SiteId,
            "wcs-01",
            10,
            null);
        Assert.Equal(created.Id, Assert.Single(page.Items).Id);

        var updated = await fixture.Service.UpdateMappingAsync(
            fixture.SiteId,
            created.Id,
            new UpdateSpaceDeviceMappingRequest(
                "Conveyor",
                fixture.ConveyorElementId,
                created.RowVersion));
        Assert.Equal("Conveyor", updated.DeviceKind);
        Assert.Equal(fixture.ConveyorElementId, updated.ElementLogicalId);

        var duplicate = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.CreateMappingAsync(
                fixture.SiteId,
                new CreateSpaceDeviceMappingRequest(
                    "WCS-01",
                    "Real",
                    "CONVEYOR-02",
                    "Conveyor",
                    fixture.ConveyorElementId)));
        Assert.Equal(SpaceErrorCodes.DeviceMappingConflict, duplicate.Code);
    }

    [Fact]
    public async Task Mapping_requires_compatible_published_element_and_internal_subject()
    {
        await using var fixture = await Fixture.CreateAsync();
        var incompatible = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.CreateMappingAsync(
                fixture.SiteId,
                new CreateSpaceDeviceMappingRequest(
                    "WCS-01",
                    "Real",
                    "AGV-01",
                    "Agv",
                    fixture.ConveyorElementId)));
        Assert.Equal(SpaceErrorCodes.DeviceElementNotFound, incompatible.Code);

        await using var external = await Fixture.CreateAsync(isExternal: true);
        var denied = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            external.Service.GetMappingsAsync(
                external.SiteId,
                null,
                10,
                null));
        Assert.Equal(SpaceErrorCodes.ExternalSubjectDenied, denied.Code);
    }

    [Fact]
    public async Task Event_batch_is_append_only_normalized_and_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.MapDeviceAsync();
        var request = Request(
            Position(
                " position-01 ",
                Now.AddMinutes(-4),
                1,
                fixture.FloorId),
            State("state-01", Now.AddMinutes(-3), "Running", 2),
            AlarmRaised("alarm-raised-01", Now.AddMinutes(-2), 3),
            AlarmCleared("alarm-cleared-01", Now.AddMinutes(-1), 4));

        var accepted = await fixture.Service.IngestAsync(fixture.SiteId, request);
        var replay = await fixture.Service.IngestAsync(fixture.SiteId, request);

        Assert.Equal(4, accepted.AcceptedCount);
        Assert.Equal(0, accepted.DuplicateCount);
        Assert.Equal("WCS-01", accepted.SourceId);
        Assert.All(accepted.Receipts, value =>
            Assert.Equal("AGV-01", value.DeviceExternalId));
        Assert.Equal(0, replay.AcceptedCount);
        Assert.Equal(4, replay.DuplicateCount);
        Assert.Equal(4, await fixture.Context.DeviceEvents.CountAsync());
        var events = await fixture.Context.DeviceEvents
            .OrderBy(value => value.OccurredAtUtc)
            .ToListAsync();
        Assert.Equal(SpaceDeviceEventKind.PositionObserved, events[0].EventKind);
        Assert.Equal(SpaceDeviceOperatingState.Running, events[1].OperatingState);
        Assert.Equal("ALARM-01", events[2].AlarmExternalId);
        Assert.Equal(SpaceDeviceAlarmSeverity.Critical, events[2].AlarmSeverity);
        Assert.Equal(SpaceDeviceEventKind.AlarmCleared, events[3].EventKind);
    }

    [Fact]
    public async Task Unmapped_source_switch_and_reused_event_identity_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var unmapped = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.IngestAsync(
                fixture.SiteId,
                Request(State("state-01", Now.AddMinutes(-2), "Idle", 1))));
        Assert.Equal(SpaceErrorCodes.DeviceMappingNotFound, unmapped.Code);

        await fixture.MapDeviceAsync();
        var first = Request(State("state-01", Now.AddMinutes(-2), "Idle", 1));
        await fixture.Service.IngestAsync(fixture.SiteId, first);
        var changed = Request(State("state-01", Now.AddMinutes(-1), "Running", 2));
        var reused = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.IngestAsync(fixture.SiteId, changed));
        Assert.Equal(SpaceErrorCodes.DeviceEventConflict, reused.Code);

        var simulated = Request(State(
            "state-02",
            Now.AddMinutes(-1),
            "Running",
            2)) with
        {
            SourceKind = "Simulated",
        };
        var switched = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.IngestAsync(fixture.SiteId, simulated));
        Assert.Equal(SpaceErrorCodes.DeviceMappingConflict, switched.Code);
        Assert.Single(await fixture.Context.DeviceEvents.ToListAsync());
    }

    [Fact]
    public async Task Invalid_shape_numeric_enum_and_future_clock_are_rejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.MapDeviceAsync();
        var mixed = State("state-01", Now.AddMinutes(-1), "Running", 1) with
        {
            AlarmExternalId = "ALARM-01",
        };
        var shape = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.IngestAsync(fixture.SiteId, Request(mixed)));
        Assert.Equal(SpaceErrorCodes.DeviceEventInvalid, shape.Code);

        var numeric = State("state-02", Now.AddMinutes(-1), "0", 2);
        var enumError = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.IngestAsync(fixture.SiteId, Request(numeric)));
        Assert.Equal(SpaceErrorCodes.DeviceEventInvalid, enumError.Code);

        var future = State("state-03", Now.AddMinutes(6), "Idle", 3);
        var clock = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.IngestAsync(fixture.SiteId, Request(future)));
        Assert.Equal(SpaceErrorCodes.DeviceEventInvalid, clock.Code);

        var unknownFloor = Position(
            "position-01",
            Now.AddMinutes(-1),
            4,
            Guid.NewGuid());
        var spatial = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.IngestAsync(
                fixture.SiteId,
                Request(unknownFloor)));
        Assert.Equal(SpaceErrorCodes.DeviceEventInvalid, spatial.Code);
        Assert.Empty(await fixture.Context.DeviceEvents.ToListAsync());
    }

    [Fact]
    public async Task Ef_model_freezes_mapping_identity_and_event_ledger()
    {
        await using var fixture = await Fixture.CreateAsync();
        var mappingType = fixture.Context.Model.FindEntityType(
            typeof(SpaceDeviceMapping))!;
        var eventType = fixture.Context.Model.FindEntityType(
            typeof(SpaceDeviceEvent))!;
        Assert.Equal("Space_DeviceMapping", mappingType.GetTableName());
        Assert.Contains(mappingType.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(value => value.Name)
                .SequenceEqual(new[]
                {
                    "TenantId", "SiteId", "SourceId", "DeviceExternalId",
                }));
        Assert.Equal("Space_DeviceEvent", eventType.GetTableName());
        Assert.Contains(eventType.GetIndexes(), index =>
            index.IsUnique && index.Properties.Select(value => value.Name)
                .SequenceEqual(new[]
                {
                    "TenantId", "SiteId", "SourceId", "SourceEventId",
                }));

        await fixture.MapDeviceAsync();
        await fixture.Service.IngestAsync(
            fixture.SiteId,
            Request(State("state-01", Now.AddMinutes(-1), "Idle", 1)));
        var ledger = await fixture.Context.DeviceEvents.SingleAsync();
        fixture.Context.Remove(ledger);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Context.SaveChangesAsync());
    }

    private static IngestSpaceDeviceEventsRequest Request(
        params SpaceDeviceEventInput[] events) =>
        new(
            SpaceDeviceEventContract.Version,
            " wcs-01 ",
            "Real",
            events);

    private static SpaceDeviceEventInput Position(
        string id,
        DateTime occurredAtUtc,
        long sequence,
        Guid floorId) =>
        new(
            id,
            " agv-01 ",
            "PositionObserved",
            null,
            floorId,
            null,
            10m,
            20m,
            0m,
            50m,
            null,
            null,
            null,
            null,
            sequence,
            new DateTimeOffset(occurredAtUtc));

    private static SpaceDeviceEventInput State(
        string id,
        DateTime occurredAtUtc,
        string state,
        long sequence) =>
        new(
            id,
            " agv-01 ",
            "OperatingStateChanged",
            state,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            sequence,
            new DateTimeOffset(occurredAtUtc));

    private static SpaceDeviceEventInput AlarmRaised(
        string id,
        DateTime occurredAtUtc,
        long sequence) =>
        new(
            id,
            " agv-01 ",
            "AlarmRaised",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            " alarm-01 ",
            " motor-overheat ",
            "Critical",
            "Motor temperature exceeded the source threshold.",
            sequence,
            new DateTimeOffset(occurredAtUtc));

    private static SpaceDeviceEventInput AlarmCleared(
        string id,
        DateTime occurredAtUtc,
        long sequence) =>
        new(
            id,
            " agv-01 ",
            "AlarmCleared",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            " alarm-01 ",
            null,
            null,
            null,
            sequence,
            new DateTimeOffset(occurredAtUtc));

    private sealed record Fixture(
        SpaceContext Context,
        SpaceDeviceEventService Service,
        Guid SiteId,
        Guid PublishedVersionId,
        Guid FloorId,
        Guid DeviceElementId,
        Guid ConveyorElementId) : IAsyncDisposable
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
                    .UseInMemoryDatabase(
                        Guid.NewGuid().ToString("N"),
                        SpaceTestDatabaseRoots.InMemory)
                    .Options,
                execution,
                new FixedClock());
            var siteId = Guid.NewGuid();
            var model = SpaceModel.Create(tenantId, siteId);
            var version = SpaceModelVersion.CreateDraft(
                tenantId,
                model.Id,
                1,
                "Published");
            var floorId = Guid.NewGuid();
            var floor = SpaceFloorRevision.Create(
                tenantId,
                version.Id,
                floorId,
                siteId,
                1,
                "F1",
                "Floor 1",
                0,
                5_000);
            var deviceElementId = Guid.NewGuid();
            var device = SpaceElementRevision.Create(
                tenantId,
                version.Id,
                deviceElementId,
                floorId,
                SpaceElementTypes.Device,
                PointGeometry());
            var conveyorElementId = Guid.NewGuid();
            var conveyor = SpaceElementRevision.Create(
                tenantId,
                version.Id,
                conveyorElementId,
                floorId,
                SpaceElementTypes.Conveyor,
                PointGeometry());
            context.AddRange(model, version, floor, device, conveyor);
            await context.SaveChangesAsync();
            version.BeginValidation();
            version.MarkReady(new string('a', 64), "space-v1", new string('b', 64));
            version.BeginPublishing();
            version.MarkPublished(execution.ActorId, Now);
            model.BeginCutover(Guid.NewGuid());
            model.MarkFrozen();
            model.MarkBootstrapping();
            model.MarkVerified(version);
            model.ActivateDesignV1();
            await context.SaveChangesAsync();

            var service = new SpaceDeviceEventService(
                context,
                execution,
                new FixedClock(),
                new AllowAccess(),
                new TestCursorCodec());
            return new Fixture(
                context,
                service,
                siteId,
                version.Id,
                floorId,
                deviceElementId,
                conveyorElementId);
        }

        public Task<SpaceDeviceMappingDto> MapDeviceAsync() =>
            Service.CreateMappingAsync(
                SiteId,
                new CreateSpaceDeviceMappingRequest(
                    "WCS-01",
                    "Real",
                    "AGV-01",
                    "Agv",
                    DeviceElementId));

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private static string PointGeometry() =>
        "{\"schemaVersion\":1,\"kind\":\"point\",\"x\":0,\"y\":0,\"z\":0}";

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
