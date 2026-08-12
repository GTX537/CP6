using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceCadParseJobTests
{
    private static readonly DateTime Now =
        new(2026, 8, 6, 19, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Start_replays_one_job_and_keeps_source_recoverable()
    {
        await using var fixture = await CreateFixtureAsync();
        var request = Request(fixture.Source.Sha256);

        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            request,
            "cad-start-1");
        var replay = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            request,
            "cad-start-1");

        Assert.Equal(started.JobId, replay.JobId);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal("Ready", started.Source.State);
        Assert.Equal(SpaceCadParseJobProcessor.Version, started.Source.ParserVersion);
        Assert.Equal(request.MappingProfileId, started.Source.MappingProfileId);
        Assert.Single(await fixture.Context.Jobs.ToListAsync());
        Assert.Single(await fixture.Context.IdempotencyRecords.ToListAsync());
        Assert.Empty(await fixture.Context.Artifacts.ToListAsync());
    }

    [Fact]
    public async Task Queued_cancel_and_explicit_retry_are_safe_and_idempotent()
    {
        await using var fixture = await CreateFixtureAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            Request(fixture.Source.Sha256),
            "cad-cancel-1");

        var cancelled = await fixture.Service.CancelAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            started.JobId);
        var retry = await fixture.Service.RetryAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            started.JobId,
            "cad-retry-1");
        var replay = await fixture.Service.RetryAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            started.JobId,
            "cad-retry-1");

        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal(retry.JobId, replay.JobId);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal("Ready", fixture.Source.State.ToString());
        var jobs = await fixture.Context.Jobs.OrderBy(job => job.RequestedAtUtc)
            .ThenBy(job => job.Id)
            .ToListAsync();
        Assert.Equal(2, jobs.Count);
        Assert.Equal(SpaceJobStatus.Cancelled, jobs.Single(job => job.Id == started.JobId).Status);
        Assert.Equal(started.JobId, jobs.Single(job => job.Id == retry.JobId).RetryOfJobId);
        Assert.Empty(await fixture.Context.Artifacts.ToListAsync());
    }

    [Fact]
    public async Task Reexecuted_generation_step_reuses_three_persisted_artifacts()
    {
        await using var fixture = await CreateFixtureAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            Request(fixture.Source.Sha256),
            "cad-artifacts-1");
        var lease = await ClaimAsync(fixture, started.JobId);
        var provider = new DeterministicProvider();
        var executor = new SpaceCadParseJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(fixture.Files),
            provider);
        var execution = new SpaceJobStepExecution(
            lease,
            1,
            SpaceCadParseJobProcessor.GenerateArtifacts);

        var first = await executor.ExecuteAsync(execution);
        var second = await executor.ExecuteAsync(execution);

        Assert.Equal(first, second);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(3, await fixture.Context.Artifacts.CountAsync());
        Assert.Equal(3, await fixture.Context.Files.CountAsync(file =>
            file.RetentionClass == SpaceFileRetentionClass.Artifact));
        Assert.Equal(
            [
                SpaceArtifactType.CadIr,
                SpaceArtifactType.LayerInventory,
                SpaceArtifactType.PreviewSet,
            ],
            await fixture.Context.Artifacts
                .OrderBy(artifact => artifact.ArtifactType)
                .Select(artifact => artifact.ArtifactType)
                .ToArrayAsync());
    }

    [Fact]
    public async Task Runner_completes_preview_without_draft_writes()
    {
        await using var fixture = await CreateFixtureAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            Request(fixture.Source.Sha256),
            "cad-runner-1");
        var provider = new DeterministicProvider();
        var executor = new SpaceCadParseJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(fixture.Files),
            provider);
        var lease = await ClaimAsync(fixture, started.JobId);
        var job = fixture.Context.Jobs.Local.Single(item => item.Id == started.JobId);
        var attempt = fixture.Context.JobAttempts.Local.Single(
            item => item.Id == lease.AttemptId);
        var generateStep = SpaceJobStep.Start(
            fixture.Execution.TenantId,
            attempt.Id,
            1,
            SpaceCadParseJobProcessor.GenerateArtifacts,
            Now);
        fixture.Context.JobSteps.Add(generateStep);
        await fixture.Context.SaveChangesAsync();
        var generated = await executor.ExecuteAsync(
            new(
                lease,
                1,
                SpaceCadParseJobProcessor.GenerateArtifacts));
        generateStep.Complete(generated.CheckpointJson, generated.OutputHash, Now);
        await fixture.Context.SaveChangesAsync();
        var finalizeStep = SpaceJobStep.Start(
            fixture.Execution.TenantId,
            attempt.Id,
            2,
            SpaceCadParseJobProcessor.FinalizePreview,
            Now);
        fixture.Context.JobSteps.Add(finalizeStep);
        await fixture.Context.SaveChangesAsync();
        var finalized = await executor.ExecuteAsync(
            new(
                lease,
                2,
                SpaceCadParseJobProcessor.FinalizePreview));
        finalizeStep.Complete(finalized.CheckpointJson, finalized.OutputHash, Now);
        attempt.Succeed(Now);
        job.Complete(attempt.Id, attempt.WorkerId, Now, finalized.CheckpointJson);
        await fixture.Context.SaveChangesAsync();

        fixture.Context.ChangeTracker.Clear();
        var parse = await fixture.Service.GetAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            started.JobId);
        Assert.Equal("Succeeded", parse.Status);
        Assert.Equal("PreviewReady", parse.SourceState);
        Assert.Equal(3, parse.Artifacts.Count);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(0, (await fixture.Context.Versions.SingleAsync(
            version => version.Id == fixture.Version.Id)).ContentRevision);
        Assert.Empty(await fixture.Context.ElementRevisions.ToListAsync());
        Assert.Empty(await fixture.Context.RackRevisions.ToListAsync());
        Assert.Empty(await fixture.Context.ZoneRevisions.ToListAsync());
    }

    [Fact]
    public async Task Completed_parse_rejects_an_invalid_review_artifact_with_stable_error()
    {
        await using var fixture = await CreateFixtureAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            Request(fixture.Source.Sha256),
            "cad-review-workspace-1");
        var provider = new DeterministicProvider();
        var executor = new SpaceCadParseJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(fixture.Files),
            provider);
        var lease = await ClaimAsync(fixture, started.JobId);
        var job = fixture.Context.Jobs.Local.Single(item => item.Id == started.JobId);
        var attempt = fixture.Context.JobAttempts.Local.Single(
            item => item.Id == lease.AttemptId);
        var generateStep = SpaceJobStep.Start(
            fixture.Execution.TenantId,
            attempt.Id,
            1,
            SpaceCadParseJobProcessor.GenerateArtifacts,
            Now);
        fixture.Context.JobSteps.Add(generateStep);
        await fixture.Context.SaveChangesAsync();
        var generated = await executor.ExecuteAsync(
            new(lease, 1, SpaceCadParseJobProcessor.GenerateArtifacts));
        generateStep.Complete(generated.CheckpointJson, generated.OutputHash, Now);
        await fixture.Context.SaveChangesAsync();
        var finalizeStep = SpaceJobStep.Start(
            fixture.Execution.TenantId,
            attempt.Id,
            2,
            SpaceCadParseJobProcessor.FinalizePreview,
            Now);
        fixture.Context.JobSteps.Add(finalizeStep);
        await fixture.Context.SaveChangesAsync();
        var finalized = await executor.ExecuteAsync(
            new(lease, 2, SpaceCadParseJobProcessor.FinalizePreview));
        finalizeStep.Complete(finalized.CheckpointJson, finalized.OutputHash, Now);
        attempt.Succeed(Now);
        job.Complete(attempt.Id, attempt.WorkerId, Now, finalized.CheckpointJson);
        await fixture.Context.SaveChangesAsync();

        fixture.Context.ChangeTracker.Clear();
        var problem = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.GetReviewWorkspaceAsync(
                fixture.Version.Id,
                fixture.Source.Id,
                started.JobId));

        Assert.Equal(SpaceErrorCodes.SourceUnsafe, problem.Code);
    }

    private static async Task<SpaceJobLease> ClaimAsync(Fixture fixture, Guid jobId)
    {
        fixture.Context.ChangeTracker.Clear();
        var job = await fixture.Context.Jobs.SingleAsync(item => item.Id == jobId);
        var attempt = job.Claim(
            "cad-worker",
            SpaceCadParseJobProcessor.Version,
            Now,
            TimeSpan.FromMinutes(5));
        fixture.Context.JobAttempts.Add(attempt);
        await fixture.Context.SaveChangesAsync();
        return new SpaceJobLease(
            fixture.Execution.TenantId,
            job.Id,
            attempt.Id,
            attempt.AttemptNo,
            attempt.WorkerId,
            job.JobType,
            job.SubjectType,
            job.SubjectId,
            job.InputHash,
            job.LockExpiresAtUtc!.Value,
            job.RowVersion);
    }

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
                .Options,
            execution,
            clock);
        var model = SpaceModel.Create(tenantId, Guid.NewGuid());
        var published = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "Published");
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
            "Draft",
            published.Id);
        model.ReserveDraft(version);
        var fileId = Guid.NewGuid();
        var storageKey = $"{tenantId:N}/{fileId:N}/source.content";
        var sourceBytes = Encoding.ASCII.GetBytes("0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n");
        var sourceHash = Sha256(sourceBytes);
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
            sourceBytes.Length,
            sourceHash);
        file.BeginScanning();
        file.MarkClean("test", "v1");
        var source = SpaceModelSource.CreateFileSource(
            tenantId,
            version.Id,
            SpaceSourceType.Dxf,
            file,
            "warehouse.dxf");
        context.AddRange(model, published, version, file, source);
        await context.SaveChangesAsync();
        var files = new MemoryFileStore();
        files.Seed(tenantId, fileId, storageKey, sourceBytes);
        var service = new SpaceCadParseService(
            context,
            execution,
            new AllowAccess(),
            null!,
            null!,
            clock,
            files);
        return new Fixture(
            context,
            execution,
            clock,
            version,
            source,
            files,
            service);
    }

    private static StartSpaceCadParseRequest Request(string sourceSha256)
    {
        var floorId = Guid.NewGuid();
        var transformHash = new string('d', 64);
        var metadata = new SpaceCadCoordinateMetadataV1(
            SpaceCadCoordinateVersions.SchemaVersion,
            sourceSha256,
            true,
            SpaceCadUnit.Millimeter,
            1m,
            SpaceCadUnit.Millimeter,
            1m,
            new SpaceCadPointV1(0, 0),
            new SpaceCadMillimeterPointV1(0, 0),
            0m,
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
            transformHash);
        return new StartSpaceCadParseRequest(
            floorId,
            SpaceCadUnit.Millimeter,
            1m,
            JsonSerializer.Serialize(metadata, JsonOptions),
            transformHash,
            Guid.NewGuid(),
            1,
            new string('e', 64),
            new string('f', 64));
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class DeterministicProvider : ISpaceCadParseProvider
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<SpaceCadGeneratedArtifact>> GenerateAsync(
            SpaceCadParseProviderRequest request,
            Stream source,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            IReadOnlyList<SpaceCadGeneratedArtifact> artifacts =
            [
                Artifact(SpaceArtifactType.CadIr, "cad-ir.json"),
                Artifact(SpaceArtifactType.LayerInventory, "layers.json"),
                Artifact(SpaceArtifactType.PreviewSet, "preview.json"),
            ];
            return Task.FromResult(artifacts);
        }

        private static SpaceCadGeneratedArtifact Artifact(
            SpaceArtifactType type,
            string fileName)
        {
            var bytes = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { schemaVersion = 1, type = type.ToString() }));
            return new SpaceCadGeneratedArtifact(
                type,
                "1",
                fileName,
                "application/json",
                ".json",
                bytes.Length,
                Sha256(bytes),
                _ => ValueTask.FromResult<Stream>(
                    new MemoryStream(bytes, writable: false)));
        }
    }

    private sealed class MemoryFileStore :
        ISpaceFileStore,
        ISpaceQuarantineStore
    {
        private readonly Dictionary<string, byte[]> _objects =
            new(StringComparer.Ordinal);

        public void Seed(Guid tenantId, Guid fileId, string storageKey, byte[] bytes) =>
            _objects[storageKey] = bytes;

        public Task<ISpaceQuarantineWriteSession> OpenWriteAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken cancellationToken = default)
        {
            var key = $"{tenantId:N}/{fileId:N}/{Guid.NewGuid():N}.content";
            return Task.FromResult<ISpaceQuarantineWriteSession>(
                new Session(key, _objects));
        }

        public Task<Stream> OpenQuarantinedReadAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(
                _objects[storageKey],
                writable: false));

        public Task DeleteAsync(
            Guid tenantId,
            Guid fileId,
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            _objects.Remove(storageKey);
            return Task.CompletedTask;
        }

        private sealed class Session(
            string storageKey,
            IDictionary<string, byte[]> objects) : ISpaceQuarantineWriteSession
        {
            private readonly MemoryStream _content = new();
            private bool _committed;

            public string StorageKey { get; } = storageKey;
            public Stream Content => _content;

            public Task CommitAsync(CancellationToken cancellationToken = default)
            {
                objects[StorageKey] = _content.ToArray();
                _committed = true;
                return Task.CompletedTask;
            }

            public Task AbortAsync(CancellationToken cancellationToken = default) =>
                Task.CompletedTask;

            public ValueTask DisposeAsync()
            {
                _content.Dispose();
                if (!_committed)
                    objects.Remove(StorageKey);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FileServiceProvider(MemoryFileStore files) :
        IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ISpaceFileStore) ||
            serviceType == typeof(ISpaceQuarantineStore)
                ? files
                : null;
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

    private sealed record Fixture(
        SpaceContext Context,
        TestExecutionContext Execution,
        FixedClock Clock,
        SpaceModelVersion Version,
        SpaceModelSource Source,
        MemoryFileStore Files,
        SpaceCadParseService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
