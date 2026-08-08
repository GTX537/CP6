using System.Data;
using System.Globalization;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceGenerationApplyStepExecutor(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceClock clock,
    ISpaceAiApplyFaultInjector faultInjector) :
    ISpaceGenerationApplyStepExecutor
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution executionStep,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionStep);
        EnsureExecution(executionStep);
        try
        {
            return executionStep.StepCode switch
            {
                SpaceGenerationApplyJobSteps.PrepareStaging =>
                    await PrepareStagingAsync(
                        executionStep.Lease,
                        cancellationToken),
                SpaceGenerationApplyJobSteps.ValidateStaging =>
                    await ValidateStagingAsync(
                        executionStep.Lease,
                        cancellationToken),
                SpaceGenerationApplyJobSteps.CommitDraft =>
                    await CommitDraftAsync(
                        executionStep.Lease,
                        cancellationToken),
                _ => throw new SpaceJobProcessingException(
                    SpaceJobFailureKind.Bug,
                    SpaceErrorCodes.JobProcessorFailed,
                    "The AI Apply Job step is unsupported."),
            };
        }
        catch (SpaceAiApplyStaleException)
        {
            await MarkStaleAsync(
                executionStep.Lease.SubjectId,
                CancellationToken.None);
            throw new SpaceJobProcessingException(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.AiRunStale,
                "The Draft changed before AI Apply committed.");
        }
        catch (SpaceAiApplyValidationException exception)
        {
            await MarkFailedAsync(
                executionStep.Lease.SubjectId,
                SpaceErrorCodes.AiApplyInvalid,
                exception.Message,
                CancellationToken.None);
            throw new SpaceJobProcessingException(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.AiApplyInvalid,
                exception.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SpaceJobProcessingException)
        {
            throw;
        }
        catch (Exception)
        {
            await MarkFailedIfAttemptsExhaustedAsync(
                executionStep.Lease.SubjectId,
                executionStep.Lease.JobId,
                SpaceErrorCodes.AiApplyFailed,
                "The atomic AI Apply transaction failed and was rolled back.",
                CancellationToken.None);
            throw new SpaceJobProcessingException(
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.AiApplyFailed,
                "The atomic AI Apply transaction failed and was rolled back.");
        }
    }

    private async Task MarkFailedIfAttemptsExhaustedAsync(
        Guid runId,
        Guid jobId,
        string code,
        string summary,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var job = await context.Jobs.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == jobId,
            cancellationToken);
        if (job is null || job.AttemptCount < job.MaxAttempts)
            return;
        await MarkFailedAsync(runId, code, summary, cancellationToken);
    }

    private async Task<SpaceJobStepOutput> PrepareStagingAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var run = await RequireRunAsync(lease, cancellationToken);
        if (run.Status == SpaceGenerationRunStatus.Succeeded)
            return CompletedOutput(run);
        RequireApplying(run);
        await EnsureDraftIsFreshAsync(run, cancellationToken);

        var existing = await context.GenerationStagingElements
            .AsNoTracking()
            .Where(item => item.RunId == run.Id)
            .OrderBy(item => item.SequenceNo)
            .ToArrayAsync(cancellationToken);
        if (existing.Length > 0)
        {
            return Output(
                new
                {
                    runId = run.Id,
                    preparedCount = existing.Length,
                    reused = true,
                },
                HashStages(existing));
        }

        var proposals = await context.GenerationProposals
            .Where(item =>
                item.RunId == run.Id &&
                (item.Status == SpaceGenerationProposalStatus.Accepted ||
                 item.Status == SpaceGenerationProposalStatus.Modified))
            .OrderBy(item => item.ProposalType)
            .ThenBy(item => item.SourceKey)
            .ThenBy(item => item.Id)
            .ToArrayAsync(cancellationToken);
        if (proposals.Length is 0 or >
            SpaceAiAtomicApplyContract.MaximumProposalCount)
        {
            throw Invalid(
                "AI Apply requires 1 to 100,000 accepted or modified proposals.");
        }

        var decisions = await context.ProposalDecisions.AsNoTracking()
            .Where(item => item.RunId == run.Id)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .ToArrayAsync(cancellationToken);
        var finalByProposal = decisions
            .Where(item => item.AfterJson is not null)
            .GroupBy(item => item.ProposalId)
            .ToDictionary(
                group => group.Key,
                group => group.First().AfterJson!,
                EqualityComparer<Guid>.Default);
        if (proposals.Any(item => !finalByProposal.ContainsKey(item.Id)))
        {
            throw Invalid(
                "Every accepted or modified proposal requires an authoritative final Decision snapshot.");
        }

        var logicalBySource = proposals.ToDictionary(
            item => item.SourceKey,
            item => WarehouseDeterministicIdentity.CreateObjectLogicalId(
                run.ModelVersionId,
                run.SourceHash,
                item.SourceKey),
            StringComparer.Ordinal);
        var typeByLogical = proposals.ToDictionary(
            item => logicalBySource[item.SourceKey],
            item => item.ProposalType);
        long locationCount = 0;
        var staged = new List<SpaceGenerationStagingElement>(proposals.Length);
        for (var index = 0; index < proposals.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var proposal = proposals[index];
            var payload = BuildPayload(
                run,
                proposal,
                finalByProposal[proposal.Id],
                logicalBySource,
                typeByLogical);
            locationCount = checked(
                locationCount + payload.Locations.Count);
            if (locationCount >
                SpaceAiAtomicApplyContract.MaximumDerivedLocationCount)
            {
                throw Invalid(
                    "AI Apply exceeds the one-million derived Location limit.");
            }
            staged.Add(SpaceGenerationStagingElement.Create(
                execution.TenantId,
                run.Id,
                proposal.Id,
                run.ModelVersionId,
                index,
                payload.LogicalId,
                payload.FloorLogicalId,
                payload.ProposalType,
                Serialize(payload)));
        }

        faultInjector.ThrowIfRequested("prepare-before-save");
        context.GenerationStagingElements.AddRange(staged);
        await context.SaveChangesAsync(cancellationToken);
        return Output(
            new
            {
                runId = run.Id,
                preparedCount = staged.Count,
                derivedLocationCount = locationCount,
                reused = false,
            },
            HashStages(staged));
    }

    private async Task<SpaceJobStepOutput> ValidateStagingAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var run = await RequireRunAsync(lease, cancellationToken);
        if (run.Status == SpaceGenerationRunStatus.Succeeded)
            return CompletedOutput(run);
        RequireApplying(run);
        await EnsureDraftIsFreshAsync(run, cancellationToken);
        await EnsureReviewUnchangedAsync(run, cancellationToken);

        var stages = await context.GenerationStagingElements
            .Where(item => item.RunId == run.Id)
            .OrderBy(item => item.SequenceNo)
            .ToArrayAsync(cancellationToken);
        if (stages.Length == 0)
            throw Invalid("AI Apply staging is empty.");
        if (stages.All(item =>
                item.ValidationStatus ==
                SpaceGenerationStagingValidationStatus.Validated) &&
            run.ApplyPlanHash is not null)
        {
            return Output(
                new
                {
                    runId = run.Id,
                    validatedCount = stages.Length,
                    applyPlanHash = run.ApplyPlanHash,
                    reused = true,
                },
                run.ApplyPlanHash);
        }
        if (stages.Any(item =>
                item.ValidationStatus !=
                SpaceGenerationStagingValidationStatus.Prepared))
        {
            throw Invalid("AI Apply staging has a partial validation state.");
        }

        var payloads = stages
            .Select(item => Deserialize(item.NormalizedPayloadJson))
            .ToArray();
        await ValidatePayloadsAsync(run, payloads, cancellationToken);
        foreach (var stage in stages)
        {
            stage.MarkValidated(SpaceAiAtomicApplyService.Hash(
                stage.NormalizedPayloadJson));
        }
        var planHash = BuildPlanHash(run, stages);
        run.RecordApplyPlan(planHash, UtcNow());
        faultInjector.ThrowIfRequested("validate-before-save");
        await context.SaveChangesAsync(cancellationToken);
        return Output(
            new
            {
                runId = run.Id,
                validatedCount = stages.Length,
                applyPlanHash = planHash,
                reused = false,
            },
            planHash);
    }

    private async Task<SpaceJobStepOutput> CommitDraftAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.ChangeTracker.Clear();
        var preflight = await RequireRunAsync(lease, cancellationToken);
        if (preflight.Status == SpaceGenerationRunStatus.Succeeded)
            return CompletedOutput(preflight);
        RequireApplying(preflight);

        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                CancellationToken.None)
            : null;
        try
        {
            context.ChangeTracker.Clear();
            var run = await LockRunAsync(lease.SubjectId);
            if (run.Status == SpaceGenerationRunStatus.Succeeded)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(CancellationToken.None);
                return CompletedOutput(run);
            }
            RequireApplying(run);
            var version = await LockVersionAsync(run.ModelVersionId);
            _ = await LockModelAsync(version.ModelId);
            if (version.Status != SpaceVersionStatus.Draft ||
                version.ContentRevision != run.BaseContentRevision)
            {
                throw new SpaceAiApplyStaleException();
            }
            await EnsureReviewUnchangedAsync(run, CancellationToken.None);

            var floor = await context.FloorRevisions.SingleOrDefaultAsync(
                item =>
                    item.ModelVersionId == run.ModelVersionId &&
                    item.LogicalId == run.TargetFloorLogicalId,
                CancellationToken.None)
                ?? throw Invalid("The pinned target Floor no longer exists.");
            var stages = await context.GenerationStagingElements
                .Where(item => item.RunId == run.Id)
                .OrderBy(item => item.SequenceNo)
                .ToArrayAsync(CancellationToken.None);
            if (stages.Length == 0 ||
                stages.Any(item =>
                    item.ValidationStatus !=
                    SpaceGenerationStagingValidationStatus.Validated ||
                    item.ValidationHash == null) ||
                run.ApplyPlanHash is null ||
                !SpaceAiAtomicApplyService.FixedEquals(
                    run.ApplyPlanHash,
                    BuildPlanHash(run, stages)))
            {
                throw Invalid("The AI Apply plan is incomplete or changed.");
            }
            var payloads = stages
                .Select(item => Deserialize(item.NormalizedPayloadJson))
                .ToArray();
            await ValidatePayloadsAsync(
                run,
                payloads,
                CancellationToken.None);
            faultInjector.ThrowIfRequested("commit-after-revalidate");

            var source = await context.Sources.SingleAsync(
                item =>
                    item.Id == run.SourceId &&
                    item.ModelVersionId == run.ModelVersionId,
                CancellationToken.None);
            var proposalById = await context.GenerationProposals
                .Where(item =>
                    item.RunId == run.Id &&
                    stages.Select(stage => stage.ProposalId)
                        .Contains(item.Id))
                .ToDictionaryAsync(item => item.Id, CancellationToken.None);
            var commandBatch = SpaceElementCommandBatch.Create(
                execution.TenantId,
                run.ApplyCommandBatchId!.Value,
                run.ModelVersionId,
                floor.LogicalId,
                run.Id,
                floor.Revision,
                run.ApplyPlanHash,
                (await context.Jobs.SingleAsync(
                    item => item.Id == run.ApplyJobId,
                    CancellationToken.None)).RequestedBy,
                UtcNow());
            context.ElementCommandBatches.Add(commandBatch);

            for (var index = 0; index < stages.Length; index++)
            {
                var stage = stages[index];
                var payload = payloads[index];
                var beforeJson = await ReadBeforeJsonAsync(
                    source.ModelVersionId,
                    payload,
                    CancellationToken.None);
                await ApplyPayloadAsync(
                    payload,
                    source,
                    CancellationToken.None);
                proposalById[stage.ProposalId].MarkApplied(payload.LogicalId);
                context.ElementCommandRecords.Add(
                    SpaceElementCommandRecord.Create(
                        execution.TenantId,
                        Guid.NewGuid(),
                        commandBatch,
                        index,
                        "ApplyGenerationProposal",
                        payload.LogicalId,
                        stage.NormalizedPayloadJson,
                        beforeJson,
                        stage.NormalizedPayloadJson));
                faultInjector.ThrowIfRequested(
                    $"commit-after-proposal-{index + 1}");
            }

            faultInjector.ThrowIfRequested("commit-before-revision");
            floor.AdvanceRevision(floor.Revision);
            version.TouchContent();
            var counts = Counts(payloads);
            var countsJson = Serialize(counts);
            commandBatch.Complete(
                floor.Revision,
                version.ContentRevision,
                Serialize(new
                {
                    runId = run.Id,
                    applyPlanHash = run.ApplyPlanHash,
                    appliedContentRevision = version.ContentRevision,
                    appliedCounts = counts,
                }));
            run.MarkSucceeded(version.ContentRevision, countsJson);
            faultInjector.ThrowIfRequested("commit-before-save");
            await context.SaveChangesAsync(CancellationToken.None);
            faultInjector.ThrowIfRequested("commit-before-commit");
            if (transaction is not null)
                await transaction.CommitAsync(CancellationToken.None);
            return Output(
                new
                {
                    runId = run.Id,
                    status = run.Status.ToString(),
                    appliedContentRevision = version.ContentRevision,
                    appliedCounts = counts,
                    applyPlanHash = run.ApplyPlanHash,
                },
                run.ApplyPlanHash);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private SpaceAiStagedElementPayloadV1 BuildPayload(
        SpaceGenerationRun run,
        SpaceGenerationProposal proposal,
        string finalSnapshotJson,
        IReadOnlyDictionary<string, Guid> logicalBySource,
        IReadOnlyDictionary<Guid, string> typeByLogical)
    {
        using var snapshot = JsonDocument.Parse(finalSnapshotJson);
        var root = RequireObject(snapshot.RootElement, "Decision final snapshot");
        var proposalType = String(root, "proposalType", required: true)!;
        if (!string.Equals(
                proposalType,
                proposal.ProposalType,
                StringComparison.Ordinal))
        {
            throw Invalid("The Decision final proposal type does not match its Proposal.");
        }
        if (!TryProperty(root, "geometry", out var geometry) ||
            !TryProperty(root, "attributes", out var attributes) ||
            !TryProperty(root, "relations", out var relations) ||
            attributes.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("The Decision final snapshot is incomplete.");
        }

        var logicalId = logicalBySource[proposal.SourceKey];
        var floorLogicalId = run.TargetFloorLogicalId!.Value;
        var name = String(attributes, "name")
            ?? String(attributes, "semanticLabel")
            ?? proposal.SourceKey;
        RequireText(name, 200, "Proposal display name");
        var bounds = Bounds(geometry);
        var z = FirstPointZ(geometry);
        var rotation = Decimal(attributes, "rotationZDegrees") ?? 0m;
        var semantic = SemanticAttributes(attributes);

        string? code = null;
        short? kindCode = null;
        Guid? zoneLogicalId = null;
        Guid? aisleLogicalId = null;
        Guid? parentLogicalId = null;
        string primaryGeometry = "{}";
        string? secondaryGeometry = null;
        string? rackType = null;
        Guid? rackProfileVersionId = null;
        var levels = new List<SpaceAiStagedRackLevelV1>();
        var locations = new List<SpaceAiStagedLocationV1>();
        var width = Positive(attributes, "widthMillimeters") ?? bounds.Width;
        var depth = Positive(attributes, "depthMillimeters") ?? bounds.Depth;
        var height = Positive(attributes, "heightMillimeters") ?? 0;

        switch (proposalType)
        {
            case "Zone":
                code = Code(attributes, "zoneCode", name);
                kindCode = ParseZoneType(attributes);
                primaryGeometry = Serialize(Points(geometry, minimum: 3));
                break;
            case "Aisle":
                code = Code(attributes, "aisleCode", name);
                zoneLogicalId = ResolveRelation(
                    relations,
                    "zoneLogicalId",
                    "zoneSourceKey",
                    "Zone",
                    logicalBySource,
                    typeByLogical,
                    required: true);
                kindCode = ParseDirection(attributes);
                var aislePoints = Points(geometry, minimum: 2);
                primaryGeometry = Serialize(Rectangle(bounds));
                secondaryGeometry = Serialize(aislePoints);
                break;
            case "Rack":
                code = Code(attributes, "rackCode", name);
                zoneLogicalId = ResolveRelation(
                    relations,
                    "zoneLogicalId",
                    "zoneSourceKey",
                    "Zone",
                    logicalBySource,
                    typeByLogical,
                    required: true);
                aisleLogicalId = ResolveRelation(
                    relations,
                    "aisleLogicalId",
                    "aisleSourceKey",
                    "Aisle",
                    logicalBySource,
                    typeByLogical,
                    required: false);
                rackType = String(attributes, "rackType");
                if (!TryProperty(attributes, "rackDerivation", out var derivation) ||
                    derivation.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid("Rack Apply requires a frozen rackDerivation.");
                }
                rackProfileVersionId = GuidValue(
                    derivation,
                    "profileVersionId",
                    required: true);
                width = RequiredPositive(derivation, "rackWidthMillimeters");
                depth = RequiredPositive(derivation, "rackDepthMillimeters");
                height = RequiredPositive(derivation, "rackHeightMillimeters");
                BuildRackDerivation(
                    logicalId,
                    floorLogicalId,
                    derivation,
                    levels,
                    locations);
                break;
            case "Wall":
            case "Column":
            case "Door":
            case "Dock":
            case "StaticEquipment":
                parentLogicalId = ResolveElementParent(
                    relations,
                    logicalBySource,
                    typeByLogical);
                primaryGeometry = ElementGeometry(
                    geometry,
                    attributes,
                    bounds);
                width = RequiredDimension(width, "element width");
                depth = RequiredDimension(depth, "element depth");
                height = RequiredDimension(height, "element height");
                break;
            default:
                throw Invalid(
                    $"Proposal type {proposalType} is not supported by atomic Apply.");
        }

        return new SpaceAiStagedElementPayloadV1(
            SpaceAiAtomicApplyContract.SchemaVersion,
            proposal.Id,
            logicalId,
            floorLogicalId,
            proposalType,
            proposal.SourceKey,
            name,
            code,
            kindCode,
            zoneLogicalId,
            aisleLogicalId,
            parentLogicalId,
            bounds.MinX,
            bounds.MinY,
            z,
            rotation,
            width,
            height,
            depth,
            primaryGeometry,
            secondaryGeometry,
            rackType,
            rackProfileVersionId,
            levels,
            locations,
            semantic);
    }

    private async Task ValidatePayloadsAsync(
        SpaceGenerationRun run,
        IReadOnlyList<SpaceAiStagedElementPayloadV1> payloads,
        CancellationToken cancellationToken)
    {
        if (payloads.Count == 0 ||
            payloads.Select(item => item.ProposalId).Distinct().Count() !=
                payloads.Count ||
            payloads.Select(item => item.LogicalId).Distinct().Count() !=
                payloads.Count ||
            payloads.Any(item =>
                item.SchemaVersion !=
                    SpaceAiAtomicApplyContract.SchemaVersion ||
                item.FloorLogicalId != run.TargetFloorLogicalId))
        {
            throw Invalid("AI Apply staging identity is invalid.");
        }

        var ids = payloads.Select(item => item.LogicalId).ToArray();
        var stagedZoneIds = payloads
            .Where(item => item.ProposalType == "Zone")
            .Select(item => item.LogicalId)
            .ToHashSet();
        var stagedAisleIds = payloads
            .Where(item => item.ProposalType == "Aisle")
            .Select(item => item.LogicalId)
            .ToHashSet();
        var stagedRackIds = payloads
            .Where(item => item.ProposalType == "Rack")
            .Select(item => item.LogicalId)
            .ToHashSet();
        var stagedElementIds = payloads
            .Where(item => item.ProposalType is not ("Zone" or "Aisle" or "Rack"))
            .Select(item => item.LogicalId)
            .ToHashSet();
        if (await context.FloorRevisions.AnyAsync(item =>
                item.ModelVersionId == run.ModelVersionId &&
                ids.Contains(item.LogicalId), cancellationToken) ||
            (await context.ZoneRevisions.AsNoTracking()
                .Where(item =>
                    item.ModelVersionId == run.ModelVersionId &&
                    ids.Contains(item.LogicalId))
                .Select(item => new { item.LogicalId, item.FloorLogicalId })
                .ToArrayAsync(cancellationToken))
                .Any(item =>
                    !stagedZoneIds.Contains(item.LogicalId) ||
                    item.FloorLogicalId != run.TargetFloorLogicalId) ||
            (await context.AisleRevisions.AsNoTracking()
                .Where(item =>
                    item.ModelVersionId == run.ModelVersionId &&
                    ids.Contains(item.LogicalId))
                .Select(item => item.LogicalId)
                .ToArrayAsync(cancellationToken))
                .Any(item => !stagedAisleIds.Contains(item)) ||
            (await context.RackRevisions.AsNoTracking()
                .Where(item =>
                    item.ModelVersionId == run.ModelVersionId &&
                    ids.Contains(item.LogicalId))
                .Select(item => new { item.LogicalId, item.FloorLogicalId })
                .ToArrayAsync(cancellationToken))
                .Any(item =>
                    !stagedRackIds.Contains(item.LogicalId) ||
                    item.FloorLogicalId != run.TargetFloorLogicalId) ||
            (await context.ElementRevisions.AsNoTracking()
                .Where(item =>
                    item.ModelVersionId == run.ModelVersionId &&
                    ids.Contains(item.LogicalId))
                .Select(item => new
                {
                    item.LogicalId,
                    item.FloorLogicalId,
                    item.ModelAssetId,
                })
                .ToArrayAsync(cancellationToken))
                .Any(item =>
                    !stagedElementIds.Contains(item.LogicalId) ||
                    item.FloorLogicalId != run.TargetFloorLogicalId ||
                    item.ModelAssetId.HasValue) ||
            await context.RackLevelRevisions.AnyAsync(item =>
                item.ModelVersionId == run.ModelVersionId &&
                ids.Contains(item.LogicalId), cancellationToken) ||
            await context.LocationRevisions.AnyAsync(item =>
                item.ModelVersionId == run.ModelVersionId &&
                ids.Contains(item.LogicalId), cancellationToken))
        {
            throw Invalid(
                "AI Apply logical identity collides with another Draft object type or an asset-backed element.");
        }

        var derivedLevels = payloads
            .SelectMany(payload => payload.RackLevels.Select(item =>
                new { item.LogicalId, RackLogicalId = payload.LogicalId }))
            .ToArray();
        var derivedLocations = payloads
            .SelectMany(payload => payload.Locations.Select(item =>
                new { item.LogicalId, RackLogicalId = payload.LogicalId }))
            .ToArray();
        if (derivedLevels.Select(item => item.LogicalId).Distinct().Count() !=
                derivedLevels.Length ||
            derivedLocations.Select(item => item.LogicalId).Distinct().Count() !=
                derivedLocations.Length)
        {
            throw Invalid("AI Apply derived logical identities are not unique.");
        }
        var derivedLevelOwners = derivedLevels.ToDictionary(
            item => item.LogicalId,
            item => item.RackLogicalId);
        var derivedLocationOwners = derivedLocations.ToDictionary(
            item => item.LogicalId,
            item => item.RackLogicalId);
        var derivedIds = derivedLevels.Select(item => item.LogicalId)
            .Concat(derivedLocations.Select(item => item.LogicalId))
            .ToArray();
        if (derivedIds.Distinct().Count() != derivedIds.Length ||
            derivedIds.Intersect(ids).Any() ||
            await context.FloorRevisions.AnyAsync(item =>
                item.ModelVersionId == run.ModelVersionId &&
                derivedIds.Contains(item.LogicalId), cancellationToken) ||
            await context.ZoneRevisions.AnyAsync(item =>
                item.ModelVersionId == run.ModelVersionId &&
                derivedIds.Contains(item.LogicalId), cancellationToken) ||
            await context.AisleRevisions.AnyAsync(item =>
                item.ModelVersionId == run.ModelVersionId &&
                derivedIds.Contains(item.LogicalId), cancellationToken) ||
            await context.RackRevisions.AnyAsync(item =>
                item.ModelVersionId == run.ModelVersionId &&
                derivedIds.Contains(item.LogicalId), cancellationToken) ||
            await context.ElementRevisions.AnyAsync(item =>
                item.ModelVersionId == run.ModelVersionId &&
                derivedIds.Contains(item.LogicalId), cancellationToken) ||
            (await context.RackLevelRevisions.AsNoTracking()
                .Where(item =>
                    item.ModelVersionId == run.ModelVersionId &&
                    derivedIds.Contains(item.LogicalId))
                .Select(item => new { item.LogicalId, item.RackLogicalId })
                .ToArrayAsync(cancellationToken))
                .Any(item =>
                    !derivedLevelOwners.TryGetValue(item.LogicalId, out var owner) ||
                    owner != item.RackLogicalId) ||
            (await context.LocationRevisions.AsNoTracking()
                .Where(item =>
                    item.ModelVersionId == run.ModelVersionId &&
                    derivedIds.Contains(item.LogicalId))
                .Select(item => new
                {
                    item.LogicalId,
                    item.FloorLogicalId,
                    item.RackLogicalId,
                })
                .ToArrayAsync(cancellationToken))
                .Any(item =>
                    !derivedLocationOwners.TryGetValue(item.LogicalId, out var owner) ||
                    owner != item.RackLogicalId ||
                    item.FloorLogicalId != run.TargetFloorLogicalId))
        {
            throw Invalid(
                "AI Apply derived logical identity collides with another Draft object.");
        }
        var derivedLocationIds = derivedLocationOwners.Keys.ToArray();
        if (await context.LocationRevisions.AnyAsync(item =>
                item.ModelVersionId == run.ModelVersionId &&
                item.RackLogicalId.HasValue &&
                stagedRackIds.Contains(item.RackLogicalId.Value) &&
                !derivedLocationIds.Contains(item.LogicalId) &&
                item.ExternalBindingState != SpaceExternalBindingState.Unbound,
                cancellationToken))
        {
            throw Invalid(
                "AI Apply cannot remove a WMS-bound Location from a Rack derivation.");
        }

        var zones = payloads.Where(item => item.ProposalType == "Zone")
            .ToDictionary(item => item.LogicalId);
        var aisles = payloads.Where(item => item.ProposalType == "Aisle")
            .ToDictionary(item => item.LogicalId);
        var zoneIds = payloads.Where(item => item.ZoneLogicalId.HasValue)
            .Select(item => item.ZoneLogicalId!.Value)
            .Distinct()
            .ToArray();
        var aisleIds = payloads.Where(item => item.AisleLogicalId.HasValue)
            .Select(item => item.AisleLogicalId!.Value)
            .Distinct()
            .ToArray();
        var existingZones = await context.ZoneRevisions.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == run.ModelVersionId &&
                item.LifecycleState == SpaceLifecycleState.Active &&
                zoneIds.Contains(item.LogicalId))
            .Select(item => item.LogicalId)
            .ToArrayAsync(cancellationToken);
        var existingAisles = await context.AisleRevisions.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == run.ModelVersionId &&
                item.LifecycleState == SpaceLifecycleState.Active &&
                aisleIds.Contains(item.LogicalId))
            .Select(item => new { item.LogicalId, item.ZoneLogicalId })
            .ToArrayAsync(cancellationToken);
        foreach (var payload in payloads)
        {
            if (payload.ZoneLogicalId.HasValue &&
                !zones.ContainsKey(payload.ZoneLogicalId.Value) &&
                !existingZones.Contains(payload.ZoneLogicalId.Value))
            {
                throw Invalid("AI Apply references a missing Zone.");
            }
            if (payload.AisleLogicalId.HasValue)
            {
                var stagedAisle = aisles.GetValueOrDefault(
                    payload.AisleLogicalId.Value);
                var existingAisle = existingAisles.SingleOrDefault(item =>
                    item.LogicalId == payload.AisleLogicalId.Value);
                var aisleZone = stagedAisle?.ZoneLogicalId ??
                    existingAisle?.ZoneLogicalId;
                if (aisleZone is null || aisleZone != payload.ZoneLogicalId)
                {
                    throw Invalid(
                        "AI Apply Rack/Aisle relationships are inconsistent.");
                }
            }
        }

        await ValidateCodesAsync(run.ModelVersionId, payloads, cancellationToken);
        await ValidateBoundaryAsync(run, payloads, cancellationToken);
        await ValidateRackCollisionsAsync(
            run.ModelVersionId,
            payloads,
            cancellationToken);
    }

    private async Task ValidateCodesAsync(
        Guid versionId,
        IReadOnlyList<SpaceAiStagedElementPayloadV1> payloads,
        CancellationToken cancellationToken)
    {
        static void EnsureUnique(IEnumerable<string> values, string label)
        {
            var list = values.ToArray();
            if (list.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                list.Length)
            {
                throw Invalid($"AI Apply contains duplicate {label} codes.");
            }
        }
        var zones = payloads.Where(item => item.ProposalType == "Zone")
            .ToArray();
        var aisles = payloads.Where(item => item.ProposalType == "Aisle")
            .ToArray();
        var racks = payloads.Where(item => item.ProposalType == "Rack")
            .ToArray();
        var zoneCodes = zones.Select(item => item.Code!).ToArray();
        var aisleCodes = aisles.Select(item => item.Code!).ToArray();
        var rackCodes = racks.Select(item => item.Code!).ToArray();
        EnsureUnique(zoneCodes, "Zone");
        EnsureUnique(aisleCodes, "Aisle");
        EnsureUnique(rackCodes, "Rack");
        var zoneOwners = zones.ToDictionary(
            item => item.Code!,
            item => item.LogicalId,
            StringComparer.OrdinalIgnoreCase);
        var aisleOwners = aisles.ToDictionary(
            item => item.Code!,
            item => item.LogicalId,
            StringComparer.OrdinalIgnoreCase);
        var rackOwners = racks.ToDictionary(
            item => item.Code!,
            item => item.LogicalId,
            StringComparer.OrdinalIgnoreCase);
        var existingZones = await context.ZoneRevisions.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == versionId &&
                zoneCodes.Contains(item.ZoneCode))
            .Select(item => new { item.LogicalId, Code = item.ZoneCode })
            .ToArrayAsync(cancellationToken);
        var existingAisles = await context.AisleRevisions.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == versionId &&
                aisleCodes.Contains(item.AisleCode))
            .Select(item => new { item.LogicalId, Code = item.AisleCode })
            .ToArrayAsync(cancellationToken);
        var existingRacks = await context.RackRevisions.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == versionId &&
                rackCodes.Contains(item.RackCode))
            .Select(item => new { item.LogicalId, Code = item.RackCode })
            .ToArrayAsync(cancellationToken);
        if (existingZones.Any(item => zoneOwners[item.Code] != item.LogicalId) ||
            existingAisles.Any(item => aisleOwners[item.Code] != item.LogicalId) ||
            existingRacks.Any(item => rackOwners[item.Code] != item.LogicalId))
        {
            throw Invalid("AI Apply code preflight found an existing code.");
        }
    }

    private async Task ValidateBoundaryAsync(
        SpaceGenerationRun run,
        IReadOnlyList<SpaceAiStagedElementPayloadV1> payloads,
        CancellationToken cancellationToken)
    {
        var boundaryJson = await context.FloorRevisions.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == run.ModelVersionId &&
                item.LogicalId == run.TargetFloorLogicalId)
            .Select(item => item.BoundaryJson)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw Invalid("The pinned target Floor does not exist.");
        using var document = JsonDocument.Parse(boundaryJson);
        var root = document.RootElement;
        var boundaryPoints = root.ValueKind == JsonValueKind.Object &&
                             root.TryGetProperty("points", out var nestedPoints)
            ? nestedPoints
            : root;
        if (boundaryPoints.ValueKind != JsonValueKind.Array ||
            boundaryPoints.GetArrayLength() < 3)
        {
            return;
        }
        var points = boundaryPoints.EnumerateArray()
            .Select(Point)
            .ToArray();
        var floorBounds = Bounds(points);
        if (payloads.Any(item =>
                item.X < floorBounds.MinX ||
                item.Y < floorBounds.MinY ||
                checked(item.X + Math.Max(item.Width, 0)) > floorBounds.MaxX ||
                checked(item.Y + Math.Max(item.Depth, 0)) > floorBounds.MaxY))
        {
            throw Invalid("AI Apply geometry exceeds the pinned Floor boundary.");
        }
    }

    private async Task ValidateRackCollisionsAsync(
        Guid versionId,
        IReadOnlyList<SpaceAiStagedElementPayloadV1> payloads,
        CancellationToken cancellationToken)
    {
        var staged = payloads.Where(item => item.ProposalType == "Rack")
            .Select(item => new
            {
                item.LogicalId,
                Bounds = new Box(item.X, item.Y, item.Width, item.Depth),
            })
            .ToArray();
        var stagedIds = staged.Select(item => item.LogicalId).ToArray();
        for (var left = 0; left < staged.Length; left++)
        for (var right = left + 1; right < staged.Length; right++)
        {
            if (Overlaps(staged[left].Bounds, staged[right].Bounds))
                throw Invalid("AI Apply contains colliding staged Racks.");
        }
        if (staged.Length == 0)
            return;
        var existing = await context.RackRevisions.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == versionId &&
                item.LifecycleState == SpaceLifecycleState.Active &&
                !stagedIds.Contains(item.LogicalId))
            .Select(item => new Box(item.X, item.Y, item.Width, item.Depth))
            .ToArrayAsync(cancellationToken);
        if (staged.Any(candidate =>
                existing.Any(item => Overlaps(candidate.Bounds, item))))
            throw Invalid("AI Apply Rack geometry collides with the Draft.");
    }

    private async Task<string> ReadBeforeJsonAsync(
        Guid modelVersionId,
        SpaceAiStagedElementPayloadV1 payload,
        CancellationToken cancellationToken)
    {
        switch (payload.ProposalType)
        {
            case "Zone":
                var zone = await context.ZoneRevisions.AsNoTracking()
                    .SingleOrDefaultAsync(item =>
                        item.ModelVersionId == modelVersionId &&
                        item.LogicalId == payload.LogicalId,
                        cancellationToken);
                return zone is null ? "null" : Serialize(zone);
            case "Aisle":
                var aisle = await context.AisleRevisions.AsNoTracking()
                    .SingleOrDefaultAsync(item =>
                        item.ModelVersionId == modelVersionId &&
                        item.LogicalId == payload.LogicalId,
                        cancellationToken);
                return aisle is null ? "null" : Serialize(aisle);
            case "Rack":
                var rack = await context.RackRevisions.AsNoTracking()
                    .SingleOrDefaultAsync(item =>
                        item.ModelVersionId == modelVersionId &&
                        item.LogicalId == payload.LogicalId,
                        cancellationToken);
                if (rack is null)
                    return "null";
                var levels = await context.RackLevelRevisions.AsNoTracking()
                    .Where(item =>
                        item.ModelVersionId == modelVersionId &&
                        item.RackLogicalId == payload.LogicalId)
                    .OrderBy(item => item.LevelNo)
                    .ThenBy(item => item.LogicalId)
                    .ToArrayAsync(cancellationToken);
                var locations = await context.LocationRevisions.AsNoTracking()
                    .Where(item =>
                        item.ModelVersionId == modelVersionId &&
                        item.RackLogicalId == payload.LogicalId)
                    .OrderBy(item => item.LevelNo)
                    .ThenBy(item => item.ColumnNo)
                    .ThenBy(item => item.DepthNo)
                    .ThenBy(item => item.LogicalId)
                    .ToArrayAsync(cancellationToken);
                return Serialize(new { rack, levels, locations });
            default:
                var element = await context.ElementRevisions.AsNoTracking()
                    .SingleOrDefaultAsync(item =>
                        item.ModelVersionId == modelVersionId &&
                        item.LogicalId == payload.LogicalId,
                        cancellationToken);
                if (element is null)
                    return "null";
                var attributes = await context.ElementAttributes.AsNoTracking()
                    .Where(item =>
                        item.ModelVersionId == modelVersionId &&
                        item.ElementRevisionId == element.Id)
                    .OrderBy(item => item.Namespace)
                    .ThenBy(item => item.Key)
                    .ToArrayAsync(cancellationToken);
                return Serialize(new { element, attributes });
        }
    }

    private async Task ApplyPayloadAsync(
        SpaceAiStagedElementPayloadV1 payload,
        SpaceModelSource source,
        CancellationToken cancellationToken)
    {
        switch (payload.ProposalType)
        {
            case "Zone":
                var zone = await context.ZoneRevisions.SingleOrDefaultAsync(
                    item =>
                        item.ModelVersionId == source.ModelVersionId &&
                        item.LogicalId == payload.LogicalId,
                    cancellationToken);
                if (zone is null)
                {
                    zone = SpaceZoneRevision.Create(
                        execution.TenantId,
                        source.ModelVersionId,
                        payload.LogicalId,
                        payload.FloorLogicalId,
                        payload.Code!,
                        payload.KindCode!.Value,
                        payload.Name);
                    context.ZoneRevisions.Add(zone);
                }
                else
                {
                    zone.UpdateDefinition(
                        payload.FloorLogicalId,
                        payload.Code!,
                        payload.KindCode!.Value,
                        payload.Name);
                }
                zone.ConfigureShape(payload.PrimaryGeometryJson);
                zone.AttachSource(source, payload.SourceKey);
                break;
            case "Aisle":
                var aisle = await context.AisleRevisions.SingleOrDefaultAsync(
                    item =>
                        item.ModelVersionId == source.ModelVersionId &&
                        item.LogicalId == payload.LogicalId,
                    cancellationToken);
                if (aisle is null)
                {
                    aisle = SpaceAisleRevision.Create(
                        execution.TenantId,
                        source.ModelVersionId,
                        payload.LogicalId,
                        payload.ZoneLogicalId!.Value,
                        payload.Code!,
                        payload.KindCode!.Value,
                        payload.Name);
                    context.AisleRevisions.Add(aisle);
                }
                else
                {
                    aisle.UpdateDefinition(
                        payload.ZoneLogicalId!.Value,
                        payload.Code!,
                        payload.KindCode!.Value,
                        payload.Name);
                }
                aisle.ConfigureShape(
                    payload.PrimaryGeometryJson,
                    payload.SecondaryGeometryJson!);
                aisle.AttachSource(source, payload.SourceKey);
                break;
            case "Rack":
                var rack = await context.RackRevisions.SingleOrDefaultAsync(
                    item =>
                        item.ModelVersionId == source.ModelVersionId &&
                        item.LogicalId == payload.LogicalId,
                    cancellationToken);
                if (rack is null)
                {
                    rack = SpaceRackRevision.Create(
                        execution.TenantId,
                        source.ModelVersionId,
                        payload.LogicalId,
                        payload.FloorLogicalId,
                        payload.ZoneLogicalId!.Value,
                        payload.Code!,
                        payload.AisleLogicalId,
                        payload.Name,
                        payload.RackType);
                    context.RackRevisions.Add(rack);
                }
                else
                {
                    rack.UpdateDefinition(
                        payload.FloorLogicalId,
                        payload.ZoneLogicalId!.Value,
                        payload.Code!,
                        payload.AisleLogicalId,
                        payload.Name,
                        payload.RackType);
                }
                rack.ConfigureGeometry(
                    payload.X,
                    payload.Y,
                    payload.Z,
                    payload.RotationZ,
                    payload.Width,
                    payload.Depth,
                    payload.Height,
                    payload.RackProfileVersionId);
                rack.AttachSource(source, payload.SourceKey);
                var existingLevels = await context.RackLevelRevisions
                    .Where(item =>
                        item.ModelVersionId == source.ModelVersionId &&
                        item.RackLogicalId == payload.LogicalId)
                    .ToDictionaryAsync(item => item.LogicalId, cancellationToken);
                var expectedLevelIds = payload.RackLevels
                    .Select(item => item.LogicalId)
                    .ToHashSet();
                foreach (var item in payload.RackLevels)
                {
                    if (!existingLevels.TryGetValue(item.LogicalId, out var level))
                    {
                        level = SpaceRackLevelRevision.Create(
                            execution.TenantId,
                            source.ModelVersionId,
                            item.LogicalId,
                            payload.LogicalId,
                            item.LevelNo,
                            item.BottomZ,
                            item.ClearHeight,
                            item.BinCount,
                            item.DepthCount,
                            item.CellWidth,
                            item.CellDepth,
                            item.MaxLoad,
                            item.BeamHeight);
                        context.RackLevelRevisions.Add(level);
                    }
                    else
                    {
                        level.UpdateSpecification(
                            item.LevelNo,
                            item.BottomZ,
                            item.ClearHeight,
                            item.BinCount,
                            item.DepthCount,
                            item.CellWidth,
                            item.CellDepth,
                            item.MaxLoad,
                            item.BeamHeight);
                        level.Restore();
                    }
                    level.AttachSource(source, payload.SourceKey);
                }
                foreach (var obsolete in existingLevels.Values.Where(item =>
                             !expectedLevelIds.Contains(item.LogicalId)))
                    obsolete.ChangeLifecycle(SpaceLifecycleState.Disabled);

                var existingLocations = await context.LocationRevisions
                    .Where(item =>
                        item.ModelVersionId == source.ModelVersionId &&
                        item.RackLogicalId == payload.LogicalId)
                    .ToDictionaryAsync(item => item.LogicalId, cancellationToken);
                var expectedLocationIds = payload.Locations
                    .Select(item => item.LogicalId)
                    .ToHashSet();
                foreach (var item in payload.Locations)
                {
                    if (!existingLocations.TryGetValue(
                            item.LogicalId,
                            out var location))
                    {
                        location = SpaceLocationRevision.Create(
                            execution.TenantId,
                            source.ModelVersionId,
                            item.LogicalId,
                            payload.FloorLogicalId,
                            payload.LogicalId,
                            null,
                            item.ColumnNo,
                            item.LevelNo,
                            item.DepthNo,
                            item.Width,
                            item.Height,
                            item.Depth,
                            item.MaxLoad);
                        context.LocationRevisions.Add(location);
                    }
                    else
                    {
                        location.UpdateGeneratedSpecification(
                            payload.FloorLogicalId,
                            payload.LogicalId,
                            item.ColumnNo,
                            item.LevelNo,
                            item.DepthNo,
                            item.Width,
                            item.Height,
                            item.Depth,
                            item.MaxLoad);
                    }
                    location.AttachSource(source, payload.SourceKey);
                }
                foreach (var obsolete in existingLocations.Values.Where(item =>
                             !expectedLocationIds.Contains(item.LogicalId)))
                {
                    if (obsolete.ExternalBindingState !=
                        SpaceExternalBindingState.Unbound)
                    {
                        throw Invalid(
                            "AI Apply cannot remove a WMS-bound Location from a Rack derivation.");
                    }
                    obsolete.ChangeLifecycle(SpaceLifecycleState.Disabled);
                }
                break;
            default:
                var element = await context.ElementRevisions.SingleOrDefaultAsync(
                    item =>
                        item.ModelVersionId == source.ModelVersionId &&
                        item.LogicalId == payload.LogicalId,
                    cancellationToken);
                if (element is null)
                {
                    element = SpaceElementRevision.Create(
                        execution.TenantId,
                        source.ModelVersionId,
                        payload.LogicalId,
                        payload.FloorLogicalId,
                        payload.ProposalType,
                        payload.PrimaryGeometryJson,
                        payload.ParentLogicalId);
                    context.ElementRevisions.Add(element);
                }
                else
                {
                    element.UpdateDefinition(
                        payload.FloorLogicalId,
                        payload.ProposalType,
                        payload.PrimaryGeometryJson,
                        payload.ParentLogicalId);
                }
                element.ConfigurePlacement(
                    payload.X,
                    payload.Y,
                    payload.Z,
                    payload.RotationZ,
                    payload.Width,
                    payload.Height,
                    payload.Depth);
                element.AttachSource(source, payload.SourceKey);
                var existingAttributeRows = await context.ElementAttributes
                    .Where(item =>
                        item.ModelVersionId == source.ModelVersionId &&
                        item.ElementRevisionId == element.Id &&
                        item.Namespace == SpaceElementAttributeNamespaces.Design)
                    .ToArrayAsync(cancellationToken);
                var existingAttributes = existingAttributeRows.ToDictionary(
                    item => item.Key,
                    StringComparer.OrdinalIgnoreCase);
                var expectedAttributeKeys = payload.SemanticAttributes.Keys
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var attribute in payload.SemanticAttributes)
                {
                    if (existingAttributes.TryGetValue(
                            attribute.Key,
                            out var existingAttribute))
                    {
                        existingAttribute.UpdateValue(
                            SpaceElementAttributeValueTypes.String,
                            attribute.Value);
                    }
                    else
                    {
                        context.ElementAttributes.Add(SpaceElementAttribute.Create(
                            execution.TenantId,
                            element,
                            SpaceElementAttributeNamespaces.Design,
                            attribute.Key,
                            SpaceElementAttributeValueTypes.String,
                            attribute.Value));
                    }
                }
                foreach (var obsolete in existingAttributes.Values.Where(item =>
                             !expectedAttributeKeys.Contains(item.Key)))
                    obsolete.Remove();
                break;
        }
    }

    private static void BuildRackDerivation(
        Guid rackLogicalId,
        Guid floorLogicalId,
        JsonElement derivation,
        ICollection<SpaceAiStagedRackLevelV1> levels,
        ICollection<SpaceAiStagedLocationV1> locations)
    {
        if (!TryProperty(derivation, "levels", out var levelArray) ||
            levelArray.ValueKind != JsonValueKind.Array ||
            levelArray.GetArrayLength() == 0)
        {
            throw Invalid("Rack derivation requires at least one level.");
        }
        var levelNumbers = new HashSet<int>();
        foreach (var value in levelArray.EnumerateArray())
        {
            var level = RequireObject(value, "Rack level derivation");
            var levelNo = RequiredPositive(level, "levelNo");
            if (!levelNumbers.Add(levelNo))
                throw Invalid("Rack derivation level numbers must be unique.");
            var bottomZ = NonNegative(level, "bottomZMillimeters");
            var clearHeight = RequiredPositive(
                level,
                "clearHeightMillimeters");
            var binCount = RequiredPositive(level, "binCount");
            var depthCount = RequiredPositive(level, "depthCount");
            var cellWidth = RequiredPositive(
                level,
                "cellWidthMillimeters");
            var cellDepth = RequiredPositive(
                level,
                "cellDepthMillimeters");
            var beamHeight = NonNegative(
                level,
                "beamHeightMillimeters",
                defaultValue: 0);
            var maxLoad = Decimal(level, "maxLoadKilograms");
            if (maxLoad < 0)
                throw Invalid("Rack level max load cannot be negative.");
            var levelLogicalId =
                WarehouseDeterministicIdentity.CreateRackLevelLogicalId(
                    rackLogicalId,
                    levelNo);
            levels.Add(new SpaceAiStagedRackLevelV1(
                levelLogicalId,
                levelNo,
                bottomZ,
                clearHeight,
                binCount,
                depthCount,
                cellWidth,
                cellDepth,
                beamHeight,
                maxLoad));
            for (var columnNo = 1; columnNo <= binCount; columnNo++)
            for (var depthNo = 1; depthNo <= depthCount; depthNo++)
            {
                locations.Add(new SpaceAiStagedLocationV1(
                    WarehouseDeterministicIdentity.CreateLocationLogicalId(
                        rackLogicalId,
                        levelNo,
                        columnNo,
                        depthNo),
                    floorLogicalId,
                    levelNo,
                    columnNo,
                    depthNo,
                    cellWidth,
                    clearHeight,
                    cellDepth,
                    maxLoad));
            }
        }
    }

    private static Guid? ResolveElementParent(
        JsonElement relations,
        IReadOnlyDictionary<string, Guid> logicalBySource,
        IReadOnlyDictionary<Guid, string> typeByLogical)
    {
        var explicitParent = ResolveRelation(
            relations,
            "parentLogicalId",
            "parentSourceKey",
            expectedType: null,
            logicalBySource,
            typeByLogical,
            required: false);
        return explicitParent ?? ResolveRelation(
            relations,
            "wallLogicalId",
            "wallSourceKey",
            "Wall",
            logicalBySource,
            typeByLogical,
            required: false);
    }

    private static Guid? ResolveRelation(
        JsonElement relations,
        string logicalProperty,
        string sourceProperty,
        string? expectedType,
        IReadOnlyDictionary<string, Guid> logicalBySource,
        IReadOnlyDictionary<Guid, string> typeByLogical,
        bool required)
    {
        Guid? result = null;
        if (relations.ValueKind == JsonValueKind.Object)
        {
            result = GuidValue(relations, logicalProperty, required: false);
            var sourceKey = String(relations, sourceProperty);
            if (sourceKey is not null)
            {
                if (!logicalBySource.TryGetValue(sourceKey, out var logicalId))
                    throw Invalid("A Proposal relation references a rejected or missing SourceKey.");
                if (result.HasValue && result != logicalId)
                    throw Invalid("A Proposal relation has conflicting identities.");
                result = logicalId;
            }
        }
        else if (relations.ValueKind == JsonValueKind.Array)
        {
            foreach (var relation in relations.EnumerateArray())
            {
                if (!TryProperty(relation, "targetLogicalId", out var target) ||
                    target.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(target.GetString(), out var id) ||
                    id == Guid.Empty)
                {
                    continue;
                }
                if (expectedType is not null &&
                    typeByLogical.GetValueOrDefault(id) != expectedType)
                {
                    continue;
                }
                result = id;
                break;
            }
        }
        if (result.HasValue && expectedType is not null &&
            typeByLogical.TryGetValue(result.Value, out var actualType) &&
            actualType != expectedType)
        {
            throw Invalid($"A Proposal relation must target {expectedType}.");
        }
        if (required && result is null)
            throw Invalid($"A Proposal requires a {expectedType ?? "parent"} relation.");
        return result;
    }

    private static IReadOnlyDictionary<string, string> SemanticAttributes(
        JsonElement attributes)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "name",
            "semanticLabel",
            "wallType",
            "columnType",
            "doorType",
            "dockType",
            "equipmentType",
        };
        return attributes.EnumerateObject()
            .Where(item =>
                allowed.Contains(item.Name) &&
                item.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(
                item => item.Name,
                item => RequireText(
                    item.Value.GetString()!,
                    256,
                    $"Attribute {item.Name}"),
                StringComparer.Ordinal);
    }

    private static string ElementGeometry(
        JsonElement geometry,
        JsonElement attributes,
        GeometryBounds bounds)
    {
        var kind = String(geometry, "kind", required: true)!;
        var points = Points(geometry, kind == "Point" ? 1 : 2);
        return kind switch
        {
            "Point" => Serialize(new
            {
                schemaVersion = 1,
                kind = "point",
                x = points[0].X,
                y = points[0].Y,
                z = points[0].Z,
            }),
            "Path" => Serialize(new
            {
                schemaVersion = 1,
                kind = "path",
                points,
                width = RequiredPositive(attributes, "thicknessMillimeters"),
            }),
            "Polygon" => Serialize(new
            {
                schemaVersion = 1,
                kind = "polygon",
                outer = Points(geometry, 3),
                holes = Array.Empty<object>(),
                height = RequiredPositive(attributes, "heightMillimeters"),
            }),
            "Circle" or "Arc" or "BlockInstance" => Serialize(new
            {
                schemaVersion = 1,
                kind = "box",
                width = RequiredDimension(bounds.Width, "box width"),
                height = RequiredPositive(attributes, "heightMillimeters"),
                depth = RequiredDimension(bounds.Depth, "box depth"),
            }),
            _ => throw Invalid("The Proposal geometry kind is unsupported."),
        };
    }

    private async Task EnsureReviewUnchangedAsync(
        SpaceGenerationRun run,
        CancellationToken cancellationToken)
    {
        if (run.ApplyExpectedRunRowVersion is null ||
            run.ApplyReviewEtag is null)
        {
            throw Invalid("The AI Apply review snapshot is missing.");
        }
        var review = await SpaceAiReviewStateReader.ReadAsync(
            context,
            run.Id,
            run.ApplyExpectedRunRowVersion,
            cancellationToken);
        if (!SpaceAiAtomicApplyService.FixedEquals(
                review.ReviewEtag,
                run.ApplyReviewEtag) ||
            review.Summary.ProposedCount != 0 ||
            review.Summary.OpenRunBlockingIssueCount != 0 ||
            review.Summary.OpenProposalBlockingIssueCount != 0)
        {
            throw Invalid("The proposal review changed after Apply was queued.");
        }
    }

    private async Task EnsureDraftIsFreshAsync(
        SpaceGenerationRun run,
        CancellationToken cancellationToken)
    {
        var version = await context.Versions.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == run.ModelVersionId,
                cancellationToken) ?? throw Invalid("The target Draft is missing.");
        if (version.Status != SpaceVersionStatus.Draft ||
            version.ContentRevision != run.BaseContentRevision)
        {
            throw new SpaceAiApplyStaleException();
        }
    }

    private async Task<SpaceGenerationRun> RequireRunAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var run = await context.GenerationRuns.SingleOrDefaultAsync(
            item => item.Id == lease.SubjectId,
            cancellationToken) ?? throw Invalid("The AI Apply Run is missing.");
        if (run.ApplyJobId != lease.JobId)
            throw Invalid("The AI Apply Job is not pinned to this Run.");
        return run;
    }

    private async Task<SpaceGenerationRun> LockRunAsync(Guid runId)
    {
        if (!context.Database.IsRelational())
        {
            return await context.GenerationRuns.SingleAsync(
                item => item.Id == runId,
                CancellationToken.None);
        }
        return await context.GenerationRuns
            .FromSqlInterpolated(
                $"SELECT * FROM [Space_GenerationRun] WITH (UPDLOCK, HOLDLOCK) WHERE [TenantId] = {execution.TenantId} AND [Id] = {runId} AND [IsDeleted] = CAST(0 AS bit)")
            .SingleAsync(CancellationToken.None);
    }

    private async Task<SpaceModelVersion> LockVersionAsync(Guid versionId)
    {
        if (!context.Database.IsRelational())
        {
            return await context.Versions.SingleAsync(
                item => item.Id == versionId,
                CancellationToken.None);
        }
        return await context.Versions
            .FromSqlInterpolated(
                $"SELECT * FROM [Space_ModelVersion] WITH (UPDLOCK, HOLDLOCK) WHERE [TenantId] = {execution.TenantId} AND [Id] = {versionId} AND [IsDeleted] = CAST(0 AS bit)")
            .SingleAsync(CancellationToken.None);
    }

    private async Task<SpaceModel> LockModelAsync(Guid modelId)
    {
        if (!context.Database.IsRelational())
        {
            return await context.Models.SingleAsync(
                item => item.Id == modelId,
                CancellationToken.None);
        }
        return await context.Models
            .FromSqlInterpolated(
                $"SELECT * FROM [Space_Model] WITH (UPDLOCK, HOLDLOCK) WHERE [TenantId] = {execution.TenantId} AND [Id] = {modelId} AND [IsDeleted] = CAST(0 AS bit)")
            .SingleAsync(CancellationToken.None);
    }

    private async Task MarkStaleAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var run = await context.GenerationRuns.SingleOrDefaultAsync(
            item => item.Id == runId,
            cancellationToken);
        if (run?.Status == SpaceGenerationRunStatus.Applying)
        {
            run.MarkStale();
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task MarkFailedAsync(
        Guid runId,
        string code,
        string summary,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var run = await context.GenerationRuns.SingleOrDefaultAsync(
            item => item.Id == runId,
            cancellationToken);
        if (run?.Status == SpaceGenerationRunStatus.Applying)
        {
            run.MarkFailed(code, RequireText(summary, 1024, "Failure summary"));
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private void EnsureExecution(SpaceJobStepExecution step)
    {
        if (execution.IsExternal ||
            execution.TenantId == Guid.Empty ||
            execution.ActorId == Guid.Empty ||
            step.Lease.TenantId != execution.TenantId ||
            step.Lease.JobType != SpaceJobType.ApplyGeneration ||
            step.Lease.SubjectType != SpaceJobSubjectType.GenerationRun)
        {
            throw new SpaceJobProcessingException(
                SpaceJobFailureKind.Security,
                SpaceErrorCodes.ExternalSubjectDenied,
                "The AI Apply Job execution context is invalid.");
        }
    }

    private static void RequireApplying(SpaceGenerationRun run)
    {
        if (run.Status != SpaceGenerationRunStatus.Applying ||
            run.ApplyJobId is null ||
            run.ApplyCommandBatchId is null ||
            run.TargetFloorLogicalId is null)
        {
            throw Invalid("The generation Run is not ready for Apply processing.");
        }
    }

    private DateTime UtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static SpaceJobStepOutput CompletedOutput(
        SpaceGenerationRun run) => Output(
        new
        {
            runId = run.Id,
            status = run.Status.ToString(),
            appliedContentRevision = run.AppliedContentRevision,
            applyPlanHash = run.ApplyPlanHash,
            recovered = true,
        },
        run.ApplyPlanHash ?? new string('0', 64));

    private static SpaceJobStepOutput Output(object value, string hash) =>
        new(Serialize(value), hash);

    private static string HashStages(
        IEnumerable<SpaceGenerationStagingElement> stages) =>
        SpaceAiAtomicApplyService.Hash(string.Join(
            "\n",
            stages.OrderBy(item => item.SequenceNo).Select(item =>
                $"{item.SequenceNo}:{item.ProposalId:D}:{item.LogicalId:D}:" +
                SpaceAiAtomicApplyService.Hash(item.NormalizedPayloadJson))));

    private static string BuildPlanHash(
        SpaceGenerationRun run,
        IEnumerable<SpaceGenerationStagingElement> stages) =>
        SpaceAiAtomicApplyService.Hash(string.Join(
            "\n",
            new[]
            {
                run.Id.ToString("D"),
                run.ModelVersionId.ToString("D"),
                run.BaseContentRevision.ToString(CultureInfo.InvariantCulture),
                run.TargetFloorLogicalId!.Value.ToString("D"),
            }.Concat(stages.OrderBy(item => item.SequenceNo).Select(item =>
                $"{item.SequenceNo}:{item.ProposalId:D}:{item.LogicalId:D}:" +
                $"{item.ValidationHash}"))));

    private static SpaceAiAppliedCountsDto Counts(
        IReadOnlyList<SpaceAiStagedElementPayloadV1> payloads) => new(
        Floors: payloads.Count == 0 ? 0 : 1,
        Zones: payloads.LongCount(item => item.ProposalType == "Zone"),
        Aisles: payloads.LongCount(item => item.ProposalType == "Aisle"),
        Racks: payloads.LongCount(item => item.ProposalType == "Rack"),
        RackLevels: payloads.Sum(item => (long)item.RackLevels.Count),
        Locations: payloads.Sum(item => (long)item.Locations.Count),
        Elements: payloads.LongCount(item => item.ProposalType is
            "Wall" or "Column" or "Door" or "Dock" or "StaticEquipment"),
        Proposals: payloads.Count);

    private static SpaceAiStagedElementPayloadV1 Deserialize(string json) =>
        JsonSerializer.Deserialize<SpaceAiStagedElementPayloadV1>(
            json,
            JsonOptions) ?? throw Invalid("AI Apply staging JSON is invalid.");

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private static GeometryBounds Bounds(JsonElement geometry)
    {
        if (TryProperty(geometry, "bounds", out var bounds) &&
            bounds.ValueKind == JsonValueKind.Object)
        {
            var minX = Integer(bounds, "minX", required: true);
            var minY = Integer(bounds, "minY", required: true);
            var maxX = Integer(bounds, "maxX", required: true);
            var maxY = Integer(bounds, "maxY", required: true);
            if (maxX < minX || maxY < minY)
                throw Invalid("Proposal geometry bounds are invalid.");
            return new GeometryBounds(
                minX,
                minY,
                maxX,
                maxY);
        }
        return Bounds(Points(geometry, minimum: 1));
    }

    private static GeometryBounds Bounds(
        IReadOnlyCollection<SpaceAiPointV1> points) => new(
        points.Min(item => item.X),
        points.Min(item => item.Y),
        points.Max(item => item.X),
        points.Max(item => item.Y));

    private static SpaceAiPointV1[] Points(
        JsonElement geometry,
        int minimum)
    {
        if (!TryProperty(geometry, "points", out var points) ||
            points.ValueKind != JsonValueKind.Array ||
            points.GetArrayLength() < minimum)
        {
            throw Invalid(
                $"Proposal geometry requires at least {minimum} points.");
        }
        return points.EnumerateArray().Select(Point).ToArray();
    }

    private static SpaceAiPointV1 Point(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            if (value.GetArrayLength() < 2 ||
                value.GetArrayLength() > 3)
            {
                throw Invalid("A geometry point array must contain x, y, and optional z.");
            }
            return new SpaceAiPointV1(
                JsonInteger(value[0], "x"),
                JsonInteger(value[1], "y"),
                value.GetArrayLength() == 3
                    ? JsonInteger(value[2], "z")
                    : 0);
        }
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid("A geometry point must be an object or coordinate array.");
        return new SpaceAiPointV1(
            Integer(value, "x", required: true),
            Integer(value, "y", required: true),
            Integer(value, "z", required: false));
    }

    private static int JsonInteger(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var parsed))
        {
            throw Invalid($"Geometry point {label} must be a 32-bit integer.");
        }
        return parsed;
    }

    private static SpaceAiPointV1[] Rectangle(GeometryBounds bounds)
    {
        if (bounds.Width <= 0 || bounds.Depth <= 0)
            throw Invalid("Aisle geometry requires non-zero bounds.");
        return
        [
            new(bounds.MinX, bounds.MinY, 0),
            new(bounds.MaxX, bounds.MinY, 0),
            new(bounds.MaxX, bounds.MaxY, 0),
            new(bounds.MinX, bounds.MaxY, 0),
        ];
    }

    private static int FirstPointZ(JsonElement geometry) =>
        TryProperty(geometry, "points", out var points) &&
        points.ValueKind == JsonValueKind.Array &&
        points.GetArrayLength() > 0
            ? Integer(points[0], "z", required: false)
            : 0;

    private static string Code(
        JsonElement attributes,
        string propertyName,
        string fallback)
    {
        var value = String(attributes, propertyName) ?? fallback;
        return RequireText(value, 100, propertyName);
    }

    private static short ParseZoneType(JsonElement attributes)
    {
        var value = String(attributes, "zonePurpose") ?? "Unknown";
        return Enum.TryParse<WarehouseZonePurpose>(
                   value,
                   ignoreCase: false,
                   out var parsed) && Enum.IsDefined(parsed)
            ? checked((short)parsed)
            : throw Invalid("Zone purpose is invalid.");
    }

    private static short ParseDirection(JsonElement attributes) =>
        (String(attributes, "direction") ?? "Unknown") switch
        {
            "Unknown" => 0,
            "OneWay" => 1,
            "TwoWay" or "Bidirectional" => 2,
            _ => throw Invalid("Aisle direction is invalid."),
        };

    private static Guid? GuidValue(
        JsonElement value,
        string propertyName,
        bool required)
    {
        if (!TryProperty(value, propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            if (required)
                throw Invalid($"{propertyName} is required.");
            return null;
        }
        return property.ValueKind == JsonValueKind.String &&
               Guid.TryParse(property.GetString(), out var parsed) &&
               parsed != Guid.Empty
            ? parsed
            : throw Invalid($"{propertyName} is invalid.");
    }

    private static string? String(
        JsonElement value,
        string propertyName,
        bool required = false)
    {
        if (!TryProperty(value, propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            if (required)
                throw Invalid($"{propertyName} is required.");
            return null;
        }
        if (property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw Invalid($"{propertyName} must be a non-empty string.");
        }
        return property.GetString()!.Trim();
    }

    private static int Integer(
        JsonElement value,
        string propertyName,
        bool required)
    {
        if (!TryProperty(value, propertyName, out var property))
        {
            if (required)
                throw Invalid($"{propertyName} is required.");
            return 0;
        }
        return property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var result)
            ? result
            : throw Invalid($"{propertyName} must be an integer millimeter value.");
    }

    private static int? Positive(JsonElement value, string propertyName)
    {
        if (!TryProperty(value, propertyName, out var property))
            return null;
        return property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var result) &&
               result > 0
            ? result
            : throw Invalid($"{propertyName} must be positive.");
    }

    private static int RequiredPositive(
        JsonElement value,
        string propertyName) =>
        Positive(value, propertyName) ??
        throw Invalid($"{propertyName} is required.");

    private static int NonNegative(
        JsonElement value,
        string propertyName,
        int? defaultValue = null)
    {
        if (!TryProperty(value, propertyName, out var property))
        {
            return defaultValue ??
                throw Invalid($"{propertyName} is required.");
        }
        return property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out var result) &&
               result >= 0
            ? result
            : throw Invalid($"{propertyName} cannot be negative.");
    }

    private static decimal? Decimal(
        JsonElement value,
        string propertyName)
    {
        if (!TryProperty(value, propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null)
            return null;
        return property.ValueKind == JsonValueKind.Number &&
               property.TryGetDecimal(out var result)
            ? result
            : throw Invalid($"{propertyName} must be decimal.");
    }

    private static int RequiredDimension(int value, string label) =>
        value > 0 ? value : throw Invalid($"{label} must be positive.");

    private static JsonElement RequireObject(
        JsonElement value,
        string label) =>
        value.ValueKind == JsonValueKind.Object
            ? value
            : throw Invalid($"{label} must be a JSON object.");

    private static bool TryProperty(
        JsonElement value,
        string propertyName,
        out JsonElement property)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var candidate in value.EnumerateObject())
            {
                if (candidate.Name.Equals(
                        propertyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    property = candidate.Value;
                    return true;
                }
            }
        }
        property = default;
        return false;
    }

    private static bool Overlaps(Box left, Box right) =>
        left.X < checked(right.X + right.Width) &&
        checked(left.X + left.Width) > right.X &&
        left.Y < checked(right.Y + right.Depth) &&
        checked(left.Y + left.Depth) > right.Y;

    private static string RequireText(
        string value,
        int maximumLength,
        string label)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            throw Invalid($"{label} is invalid.");
        }
        return normalized;
    }

    private static SpaceAiApplyValidationException Invalid(string message) =>
        new(message);

    private sealed record SpaceAiPointV1(int X, int Y, int Z);

    private sealed record GeometryBounds(
        int MinX,
        int MinY,
        int MaxX,
        int MaxY)
    {
        public int Width => checked(MaxX - MinX);
        public int Depth => checked(MaxY - MinY);
    }

    private sealed record Box(int X, int Y, int Width, int Depth);
}

