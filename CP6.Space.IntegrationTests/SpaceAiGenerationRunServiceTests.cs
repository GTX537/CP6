using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceAiGenerationRunServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 8, 18, 30, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Rule_only_initial_create_is_pinned_and_idempotent()
    {
        await using var fixture = await CreateFixtureAsync();
        var request = Request(fixture);
        var expected = Convert.ToBase64String(fixture.Version.RowVersion);

        var created = await fixture.Service.CreateAsync(
            fixture.Version.Id,
            request,
            expected,
            "create-run-1");
        var replay = await fixture.Service.CreateAsync(
            fixture.Version.Id,
            request,
            expected,
            "create-run-1");
        var reused = await fixture.Service.CreateAsync(
            fixture.Version.Id,
            request,
            expected,
            "create-run-2");

        Assert.Equal(created.RunId, replay.RunId);
        Assert.Equal(created.RunId, reused.RunId);
        Assert.False(created.IdempotentReplay);
        Assert.True(replay.IdempotentReplay);
        Assert.True(reused.Reused);
        Assert.False(reused.IdempotentReplay);
        Assert.Equal("Queued", created.Status);
        Assert.Equal("Disabled", created.Policy);
        Assert.Equal(
            $"/api/space/design/v1/generation-runs/{created.RunId}",
            created.Links.Self);

        var run = await fixture.Context.GenerationRuns.SingleAsync();
        Assert.Equal(fixture.Source.Id, run.SourceId);
        Assert.Equal(fixture.FloorId, run.TargetFloorLogicalId);
        Assert.Equal(fixture.MappingProfileId, run.MappingProfileVersionId);
        Assert.Equal(SpaceAiPolicySnapshot.Disabled, run.PolicySnapshot);
        Assert.Null(run.ProviderConfigVersionId);
        Assert.Null(run.BasedOnRunId);
        Assert.Single(await fixture.Context.Jobs.Where(item =>
            item.JobType == SpaceJobType.BuildScene).ToArrayAsync());
        Assert.Equal(2, await fixture.Context.IdempotencyRecords.CountAsync());

        var job = await fixture.Context.Jobs.SingleAsync(item =>
            item.Id == created.JobId);
        using var payload = JsonDocument.Parse(job.PayloadJson);
        var root = payload.RootElement;
        Assert.Equal(
            fixture.PreviewArtifactId,
            root.GetProperty("previewArtifactId").GetGuid());
        Assert.Equal(
            fixture.PreviewSha256,
            root.GetProperty("previewArtifactSha256").GetString());
        Assert.Equal(
            SpaceAiGenerationRunContract.RuleOnlyMode,
            root.GetProperty("mode").GetString());
        Assert.Equal(JsonValueKind.Null,
            root.GetProperty("basedOnRunId").ValueKind);
    }

    [Fact]
    public async Task Failed_run_recovery_reuses_create_route_and_preview_pin()
    {
        await using var fixture = await CreateFixtureAsync();
        var expectedVersion = Convert.ToBase64String(fixture.Version.RowVersion);
        var original = await fixture.Service.CreateAsync(
            fixture.Version.Id,
            Request(fixture),
            expectedVersion,
            "create-before-recovery");
        var originalRun = await fixture.Context.GenerationRuns.SingleAsync(
            item => item.Id == original.RunId);
        originalRun.MarkFailed(
            SpaceErrorCodes.AiProviderUnavailable,
            "Synthetic recoverable test failure.");
        fixture.Context.Entry(originalRun)
            .Property(item => item.RowVersion)
            .CurrentValue = [1];
        await fixture.Context.SaveChangesAsync();

        var recoveryRequest = Request(fixture) with
        {
            BasedOnRunId = originalRun.Id,
            ExpectedBasedOnRunRowVersion =
                Convert.ToBase64String(originalRun.RowVersion),
        };
        var crossOperationConflict = await Assert.ThrowsAsync<
            SpaceProblemException>(() => fixture.Service.CreateAsync(
            fixture.Version.Id,
            recoveryRequest,
            expectedVersion,
            "create-before-recovery"));
        Assert.Equal(
            SpaceErrorCodes.IdempotencyConflict,
            crossOperationConflict.Code);

        var recovered = await fixture.Service.CreateAsync(
            fixture.Version.Id,
            recoveryRequest,
            expectedVersion,
            "recover-run-1");
        var replay = await fixture.Service.CreateAsync(
            fixture.Version.Id,
            recoveryRequest,
            expectedVersion,
            "recover-run-1");

        Assert.NotEqual(original.RunId, recovered.RunId);
        Assert.Equal(recovered.RunId, replay.RunId);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(original.RunId, recovered.BasedOnRunId);
        Assert.Equal("Queued", recovered.Status);
        Assert.Equal(2, await fixture.Context.GenerationRuns.CountAsync());
        Assert.Equal(2, await fixture.Context.Jobs.CountAsync(item =>
            item.JobType == SpaceJobType.BuildScene));
        Assert.Equal(3, await fixture.Context.IdempotencyRecords.CountAsync());
        var source = await fixture.Context.GenerationRuns.SingleAsync(
            item => item.Id == original.RunId);
        Assert.False(source.IsCurrent);
        Assert.Equal(SpaceGenerationRunStatus.Cancelled, source.Status);
        var replacementJob = await fixture.Context.Jobs.SingleAsync(
            item => item.Id == recovered.JobId);
        using var payload = JsonDocument.Parse(replacementJob.PayloadJson);
        Assert.Equal(
            fixture.PreviewArtifactId,
            payload.RootElement.GetProperty("previewArtifactId").GetGuid());
        Assert.Equal(
            fixture.PreviewSha256,
            payload.RootElement.GetProperty("previewArtifactSha256")
                .GetString());
    }

    [Fact]
    public async Task Reusing_key_with_different_request_is_rejected()
    {
        await using var fixture = await CreateFixtureAsync();
        var expected = Convert.ToBase64String(fixture.Version.RowVersion);
        await fixture.Service.CreateAsync(
            fixture.Version.Id,
            Request(fixture),
            expected,
            "create-run-conflict");

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.CreateAsync(
                fixture.Version.Id,
                Request(fixture) with
                {
                    Mode = SpaceAiGenerationRunContract.AiAssistedMode,
                },
                expected,
                "create-run-conflict"));

        Assert.Equal(SpaceErrorCodes.IdempotencyConflict, error.Code);
        Assert.Single(await fixture.Context.GenerationRuns.ToArrayAsync());
    }

    [Fact]
    public async Task Ai_assisted_initial_create_is_disabled_fail_closed()
    {
        await using var fixture = await CreateFixtureAsync();

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.CreateAsync(
                fixture.Version.Id,
                Request(fixture) with
                {
                    Mode = SpaceAiGenerationRunContract.AiAssistedMode,
                },
                Convert.ToBase64String(fixture.Version.RowVersion),
                "create-run-ai"));

        Assert.Equal(SpaceErrorCodes.AiDisabled, error.Code);
        Assert.Empty(await fixture.Context.GenerationRuns.ToArrayAsync());
        Assert.Empty(await fixture.Context.Jobs.Where(item =>
            item.JobType == SpaceJobType.BuildScene).ToArrayAsync());
        Assert.Empty(await fixture.Context.IdempotencyRecords.ToArrayAsync());
    }

    [Fact]
    public async Task Unverified_rack_profile_is_not_frozen_into_initial_run()
    {
        await using var fixture = await CreateFixtureAsync();

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.CreateAsync(
                fixture.Version.Id,
                Request(fixture) with
                {
                    RackGenerationProfileVersionId = Guid.NewGuid(),
                },
                Convert.ToBase64String(fixture.Version.RowVersion),
                "create-run-rack"));

        Assert.Equal(SpaceErrorCodes.RackProfileRequired, error.Code);
        Assert.Empty(await fixture.Context.GenerationRuns.ToArrayAsync());
    }

    [Fact]
    public async Task External_principal_is_denied_before_generation_data_access()
    {
        await using var fixture = await CreateFixtureAsync(isExternal: true);

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.CreateAsync(
                Guid.NewGuid(),
                new CreateSpaceAiGenerationRunRequest(
                    Guid.NewGuid(),
                    null,
                    null,
                    SpaceAiGenerationRunContract.RuleOnlyMode,
                    0),
                string.Empty,
                "external-create"));

        Assert.Equal(SpaceErrorCodes.ExternalSubjectDenied, error.Code);
        Assert.Equal(403, error.StatusCode);
    }

    private static CreateSpaceAiGenerationRunRequest Request(Fixture fixture) =>
        new(
            fixture.Source.Id,
            fixture.MappingProfileId,
            null,
            SpaceAiGenerationRunContract.RuleOnlyMode,
            fixture.Version.ContentRevision);

    private static async Task<Fixture> CreateFixtureAsync(
        bool isExternal = false)
    {
        var tenantId = Guid.NewGuid();
        var execution = new TestExecutionContext(
            tenantId,
            Guid.NewGuid(),
            isExternal);
        var clock = new FixedClock();
        var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString("N"),
                    SpaceTestDatabaseRoots.InMemory)
                .Options,
            execution,
            clock);
        var siteId = Guid.NewGuid();
        var model = SpaceModel.Create(tenantId, siteId);
        var version = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "AI generation Draft");
        model.ReserveDraft(version);
        var floorId = Guid.NewGuid();
        var floor = SpaceFloorRevision.Create(
            tenantId,
            version.Id,
            floorId,
            siteId,
            1,
            "F1",
            "First floor",
            height: 8_000);

        var sourceBytes = Encoding.ASCII.GetBytes(
            "0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n");
        var sourceSha256 = Sha256(sourceBytes);
        var sourceFile = CleanFile(
            Guid.NewGuid(),
            tenantId,
            "sources/warehouse.dxf",
            "warehouse.dxf",
            "application/vnd.autocad.dxf",
            ".dxf",
            sourceBytes.Length,
            sourceSha256,
            SpaceFileRetentionClass.Source);
        var source = SpaceModelSource.CreateFileSource(
            tenantId,
            version.Id,
            SpaceSourceType.Dxf,
            sourceFile,
            "warehouse.dxf");
        var mappingProfileId = Guid.NewGuid();
        source.ConfigureImport(
            SpaceCadParseJobProcessor.Version,
            mappingProfileId,
            3,
            SpaceCadUnit.Millimeter.ToString(),
            1,
            CoordinateMetadata(sourceSha256, floorId));
        source.BeginParsing();
        source.MarkPreviewReady();

        var parseJob = SpaceJob.CreateQueued(
            tenantId,
            SpaceJobType.CadParse,
            SpaceJobSubjectType.ModelSource,
            source.Id,
            new string('a', 64),
            new string('b', 64),
            50,
            3,
            execution.ActorId,
            Now,
            Guid.NewGuid());
        CompleteJob(parseJob, tenantId);
        var previewBytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1}");
        var previewSha256 = Sha256(previewBytes);
        var previewFile = CleanFile(
            Guid.NewGuid(),
            tenantId,
            "artifacts/preview.json",
            "preview.json",
            "application/json",
            ".json",
            previewBytes.Length,
            previewSha256,
            SpaceFileRetentionClass.Artifact);
        var previewArtifact = SpaceArtifact.Create(
            tenantId,
            version.Id,
            source,
            previewFile,
            SpaceArtifactType.PreviewSet,
            SpaceCadPreviewSetVersions.ArtifactSchema);
        previewArtifact.AttachToJob(parseJob);
        context.AddRange(
            model,
            version,
            floor,
            sourceFile,
            source,
            parseJob,
            previewFile,
            previewArtifact);
        await context.SaveChangesAsync();

        var access = new AllowAccess();
        var lockedFacts = new SpaceAiLockedFactService(
            context,
            execution,
            access);
        var recovery = new SpaceAiRunRecoveryService(
            context,
            execution,
            access,
            clock,
            lockedFacts);
        var service = new SpaceAiGenerationRunService(
            context,
            execution,
            access,
            clock,
            new DisabledSpaceAiTenantPolicySource(),
            recovery);
        return new Fixture(
            context,
            version,
            source,
            floorId,
            mappingProfileId,
            previewArtifact.Id,
            previewSha256,
            service);
    }

    private static string CoordinateMetadata(
        string sourceSha256,
        Guid floorId)
    {
        var metadata = new SpaceCadCoordinateMetadataV1(
            SpaceCadCoordinateVersions.SchemaVersion,
            sourceSha256,
            true,
            SpaceCadUnit.Millimeter,
            1,
            SpaceCadUnit.Millimeter,
            1,
            new SpaceCadPointV1(0, 0),
            new SpaceCadMillimeterPointV1(0, 0),
            0,
            new SpaceCadFloorAssignmentV1(
                floorId,
                "F1",
                1,
                0,
                SpaceCadCoordinateVersions.TargetCoordinateSystem,
                new SpaceCadBoundsV1(0, 0, 100_000, 100_000)),
            SpaceCadAffineTransformV1.Identity,
            new SpaceCadBoundsV1(0, 0, 100_000, 100_000),
            new SpaceCadBoundsV1(0, 0, 100_000, 100_000),
            new string('c', 64));
        return JsonSerializer.Serialize(metadata, JsonOptions);
    }

    private static SpaceFile CleanFile(
        Guid id,
        Guid tenantId,
        string storageKey,
        string name,
        string contentType,
        string extension,
        long size,
        string sha256,
        SpaceFileRetentionClass retention)
    {
        var file = SpaceFile.CreateUploading(
            id,
            tenantId,
            storageKey,
            name,
            contentType,
            retention);
        file.CompleteQuarantine(contentType, extension, size, sha256);
        file.BeginScanning();
        file.MarkClean("test", "v1", "TEST_GENERATED");
        return file;
    }

    private static void CompleteJob(SpaceJob job, Guid tenantId)
    {
        var attempt = job.Claim(
            "cad-worker",
            SpaceCadParseJobProcessor.Version,
            Now,
            TimeSpan.FromMinutes(5));
        attempt.Succeed(Now);
        job.Complete(
            attempt.Id,
            "cad-worker",
            Now,
            JsonSerializer.Serialize(new { completed = true }));
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

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

    private sealed record Fixture(
        SpaceContext Context,
        SpaceModelVersion Version,
        SpaceModelSource Source,
        Guid FloorId,
        Guid MappingProfileId,
        Guid PreviewArtifactId,
        string PreviewSha256,
        SpaceAiGenerationRunService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
