using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpaceBuildSceneJobStepExecutorTests
{
    private static readonly DateTime Now =
        new(2026, 8, 8, 18, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Rule_only_pipeline_persists_review_without_provider_or_draft_writes()
    {
        await using var fixture = await CreateFixtureAsync();
        var executor = new SpaceBuildSceneJobStepExecutor(
            fixture.Context,
            fixture.Execution,
            new FileServiceProvider(fixture.Files),
            new WarehouseGenerationOutputValidator(),
            new WarehouseDraftSynthesizer());
        var lease = await ClaimAsync(fixture);
        var job = fixture.Context.Jobs.Local.Single(item =>
            item.Id == fixture.BuildJob.Id);
        var attempt = fixture.Context.JobAttempts.Local.Single(item =>
            item.Id == lease.AttemptId);
        SpaceJobStepOutput? output = null;
        for (var index = 0; index < SpaceBuildSceneJobSteps.All.Count; index++)
        {
            var step = SpaceJobStep.Start(
                fixture.Execution.TenantId,
                attempt.Id,
                index + 1,
                SpaceBuildSceneJobSteps.All[index],
                Now);
            fixture.Context.JobSteps.Add(step);
            await fixture.Context.SaveChangesAsync();
            output = await executor.ExecuteAsync(new SpaceJobStepExecution(
                lease,
                index + 1,
                SpaceBuildSceneJobSteps.All[index]));
            if (SpaceBuildSceneJobSteps.All[index] ==
                SpaceBuildSceneJobSteps.PersistProposalsAndIssues)
            {
                var replay = await executor.ExecuteAsync(new SpaceJobStepExecution(
                    lease,
                    index + 1,
                    SpaceBuildSceneJobSteps.All[index]));
                Assert.Contains("\"reused\":true", replay.CheckpointJson);
                Assert.Equal(output.OutputHash, replay.OutputHash);
            }
            step.Complete(output.CheckpointJson, output.OutputHash, Now);
            await fixture.Context.SaveChangesAsync();
        }
        attempt.Succeed(Now);
        job.Complete(
            attempt.Id,
            attempt.WorkerId,
            Now,
            output!.CheckpointJson);
        await fixture.Context.SaveChangesAsync();

        fixture.Context.ChangeTracker.Clear();
        var run = await fixture.Context.GenerationRuns.SingleAsync(item =>
            item.Id == fixture.TargetRunId);
        var proposal = await fixture.Context.GenerationProposals.SingleAsync(item =>
            item.RunId == fixture.TargetRunId);
        Assert.Equal(SpaceGenerationRunStatus.AwaitingReview, run.Status);
        Assert.Equal(100, run.Progress);
        Assert.Equal("RULE_ONLY", run.DegradedReason);
        Assert.Equal(SpaceGenerationProposalStatus.Proposed, proposal.Status);
        Assert.Equal("Zone", proposal.ProposalType);
        Assert.Equal(fixture.Source.Sha256, proposal.SourceHash);
        Assert.Contains("Locked Zone", proposal.SuggestedAttributesJson);
        Assert.Contains("HumanLocked", proposal.FieldProvenanceJson);
        Assert.Empty(await fixture.Context.AiUsageRecords.ToListAsync());
        Assert.Empty(await fixture.Context.ZoneRevisions.ToListAsync());
        Assert.Empty(await fixture.Context.AisleRevisions.ToListAsync());
        Assert.Empty(await fixture.Context.RackRevisions.ToListAsync());
        Assert.Empty(await fixture.Context.ElementRevisions.ToListAsync());
        Assert.Equal(
            SpaceJobStatus.Succeeded,
            (await fixture.Context.Jobs.SingleAsync(item =>
                item.Id == fixture.BuildJob.Id)).Status);
        Assert.Equal(
            SpaceBuildSceneJobSteps.All.Count,
            await fixture.Context.JobSteps.CountAsync(item =>
                item.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task Provider_backed_mode_fails_closed_without_invocation()
    {
        await using var fixture = await CreateFixtureAsync(providerBacked: true);
        var executor = new SpaceBuildSceneJobStepExecutor(
            fixture.Context,
            fixture.Execution,
            new FileServiceProvider(fixture.Files),
            new WarehouseGenerationOutputValidator(),
            new WarehouseDraftSynthesizer());
        var lease = await ClaimAsync(fixture);
        _ = await executor.ExecuteAsync(new SpaceJobStepExecution(
            lease,
            1,
            SpaceBuildSceneJobSteps.LoadPinnedInputs));

        var failure = await Assert.ThrowsAsync<SpaceJobProcessingException>(() =>
            executor.ExecuteAsync(new SpaceJobStepExecution(
                lease,
                3,
                SpaceBuildSceneJobSteps.EnforceTenantPolicyAndQuota)));

        Assert.Equal(SpaceJobFailureKind.Resource, failure.FailureKind);
        Assert.Equal(SpaceErrorCodes.AiProviderUnavailable, failure.ErrorCode);
        fixture.Context.ChangeTracker.Clear();
        Assert.Equal(
            SpaceGenerationRunStatus.Failed,
            (await fixture.Context.GenerationRuns.SingleAsync(item =>
                item.Id == fixture.TargetRunId)).Status);
        Assert.Empty(await fixture.Context.GenerationProposals.Where(item =>
            item.RunId == fixture.TargetRunId).ToListAsync());
        Assert.Empty(await fixture.Context.AiUsageRecords.Where(item =>
            item.RunId == fixture.TargetRunId).ToListAsync());
    }

    [Fact]
    public async Task Rule_only_pipeline_consumes_frozen_rack_profile_version()
    {
        await using var fixture = await CreateFixtureAsync(withRackProfile: true);
        var executor = new SpaceBuildSceneJobStepExecutor(
            fixture.Context,
            fixture.Execution,
            new FileServiceProvider(fixture.Files),
            new WarehouseGenerationOutputValidator(),
            new WarehouseDraftSynthesizer());
        var lease = await ClaimAsync(fixture);
        var attempt = fixture.Context.JobAttempts.Local.Single(item =>
            item.Id == lease.AttemptId);
        for (var index = 0; index < SpaceBuildSceneJobSteps.All.Count; index++)
        {
            var step = SpaceJobStep.Start(
                fixture.Execution.TenantId,
                attempt.Id,
                index + 1,
                SpaceBuildSceneJobSteps.All[index],
                Now);
            fixture.Context.JobSteps.Add(step);
            await fixture.Context.SaveChangesAsync();
            var output = await executor.ExecuteAsync(new SpaceJobStepExecution(
                lease,
                index + 1,
                SpaceBuildSceneJobSteps.All[index]));
            step.Complete(output.CheckpointJson, output.OutputHash, Now);
            await fixture.Context.SaveChangesAsync();
        }

        fixture.Context.ChangeTracker.Clear();
        var proposal = await fixture.Context.GenerationProposals.SingleAsync(
            item => item.RunId == fixture.TargetRunId);
        Assert.Equal("Rack", proposal.ProposalType);
        Assert.Contains(
            fixture.RackProfileVersionId!.Value.ToString(),
            proposal.SuggestedAttributesJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            await fixture.Context.Issues.Where(item =>
                item.GenerationRunId == fixture.TargetRunId).ToArrayAsync(),
            item => item.Code == SpaceErrorCodes.RackProfileRequired);
    }

    private static async Task<Fixture> CreateFixtureAsync(
        bool providerBacked = false,
        bool withRackProfile = false)
    {
        var tenantId = Guid.NewGuid();
        var execution = new TestExecutionContext(tenantId, Guid.NewGuid());
        var context = new SpaceContext(
            new DbContextOptionsBuilder<SpaceContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString("N"),
                    SpaceTestDatabaseRoots.InMemory)
                .Options,
            execution,
            new FixedClock());
        var model = SpaceModel.Create(tenantId, Guid.NewGuid());
        var version = SpaceModelVersion.CreateDraft(
            tenantId,
            model.Id,
            1,
            "Rule-only Draft");
        var sourceFileId = Guid.NewGuid();
        var sourceStorageKey = $"{tenantId:N}/{sourceFileId:N}/source.dxf";
        var sourceHash = new string('b', 64);
        var sourceFile = CleanFile(
            sourceFileId,
            tenantId,
            sourceStorageKey,
            "source.dxf",
            "application/vnd.autocad.dxf",
            ".dxf",
            4,
            sourceHash,
            SpaceFileRetentionClass.Source);
        var source = SpaceModelSource.CreateFileSource(
            tenantId,
            version.Id,
            SpaceSourceType.Dxf,
            sourceFile,
            "source.dxf");
        var floorId = Guid.NewGuid();
        var request = new SpaceCadConversionRequest(
            tenantId,
            sourceFile.Id,
            source.Id,
            sourceHash,
            SpaceCadSourceFormat.Dxf,
            "test-converter",
            "1.0.0");
        var sourceRef = withRackProfile ? "H:RACK-1" : "H:ZONE-1";
        var layerName = withRackProfile ? "RACK" : "ZONE";
        var entity = new SpaceCadIrEntityV1(
            sourceRef,
            SpaceCadIrEntityType.ClosedPolyline,
            "LWPOLYLINE",
            layerName,
            null,
            [
                new(0, 0),
                new(10_000, 0),
                new(10_000, 8_000),
                new(0, 8_000),
            ],
            Radius: null,
            StartAngleDegrees: null,
            EndAngleDegrees: null,
            SpaceCadAffineTransformV1.Identity,
            new SpaceCadBoundsV1(0, 0, 10_000, 8_000),
            IsClosed: true,
            IsSupported: true,
            new Dictionary<string, string>());
        var package = new SpaceCadIrPackageV1(
            new SpaceCadIrDocumentV1(
                SpaceCadIrVersions.SchemaVersion,
                sourceHash,
                SpaceCadSourceFormat.Dxf,
                "AC1032",
                SpaceCadUnit.Millimeter,
                1,
                SpaceCadIrVersions.CoordinateSystem,
                new SpaceCadBoundsV1(0, 0, 10_000, 8_000),
                "test-converter",
                "1.0.0"),
            [new SpaceCadIrLayerV1(
                layerName,
                layerName,
                1,
                "ACI:7",
                "CONTINUOUS")],
            [],
            [entity],
            [],
            new SpaceCadIrSummaryV1(
                1,
                0,
                1,
                1,
                0,
                0,
                new SpaceCadBoundsV1(0, 0, 10_000, 8_000)));
        var preparation = SpaceCadCoordinatePreparation.Prepare(
            request,
            package,
            new SpaceCadCoordinateConfirmationV1(
                sourceHash,
                true,
                SpaceCadUnit.Millimeter,
                new SpaceCadPointV1(0, 0),
                new SpaceCadMillimeterPointV1(0, 0),
                0,
                new SpaceCadFloorAssignmentV1(
                    floorId,
                    "F1",
                    1,
                    0,
                    SpaceCadCoordinateVersions.TargetCoordinateSystem,
                    new SpaceCadBoundsV1(-1_000, -1_000, 11_000, 9_000))));
        var inventory = SpaceCadInventory.Build(request, preparation);
        var profile = SpaceCadMapping.Seal(new SpaceCadMappingProfileDraftV1(
            SpaceCadMappingVersions.SchemaVersion,
            Guid.NewGuid(),
            1,
            "Rule-only mapping",
            SpaceCadMappingScope.System,
            null,
            true,
            null,
            null,
            [new SpaceCadMappingRuleV1(
                withRackProfile ? "L-RACK" : "L-ZONE",
                100,
                SpaceCadMappingSourceKind.Layer,
                SpaceCadMappingMatchKind.Exact,
                layerName,
                null,
                null,
                null,
                withRackProfile
                    ? SpaceCadSemanticTarget.Rack
                    : SpaceCadSemanticTarget.Zone,
                null,
                SpaceCadGeometryRule.ClosedBoundary,
                null,
                null,
                0.95m,
                true)]));
        var mappingPreview = SpaceCadMapping.Preview(
            tenantId,
            inventory,
            profile);
        var semantic = SpaceCadSemanticParser.Parse(
            request,
            preparation,
            inventory,
            profile,
            mappingPreview);
        var diagnostics = SpaceCadSemanticDiagnostics.Build(
            request,
            preparation,
            inventory,
            profile,
            mappingPreview,
            semantic);

        source.ConfigureImport(
            SpaceCadParseJobProcessor.Version,
            profile.ProfileId,
            profile.Version,
            SpaceCadUnit.Millimeter.ToString(),
            1,
            SpaceCadCoordinatePreparation.SerializeMetadata(
                preparation.Metadata));
        source.BeginParsing();
        source.MarkPreviewReady();
        var parseJob = SpaceJob.CreateQueued(
            tenantId,
            SpaceJobType.CadParse,
            SpaceJobSubjectType.ModelSource,
            source.Id,
            new string('c', 64),
            new string('d', 64),
            50,
            3,
            execution.ActorId,
            Now,
            Guid.NewGuid());
        CompleteJob(parseJob, tenantId, "cad-test-worker");
        var previewSet = SpaceCadPreviewSet.Create(
            tenantId,
            version.Id,
            source.Id,
            parseJob.Id,
            semantic,
            diagnostics);
        var previewBytes = Encoding.UTF8.GetBytes(
            SpaceCadPreviewSet.Serialize(previewSet));
        var previewFileId = Guid.NewGuid();
        var previewStorageKey =
            $"{tenantId:N}/{previewFileId:N}/preview.json";
        var previewFile = CleanFile(
            previewFileId,
            tenantId,
            previewStorageKey,
            "preview.json",
            "application/json",
            ".json",
            previewBytes.Length,
            Sha256(previewBytes),
            SpaceFileRetentionClass.Artifact);
        var artifact = SpaceArtifact.Create(
            tenantId,
            version.Id,
            source,
            previewFile,
            SpaceArtifactType.PreviewSet,
            SpaceCadPreviewSetVersions.ArtifactSchema);
        artifact.AttachToJob(parseJob);

        SpaceRackGenerationProfile? rackProfile = null;
        SpaceRackGenerationProfileVersion? rackProfileVersion = null;
        if (withRackProfile)
        {
            rackProfile = SpaceRackGenerationProfile.CreateTenant(
                tenantId,
                "STANDARD-RACK",
                "Standard rack",
                null,
                execution.ActorId,
                Now);
            rackProfileVersion = SpaceRackGenerationProfileVersion.CreateReady(
                rackProfile,
                1,
                2400,
                1000,
                5000,
                [new(1, 0, 2200, 4, 2, 600, 500, 100, 1000)],
                execution.ActorId,
                Now);
        }

        var sourceRunJob = SpaceJob.CreateQueued(
            tenantId,
            SpaceJobType.BuildScene,
            SpaceJobSubjectType.ModelVersion,
            version.Id,
            new string('7', 64),
            new string('8', 64),
            70,
            5,
            execution.ActorId,
            Now,
            Guid.NewGuid());
        var sourceRun = SpaceGenerationRun.Create(new SpaceGenerationRunDefinition(
            tenantId,
            Guid.NewGuid(),
            version.Id,
            source.Id,
            sourceHash,
            version.ContentRevision,
            new string('3', 64),
            new string('4', 64),
            null,
            null,
            rackProfileVersion?.Id,
            "rules-test-v1",
            SpaceAiPolicySnapshot.Disabled,
            null,
            WarehouseGenerationInput.CurrentSchemaVersion,
            sourceRunJob.Id,
            floorId));
        var sourceProposal = SpaceGenerationProposal.Create(
            new SpaceGenerationProposalDefinition(
                tenantId,
                sourceRun.Id,
                version.Id,
                version.ContentRevision,
                sourceHash,
                "legacy-zone-key",
                withRackProfile ? "Rack" : "Zone",
                JsonSerializer.Serialize(semantic.Items.Single().Geometry, JsonOptions),
                "{\"name\":\"Original Zone\"}",
                "{}",
                JsonSerializer.Serialize(new[] { sourceRef }, JsonOptions),
                "[]",
                "{}",
                0.95m,
                SpaceConfidenceBand.High,
                false));
        sourceProposal.Modify(
            "[{\"op\":\"replace\",\"path\":\"/attributes/name\",\"value\":\"Locked Zone\"}]",
            "[\"/attributes/name\"]");
        var sourceDecision = SpaceProposalDecision.Create(
            tenantId,
            sourceRun.Id,
            sourceProposal.Id,
            SpaceProposalDecisionType.Modify,
            SpaceAiProposalPatchPolicyV1.BuildSnapshot(
                withRackProfile ? "Rack" : "Zone",
                sourceProposal.SuggestedGeometryJson,
                "{\"name\":\"Original Zone\"}",
                "{}"),
            SpaceAiProposalPatchPolicyV1.BuildSnapshot(
                withRackProfile ? "Rack" : "Zone",
                sourceProposal.SuggestedGeometryJson,
                "{\"name\":\"Locked Zone\"}",
                "{}"),
            "[\"/attributes/name\"]",
            "USER_CORRECTION",
            null,
            Guid.NewGuid());

        var runId = Guid.NewGuid();
        var buildJob = SpaceJob.CreateQueued(
            tenantId,
            SpaceJobType.BuildScene,
            SpaceJobSubjectType.ModelVersion,
            version.Id,
            new string('e', 64),
            new string('f', 64),
            70,
            5,
            execution.ActorId,
            Now,
            Guid.NewGuid(),
            JsonSerializer.Serialize(new
            {
                schemaVersion = SpaceAiRunRecoveryContract.SchemaVersion,
                runId,
                basedOnRunId = sourceRun.Id,
                sourceId = source.Id,
                expectedContentRevision = version.ContentRevision,
                mode = providerBacked
                    ? SpaceAiRunRecoveryContract.SamePolicyMode
                    : SpaceAiRunRecoveryContract.RuleOnlyMode,
            }, JsonOptions));
        var run = SpaceGenerationRun.Create(new SpaceGenerationRunDefinition(
            tenantId,
            Guid.NewGuid(),
            version.Id,
            source.Id,
            sourceHash,
            version.ContentRevision,
            new string('1', 64),
            new string('2', 64),
            sourceRun.Id,
            null,
            rackProfileVersion?.Id,
            "rules-test-v1",
            providerBacked
                ? SpaceAiPolicySnapshot.MetadataOnly
                : SpaceAiPolicySnapshot.Disabled,
            providerBacked ? Guid.NewGuid() : null,
            WarehouseGenerationInput.CurrentSchemaVersion,
            buildJob.Id,
            floorId,
            runId));
        var lockedFact = SpaceGenerationLockedFact.CreateSameSource(
            tenantId,
            run.Id,
            sourceRun.Id,
            sourceProposal.Id,
            sourceDecision.Id,
            sourceHash,
            sourceProposal.SourceKey,
            sourceProposal.ProposalType,
            "/attributes/name",
            "\"Locked Zone\"");
        var entities = new List<object>
        {
            model,
            version,
            sourceFile,
            source,
            parseJob,
            previewFile,
            artifact,
            sourceRunJob,
            sourceRun,
            sourceProposal,
            sourceDecision,
            buildJob,
            run,
            lockedFact,
        };
        if (rackProfile is not null && rackProfileVersion is not null)
        {
            entities.Add(rackProfile);
            entities.Add(rackProfileVersion);
        }
        context.AddRange(entities);
        await context.SaveChangesAsync();
        var files = new MemoryFileStore();
        files.Seed(previewStorageKey, previewBytes);
        return new Fixture(
            context,
            execution,
            source,
            buildJob,
            run.Id,
            files,
            rackProfileVersion?.Id);
    }

    private static SpaceFile CleanFile(
        Guid id,
        Guid tenantId,
        string storageKey,
        string name,
        string contentType,
        string extension,
        long size,
        string hash,
        SpaceFileRetentionClass retention)
    {
        var file = SpaceFile.CreateUploading(
            id,
            tenantId,
            storageKey,
            name,
            contentType,
            retention);
        file.CompleteQuarantine(contentType, extension, size, hash);
        file.BeginScanning();
        file.MarkClean("test", "v1", "TEST_GENERATED");
        return file;
    }

    private static void CompleteJob(
        SpaceJob job,
        Guid tenantId,
        string workerId)
    {
        var attempt = job.Claim(
            workerId,
            SpaceCadParseJobProcessor.Version,
            Now,
            TimeSpan.FromMinutes(5));
        attempt.Succeed(Now);
        job.Complete(
            attempt.Id,
            workerId,
            Now,
            JsonSerializer.Serialize(new { completed = true }));
    }

    private static async Task<SpaceJobLease> ClaimAsync(Fixture fixture)
    {
        fixture.Context.ChangeTracker.Clear();
        var job = await fixture.Context.Jobs.SingleAsync(item =>
            item.Id == fixture.BuildJob.Id);
        var attempt = job.Claim(
            "build-scene-worker",
            SpaceBuildSceneJobProcessor.Version,
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

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class MemoryFileStore : ISpaceFileStore
    {
        private readonly Dictionary<string, byte[]> _objects =
            new(StringComparer.Ordinal);

        public void Seed(string storageKey, byte[] bytes) =>
            _objects[storageKey] = bytes;

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
    }

    private sealed class FileServiceProvider(MemoryFileStore files) :
        IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ISpaceFileStore) ? files : null;
    }

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId) : ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed record Fixture(
        SpaceContext Context,
        TestExecutionContext Execution,
        SpaceModelSource Source,
        SpaceJob BuildJob,
        Guid TargetRunId,
        MemoryFileStore Files,
        Guid? RackProfileVersionId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
