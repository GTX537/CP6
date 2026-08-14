using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceCadPreparationServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 14, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Status_synchronizes_clean_scan_and_preview_seals_start_request()
    {
        await using var fixture = await CreateFixtureAsync();

        var status = await fixture.Service.GetStatusAsync(
            fixture.Version.Id,
            fixture.Source.Id);
        var profiles = await fixture.Service.ListProfilesAsync(fixture.Version.Id);
        var profile = Assert.Single(profiles);
        var preview = await fixture.Service.PreviewAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            Request(
                fixture.Floor.LogicalId,
                profile,
                [new SpaceCadLayerMappingOverrideV1(
                    "WALL",
                    Ignore: false,
                    SpaceCadSemanticTarget.Wall,
                    TargetSubtype: null,
                    SpaceCadGeometryRule.Centerline,
                    DefaultHeightMillimeters: 3_000,
                    DefaultThicknessMillimeters: 200,
                    ConfidenceWeight: .98m)]));

        Assert.True(status.ReadyForPreparation);
        Assert.Equal("Ready", status.SourceState);
        Assert.True(preview.ReadyForParsing);
        Assert.NotNull(preview.StartRequest);
        Assert.NotEqual(Guid.Empty, preview.PreparationId);
        Assert.Equal(preview.PreparationId, preview.StartRequest!.PreparationId);
        Assert.Equal(fixture.Floor.LogicalId, preview.StartRequest.FloorLogicalId);
        Assert.Equal(0, preview.BaseContentRevision);
        var preparation = Assert.Single(
            await fixture.Context.CadParsePreparations.ToListAsync());
        var snapshot = SpaceCadMappingReplaySnapshot.Deserialize(
            preparation.MappingReplaySnapshotJson);
        Assert.Equal(preview.MappingPreview!.PreviewSha256,
            snapshot.ExpectedMappingPreviewSha256);
        Assert.Single(snapshot.LayerOverrides);

        var started = await fixture.Parse.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            preview.StartRequest,
            "sealed-mapping-snapshot");
        var job = await fixture.Context.Jobs.SingleAsync(item => item.Id == started.JobId);
        var payload = JsonSerializer.Deserialize<SpaceCadParseJobPayload>(
            job.PayloadJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(payload);
        Assert.Equal(SpaceCadParsePayloadVersions.Current, payload.SchemaVersion);
        Assert.Equal(preparation.ProviderVersion, payload.PreferredProviderVersion);
        Assert.Equal(preparation.MappingReplaySnapshotJson,
            payload.MappingReplaySnapshotJson);
    }

    [Fact]
    public async Task Start_rejects_a_sealed_preparation_after_draft_revision_changes()
    {
        await using var fixture = await CreateFixtureAsync();
        _ = await fixture.Service.GetStatusAsync(
            fixture.Version.Id,
            fixture.Source.Id);
        var profile = Assert.Single(
            await fixture.Service.ListProfilesAsync(fixture.Version.Id));
        var preview = await fixture.Service.PreviewAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            Request(fixture.Floor.LogicalId, profile));
        var version = await fixture.Context.Versions.SingleAsync(
            item => item.Id == fixture.Version.Id);
        version.TouchContent();
        await fixture.Context.SaveChangesAsync();

        var problem = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Parse.StartAsync(
                fixture.Version.Id,
                fixture.Source.Id,
                preview.StartRequest!,
                "stale-preparation"));

        Assert.Equal(SpaceErrorCodes.ParseChangesetStale, problem.Code);
        Assert.Equal(409, problem.StatusCode);
        Assert.Empty(await fixture.Context.Jobs.ToListAsync());
    }

    [Fact]
    public async Task Start_rejects_a_tampered_request_without_creating_a_job()
    {
        await using var fixture = await CreateFixtureAsync();
        var profile = Assert.Single(
            await fixture.Service.ListProfilesAsync(fixture.Version.Id));
        var preview = await fixture.Service.PreviewAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            Request(fixture.Floor.LogicalId, profile));
        var tampered = preview.StartRequest! with
        {
            MappingPreviewSha256 = new string('f', 64),
        };

        var problem = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Parse.StartAsync(
                fixture.Version.Id,
                fixture.Source.Id,
                tampered,
                "tampered-preparation"));

        Assert.Equal(SpaceErrorCodes.CadPreparationInvalid, problem.Code);
        Assert.Equal(422, problem.StatusCode);
        Assert.Empty(await fixture.Context.Jobs.ToListAsync());
    }

    [Fact]
    public async Task Start_rejects_a_corrupted_server_mapping_snapshot()
    {
        await using var fixture = await CreateFixtureAsync();
        var profile = Assert.Single(
            await fixture.Service.ListProfilesAsync(fixture.Version.Id));
        var preview = await fixture.Service.PreviewAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            Request(fixture.Floor.LogicalId, profile));
        var preparation = await fixture.Context.CadParsePreparations.SingleAsync();
        fixture.Context.Entry(preparation)
            .Property(item => item.MappingReplaySnapshotJson)
            .CurrentValue = "{}";
        await fixture.Context.SaveChangesAsync();

        var problem = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Parse.StartAsync(
                fixture.Version.Id,
                fixture.Source.Id,
                preview.StartRequest!,
                "corrupt-mapping-snapshot"));

        Assert.Equal(SpaceErrorCodes.CadPreparationInvalid, problem.Code);
        Assert.Equal(422, problem.StatusCode);
        Assert.Empty(await fixture.Context.Jobs.ToListAsync());
    }

    private static PreviewSpaceCadPreparationRequest Request(
        Guid floorId,
        SpaceCadMappingProfileSummaryDto profile,
        IReadOnlyList<SpaceCadLayerMappingOverrideV1>? overrides = null) =>
        new(
            floorId,
            SpaceCadUnit.Millimeter,
            new SpaceCadPointV1(0, 0),
            new SpaceCadMillimeterPointV1(0, 0),
            0,
            profile.ProfileId,
            profile.Version,
            overrides ?? []);

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var tenantId = Guid.NewGuid();
        var execution = new TestExecutionContext(tenantId, Guid.NewGuid());
        var clock = new FixedClock();
        var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString("N"),
                    SpaceTestDatabaseRoots.InMemory)
                .ConfigureWarnings(warnings => warnings.Ignore(
                    InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            execution,
            clock);
        var model = SpaceModel.Create(tenantId, Guid.NewGuid());
        var published = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "Published baseline");
        published.BeginValidation();
        published.MarkReady(new string('a', 64), "space-v1", new string('b', 64));
        published.BeginPublishing();
        published.MarkPublished(execution.ActorId, Now);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(published);
        model.ActivateDesignV1();
        var version = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            2,
            "CAD wizard draft",
            published.Id);
        model.ReserveDraft(version);
        var floor = SpaceFloorRevision.Create(
            tenantId,
            version.Id,
            Guid.NewGuid(),
            model.SiteId,
            1,
            "F1",
            "Floor 1",
            0,
            6_000);
        floor.ConfigureBoundary(
            "[[0,0],[100000,0],[100000,100000],[0,100000]]",
            SpaceCadCoordinateVersions.TargetCoordinateSystem);
        var fileId = Guid.NewGuid();
        var storageKey = $"{tenantId:N}/{fileId:N}/source.content";
        var bytes = Encoding.ASCII.GetBytes(
            "0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n");
        var sourceHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var file = SpaceFile.CreateUploading(
            fileId,
            tenantId,
            storageKey,
            "warehouse.dxf",
            "application/vnd.autocad.dxf",
            SpaceFileRetentionClass.Source);
        file.CompleteQuarantine(
            "application/vnd.autocad.dxf",
            ".dxf",
            bytes.Length,
            sourceHash);
        file.BeginScanning();
        var source = SpaceModelSource.CreatePendingFileSource(
            tenantId,
            version.Id,
            SpaceSourceType.Dxf,
            file,
            "warehouse.dxf");
        context.AddRange(model, published, version, floor, file, source);
        await context.SaveChangesAsync();
        file.MarkClean("test", "v1");
        await context.SaveChangesAsync();

        var files = new MemoryFileStore(storageKey, bytes);
        var access = new AllowAccess();
        var provider = new DeterministicPreparationProvider(sourceHash);
        var profiles = new StandardSpaceCadMappingProfileCatalog();
        var preparation = new SpaceCadPreparationService(
            context,
            execution,
            access,
            provider,
            profiles,
            files,
            SpaceWorkerSandboxPolicy.FileSafetyDefault,
            clock);
        var design = new SpaceDesignV1Service(
            context,
            execution,
            clock,
            new TestCursorCodec(),
            access,
            new SpaceVersionCloneCoordinator(
                execution,
                new EfSpaceVersionCloneStore(context, execution, clock)),
            new SpaceSourceCoordinator(execution));
        var parse = new SpaceCadParseService(
            context,
            execution,
            access,
            null!,
            null!,
            clock,
            files,
            design);
        return new Fixture(context, version, floor, source, preparation, parse);
    }

    private sealed class DeterministicPreparationProvider(string sourceSha256) :
        ISpaceCadPreparationProvider
    {
        public Task<SpaceCadIrPackageV1> InspectAsync(
            SpaceCadPreparationProviderRequest request,
            Stream source,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SpaceCadIrPackageV1(
                new SpaceCadIrDocumentV1(
                    SpaceCadIrVersions.SchemaVersion,
                    sourceSha256,
                    SpaceCadSourceFormat.Dxf,
                    "AC1032",
                    SpaceCadUnit.Millimeter,
                    1,
                    SpaceCadIrVersions.CoordinateSystem,
                    new SpaceCadBoundsV1(0, 0, 10_000, 0),
                    "deterministic-test",
                    "1.0"),
                [new SpaceCadIrLayerV1("WALL", "WALL", 1, "ACI:7", "CONTINUOUS")],
                [],
                [new SpaceCadIrEntityV1(
                    "H:WALL-1",
                    SpaceCadIrEntityType.Line,
                    "LINE",
                    "WALL",
                    null,
                    [new(0, 0), new(10_000, 0)],
                    null,
                    null,
                    null,
                    SpaceCadAffineTransformV1.Identity,
                    new SpaceCadBoundsV1(0, 0, 10_000, 0),
                    false,
                    true,
                    new Dictionary<string, string>())],
                [],
                new SpaceCadIrSummaryV1(
                    1,
                    0,
                    1,
                    1,
                    0,
                    0,
                    new SpaceCadBoundsV1(0, 0, 10_000, 0))));
    }

    private sealed class MemoryFileStore(string storageKey, byte[] bytes) : ISpaceFileStore
    {
        public Task<Stream> OpenQuarantinedReadAsync(
            Guid tenantId,
            Guid fileId,
            string requestedStorageKey,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(storageKey, requestedStorageKey);
            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }

        public Task DeleteAsync(
            Guid tenantId,
            Guid fileId,
            string requestedStorageKey,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId) :
        ISpaceExecutionContext;

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
            throw new NotSupportedException();

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash) =>
            throw new NotSupportedException();
    }

    private sealed record Fixture(
        SpaceContext Context,
        SpaceModelVersion Version,
        SpaceFloorRevision Floor,
        SpaceModelSource Source,
        SpaceCadPreparationService Service,
        SpaceCadParseService Parse) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
