using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
        var request = fixture.Request;

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
            fixture.Request,
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
            fixture.Request,
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
    public async Task Current_payload_requires_a_sealed_mapping_replay_snapshot()
    {
        await using var fixture = await CreateFixtureAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            fixture.Request,
            "cad-missing-mapping-replay");
        var job = await fixture.Context.Jobs.SingleAsync(item => item.Id == started.JobId);
        var payload = JsonNode.Parse(job.PayloadJson)!.AsObject();
        payload.Remove("mappingReplaySnapshotJson");
        var frozenPayload = payload.ToJsonString(JsonOptions);
        fixture.Context.Entry(job).Property(item => item.PayloadJson).CurrentValue =
            frozenPayload;
        fixture.Context.Entry(job).Property(item => item.InputHash).CurrentValue =
            Sha256(Encoding.UTF8.GetBytes(frozenPayload));
        await fixture.Context.SaveChangesAsync();
        var lease = await ClaimAsync(fixture, started.JobId);
        var provider = new DeterministicProvider();
        var executor = new SpaceCadParseJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(fixture.Files),
            provider);

        var problem = await Assert.ThrowsAsync<SpaceJobProcessingException>(() =>
            executor.ExecuteAsync(new(
                lease,
                1,
                SpaceCadParseJobProcessor.GenerateArtifacts)));

        Assert.Equal(SpaceErrorCodes.CadParseInvalid, problem.ErrorCode);
        Assert.Equal(SpaceJobFailureKind.Input, problem.FailureKind);
        Assert.Equal(0, provider.CallCount);
        Assert.Empty(await fixture.Context.Artifacts.ToListAsync());
    }

    [Fact]
    public async Task Current_payload_requires_a_sealed_provider_version()
    {
        await using var fixture = await CreateFixtureAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            fixture.Request,
            "cad-missing-provider-version");
        var job = await fixture.Context.Jobs.SingleAsync(item => item.Id == started.JobId);
        var payload = JsonNode.Parse(job.PayloadJson)!.AsObject();
        payload.Remove("preferredProviderVersion");
        var frozenPayload = payload.ToJsonString(JsonOptions);
        fixture.Context.Entry(job).Property(item => item.PayloadJson).CurrentValue =
            frozenPayload;
        fixture.Context.Entry(job).Property(item => item.InputHash).CurrentValue =
            Sha256(Encoding.UTF8.GetBytes(frozenPayload));
        await fixture.Context.SaveChangesAsync();
        var lease = await ClaimAsync(fixture, started.JobId);
        var provider = new DeterministicProvider();
        var executor = new SpaceCadParseJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(fixture.Files),
            provider);

        var problem = await Assert.ThrowsAsync<SpaceJobProcessingException>(() =>
            executor.ExecuteAsync(new(
                lease,
                1,
                SpaceCadParseJobProcessor.GenerateArtifacts)));

        Assert.Equal(SpaceErrorCodes.CadParseInvalid, problem.ErrorCode);
        Assert.Equal(SpaceJobFailureKind.Input, problem.FailureKind);
        Assert.Equal(0, provider.CallCount);
        Assert.Empty(await fixture.Context.Artifacts.ToListAsync());
    }

    [Fact]
    public async Task Legacy_v4_payload_without_provider_version_remains_explicitly_supported()
    {
        await using var fixture = await CreateFixtureAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            fixture.Request,
            "cad-legacy-v4-provider-version");
        var job = await fixture.Context.Jobs.SingleAsync(item => item.Id == started.JobId);
        var payload = JsonNode.Parse(job.PayloadJson)!.AsObject();
        payload["schemaVersion"] = SpaceCadParsePayloadVersions.LegacyMappingReplay;
        payload.Remove("preferredProviderVersion");
        var frozenPayload = payload.ToJsonString(JsonOptions);
        fixture.Context.Entry(job).Property(item => item.PayloadJson).CurrentValue =
            frozenPayload;
        fixture.Context.Entry(job).Property(item => item.InputHash).CurrentValue =
            Sha256(Encoding.UTF8.GetBytes(frozenPayload));
        await fixture.Context.SaveChangesAsync();
        var lease = await ClaimAsync(fixture, started.JobId);
        var provider = new DeterministicProvider();
        var executor = new SpaceCadParseJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(fixture.Files),
            provider);

        await executor.ExecuteAsync(new(
            lease,
            1,
            SpaceCadParseJobProcessor.GenerateArtifacts));

        Assert.Equal(1, provider.CallCount);
        Assert.Equal(3, await fixture.Context.Artifacts.CountAsync());
    }

    [Fact]
    public async Task Legacy_v3_payload_without_mapping_replay_snapshot_remains_explicitly_supported()
    {
        await using var fixture = await CreateFixtureAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            fixture.Request,
            "cad-legacy-v3-mapping-replay");
        var job = await fixture.Context.Jobs.SingleAsync(item => item.Id == started.JobId);
        var payload = JsonNode.Parse(job.PayloadJson)!.AsObject();
        payload["schemaVersion"] = SpaceCadParsePayloadVersions.LegacyProviderRouting;
        payload.Remove("mappingReplaySnapshotJson");
        var frozenPayload = payload.ToJsonString(JsonOptions);
        fixture.Context.Entry(job).Property(item => item.PayloadJson).CurrentValue =
            frozenPayload;
        fixture.Context.Entry(job).Property(item => item.InputHash).CurrentValue =
            Sha256(Encoding.UTF8.GetBytes(frozenPayload));
        await fixture.Context.SaveChangesAsync();
        var lease = await ClaimAsync(fixture, started.JobId);
        var provider = new DeterministicProvider();
        var executor = new SpaceCadParseJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(fixture.Files),
            provider);

        await executor.ExecuteAsync(new(
            lease,
            1,
            SpaceCadParseJobProcessor.GenerateArtifacts));

        Assert.Equal(1, provider.CallCount);
        Assert.Equal(3, await fixture.Context.Artifacts.CountAsync());
    }

    [Fact]
    public async Task Runner_completes_preview_without_draft_writes()
    {
        await using var fixture = await CreateFixtureAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            fixture.Request,
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
            fixture.Request,
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

    [Fact]
    public async Task Completed_parse_loads_a_bound_review_workspace_and_detects_stale_draft()
    {
        await using var fixture = await CreateFixtureAsync();
        var provider = new ReviewWorkspaceProvider(
            fixture.Execution.TenantId,
            fixture.Source.Sha256);
        var request = await ConfirmPreparationAsync(
            fixture,
            provider.Request,
            provider.SemanticPreviewSha256);
        var model = await fixture.Context.Models.SingleAsync();
        fixture.Context.FloorRevisions.Add(SpaceFloorRevision.Create(
            fixture.Execution.TenantId,
            fixture.Version.Id,
            request.FloorLogicalId,
            model.SiteId,
            1,
            "F1",
            "Floor 1",
            0,
            6000));
        await fixture.Context.SaveChangesAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            request,
            "cad-review-workspace-success");
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
        var workspace = await fixture.Service.GetReviewWorkspaceAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            started.JobId);
        Assert.Equal(fixture.Version.Id, workspace.ModelVersionId);
        Assert.Equal(request.FloorLogicalId, workspace.FloorLogicalId);
        Assert.Equal(0, workspace.EditorContentRevision);
        Assert.Matches("^[0-9a-f]{64}$", workspace.WorkspaceSha256);
        Assert.Equal(fixture.Source.Id, workspace.SourceId);
        Assert.Equal(started.JobId, workspace.CadParseJobId);
        Assert.Matches("^[0-9a-f]{64}$", workspace.ChangesetSha256!);
        var change = Assert.Single(workspace.Changes!);
        Assert.Equal(SpaceCadChangeKind.Add, change.Kind);
        Assert.True(change.CanApply);
        Assert.Equal(SpaceElementTypes.Wall, change.ObjectType);
        Assert.Equal(1, workspace.ChangeSummary!.AddCount);

        var currentCandidates = await fixture.Service.ListReviewCandidatesAsync(
            fixture.Version.Id,
            request.FloorLogicalId,
            50);
        Assert.Equal(0, currentCandidates.CurrentContentRevision);
        Assert.False(currentCandidates.Truncated);
        var currentCandidate = Assert.Single(currentCandidates.Items);
        Assert.Equal(fixture.Source.Id, currentCandidate.SourceId);
        Assert.Equal(started.JobId, currentCandidate.JobId);
        Assert.Equal("warehouse.dxf", currentCandidate.SourceDisplayName);
        Assert.True(currentCandidate.IsCurrentRevision);
        Assert.True(currentCandidate.CanLoadReview);
        Assert.Equal(request.MappingProfileId, currentCandidate.MappingProfileId);

        var version = await fixture.Context.Versions.SingleAsync(
            item => item.Id == fixture.Version.Id);
        version.TouchContent();
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var historicalCandidates = await fixture.Service.ListReviewCandidatesAsync(
            fixture.Version.Id,
            request.FloorLogicalId,
            50);
        Assert.Equal(1, historicalCandidates.CurrentContentRevision);
        var historicalCandidate = Assert.Single(historicalCandidates.Items);
        Assert.False(historicalCandidate.IsCurrentRevision);
        Assert.False(historicalCandidate.CanLoadReview);

        var stale = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.GetReviewWorkspaceAsync(
                fixture.Version.Id,
                fixture.Source.Id,
                started.JobId));
        Assert.Equal(SpaceErrorCodes.ParseChangesetStale, stale.Code);
        Assert.Equal(409, stale.StatusCode);
        Assert.Equal("start-new-cad-parse", stale.RecoveryAction);
    }

    [Fact]
    public async Task Legacy_v1_payload_requires_a_new_parse_instead_of_permanent_retry()
    {
        await using var fixture = await CreateFixtureAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            fixture.Request,
            "cad-legacy-v1");
        var job = await fixture.Context.Jobs.SingleAsync(item => item.Id == started.JobId);
        var payload = JsonNode.Parse(job.PayloadJson)!.AsObject();
        payload["schemaVersion"] = 1;
        payload.Remove("baseContentRevision");
        payload.Remove("baseContentHash");
        fixture.Context.Entry(job).Property(item => item.PayloadJson).CurrentValue =
            payload.ToJsonString(JsonOptions);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var problem = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.RetryAsync(
                fixture.Version.Id,
                fixture.Source.Id,
                started.JobId,
                "cad-legacy-v1-retry"));

        Assert.Equal(SpaceErrorCodes.CadParseInvalid, problem.Code);
        Assert.Equal(409, problem.StatusCode);
        Assert.Equal("start-new-cad-parse", problem.RecoveryAction);
        Assert.Single(await fixture.Context.Jobs.ToListAsync());
    }

    [Fact]
    public async Task Review_changes_apply_once_replay_safely_and_reject_changed_selection()
    {
        await using var fixture = await CreateFixtureAsync();
        var provider = new ReviewWorkspaceProvider(
            fixture.Execution.TenantId,
            fixture.Source.Sha256);
        var model = await fixture.Context.Models.SingleAsync();
        var floor = SpaceFloorRevision.Create(
            fixture.Execution.TenantId,
            fixture.Version.Id,
            provider.Request.FloorLogicalId,
            model.SiteId,
            1,
            "F1",
            "Floor 1",
            0,
            6000);
        fixture.Context.FloorRevisions.Add(floor);
        await fixture.Context.SaveChangesAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            await ConfirmPreparationAsync(
                fixture,
                provider.Request,
                provider.SemanticPreviewSha256),
            "cad-review-apply");
        await CompleteParseAsync(fixture, provider, started.JobId);

        fixture.Context.ChangeTracker.Clear();
        var workspace = await fixture.Service.GetReviewWorkspaceAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            started.JobId);
        var change = Assert.Single(workspace.Changes!);
        var clientId = Guid.NewGuid();
        var lease = SpaceEditLease.Create(
            fixture.Execution.TenantId,
            fixture.Version.Id,
            floor.LogicalId,
            fixture.Execution.ActorId,
            "CAD reviewer",
            clientId,
            Now,
            TimeSpan.FromSeconds(90));
        fixture.Context.EditLeases.Add(lease);
        await fixture.Context.SaveChangesAsync();
        var request = new ApplySpaceCadChangesetRequest(
            Guid.NewGuid(),
            clientId,
            lease.LeaseId,
            ExpectedFloorRevision: 0,
            workspace.EditorContentRevision,
            workspace.EditorContentHash,
            workspace.WorkspaceSha256,
            [change.ChangeId]);

        var applied = await fixture.Service.ApplyReviewChangesAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            started.JobId,
            request);
        var replay = await fixture.Service.ApplyReviewChangesAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            started.JobId,
            request);

        Assert.False(applied.IdempotentReplay);
        Assert.True(replay.IdempotentReplay);
        Assert.Equal(applied.FloorRevision, replay.FloorRevision);
        Assert.Equal(1, applied.AppliedChangeCount);
        var addUndo = Assert.Single(applied.UndoCommands);
        Assert.Equal(SpaceElementCommandContract.DeleteObject, addUndo.Type);
        Assert.Equal(change.LogicalId, addUndo.TargetLogicalId);
        Assert.Null(addUndo.UpdateProperties);
        var addRedo = Assert.Single(applied.RedoCommands);
        Assert.Equal(
            SpaceElementCommandContract.RestoreLogicalObject,
            addRedo.Type);
        Assert.Equal(change.LogicalId, addRedo.TargetLogicalId);
        Assert.Equal(applied.UndoCommands, replay.UndoCommands);
        Assert.Equal(applied.RedoCommands, replay.RedoCommands);
        var element = Assert.Single(await fixture.Context.ElementRevisions
            .AsNoTracking()
            .ToListAsync());
        Assert.Equal(SpaceElementTypes.Wall, element.ElementType);
        Assert.Equal(fixture.Source.Id, element.SourceId);
        Assert.Equal(change.SourceRef, element.SourceRef);
        var batch = Assert.Single(await fixture.Context.ElementCommandBatches
            .AsNoTracking()
            .ToListAsync());
        Assert.Equal(workspace.EditorContentRevision, batch.ExpectedContentRevision);
        Assert.Matches("^[0-9a-f]{64}$", batch.ChangesetSha256!);

        var conflict = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.ApplyReviewChangesAsync(
                fixture.Version.Id,
                fixture.Source.Id,
                started.JobId,
                request with { ChangeIds = ["cad-change-different"] }));
        Assert.Equal(SpaceErrorCodes.CommandConflict, conflict.Code);
        Assert.Single(await fixture.Context.ElementRevisions.AsNoTracking().ToListAsync());
        Assert.Single(await fixture.Context.ElementCommandBatches.AsNoTracking().ToListAsync());

        var currentVersion = await fixture.Context.Versions.SingleAsync(
            item => item.Id == fixture.Version.Id);
        currentVersion.TouchContent();
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var staleReplay = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.ApplyReviewChangesAsync(
                fixture.Version.Id,
                fixture.Source.Id,
                started.JobId,
                request));
        Assert.Equal(SpaceErrorCodes.ParseChangesetStale, staleReplay.Code);

        lease = await fixture.Context.EditLeases.SingleAsync();
        lease.Release(lease.LeaseId, fixture.Execution.ActorId, clientId, Now);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var lost = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.ApplyReviewChangesAsync(
                fixture.Version.Id,
                fixture.Source.Id,
                started.JobId,
                request));
        Assert.Equal(SpaceErrorCodes.EditLeaseLost, lost.Code);
        Assert.Single(await fixture.Context.ElementRevisions.AsNoTracking().ToListAsync());
        Assert.Single(await fixture.Context.ElementCommandBatches.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Cad_changeset_applies_more_than_the_interactive_command_limit_atomically()
    {
        const int changeCount = 101;
        await using var fixture = await CreateFixtureAsync();
        var provider = new ReviewWorkspaceProvider(
            fixture.Execution.TenantId,
            fixture.Source.Sha256,
            changeCount);
        var model = await fixture.Context.Models.SingleAsync();
        var floor = SpaceFloorRevision.Create(
            fixture.Execution.TenantId,
            fixture.Version.Id,
            provider.Request.FloorLogicalId,
            model.SiteId,
            1,
            "F1",
            "Floor 1",
            0,
            6000);
        fixture.Context.FloorRevisions.Add(floor);
        await fixture.Context.SaveChangesAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            await ConfirmPreparationAsync(
                fixture,
                provider.Request,
                provider.SemanticPreviewSha256),
            "cad-review-large-apply");
        await CompleteParseAsync(fixture, provider, started.JobId);
        fixture.Context.ChangeTracker.Clear();

        var workspace = await fixture.Service.GetReviewWorkspaceAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            started.JobId);
        Assert.Equal(changeCount, workspace.Changes!.Count);
        Assert.All(workspace.Changes, change => Assert.True(change.CanApply));
        var clientId = Guid.NewGuid();
        var lease = SpaceEditLease.Create(
            fixture.Execution.TenantId,
            fixture.Version.Id,
            floor.LogicalId,
            fixture.Execution.ActorId,
            "CAD reviewer",
            clientId,
            Now,
            TimeSpan.FromSeconds(90));
        fixture.Context.EditLeases.Add(lease);
        await fixture.Context.SaveChangesAsync();

        var interactive = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Design.ApplyElementCommandsAsync(
                fixture.Version.Id,
                floor.LogicalId,
                new ApplySpaceElementCommandBatchRequest(
                    SpaceElementCommandContract.SchemaVersion,
                    Guid.NewGuid(),
                    clientId,
                    lease.LeaseId,
                    ExpectedFloorRevision: 0,
                    workspace.Changes.Select(change =>
                        new SpaceElementCommandDto(
                            Guid.NewGuid(),
                            SpaceElementCommandContract.CreateElement,
                            change.LogicalId,
                            UpdateProperties: null,
                            CreateElement: new SpaceCreateElementDto(
                                SpaceElementTypes.Wall,
                                """
                                {"schemaVersion":1,"kind":"box","width":10000,"height":6000,"depth":200}
                                """,
                                0,
                                0,
                                0,
                                0,
                                10_000,
                                6_000,
                                200,
                                null,
                                null,
                                fixture.Source.Id,
                                change.SourceRef,
                                [])))
                        .ToArray(),
                    workspace.EditorContentRevision,
                    workspace.EditorContentHash,
                    workspace.ChangesetSha256)));
        Assert.Equal(SpaceErrorCodes.RequestInvalid, interactive.Code);
        Assert.Contains("between 1 and 100", interactive.Detail);

        var applied = await fixture.Service.ApplyReviewChangesAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            started.JobId,
            new ApplySpaceCadChangesetRequest(
                Guid.NewGuid(),
                clientId,
                lease.LeaseId,
                ExpectedFloorRevision: 0,
                workspace.EditorContentRevision,
                workspace.EditorContentHash,
                workspace.WorkspaceSha256,
                workspace.Changes.Select(change => change.ChangeId).ToArray()));

        Assert.Equal(changeCount, applied.AppliedChangeCount);
        Assert.Equal(changeCount, applied.UndoCommands.Count);
        Assert.Equal(changeCount, applied.RedoCommands.Count);
        Assert.Equal(1, applied.FloorRevision);
        Assert.Equal(
            workspace.EditorContentRevision + 1,
            applied.VersionContentRevision);
        Assert.Equal(
            changeCount,
            await fixture.Context.ElementRevisions.AsNoTracking().CountAsync());
        Assert.Single(await fixture.Context.ElementCommandBatches
            .AsNoTracking()
            .ToListAsync());
    }

    [Fact]
    public async Task Locked_manual_correction_survives_reparse_as_a_blocking_conflict()
    {
        await using var fixture = await CreateFixtureAsync();
        var provider = new ReviewWorkspaceProvider(
            fixture.Execution.TenantId,
            fixture.Source.Sha256);
        var model = await fixture.Context.Models.SingleAsync();
        var floor = SpaceFloorRevision.Create(
            fixture.Execution.TenantId,
            fixture.Version.Id,
            provider.Request.FloorLogicalId,
            model.SiteId,
            1,
            "F1",
            "Floor 1",
            0,
            6000);
        fixture.Context.FloorRevisions.Add(floor);
        await fixture.Context.SaveChangesAsync();
        var firstParse = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            await ConfirmPreparationAsync(
                fixture,
                provider.Request,
                provider.SemanticPreviewSha256),
            "cad-review-lock-first");
        await CompleteParseAsync(fixture, provider, firstParse.JobId);
        fixture.Context.ChangeTracker.Clear();

        var firstWorkspace = await fixture.Service.GetReviewWorkspaceAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            firstParse.JobId);
        var firstChange = Assert.Single(firstWorkspace.Changes!);
        var clientId = Guid.NewGuid();
        var lease = SpaceEditLease.Create(
            fixture.Execution.TenantId,
            fixture.Version.Id,
            floor.LogicalId,
            fixture.Execution.ActorId,
            "CAD reviewer",
            clientId,
            Now,
            TimeSpan.FromSeconds(90));
        fixture.Context.EditLeases.Add(lease);
        await fixture.Context.SaveChangesAsync();
        await fixture.Service.ApplyReviewChangesAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            firstParse.JobId,
            new ApplySpaceCadChangesetRequest(
                Guid.NewGuid(),
                clientId,
                lease.LeaseId,
                ExpectedFloorRevision: 0,
                firstWorkspace.EditorContentRevision,
                firstWorkspace.EditorContentHash,
                firstWorkspace.WorkspaceSha256,
                [firstChange.ChangeId]));

        fixture.Context.ChangeTracker.Clear();
        var element = await fixture.Context.ElementRevisions.SingleAsync();
        var version = await fixture.Context.Versions.SingleAsync(item =>
            item.Id == fixture.Version.Id);
        var attributes = await fixture.Context.ElementAttributes
            .Where(item => item.ElementRevisionId == element.Id)
            .Select(item => new SpaceElementAttributeWriteDto(
                item.Namespace,
                item.Key,
                item.ValueType,
                item.Value,
                item.Unit))
            .ToArrayAsync();
        SpaceUpdateElementPropertiesDto update(
            SpaceElementRevision target,
            int x,
            bool? lockState = null) => new(
                target.GeometryJson,
                x,
                target.Y,
                target.Z,
                target.RotationZ,
                target.Width,
                target.Height,
                target.Depth,
                target.BusinessCode,
                target.LinkedEntityType,
                target.LinkedLogicalId,
                attributes,
                target.ElementType,
                lockState);
        ApplySpaceElementCommandBatchRequest command(
            long floorRevision,
            long contentRevision,
            string? contentHash,
            SpaceUpdateElementPropertiesDto payload) => new(
                SpaceElementCommandContract.SchemaVersion,
                Guid.NewGuid(),
                clientId,
                lease.LeaseId,
                floorRevision,
                [new SpaceElementCommandDto(
                    Guid.NewGuid(),
                    SpaceElementCommandContract.UpdateProperties,
                    element.LogicalId,
                    payload)],
                contentRevision,
                contentHash);

        var parsedX = element.X;
        var manualCorrection = await fixture.Design.ApplyElementCommandsAsync(
            fixture.Version.Id,
            floor.LogicalId,
            command(
                1,
                version.ContentRevision,
                version.ContentHash,
                update(element, element.X + 250)));
        var manualX = Assert.Single(manualCorrection.AffectedObjects).Element.X;
        fixture.Context.ChangeTracker.Clear();
        var correctionParse = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            await ConfirmPreparationAsync(
                fixture,
                provider.Request,
                provider.SemanticPreviewSha256),
            "cad-review-correction-history");
        await CompleteParseAsync(fixture, provider, correctionParse.JobId);
        fixture.Context.ChangeTracker.Clear();
        var correctionWorkspace = await fixture.Service.GetReviewWorkspaceAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            correctionParse.JobId);
        var modify = Assert.Single(correctionWorkspace.Changes!);
        Assert.Equal(SpaceCadChangeKind.Modify, modify.Kind);
        var correctionApply = new ApplySpaceCadChangesetRequest(
            Guid.NewGuid(),
            clientId,
            lease.LeaseId,
            ExpectedFloorRevision: 2,
            correctionWorkspace.EditorContentRevision,
            correctionWorkspace.EditorContentHash,
            correctionWorkspace.WorkspaceSha256,
            [modify.ChangeId]);
        var correctionApplied = await fixture.Service.ApplyReviewChangesAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            correctionParse.JobId,
            correctionApply);
        var modifyUndo = Assert.Single(correctionApplied.UndoCommands);
        var modifyRedo = Assert.Single(correctionApplied.RedoCommands);
        Assert.Equal(SpaceElementCommandContract.UpdateProperties, modifyUndo.Type);
        Assert.Equal(manualX, modifyUndo.UpdateProperties!.X);
        Assert.Equal(SpaceElementCommandContract.UpdateProperties, modifyRedo.Type);
        Assert.Equal(parsedX, modifyRedo.UpdateProperties!.X);
        var correctionReplay = await fixture.Service.ApplyReviewChangesAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            correctionParse.JobId,
            correctionApply);
        Assert.True(correctionReplay.IdempotentReplay);
        Assert.Equal(correctionApplied.UndoCommands, correctionReplay.UndoCommands);
        Assert.Equal(correctionApplied.RedoCommands, correctionReplay.RedoCommands);

        fixture.Context.ChangeTracker.Clear();
        version = await fixture.Context.Versions.SingleAsync(item =>
            item.Id == fixture.Version.Id);
        element = await fixture.Context.ElementRevisions.SingleAsync();

        var locked = await fixture.Design.ApplyElementCommandsAsync(
            fixture.Version.Id,
            floor.LogicalId,
            command(
                3,
                version.ContentRevision,
                version.ContentHash,
                update(element, element.X + 250, true)));
        Assert.True(Assert.Single(locked.AffectedObjects)
            .Element.IsManualCorrectionLocked);
        Assert.Equal(1, locked.AffectedObjects[0].Element.UserCorrectionVersion);

        fixture.Context.ChangeTracker.Clear();
        version = await fixture.Context.Versions.SingleAsync(item =>
            item.Id == fixture.Version.Id);
        element = await fixture.Context.ElementRevisions.SingleAsync();
        var revised = await fixture.Design.ApplyElementCommandsAsync(
            fixture.Version.Id,
            floor.LogicalId,
            command(
                4,
                version.ContentRevision,
                version.ContentHash,
                update(element, element.X + 250)));
        Assert.Equal(2, Assert.Single(revised.AffectedObjects)
            .Element.UserCorrectionVersion);
        var protectedX = revised.AffectedObjects[0].Element.X;

        fixture.Context.ChangeTracker.Clear();
        var secondParse = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            await ConfirmPreparationAsync(
                fixture,
                provider.Request,
                provider.SemanticPreviewSha256),
            "cad-review-lock-second");
        await CompleteParseAsync(fixture, provider, secondParse.JobId);
        fixture.Context.ChangeTracker.Clear();
        var workspace = await fixture.Service.GetReviewWorkspaceAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            secondParse.JobId);

        var conflict = Assert.Single(workspace.Changes!);
        Assert.Equal(SpaceCadChangeKind.Conflict, conflict.Kind);
        Assert.False(conflict.CanApply);
        Assert.True(conflict.IsManualCorrectionLocked);
        Assert.Equal(2, conflict.UserCorrectionVersion);
        Assert.Equal(
            SpaceErrorCodes.CadManualCorrectionLocked,
            conflict.BlockingReasonCode);
        Assert.Equal(1, workspace.ChangeSummary!.ConflictCount);
        Assert.Equal(1, workspace.Summary.OpenBlockingCount);
        var blocking = Assert.Single(workspace.Items, item =>
            item.Code == SpaceErrorCodes.CadManualCorrectionLocked);
        Assert.Equal(conflict.LogicalId, blocking.TargetLogicalId);

        var batchesBefore = await fixture.Context.ElementCommandBatches.CountAsync();
        var rejected = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.ApplyReviewChangesAsync(
                fixture.Version.Id,
                fixture.Source.Id,
                secondParse.JobId,
                new ApplySpaceCadChangesetRequest(
                    Guid.NewGuid(),
                    clientId,
                    lease.LeaseId,
                    ExpectedFloorRevision: 5,
                    workspace.EditorContentRevision,
                    workspace.EditorContentHash,
                    workspace.WorkspaceSha256,
                    [conflict.ChangeId])));
        Assert.Equal(SpaceErrorCodes.CadParseInvalid, rejected.Code);
        Assert.Equal(
            batchesBefore,
            await fixture.Context.ElementCommandBatches.CountAsync());
        Assert.Equal(
            protectedX,
            (await fixture.Context.ElementRevisions.AsNoTracking().SingleAsync()).X);
    }

    [Fact]
    public async Task Review_changes_stale_apply_is_zero_write()
    {
        await using var fixture = await CreateFixtureAsync();
        var provider = new ReviewWorkspaceProvider(
            fixture.Execution.TenantId,
            fixture.Source.Sha256);
        var model = await fixture.Context.Models.SingleAsync();
        var floor = SpaceFloorRevision.Create(
            fixture.Execution.TenantId,
            fixture.Version.Id,
            provider.Request.FloorLogicalId,
            model.SiteId,
            1,
            "F1",
            "Floor 1",
            0,
            6000);
        fixture.Context.FloorRevisions.Add(floor);
        await fixture.Context.SaveChangesAsync();
        var started = await fixture.Service.StartAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            await ConfirmPreparationAsync(
                fixture,
                provider.Request,
                provider.SemanticPreviewSha256),
            "cad-review-stale-apply");
        await CompleteParseAsync(fixture, provider, started.JobId);
        fixture.Context.ChangeTracker.Clear();
        var workspace = await fixture.Service.GetReviewWorkspaceAsync(
            fixture.Version.Id,
            fixture.Source.Id,
            started.JobId);
        var version = await fixture.Context.Versions.SingleAsync(
            item => item.Id == fixture.Version.Id);
        version.TouchContent();
        var clientId = Guid.NewGuid();
        var lease = SpaceEditLease.Create(
            fixture.Execution.TenantId,
            fixture.Version.Id,
            floor.LogicalId,
            fixture.Execution.ActorId,
            "CAD reviewer",
            clientId,
            Now,
            TimeSpan.FromSeconds(90));
        fixture.Context.EditLeases.Add(lease);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();

        var problem = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.ApplyReviewChangesAsync(
                fixture.Version.Id,
                fixture.Source.Id,
                started.JobId,
                new ApplySpaceCadChangesetRequest(
                    Guid.NewGuid(),
                    clientId,
                    lease.LeaseId,
                    0,
                    workspace.EditorContentRevision,
                    workspace.EditorContentHash,
                    workspace.WorkspaceSha256,
                    [Assert.Single(workspace.Changes!).ChangeId])));

        Assert.Equal(SpaceErrorCodes.ParseChangesetStale, problem.Code);
        Assert.Empty(await fixture.Context.ElementRevisions.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Context.ElementCommandBatches.AsNoTracking().ToListAsync());
    }

    private static async Task CompleteParseAsync(
        Fixture fixture,
        ISpaceCadParseProvider provider,
        Guid jobId)
    {
        var executor = new SpaceCadParseJobStepExecutor(
            fixture.Context,
            new FileServiceProvider(fixture.Files),
            provider);
        var lease = await ClaimAsync(fixture, jobId);
        var job = fixture.Context.Jobs.Local.Single(item => item.Id == jobId);
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
        var generated = await executor.ExecuteAsync(new(
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
        var finalized = await executor.ExecuteAsync(new(
            lease,
            2,
            SpaceCadParseJobProcessor.FinalizePreview));
        finalizeStep.Complete(finalized.CheckpointJson, finalized.OutputHash, Now);
        attempt.Succeed(Now);
        job.Complete(attempt.Id, attempt.WorkerId, Now, finalized.CheckpointJson);
        await fixture.Context.SaveChangesAsync();
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
        var request = Request(sourceHash);
        var preparation = Preparation(
            tenantId,
            version,
            source,
            request,
            new string('9', 64));
        context.CadParsePreparations.Add(preparation);
        await context.SaveChangesAsync();
        request = request with { PreparationId = preparation.Id };
        var files = new MemoryFileStore();
        files.Seed(tenantId, fileId, storageKey, sourceBytes);
        var access = new AllowAccess();
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
        var service = new SpaceCadParseService(
            context,
            execution,
            access,
            null!,
            null!,
            clock,
            files,
            design);
        return new Fixture(
            context,
            execution,
            clock,
            version,
            source,
            request,
            files,
            service,
            design);
    }

    private static async Task<StartSpaceCadParseRequest> ConfirmPreparationAsync(
        Fixture fixture,
        StartSpaceCadParseRequest request,
        string semanticPreviewSha256)
    {
        var version = await fixture.Context.Versions.SingleAsync(item =>
            item.Id == fixture.Version.Id);
        var preparation = Preparation(
            fixture.Execution.TenantId,
            version,
            fixture.Source,
            request,
            semanticPreviewSha256);
        fixture.Context.CadParsePreparations.Add(preparation);
        await fixture.Context.SaveChangesAsync();
        return request with { PreparationId = preparation.Id };
    }

    private static SpaceCadParsePreparation Preparation(
        Guid tenantId,
        SpaceModelVersion version,
        SpaceModelSource source,
        StartSpaceCadParseRequest request,
        string semanticPreviewSha256) =>
        SpaceCadParsePreparation.Create(
            tenantId,
            version.Id,
            source.Id,
            source.Sha256,
            request.FloorLogicalId,
            request.ConfirmedUnit.ToString(),
            request.ConfirmedScaleToMillimeters,
            request.CoordinateMetadataJson,
            request.CoordinateTransformSha256,
            request.MappingProfileId,
            request.MappingProfileVersion,
            request.MappingDefinitionSha256,
            request.MappingPreviewSha256,
            MappingReplaySnapshot(tenantId, source.Sha256, request),
            semanticPreviewSha256,
            "review-test",
            "1.0",
            true,
            version.ContentRevision,
            version.ContentHash,
            Now.AddHours(2));

    private static string MappingReplaySnapshot(
        Guid tenantId,
        string sourceSha256,
        StartSpaceCadParseRequest request) =>
        SpaceCadMappingReplaySnapshot.Serialize(
            SpaceCadMappingReplaySnapshot.Create(
                tenantId,
                request.MappingProfileId,
                request.MappingProfileVersion,
                request.MappingDefinitionSha256,
                sourceSha256,
                new string('7', 64),
                new string('8', 64),
                request.MappingPreviewSha256,
                []));

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
            Guid.Empty,
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

    private sealed class ReviewWorkspaceProvider : ISpaceCadParseProvider
    {
        private readonly Guid _tenantId;
        private readonly SpaceCadIrPackageV1 _package;
        private readonly SpaceCadInventoryV1 _inventory;
        private readonly SpaceCadSemanticPreviewV1 _semantic;
        private readonly SpaceCadSemanticDiagnosticIndexV1 _diagnostics;

        public ReviewWorkspaceProvider(
            Guid tenantId,
            string sourceSha256,
            int wallCount = 1)
        {
            if (wallCount < 1)
                throw new ArgumentOutOfRangeException(nameof(wallCount));
            _tenantId = tenantId;
            var fileId = Guid.NewGuid();
            var sourceId = Guid.NewGuid();
            var conversion = new SpaceCadConversionRequest(
                tenantId,
                fileId,
                sourceId,
                sourceSha256,
                SpaceCadSourceFormat.Dxf,
                "review-test",
                "1.0");
            var entities = Enumerable.Range(0, wallCount)
                .Select(index =>
                {
                    var y = checked(index * 1_000m);
                    return new SpaceCadIrEntityV1(
                        $"H:WALL-{index + 1}",
                        SpaceCadIrEntityType.Line,
                        "LINE",
                        "WALL",
                        null,
                        [new(0, y), new(10_000, y)],
                        null,
                        null,
                        null,
                        SpaceCadAffineTransformV1.Identity,
                        new SpaceCadBoundsV1(0, y, 10_000, y),
                        false,
                        true,
                        new Dictionary<string, string>());
                })
                .ToArray();
            var packageBounds = new SpaceCadBoundsV1(
                0,
                0,
                10_000,
                checked((wallCount - 1) * 1_000m));
            _package = new SpaceCadIrPackageV1(
                new SpaceCadIrDocumentV1(
                    SpaceCadIrVersions.SchemaVersion,
                    sourceSha256,
                    SpaceCadSourceFormat.Dxf,
                    "AC1032",
                    SpaceCadUnit.Millimeter,
                    1,
                    SpaceCadIrVersions.CoordinateSystem,
                    packageBounds,
                    "review-test",
                    "1.0"),
                [new SpaceCadIrLayerV1(
                    "WALL",
                    "WALL",
                    wallCount,
                    "ACI:7",
                    "CONTINUOUS")],
                [],
                entities,
                [],
                new SpaceCadIrSummaryV1(
                    1,
                    0,
                    wallCount,
                    wallCount,
                    0,
                    0,
                    packageBounds));
            var floor = new SpaceCadFloorAssignmentV1(
                Guid.NewGuid(),
                "F1",
                1,
                0,
                SpaceCadCoordinateVersions.TargetCoordinateSystem,
                new SpaceCadBoundsV1(0, 0, 100_000, 100_000));
            var preparation = SpaceCadCoordinatePreparation.Prepare(
                conversion,
                _package,
                new SpaceCadCoordinateConfirmationV1(
                    sourceSha256,
                    true,
                    SpaceCadUnit.Millimeter,
                    new SpaceCadPointV1(0, 0),
                    new SpaceCadMillimeterPointV1(0, 0),
                    0,
                    floor));
            _inventory = SpaceCadInventory.Build(conversion, preparation);
            var profile = SpaceCadMapping.Seal(new SpaceCadMappingProfileDraftV1(
                SpaceCadMappingVersions.SchemaVersion,
                Guid.NewGuid(),
                1,
                "Review test mapping",
                SpaceCadMappingScope.System,
                null,
                true,
                null,
                null,
                [new SpaceCadMappingRuleV1(
                    "L-WALL",
                    100,
                    SpaceCadMappingSourceKind.Layer,
                    SpaceCadMappingMatchKind.Exact,
                    "WALL",
                    null,
                    null,
                    null,
                    SpaceCadSemanticTarget.Wall,
                    null,
                    SpaceCadGeometryRule.Centerline,
                    3000,
                    200,
                    0.95m,
                    true)]));
            var mapping = SpaceCadMapping.Preview(tenantId, _inventory, profile);
            _semantic = SpaceCadSemanticParser.Parse(
                conversion,
                preparation,
                _inventory,
                profile,
                mapping);
            _diagnostics = SpaceCadSemanticDiagnostics.Build(
                conversion,
                preparation,
                _inventory,
                profile,
                mapping,
                _semantic);
            Request = new StartSpaceCadParseRequest(
                Guid.Empty,
                floor.FloorLogicalId,
                SpaceCadUnit.Millimeter,
                1,
                SpaceCadCoordinatePreparation.SerializeMetadata(preparation.Metadata),
                preparation.Metadata.TransformSha256,
                profile.ProfileId,
                profile.Version,
                profile.DefinitionSha256,
                mapping.PreviewSha256);
        }

        public StartSpaceCadParseRequest Request { get; }
        public string SemanticPreviewSha256 => _semantic.SemanticPreviewSha256;

        public Task<IReadOnlyList<SpaceCadGeneratedArtifact>> GenerateAsync(
            SpaceCadParseProviderRequest providerRequest,
            Stream source,
            CancellationToken cancellationToken = default)
        {
            var payload = providerRequest.Payload;
            var previewSet = SpaceCadPreviewSet.Create(
                _tenantId,
                payload.ModelVersionId,
                payload.SourceId,
                providerRequest.JobId,
                _semantic,
                _diagnostics,
                payload.BaseContentRevision,
                payload.BaseContentHash);
            IReadOnlyList<SpaceCadGeneratedArtifact> artifacts =
            [
                Artifact(
                    SpaceArtifactType.CadIr,
                    "cad-ir.json",
                    JsonSerializer.Serialize(_package, JsonOptions)),
                Artifact(
                    SpaceArtifactType.LayerInventory,
                    "layers.json",
                    SpaceCadInventory.Serialize(_inventory)),
                Artifact(
                    SpaceArtifactType.PreviewSet,
                    "preview.json",
                    SpaceCadPreviewSet.Serialize(previewSet)),
            ];
            return Task.FromResult(artifacts);
        }

        private static SpaceCadGeneratedArtifact Artifact(
            SpaceArtifactType type,
            string fileName,
            string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
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
        TestExecutionContext Execution,
        FixedClock Clock,
        SpaceModelVersion Version,
        SpaceModelSource Source,
        StartSpaceCadParseRequest Request,
        MemoryFileStore Files,
        SpaceCadParseService Service,
        SpaceDesignV1Service Design) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
}