internal sealed record SpaceAiStagedElementPayloadV1(
    int SchemaVersion,
    Guid ProposalId,
    Guid LogicalId,
    Guid FloorLogicalId,
    string ProposalType,
    string SourceKey,
    string Name,
    string? Code,
    short? KindCode,
    Guid? ZoneLogicalId,
    Guid? AisleLogicalId,
    Guid? ParentLogicalId,
    int X,
    int Y,
    int Z,
    decimal RotationZ,
    int Width,
    int Height,
    int Depth,
    string PrimaryGeometryJson,
    string? SecondaryGeometryJson,
    string? RackType,
    Guid? RackProfileVersionId,
    IReadOnlyList<SpaceAiStagedRackLevelV1> RackLevels,
    IReadOnlyList<SpaceAiStagedLocationV1> Locations,
    IReadOnlyDictionary<string, string> SemanticAttributes);

internal sealed record SpaceAiStagedRackLevelV1(
    Guid LogicalId,
    int LevelNo,
    int BottomZ,
    int ClearHeight,
    int BinCount,
    int DepthCount,
    int CellWidth,
    int CellDepth,
    int BeamHeight,
    decimal? MaxLoad);

internal sealed record SpaceAiStagedLocationV1(
    Guid LogicalId,
    Guid FloorLogicalId,
    int LevelNo,
    int ColumnNo,
    int DepthNo,
    int Width,
    int Height,
    int Depth,
    decimal? MaxLoad);

internal sealed class SpaceAiApplyValidationException(string message) :
    Exception(message);

internal sealed class SpaceAiApplyStaleException : Exception;
