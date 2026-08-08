using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

/// <summary>
/// Production BuildScene execution for the deterministic rule-only mode.
/// Provider-backed execution remains closed until its outbound minimization,
/// quota and provider configuration are explicitly installed.
/// </summary>
public sealed class SpaceBuildSceneJobStepExecutor(
    SpaceContext context,
    ISpaceExecutionContext executionContext,
    IServiceProvider services,
    IWarehouseGenerationOutputValidator outputValidator,
    IWarehouseDraftSynthesizer synthesizer) :
    ISpaceBuildSceneJobStepExecutor
{
    private const long MaximumArtifactBytes = 200L * 1024L * 1024L;
    private const string RuleOnlyReason = "RULE_ONLY";
    private const string IssueCategory = "AiGeneration";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        EnsureLease(execution.Lease);
        try
        {
            return execution.StepCode switch
            {
                SpaceBuildSceneJobSteps.LoadPinnedInputs =>
                    await LoadPinnedInputsAsync(execution.Lease, cancellationToken),
                SpaceBuildSceneJobSteps.LoadLockedFacts =>
                    await LoadLockedFactsAsync(execution.Lease, cancellationToken),
                SpaceBuildSceneJobSteps.EnforceTenantPolicyAndQuota =>
                    await EnforcePolicyAsync(execution.Lease, cancellationToken),
                SpaceBuildSceneJobSteps.MinimizeStructuredFeatures =>
                    await MinimizeAsync(execution.Lease, cancellationToken),
                SpaceBuildSceneJobSteps.InvokeProvider =>
                    await InvokeRuleOnlyAsync(execution.Lease, cancellationToken),
                SpaceBuildSceneJobSteps.ValidateProviderOutput =>
                    await ValidateRuleOnlyOutputAsync(
                        execution.Lease,
                        cancellationToken),
                SpaceBuildSceneJobSteps.FuseRulesAndAi =>
                    await FuseAsync(execution.Lease, cancellationToken),
                SpaceBuildSceneJobSteps.SynthesizeDeterministicGeometry =>
                    await VerifyProposalSetAsync(
                        execution,
                        "deterministicGeometryVerified",
                        cancellationToken),
                SpaceBuildSceneJobSteps.ValidateProposalSet =>
                    await ValidateProposalSetAsync(execution, cancellationToken),
                SpaceBuildSceneJobSteps.PersistProposalsAndIssues =>
                    await PersistAsync(execution, cancellationToken),
                SpaceBuildSceneJobSteps.RecordUsage =>
                    await RecordRuleOnlyUsageAsync(
                        execution.Lease,
                        cancellationToken),
                SpaceBuildSceneJobSteps.AwaitReview =>
                    await AwaitReviewAsync(execution, cancellationToken),
                _ => throw Failure(
                    SpaceJobFailureKind.Bug,
                    SpaceErrorCodes.JobProcessorFailed,
                    "The BuildScene Job step is invalid."),
            };
        }
        catch (SpaceJobProcessingException exception)
        {
            await MarkRunFailedIfTerminalAsync(
                execution.Lease.JobId,
                exception.FailureKind,
                exception.ErrorCode,
                exception.SanitizedError,
                CancellationToken.None);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            const string summary =
                "The frozen BuildScene input or deterministic proposal evidence is invalid.";
            await MarkRunFailedIfTerminalAsync(
                execution.Lease.JobId,
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.AiOutputInvalid,
                summary,
                CancellationToken.None);
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.AiOutputInvalid,
                summary);
        }
        catch (ArgumentException)
        {
            const string summary =
                "The frozen BuildScene input violates the deterministic generation contract.";
            await MarkRunFailedIfTerminalAsync(
                execution.Lease.JobId,
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.AiOutputInvalid,
                summary,
                CancellationToken.None);
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.AiOutputInvalid,
                summary);
        }
        catch (IOException)
        {
            const string summary =
                "The private BuildScene artifact could not be read.";
            await MarkRunFailedIfTerminalAsync(
                execution.Lease.JobId,
                SpaceJobFailureKind.Resource,
                SpaceErrorCodes.JobProcessorFailed,
                summary,
                CancellationToken.None);
            throw Failure(
                SpaceJobFailureKind.Resource,
                SpaceErrorCodes.JobProcessorFailed,
                summary);
        }
        catch (Exception)
        {
            const string summary =
                "The deterministic BuildScene processor failed.";
            await MarkRunFailedIfTerminalAsync(
                execution.Lease.JobId,
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.JobProcessorFailed,
                summary,
                CancellationToken.None);
            throw Failure(
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.JobProcessorFailed,
                summary);
        }
    }

    private async Task<SpaceJobStepOutput> LoadPinnedInputsAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var input = await LoadInputAsync(lease, cancellationToken);
        await AdvanceRunAsync(input.Run, RunStage.Preparing, 8, cancellationToken);
        return Output(new
        {
            schemaVersion = 1,
            input.Run.Id,
            input.Run.ModelVersionId,
            input.Run.SourceId,
            input.Run.SourceHash,
            input.Run.BaseContentRevision,
            input.Run.TargetFloorLogicalId,
            input.PreviewArtifactId,
            input.Preview.PreviewSetSha256,
            input.Preview.SemanticPreview.SemanticPreviewSha256,
            mode = input.Payload.Mode,
        });
    }

    private async Task<SpaceJobStepOutput> LoadLockedFactsAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var input = await LoadInputAsync(lease, cancellationToken);
        var facts = await BuildLockedFactsAsync(input, cancellationToken);
        return Output(new
        {
            schemaVersion = 1,
            runId = input.Run.Id,
            facts,
            factCount = facts.Length,
        });
    }

    private async Task<SpaceJobStepOutput> EnforcePolicyAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var input = await LoadInputAsync(lease, cancellationToken);
        if (!string.Equals(
                input.Payload.Mode,
                SpaceAiRunRecoveryContract.RuleOnlyMode,
                StringComparison.Ordinal) ||
            input.Run.PolicySnapshot != SpaceAiPolicySnapshot.Disabled ||
            input.Run.ProviderConfigVersionId is not null)
        {
            throw Failure(
                SpaceJobFailureKind.Resource,
                SpaceErrorCodes.AiProviderUnavailable,
                "Provider-backed BuildScene execution is not configured; use rule-only recovery.");
        }

        return Output(new
        {
            schemaVersion = 1,
            runId = input.Run.Id,
            policy = input.Run.PolicySnapshot.ToString(),
            mode = input.Payload.Mode,
            providerInvoked = false,
            quotaConsumed = false,
        });
    }

    private async Task<SpaceJobStepOutput> MinimizeAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var input = await RequireRuleOnlyInputAsync(lease, cancellationToken);
        var facts = await BuildLockedFactsAsync(input, cancellationToken);
        var snapshot = SpaceAiCadFeatureMinimizer.CreateRuleOnlySnapshot(
            input.Run.ModelVersionId,
            input.Run.Id,
            input.Preview.SemanticPreview,
            facts);
        return Output(new
        {
            schemaVersion = 1,
            runId = input.Run.Id,
            localOnly = true,
            providerInvoked = false,
            featureCount = snapshot.LocalSourceMap.FeatureCount,
            lockedFactCount = facts.Length,
            snapshot.LocalSourceMap.ProviderInputSha256,
            snapshot.LocalSourceMap.SourceMapSha256,
        }, snapshot.LocalSourceMap.SourceMapSha256);
    }

    private async Task<SpaceJobStepOutput> InvokeRuleOnlyAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var input = await RequireRuleOnlyInputAsync(lease, cancellationToken);
        await AdvanceRunAsync(input.Run, RunStage.Inferring, 42, cancellationToken);
        var result = EmptyRuleOnlyResult(input.Run);
        return Output(result);
    }

    private async Task<SpaceJobStepOutput> ValidateRuleOnlyOutputAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var input = await RequireRuleOnlyInputAsync(lease, cancellationToken);
        var facts = await BuildLockedFactsAsync(input, cancellationToken);
        var snapshot = SpaceAiCadFeatureMinimizer.CreateRuleOnlySnapshot(
            input.Run.ModelVersionId,
            input.Run.Id,
            input.Preview.SemanticPreview,
            facts);
        var validated = outputValidator.Validate(
            snapshot.ProviderInput,
            EmptyRuleOnlyResult(input.Run));
        await AdvanceRunAsync(input.Run, RunStage.Validating, 50, cancellationToken);
        return Output(new
        {
            schemaVersion = 1,
            runId = input.Run.Id,
            providerInvoked = false,
            outputSchemaVersion = validated.Output.SchemaVersion,
            validated.CanonicalSha256,
        }, validated.CanonicalSha256);
    }

    private async Task<SpaceJobStepOutput> FuseAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var input = await RequireRuleOnlyInputAsync(lease, cancellationToken);
        var facts = await BuildLockedFactsAsync(input, cancellationToken);
        var snapshot = SpaceAiCadFeatureMinimizer.CreateRuleOnlySnapshot(
            input.Run.ModelVersionId,
            input.Run.Id,
            input.Preview.SemanticPreview,
            facts);
        var validated = outputValidator.Validate(
            snapshot.ProviderInput,
            EmptyRuleOnlyResult(input.Run));
        var proposalSet = await synthesizer.SynthesizeAsync(
            new WarehouseDraftSynthesisRequestV1(
                input.Run.ModelVersionId,
                input.Run.RuleVersion,
                snapshot,
                input.Preview.SemanticPreview,
                validated,
                facts,
                [],
                []),
            cancellationToken);
        await AdvanceRunAsync(input.Run, RunStage.Validating, 67, cancellationToken);
        return new SpaceJobStepOutput(
            WarehouseDraftSynthesizer.Serialize(proposalSet),
            proposalSet.ProposalSetSha256);
    }

    private async Task<SpaceJobStepOutput> VerifyProposalSetAsync(
        SpaceJobStepExecution execution,
        string marker,
        CancellationToken cancellationToken)
    {
        var proposalSet = await LoadProposalSetAsync(
            execution.Lease,
            cancellationToken);
        return Output(new
        {
            schemaVersion = 1,
            buildJobId = execution.Lease.JobId,
            marker,
            proposalSet.ProposalSetSha256,
            proposalSet.Summary.ProposalCount,
            proposalSet.Summary.BlockingCount,
        }, proposalSet.ProposalSetSha256);
    }

    private async Task<SpaceJobStepOutput> ValidateProposalSetAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken)
    {
        var proposalSet = await LoadProposalSetAsync(
            execution.Lease,
            cancellationToken);
        if (!proposalSet.Summary.CanEnterReview || proposalSet.Proposals.Count == 0)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.AiOutputInvalid,
                "The deterministic BuildScene result has no reviewable proposals.");
        }
        return Output(new
        {
            schemaVersion = 1,
            validated = true,
            proposalSet.ProposalSetSha256,
            proposalSet.Summary,
        }, proposalSet.ProposalSetSha256);
    }

    private async Task<SpaceJobStepOutput> PersistAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken)
    {
        var input = await RequireRuleOnlyInputAsync(
            execution.Lease,
            cancellationToken);
        var proposalSet = await LoadProposalSetAsync(
            execution.Lease,
            cancellationToken);
        var expected = BuildPersistencePlan(input, proposalSet);
        IDbContextTransaction? transaction = context.Database.IsRelational() &&
                                             context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        try
        {
            var existing = await context.GenerationProposals
                .Where(item => item.RunId == input.Run.Id)
                .OrderBy(item => item.SourceKey)
                .ToArrayAsync(cancellationToken);
            IReadOnlyDictionary<string, SpaceGenerationProposal> bySourceKey;
            var reused = existing.Length > 0;
            if (reused)
            {
                ValidateExistingProposals(existing, expected.Proposals);
                bySourceKey = existing.ToDictionary(
                    item => item.SourceKey,
                    StringComparer.Ordinal);
            }
            else
            {
                var created = expected.Proposals
                    .Select(item => SpaceGenerationProposal.Create(item.Definition))
                    .ToArray();
                context.GenerationProposals.AddRange(created);
                bySourceKey = created.ToDictionary(
                    item => item.SourceKey,
                    StringComparer.Ordinal);
            }

            var existingIssues = await context.Issues
                .Where(item => item.GenerationRunId == input.Run.Id)
                .ToArrayAsync(cancellationToken);
            if (existingIssues.Length > 0)
            {
                ValidateExistingIssues(
                    existingIssues,
                    expected.Issues,
                    bySourceKey);
            }
            else
            {
                context.Issues.AddRange(expected.Issues.Select(issue =>
                    SpaceModelIssue.Create(
                        executionContext.TenantId,
                        input.Run.ModelVersionId,
                        input.Run.SourceId,
                        execution.Lease.JobId,
                        issue.Severity,
                        issue.Code,
                        issue.SourceRef,
                        issue.TargetLogicalId,
                        MessageArgs(issue.DetailToken),
                        issue.SuggestedActionCode,
                        input.Run.Id,
                        issue.ProposalSourceKey is null
                            ? null
                            : bySourceKey[issue.ProposalSourceKey].Id,
                        category: IssueCategory,
                        fieldPath: issue.FieldPath,
                        evidenceJson: Evidence(issue.SourceKey))));
            }

            await AdvanceRunAsync(input.Run, RunStage.Validating, 84, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return Output(new
            {
                schemaVersion = 1,
                input.Run.Id,
                proposalSet.ProposalSetSha256,
                proposalCount = expected.Proposals.Length,
                issueCount = expected.Issues.Length,
                reused,
                draftWritten = false,
            }, proposalSet.ProposalSetSha256);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task<SpaceJobStepOutput> RecordRuleOnlyUsageAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var input = await RequireRuleOnlyInputAsync(lease, cancellationToken);
        if (await context.AiUsageRecords.AsNoTracking().AnyAsync(
                item => item.RunId == input.Run.Id,
                cancellationToken))
        {
            throw Failure(
                SpaceJobFailureKind.Security,
                SpaceErrorCodes.AiOutputInvalid,
                "A rule-only BuildScene run cannot contain Provider usage.");
        }
        return Output(new
        {
            schemaVersion = 1,
            runId = input.Run.Id,
            providerInvoked = false,
            inputUnits = 0,
            outputUnits = 0,
            estimatedCost = 0,
        });
    }

    private async Task<SpaceJobStepOutput> AwaitReviewAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken)
    {
        var input = await RequireRuleOnlyInputAsync(
            execution.Lease,
            cancellationToken);
        var proposalSet = await LoadProposalSetAsync(
            execution.Lease,
            cancellationToken);
        var count = await context.GenerationProposals.CountAsync(
            item => item.RunId == input.Run.Id,
            cancellationToken);
        if (count != proposalSet.Proposals.Count || count == 0)
        {
            throw Failure(
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.AiOutputInvalid,
                "The persisted deterministic proposal set is incomplete.");
        }

        await AdvanceRunAsync(input.Run, RunStage.AwaitingReview, 100, cancellationToken);
        return Output(new
        {
            schemaVersion = 1,
            input.Run.Id,
            status = input.Run.Status.ToString(),
            proposalSet.ProposalSetSha256,
            proposalCount = count,
            draftWritten = false,
            providerInvoked = false,
        }, proposalSet.ProposalSetSha256);
    }

    private async Task<BuildInput> RequireRuleOnlyInputAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var input = await LoadInputAsync(lease, cancellationToken);
        if (!string.Equals(
                input.Payload.Mode,
                SpaceAiRunRecoveryContract.RuleOnlyMode,
                StringComparison.Ordinal) ||
            input.Run.PolicySnapshot != SpaceAiPolicySnapshot.Disabled ||
            input.Run.ProviderConfigVersionId is not null)
        {
            throw Failure(
                SpaceJobFailureKind.Resource,
                SpaceErrorCodes.AiProviderUnavailable,
                "Provider-backed BuildScene execution is not configured; use rule-only recovery.");
        }
        return input;
    }

    private async Task<BuildInput> LoadInputAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var job = await context.Jobs.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == lease.JobId,
            cancellationToken) ?? throw InputFailure(
            "The BuildScene Job was not found.");
        var payload = DeserializePayload(job.PayloadJson);
        if (payload.SchemaVersion != SpaceAiRunRecoveryContract.SchemaVersion ||
            payload.RunId == Guid.Empty ||
            payload.SourceId == Guid.Empty ||
            payload.ExpectedContentRevision < 0 ||
            string.IsNullOrWhiteSpace(payload.Mode) ||
            job.JobType != SpaceJobType.BuildScene ||
            job.SubjectType != SpaceJobSubjectType.ModelVersion ||
            job.SubjectId != lease.SubjectId ||
            job.Status != SpaceJobStatus.Running ||
            job.ActiveAttemptId != lease.AttemptId)
        {
            throw InputFailure("The frozen BuildScene Job payload is invalid.");
        }

        var run = await context.GenerationRuns.SingleOrDefaultAsync(
            item => item.Id == payload.RunId && item.JobId == job.Id,
            cancellationToken) ?? throw InputFailure(
            "The generation run for this BuildScene Job was not found.");
        var version = await context.Versions.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == run.ModelVersionId,
            cancellationToken) ?? throw InputFailure(
            "The BuildScene Draft version was not found.");
        var source = await context.Sources.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == run.SourceId,
            cancellationToken) ?? throw InputFailure(
            "The BuildScene CAD source was not found.");
        var sourceFile = source.FileId is null
            ? null
            : await context.Files.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == source.FileId.Value,
                cancellationToken);
        if (run.ModelVersionId != lease.SubjectId ||
            run.SourceId != payload.SourceId ||
            run.BasedOnRunId != payload.BasedOnRunId ||
            run.BaseContentRevision != payload.ExpectedContentRevision ||
            version.Status != SpaceVersionStatus.Draft ||
            version.ContentRevision != run.BaseContentRevision ||
            source.ModelVersionId != run.ModelVersionId ||
            !source.Sha256.Equals(run.SourceHash, StringComparison.Ordinal) ||
            source.SourceType is not (SpaceSourceType.Dwg or SpaceSourceType.Dxf) ||
            source.State is not (SpaceSourceState.PreviewReady or SpaceSourceState.Imported) ||
            sourceFile is null ||
            sourceFile.State != SpaceFileState.Clean ||
            sourceFile.IsDeleted ||
            sourceFile.RetentionClass != SpaceFileRetentionClass.Source ||
            !string.Equals(sourceFile.Sha256, run.SourceHash, StringComparison.Ordinal) ||
            run.TargetFloorLogicalId is null)
        {
            throw InputFailure(
                "The BuildScene run no longer matches its frozen Draft and CAD source.");
        }

        var (artifactId, preview) = await LoadPreviewAsync(
            run,
            source,
            cancellationToken);
        return new BuildInput(
            job,
            payload,
            run,
            version,
            source,
            artifactId,
            preview);
    }

    private async Task<(Guid ArtifactId, SpaceCadPreviewSetV1 Preview)>
        LoadPreviewAsync(
            SpaceGenerationRun run,
            SpaceModelSource source,
            CancellationToken cancellationToken)
    {
        var rows = await (
                from artifact in context.Artifacts.AsNoTracking()
                join file in context.Files.AsNoTracking()
                    on artifact.FileId equals file.Id
                where artifact.ModelVersionId == run.ModelVersionId &&
                      artifact.SourceId == source.Id &&
                      artifact.ArtifactType == SpaceArtifactType.PreviewSet &&
                      artifact.SchemaVersion ==
                          SpaceCadPreviewSetVersions.ArtifactSchema &&
                      artifact.JobId.HasValue
                select new PersistedPreview(artifact, file))
            .ToArrayAsync(cancellationToken);
        var producerIds = rows
            .Select(item => item.Artifact.JobId!.Value)
            .Distinct()
            .ToArray();
        var producers = await context.Jobs.AsNoTracking()
            .Where(item => producerIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var selected = rows
            .Where(item =>
                producers.TryGetValue(item.Artifact.JobId!.Value, out var producer) &&
                producer.JobType == SpaceJobType.CadParse &&
                producer.SubjectType == SpaceJobSubjectType.ModelSource &&
                producer.SubjectId == source.Id &&
                producer.Status == SpaceJobStatus.Succeeded &&
                IsCleanArtifact(item.File))
            .OrderByDescending(item =>
                producers[item.Artifact.JobId!.Value].RequestedAtUtc)
            .ThenByDescending(item => item.Artifact.JobId)
            .FirstOrDefault() ?? throw InputFailure(
            "No authoritative CAD PreviewSet is available for BuildScene.");
        var files = services.GetService(typeof(ISpaceFileStore)) as ISpaceFileStore
                    ?? throw Failure(
                        SpaceJobFailureKind.Resource,
                        SpaceErrorCodes.JobProcessorUnavailable,
                        "Private Space artifact storage is not configured.");
        var json = await ReadVerifiedTextAsync(
            selected.File,
            files,
            cancellationToken);
        var preview = SpaceCadPreviewSet.Deserialize(json);
        if (preview.TenantId != executionContext.TenantId ||
            preview.ModelVersionId != run.ModelVersionId ||
            preview.SourceId != source.Id ||
            preview.CadParseJobId != selected.Artifact.JobId ||
            preview.FloorLogicalId != run.TargetFloorLogicalId ||
            !preview.SourceSha256.Equals(run.SourceHash, StringComparison.Ordinal))
        {
            throw InputFailure(
                "The CAD PreviewSet does not match the frozen BuildScene run.");
        }
        return (selected.Artifact.Id, preview);
    }

    private async Task<SpaceAiCadLockedFactV1[]> BuildLockedFactsAsync(
        BuildInput input,
        CancellationToken cancellationToken)
    {
        var rows = await context.GenerationLockedFacts.AsNoTracking()
            .Where(item => item.RunId == input.Run.Id)
            .OrderBy(item => item.SourceKey)
            .ThenBy(item => item.FieldPath)
            .ToArrayAsync(cancellationToken);
        if (rows.Length == 0)
            return [];
        if (rows.Any(item =>
                !item.IsConfirmed ||
                item.MatchMethod != SpaceLockedFactMatchMethod.SameSourceIdentity ||
                !item.SourceHash.Equals(input.Run.SourceHash, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "Only confirmed same-source locked facts can enter rule-only fusion.");
        }

        var proposalIds = rows.Select(item => item.SourceProposalId)
            .Distinct()
            .ToArray();
        var sourceRunId = input.Run.BasedOnRunId ?? rows[0].BasedOnRunId;
        var proposals = await context.GenerationProposals.AsNoTracking()
            .Where(item => item.RunId == sourceRunId ||
                           proposalIds.Contains(item.Id))
            .ToArrayAsync(cancellationToken);
        var proposalById = proposals.ToDictionary(item => item.Id);
        var sourceRefByOldKey = proposals
            .Select(item => new
            {
                item.SourceKey,
                SourceRef = SingleSourceRef(item.SourceRefsJson),
            })
            .GroupBy(item => item.SourceKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.SourceRef)
                    .Distinct(StringComparer.Ordinal)
                    .Single(),
                StringComparer.Ordinal);
        var currentSnapshot = SpaceAiCadFeatureMinimizer.CreateRuleOnlySnapshot(
            input.Run.ModelVersionId,
            input.Run.Id,
            input.Preview.SemanticPreview);
        var currentKeyByRef = currentSnapshot.LocalSourceMap.Entries
            .SelectMany(item => item.SourceRefs.Select(sourceRef =>
                new KeyValuePair<string, string>(sourceRef, item.SourceKey)))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        var facts = new List<SpaceAiCadLockedFactV1>(rows.Length);
        foreach (var row in rows)
        {
            if (!proposalById.TryGetValue(row.SourceProposalId, out var proposal))
            {
                throw new InvalidDataException(
                    "A locked fact source proposal is missing.");
            }
            var sourceRef = SingleSourceRef(proposal.SourceRefsJson);
            if (!currentKeyByRef.ContainsKey(sourceRef))
            {
                throw new InvalidDataException(
                    "A locked fact source is outside the current CAD PreviewSet.");
            }
            var fieldPath = LockedFieldPath(row.FieldPath);
            var value = LockedStringValue(row.ValueJson);
            if (fieldPath.StartsWith("relations.", StringComparison.Ordinal))
            {
                if (!sourceRefByOldKey.TryGetValue(value, out var targetRef) ||
                    !currentKeyByRef.TryGetValue(targetRef, out value))
                {
                    throw new InvalidDataException(
                        "A locked relation target is outside the current CAD PreviewSet.");
                }
            }
            facts.Add(new SpaceAiCadLockedFactV1(sourceRef, fieldPath, value));
        }
        var canonical = facts
            .OrderBy(item => item.SourceRef, StringComparer.Ordinal)
            .ThenBy(item => item.FieldPath, StringComparer.Ordinal)
            .ToArray();
        if (canonical.Select(item => $"{item.SourceRef}\n{item.FieldPath}")
            .Distinct(StringComparer.Ordinal).Count() != canonical.Length)
        {
            throw new InvalidDataException(
                "The current generation run has duplicate locked facts.");
        }
        return canonical;
    }

    private async Task<WarehouseDraftProposalSetV1> LoadProposalSetAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var step = await context.JobSteps.AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.AttemptId == lease.AttemptId &&
                item.StepCode == SpaceBuildSceneJobSteps.FuseRulesAndAi &&
                (item.Status == SpaceJobStepStatus.Succeeded ||
                 item.Status == SpaceJobStepStatus.Reused),
                cancellationToken) ?? throw Failure(
            SpaceJobFailureKind.Bug,
            SpaceErrorCodes.AiOutputInvalid,
            "The deterministic fusion checkpoint is missing.");
        if (string.IsNullOrWhiteSpace(step.CheckpointJson) ||
            string.IsNullOrWhiteSpace(step.OutputHash))
        {
            throw new InvalidDataException(
                "The deterministic fusion checkpoint is incomplete.");
        }
        var value = JsonSerializer.Deserialize<WarehouseDraftProposalSetV1>(
            step.CheckpointJson,
            JsonOptions) ?? throw new InvalidDataException(
            "The deterministic fusion checkpoint is empty.");
        _ = WarehouseDraftSynthesizer.Serialize(value);
        if (!value.ProposalSetSha256.Equals(
                step.OutputHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The deterministic fusion checkpoint hash changed.");
        }
        return value;
    }

    private static PersistencePlan BuildPersistencePlan(
        BuildInput input,
        WarehouseDraftProposalSetV1 proposalSet)
    {
        if (proposalSet.TenantId != input.Run.TenantId ||
            proposalSet.ModelVersionId != input.Run.ModelVersionId ||
            proposalSet.FloorLogicalId != input.Run.TargetFloorLogicalId ||
            !proposalSet.SourceSha256.Equals(
                input.Run.SourceHash,
                StringComparison.Ordinal) ||
            !proposalSet.SemanticPreviewSha256.Equals(
                input.Preview.SemanticPreview.SemanticPreviewSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The proposal set does not match the BuildScene run.");
        }

        var previewByRef = input.Preview.SemanticPreview.Items.ToDictionary(
            item => item.Source.SourceRef,
            StringComparer.Ordinal);
        var sourceKeyByLogicalId = proposalSet.Proposals.ToDictionary(
            item => item.LogicalId,
            item => item.SourceKey);
        var typeByLogicalId = proposalSet.Proposals.ToDictionary(
            item => item.LogicalId,
            item => item.ObjectType);
        var localIssues = new List<ExpectedIssue>();
        var proposals = new List<ExpectedProposal>(proposalSet.Proposals.Count);
        foreach (var proposal in proposalSet.Proposals)
        {
            if (!previewByRef.TryGetValue(proposal.SourceRef, out var preview))
            {
                throw new InvalidDataException(
                    "A proposal source is outside the CAD PreviewSet.");
            }
            var payload = BuildProposalPayload(
                proposal,
                preview,
                sourceKeyByLogicalId,
                typeByLogicalId,
                localIssues);
            var hasBlocking = proposalSet.Issues.Any(issue =>
                                  issue.Severity ==
                                      WarehouseProposalIssueSeverity.Blocking &&
                                  (issue.SourceKey == proposal.SourceKey ||
                                   issue.SourceRef == proposal.SourceRef)) ||
                              localIssues.Any(issue =>
                                  issue.Severity == SpaceIssueSeverity.Blocking &&
                                  issue.ProposalSourceKey == proposal.SourceKey);
            proposals.Add(new ExpectedProposal(
                proposal.SourceRef,
                new SpaceGenerationProposalDefinition(
                    input.Run.TenantId,
                    input.Run.Id,
                    input.Run.ModelVersionId,
                    input.Run.BaseContentRevision,
                    input.Run.SourceHash,
                    proposal.SourceKey,
                    proposal.ObjectType.ToString(),
                    payload.GeometryJson,
                    payload.AttributesJson,
                    payload.RelationsJson,
                    JsonSerializer.Serialize(
                        new[] { proposal.SourceRef },
                        JsonOptions),
                    payload.EvidenceJson,
                    payload.FieldProvenanceJson,
                    proposal.Confidence,
                    proposal.ConfidenceBand switch
                    {
                        WarehouseFusionConfidenceBand.High =>
                            SpaceConfidenceBand.High,
                        WarehouseFusionConfidenceBand.Medium =>
                            SpaceConfidenceBand.Medium,
                        WarehouseFusionConfidenceBand.Low =>
                            SpaceConfidenceBand.Low,
                        _ => throw new ArgumentOutOfRangeException(),
                    },
                    hasBlocking)));
        }

        var issues = proposalSet.Issues.Select(issue =>
        {
            var target = issue.SourceKey is null
                ? null
                : proposalSet.Proposals.SingleOrDefault(item =>
                    item.SourceKey == issue.SourceKey);
            return new ExpectedIssue(
                issue.Severity switch
                {
                    WarehouseProposalIssueSeverity.Info => SpaceIssueSeverity.Info,
                    WarehouseProposalIssueSeverity.Warning =>
                        SpaceIssueSeverity.Warning,
                    WarehouseProposalIssueSeverity.Blocking =>
                        SpaceIssueSeverity.Blocking,
                    _ => throw new ArgumentOutOfRangeException(),
                },
                issue.Code,
                issue.SourceRef,
                issue.SourceKey,
                issue.FieldPath,
                issue.DetailToken,
                target?.LogicalId,
                target?.SourceKey,
                SuggestedAction(issue.Severity));
        }).Concat(localIssues)
          .OrderBy(item => item.Code, StringComparer.Ordinal)
          .ThenBy(item => item.SourceRef, StringComparer.Ordinal)
          .ThenBy(item => item.FieldPath, StringComparer.Ordinal)
          .ToArray();
        return new PersistencePlan(
            proposals.OrderBy(item => item.Definition.SourceKey, StringComparer.Ordinal)
                .ToArray(),
            issues);
    }

    private static ProposalPayload BuildProposalPayload(
        WarehouseDraftProposalV1 proposal,
        SpaceCadSemanticPreviewItemV1 preview,
        IReadOnlyDictionary<Guid, string> sourceKeyByLogicalId,
        IReadOnlyDictionary<Guid, WarehouseSpaceType> typeByLogicalId,
        ICollection<ExpectedIssue> issues)
    {
        var attributes = new JsonObject();
        var relations = new JsonObject();
        foreach (var field in proposal.Fields)
        {
            if (field.FieldPath == "type")
                continue;
            if (field.FieldPath.StartsWith("attributes.", StringComparison.Ordinal))
            {
                attributes[field.FieldPath["attributes.".Length..]] =
                    field.ValueToken;
            }
            else if (field.FieldPath.StartsWith("relations.", StringComparison.Ordinal))
            {
                relations[field.FieldPath["relations.".Length..]] =
                    field.ValueToken;
            }
        }
        attributes["name"] ??= proposal.SourceRef;
        if (proposal.RackDerivation is not null)
        {
            attributes["rackDerivation"] = JsonSerializer.SerializeToNode(
                proposal.RackDerivation,
                JsonOptions);
        }

        foreach (var relation in proposal.Relations)
        {
            if (!sourceKeyByLogicalId.TryGetValue(
                    relation.TargetLogicalId,
                    out var targetSourceKey) ||
                !typeByLogicalId.TryGetValue(
                    relation.TargetLogicalId,
                    out var targetType))
            {
                throw new InvalidDataException(
                    "A deterministic proposal relation target is missing.");
            }
            var property = targetType switch
            {
                WarehouseSpaceType.Zone => "zoneSourceKey",
                WarehouseSpaceType.Aisle => "aisleSourceKey",
                WarehouseSpaceType.Wall => "wallSourceKey",
                _ => null,
            };
            if (property is not null)
                relations[property] ??= targetSourceKey;
        }

        AddDeterministicDimensions(proposal, preview, attributes, issues);
        if (proposal.ObjectType is WarehouseSpaceType.Aisle or WarehouseSpaceType.Rack &&
            relations["zoneSourceKey"] is null)
        {
            issues.Add(LocalIssue(
                proposal,
                "SPACE_RULE_ONLY_PARENT_REQUIRED",
                "relations.zoneSourceKey",
                "select-parent-in-a-new-generation-run"));
        }

        var evidence = proposal.Fields.Select(field => new
        {
            field.FieldPath,
            field.WinningSource,
            field.Confidence,
            field.Evidence,
        }).ToArray();
        var provenance = proposal.Fields.ToDictionary(
            field => field.FieldPath,
            field => new
            {
                field.WinningSource,
                field.Confidence,
                field.Evidence,
            },
            StringComparer.Ordinal);
        return new ProposalPayload(
            JsonSerializer.Serialize(proposal.Geometry, JsonOptions),
            attributes.ToJsonString(JsonOptions),
            relations.ToJsonString(JsonOptions),
            JsonSerializer.Serialize(evidence, JsonOptions),
            JsonSerializer.Serialize(provenance, JsonOptions));
    }

    private static void AddDeterministicDimensions(
        WarehouseDraftProposalV1 proposal,
        SpaceCadSemanticPreviewItemV1 preview,
        JsonObject attributes,
        ICollection<ExpectedIssue> issues)
    {
        if (proposal.ObjectType is
            WarehouseSpaceType.Zone or
            WarehouseSpaceType.Aisle or
            WarehouseSpaceType.Rack)
        {
            return;
        }
        var bounds = proposal.Geometry.Bounds;
        var width = bounds.MaxX - bounds.MinX;
        var depth = bounds.MaxY - bounds.MinY;
        var defaultThickness = preview.AppliedMapping.DefaultThicknessMillimeters;
        var defaultHeight = preview.AppliedMapping.DefaultHeightMillimeters;
        if (width > 0)
            attributes["widthMillimeters"] = width;
        if (depth > 0)
            attributes["depthMillimeters"] = depth;
        else if (defaultThickness is > 0)
            attributes["depthMillimeters"] = defaultThickness.Value;
        if (defaultHeight is > 0)
            attributes["heightMillimeters"] = defaultHeight.Value;

        foreach (var (property, path) in new[]
                 {
                     ("widthMillimeters", "attributes.widthMillimeters"),
                     ("depthMillimeters", "attributes.depthMillimeters"),
                     ("heightMillimeters", "attributes.heightMillimeters"),
                 })
        {
            if (attributes[property] is null)
            {
                issues.Add(LocalIssue(
                    proposal,
                    "SPACE_RULE_ONLY_DIMENSION_REQUIRED",
                    path,
                    "correct-cad-mapping-and-create-new-generation-run"));
            }
        }
    }

    private static ExpectedIssue LocalIssue(
        WarehouseDraftProposalV1 proposal,
        string code,
        string fieldPath,
        string action) => new(
        SpaceIssueSeverity.Blocking,
        code,
        proposal.SourceRef,
        proposal.SourceKey,
        fieldPath,
        null,
        proposal.LogicalId,
        proposal.SourceKey,
        action);

    private static void ValidateExistingProposals(
        IReadOnlyList<SpaceGenerationProposal> actual,
        IReadOnlyList<ExpectedProposal> expected)
    {
        if (actual.Count != expected.Count)
        {
            throw new InvalidDataException(
                "The persisted proposal count does not match the checkpoint.");
        }
        var expectedByKey = expected.ToDictionary(
            item => item.Definition.SourceKey,
            StringComparer.Ordinal);
        foreach (var item in actual)
        {
            if (!expectedByKey.TryGetValue(item.SourceKey, out var match))
            {
                throw new InvalidDataException(
                    "A persisted proposal is outside the checkpoint.");
            }
            var value = match.Definition;
            if (item.ModelVersionId != value.ModelVersionId ||
                item.BaseContentRevision != value.BaseContentRevision ||
                item.SourceHash != value.SourceHash ||
                item.ProposalType != value.ProposalType ||
                item.SuggestedGeometryJson != value.SuggestedGeometryJson ||
                item.SuggestedAttributesJson != value.SuggestedAttributesJson ||
                item.SuggestedRelationsJson != value.SuggestedRelationsJson ||
                item.SourceRefsJson != value.SourceRefsJson ||
                item.EvidenceJson != value.EvidenceJson ||
                item.FieldProvenanceJson != value.FieldProvenanceJson ||
                item.ConfidenceScore != value.ConfidenceScore ||
                item.ConfidenceBand != value.ConfidenceBand ||
                item.HasBlockingIssue != value.HasBlockingIssue ||
                item.Status != SpaceGenerationProposalStatus.Proposed)
            {
                throw new InvalidDataException(
                    "A persisted proposal changed after checkpointing.");
            }
        }
    }

    private static void ValidateExistingIssues(
        IReadOnlyList<SpaceModelIssue> actual,
        IReadOnlyList<ExpectedIssue> expected,
        IReadOnlyDictionary<string, SpaceGenerationProposal> proposals)
    {
        if (actual.Count != expected.Count)
        {
            throw new InvalidDataException(
                "The persisted generation issue count does not match the checkpoint.");
        }
        var proposalKeyById = proposals.Values.ToDictionary(
            item => item.Id,
            item => item.SourceKey);
        var actualKeys = actual.Select(item => IssueKey(
                item.Severity,
                item.Code,
                item.SourceRef,
                item.FieldPath,
                item.GenerationProposalId is null
                    ? null
                    : proposalKeyById.GetValueOrDefault(
                        item.GenerationProposalId.Value)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedKeys = expected.Select(item => IssueKey(
                item.Severity,
                item.Code,
                item.SourceRef,
                item.FieldPath,
                item.ProposalSourceKey))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!actualKeys.SequenceEqual(expectedKeys, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Persisted generation issues changed after checkpointing.");
        }
    }

    private async Task AdvanceRunAsync(
        SpaceGenerationRun run,
        RunStage target,
        int progress,
        CancellationToken cancellationToken)
    {
        if (run.Status == SpaceGenerationRunStatus.Queued &&
            target >= RunStage.Preparing)
        {
            run.BeginPreparing();
        }
        if (run.Status == SpaceGenerationRunStatus.Preparing &&
            target >= RunStage.Inferring)
        {
            run.BeginInferring();
            run.RecordDegradedReason(RuleOnlyReason);
        }
        if (run.Status == SpaceGenerationRunStatus.Inferring &&
            target >= RunStage.Validating)
        {
            if (run.DegradedReason is null)
                run.RecordDegradedReason(RuleOnlyReason);
            run.BeginValidating();
        }
        if (run.Status == SpaceGenerationRunStatus.Validating &&
            target >= RunStage.AwaitingReview)
        {
            run.MarkAwaitingReview();
        }
        var minimum = target switch
        {
            RunStage.Preparing => SpaceGenerationRunStatus.Preparing,
            RunStage.Inferring => SpaceGenerationRunStatus.Inferring,
            RunStage.Validating => SpaceGenerationRunStatus.Validating,
            RunStage.AwaitingReview => SpaceGenerationRunStatus.AwaitingReview,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        if (run.Status < minimum ||
            run.Status is not (
                SpaceGenerationRunStatus.Preparing or
                SpaceGenerationRunStatus.Inferring or
                SpaceGenerationRunStatus.Validating or
                SpaceGenerationRunStatus.AwaitingReview))
        {
            throw InputFailure(
                "The generation run is not in a BuildScene processing state.");
        }
        if (progress > run.Progress)
            run.ReportProgress(progress);
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkRunFailedIfTerminalAsync(
        Guid jobId,
        SpaceJobFailureKind failureKind,
        string code,
        string summary,
        CancellationToken cancellationToken)
    {
        try
        {
            context.ChangeTracker.Clear();
            var job = await context.Jobs.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == jobId,
                cancellationToken);
            var final = failureKind is not (
                            SpaceJobFailureKind.Transient or
                            SpaceJobFailureKind.Bug) ||
                        job is not null && job.AttemptCount >= job.MaxAttempts;
            if (!final)
                return;
            var run = await context.GenerationRuns.SingleOrDefaultAsync(
                item => item.JobId == jobId,
                cancellationToken);
            if (run is null || run.Status is not (
                    SpaceGenerationRunStatus.Queued or
                    SpaceGenerationRunStatus.Preparing or
                    SpaceGenerationRunStatus.Inferring or
                    SpaceGenerationRunStatus.Validating or
                    SpaceGenerationRunStatus.Applying))
            {
                return;
            }
            run.MarkFailed(code, summary);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // The Job ledger remains authoritative if failure projection fails.
        }
    }

    private static WarehouseGenerationResult EmptyRuleOnlyResult(
        SpaceGenerationRun run) => new(
        WarehouseGenerationInput.CurrentSchemaVersion,
        $"rule-only-{run.Id:N}",
        "cp6-deterministic-rules",
        new WarehouseGenerationUsage(0, 0),
        [],
        []);

    private static BuildScenePayload DeserializePayload(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<BuildScenePayload>(
                       json,
                       JsonOptions) ?? throw new JsonException();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The BuildScene Job payload is invalid JSON.",
                exception);
        }
    }

    private static string SingleSourceRef(string json)
    {
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(json, JsonOptions)
                         ?? throw new JsonException();
            if (values.Length != 1 ||
                string.IsNullOrWhiteSpace(values[0]) ||
                !values[0].Equals(values[0].Trim(), StringComparison.Ordinal))
            {
                throw new JsonException();
            }
            return values[0];
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "A source proposal has invalid source references.",
                exception);
        }
    }

    private static string LockedFieldPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2 ||
            segments[0] is not ("attributes" or "relations") ||
            string.IsNullOrWhiteSpace(segments[1]))
        {
            throw new InvalidDataException("A locked fact path is invalid.");
        }
        return $"{segments[0]}.{segments[1]}";
    }

    private static string LockedStringValue(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException(
                    "A rule-only locked fact must contain a string value.");
            }
            return document.RootElement.GetString()
                   ?? throw new InvalidDataException(
                       "A rule-only locked fact value is missing.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "A locked fact value is invalid JSON.",
                exception);
        }
    }

    private static async Task<string> ReadVerifiedTextAsync(
        SpaceFile file,
        ISpaceFileStore files,
        CancellationToken cancellationToken)
    {
        if (!IsCleanArtifact(file) ||
            file.SizeBytes is < 1 or > MaximumArtifactBytes)
        {
            throw new InvalidDataException(
                "The CAD PreviewSet artifact file is not readable.");
        }
        await using var stream = await files.OpenQuarantinedReadAsync(
            file.TenantId,
            file.Id,
            file.StorageKey,
            cancellationToken);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 64 * 1024,
            leaveOpen: false);
        var json = await reader.ReadToEndAsync(cancellationToken);
        if (Encoding.UTF8.GetByteCount(json) != file.SizeBytes ||
            !Hash(json).Equals(file.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The CAD PreviewSet artifact hash or size changed.");
        }
        return json;
    }

    private static bool IsCleanArtifact(SpaceFile file) =>
        file.State == SpaceFileState.Clean &&
        !file.IsDeleted &&
        file.RetentionClass == SpaceFileRetentionClass.Artifact &&
        file.Sha256 is { Length: 64 };

    private static SpaceJobStepOutput Output(object value, string? hash = null)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return new SpaceJobStepOutput(json, hash ?? Hash(json));
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string MessageArgs(string? detailToken) =>
        JsonSerializer.Serialize(new { detailToken }, JsonOptions);

    private static string Evidence(string? sourceKey) =>
        JsonSerializer.Serialize(new { sourceKey }, JsonOptions);

    private static string? SuggestedAction(
        WarehouseProposalIssueSeverity severity) => severity ==
            WarehouseProposalIssueSeverity.Blocking
        ? "repair-or-reject-proposal"
        : null;

    private static string IssueKey(
        SpaceIssueSeverity severity,
        string code,
        string? sourceRef,
        string? fieldPath,
        string? proposalSourceKey) => string.Join(
        '\n',
        severity,
        code,
        sourceRef ?? string.Empty,
        fieldPath ?? string.Empty,
        proposalSourceKey ?? string.Empty);

    private void EnsureLease(SpaceJobLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (executionContext.IsExternal ||
            executionContext.TenantId == Guid.Empty ||
            executionContext.ActorId == Guid.Empty ||
            context.CurrentTenantId != executionContext.TenantId ||
            lease.TenantId != executionContext.TenantId ||
            lease.JobType != SpaceJobType.BuildScene ||
            lease.SubjectType != SpaceJobSubjectType.ModelVersion ||
            lease.JobId == Guid.Empty ||
            lease.AttemptId == Guid.Empty ||
            lease.SubjectId == Guid.Empty)
        {
            throw Failure(
                SpaceJobFailureKind.Security,
                SpaceErrorCodes.JobProcessorFailed,
                "The BuildScene worker context is invalid.");
        }
    }

    private static SpaceJobProcessingException InputFailure(string summary) =>
        Failure(
            SpaceJobFailureKind.Input,
            SpaceErrorCodes.AiOutputInvalid,
            summary);

    private static SpaceJobProcessingException Failure(
        SpaceJobFailureKind kind,
        string code,
        string summary) => new(kind, code, summary);

    private enum RunStage
    {
        Preparing = 0,
        Inferring = 1,
        Validating = 2,
        AwaitingReview = 3,
    }

    private sealed record BuildScenePayload(
        int SchemaVersion,
        Guid RunId,
        Guid? BasedOnRunId,
        Guid SourceId,
        long ExpectedContentRevision,
        string Mode);

    private sealed record BuildInput(
        SpaceJob Job,
        BuildScenePayload Payload,
        SpaceGenerationRun Run,
        SpaceModelVersion Version,
        SpaceModelSource Source,
        Guid PreviewArtifactId,
        SpaceCadPreviewSetV1 Preview);

    private sealed record PersistedPreview(
        SpaceArtifact Artifact,
        SpaceFile File);

    private sealed record ProposalPayload(
        string GeometryJson,
        string AttributesJson,
        string RelationsJson,
        string EvidenceJson,
        string FieldProvenanceJson);

    private sealed record ExpectedProposal(
        string SourceRef,
        SpaceGenerationProposalDefinition Definition);

    private sealed record ExpectedIssue(
        SpaceIssueSeverity Severity,
        string Code,
        string? SourceRef,
        string? SourceKey,
        string? FieldPath,
        string? DetailToken,
        Guid? TargetLogicalId,
        string? ProposalSourceKey,
        string? SuggestedActionCode);

    private sealed record PersistencePlan(
        ExpectedProposal[] Proposals,
        ExpectedIssue[] Issues);
}
