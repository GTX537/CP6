using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class SpaceAiGenerationRunService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access,
    ISpaceClock clock,
    ISpaceAiTenantPolicySource policySource,
    ISpaceAiRunRecoveryService recovery) : ISpaceAiGenerationRunService
{
    private const string CreateOperation = "space.ai-generation-run.create";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<SpaceAiGenerationRunAcceptedDto> CreateAsync(
        Guid versionId,
        CreateSpaceAiGenerationRunRequest request,
        string expectedVersionRowVersion,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireInternalTenant();
        ValidateRequest(versionId, request);
        var mode = NormalizeMode(request.Mode, request.BasedOnRunId.HasValue);
        var expectedVersion = NormalizeRowVersion(expectedVersionRowVersion);

        if (request.BasedOnRunId.HasValue)
        {
            return await RecoverAsync(
                versionId,
                request,
                expectedVersion,
                mode,
                idempotencyKey,
                cancellationToken);
        }

        return await CreateInitialAsync(
            versionId,
            request,
            expectedVersion,
            mode,
            idempotencyKey,
            cancellationToken);
    }

    private async Task<SpaceAiGenerationRunAcceptedDto> CreateInitialAsync(
        Guid versionId,
        CreateSpaceAiGenerationRunRequest request,
        string expectedVersionRowVersion,
        string mode,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var normalized = request with
        {
            Mode = mode,
            ExpectedBasedOnRunRowVersion = null,
        };
        var requestHash = Hash(JsonSerializer.Serialize(
            new { versionId, expectedVersionRowVersion, request = normalized },
            JsonOptions));
        var keyHash = IdempotencyKeyHash(idempotencyKey);
        var replay = await ReadReplayAsync(
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay;

        await using var transaction = await BeginTransactionAsync(
            cancellationToken);
        try
        {
            var concurrentReplay = await ReadReplayAsync(
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                await CommitAsync(transaction, cancellationToken);
                return concurrentReplay;
            }

            var target = await LoadTargetAsync(
                versionId,
                request,
                expectedVersionRowVersion,
                cancellationToken);
            if (!string.Equals(
                    mode,
                    SpaceAiGenerationRunContract.RuleOnlyMode,
                    StringComparison.Ordinal))
            {
                await RejectUnavailableAiAssistedAsync(
                    target.SiteId,
                    cancellationToken);
            }

            var businessKeyHash = Hash(string.Join(
                "\n",
                versionId.ToString("N"),
                target.Source.Id.ToString("N"),
                target.Source.Sha256,
                target.Version.ContentRevision.ToString(
                    System.Globalization.CultureInfo.InvariantCulture),
                target.TargetFloorLogicalId.ToString("N"),
                target.MappingProfileVersionId?.ToString("N") ?? string.Empty,
                target.Source.MappingProfileVersion?.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) ??
                    string.Empty,
                target.RackGenerationProfileVersionId?.ToString("N") ??
                    string.Empty,
                target.Preview.ArtifactId.ToString("N"),
                target.Preview.FileSha256,
                mode,
                SpaceAiGenerationRunContract.RuleVersion,
                WarehouseGenerationInput.CurrentSchemaVersion));
            var active = await context.GenerationRuns.SingleOrDefaultAsync(
                item => item.IsCurrent &&
                        item.BusinessKeyHash == businessKeyHash,
                cancellationToken);
            if (active is not null)
            {
                var activeJob = await context.Jobs.AsNoTracking().SingleAsync(
                    item => item.Id == active.JobId,
                    cancellationToken);
                var reused = Response(
                    active,
                    activeJob,
                    mode,
                    reused: true,
                    idempotentReplay: false);
                AddIdempotency(keyHash, requestHash, reused, UtcNow());
                await context.SaveChangesAsync(cancellationToken);
                await CommitAsync(transaction, cancellationToken);
                return reused;
            }

            var now = UtcNow();
            var runId = Guid.NewGuid();
            var jobInputHash = Hash(string.Join(
                "\n",
                businessKeyHash,
                target.Preview.ArtifactId.ToString("N"),
                target.Preview.FileSha256));
            var job = SpaceJob.CreateQueued(
                execution.TenantId,
                SpaceJobType.BuildScene,
                SpaceJobSubjectType.ModelVersion,
                versionId,
                Hash($"{businessKeyHash}\n{runId:N}"),
                jobInputHash,
                priority: 70,
                maxAttempts: 5,
                execution.ActorId,
                now,
                CorrelationId(),
                JsonSerializer.Serialize(
                    new
                    {
                        schemaVersion = SpaceAiGenerationRunContract.SchemaVersion,
                        runId,
                        basedOnRunId = (Guid?)null,
                        sourceId = target.Source.Id,
                        expectedContentRevision =
                            target.Version.ContentRevision,
                        mode,
                        previewArtifactId = target.Preview.ArtifactId,
                        previewArtifactSha256 = target.Preview.FileSha256,
                    },
                    JsonOptions));
            var run = SpaceGenerationRun.Create(
                new SpaceGenerationRunDefinition(
                    execution.TenantId,
                    target.SiteId,
                    versionId,
                    target.Source.Id,
                    target.Source.Sha256,
                    target.Version.ContentRevision,
                    keyHash,
                    businessKeyHash,
                    BasedOnRunId: null,
                    target.MappingProfileVersionId,
                    target.RackGenerationProfileVersionId,
                    SpaceAiGenerationRunContract.RuleVersion,
                    SpaceAiPolicySnapshot.Disabled,
                    ProviderConfigVersionId: null,
                    WarehouseGenerationInput.CurrentSchemaVersion,
                    job.Id,
                    target.TargetFloorLogicalId,
                    runId));
            context.Jobs.Add(job);
            context.GenerationRuns.Add(run);
            await context.SaveChangesAsync(cancellationToken);

            var response = Response(
                run,
                job,
                mode,
                reused: false,
                idempotentReplay: false);
            AddIdempotency(keyHash, requestHash, response, now);
            await context.SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return response;
        }
        catch
        {
            await RollbackAsync(transaction);
            throw;
        }
    }

    private async Task<SpaceAiGenerationRunAcceptedDto> RecoverAsync(
        Guid versionId,
        CreateSpaceAiGenerationRunRequest request,
        string expectedVersionRowVersion,
        string mode,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ExpectedBasedOnRunRowVersion))
        {
            throw Problem(
                SpaceErrorCodes.AiReviewConflict,
                400,
                "ExpectedBasedOnRunRowVersion is required for recovery.",
                "refresh-generation-run");
        }

        var normalized = request with { Mode = mode };
        var requestHash = Hash(JsonSerializer.Serialize(
            new { versionId, expectedVersionRowVersion, request = normalized },
            JsonOptions));
        var keyHash = IdempotencyKeyHash(idempotencyKey);
        var replay = await ReadReplayAsync(
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay;

        var target = await LoadTargetAsync(
            versionId,
            request,
            expectedVersionRowVersion,
            cancellationToken);
        var sourceRun = await context.GenerationRuns.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.BasedOnRunId!.Value,
                cancellationToken) ?? throw Problem(
                SpaceErrorCodes.AiRunNotFound,
                404,
                "The based-on generation run was not found.",
                "refresh-generation-runs");
        if (sourceRun.ModelVersionId != versionId ||
            sourceRun.SiteId != target.SiteId ||
            sourceRun.SourceId != request.SourceId ||
            (request.MappingProfileVersionId.HasValue &&
             sourceRun.MappingProfileVersionId !=
             request.MappingProfileVersionId) ||
            (request.RackGenerationProfileVersionId.HasValue &&
             sourceRun.RackGenerationProfileVersionId !=
             request.RackGenerationProfileVersionId))
        {
            throw Problem(
                SpaceErrorCodes.AiRunStateInvalid,
                409,
                "The recovery request does not match the frozen source run.",
                "refresh-generation-run");
        }

        var recoveryMode = string.Equals(
            mode,
            SpaceAiGenerationRunContract.RuleOnlyMode,
            StringComparison.Ordinal)
            ? SpaceAiRunRecoveryContract.RuleOnlyMode
            : SpaceAiRunRecoveryContract.SamePolicyMode;
        var recovered = await recovery.RecoverAsync(
            versionId,
            new CreateSpaceAiGenerationRecoveryRequest(
                sourceRun.Id,
                request.ExpectedContentRevision,
                request.ExpectedBasedOnRunRowVersion!,
                recoveryMode),
            $"generation-create-{keyHash}",
            cancellationToken);
        if (!recovered.ReplacementRunId.HasValue)
        {
            throw new InvalidOperationException(
                "Generation recovery did not return a replacement run.");
        }
        var replacement = await context.GenerationRuns.AsNoTracking()
            .SingleAsync(
                item => item.Id == recovered.ReplacementRunId.Value,
                cancellationToken);
        var job = await context.Jobs.AsNoTracking().SingleAsync(
            item => item.Id == replacement.JobId,
            cancellationToken);
        var response = Response(
            replacement,
            job,
            mode,
            reused: recovered.IdempotentReplay,
            idempotentReplay: recovered.IdempotentReplay);
        var concurrentReplay = await ReadReplayAsync(
            keyHash,
            requestHash,
            cancellationToken);
        if (concurrentReplay is not null)
            return concurrentReplay;
        AddIdempotency(keyHash, requestHash, response, UtcNow());
        await context.SaveChangesAsync(cancellationToken);
        return response;
    }

    private async Task<CreationTarget> LoadTargetAsync(
        Guid versionId,
        CreateSpaceAiGenerationRunRequest request,
        string expectedVersionRowVersion,
        CancellationToken cancellationToken)
    {
        var version = await context.Versions.SingleOrDefaultAsync(
            item => item.Id == versionId,
            cancellationToken) ?? throw Problem(
            SpaceErrorCodes.VersionNotFound,
            404,
            "The Draft version was not found.",
            "refresh-draft");
        var model = await context.Models.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == version.ModelId,
            cancellationToken) ?? throw Problem(
            SpaceErrorCodes.ModelNotFound,
            404,
            "The model was not found.",
            "refresh-model");
        access.EnsureSiteAccess(model.SiteId, write: true);
        EnsureExpectedVersion(version, expectedVersionRowVersion);
        if (version.Status != SpaceVersionStatus.Draft ||
            version.ContentRevision != request.ExpectedContentRevision)
        {
            throw Problem(
                SpaceErrorCodes.AiRunStale,
                409,
                "The generation target is not the expected current Draft.",
                "refresh-draft-and-create-generation-run");
        }

        var source = await context.Sources.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.SourceId &&
                        item.ModelVersionId == versionId,
                cancellationToken) ?? throw Problem(
                SpaceErrorCodes.SourceNotFound,
                404,
                "The CAD source was not found for this Draft.",
                "select-confirmed-cad-source");
        var sourceFile = source.FileId.HasValue
            ? await context.Files.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == source.FileId.Value,
                cancellationToken)
            : null;
        if (source.SourceType is not (SpaceSourceType.Dwg or SpaceSourceType.Dxf) ||
            source.State is not (SpaceSourceState.PreviewReady or SpaceSourceState.Imported) ||
            sourceFile is null ||
            sourceFile.State != SpaceFileState.Clean ||
            sourceFile.IsDeleted ||
            sourceFile.RetentionClass != SpaceFileRetentionClass.Source ||
            !string.Equals(
                sourceFile.Sha256,
                source.Sha256,
                StringComparison.Ordinal))
        {
            throw Problem(
                SpaceErrorCodes.AiSourcePolicyDenied,
                422,
                "Generation requires a clean, confirmed DWG/DXF Preview source.",
                "complete-cad-preview-confirmation");
        }

        var mappingProfileVersionId = request.MappingProfileVersionId ??
                                      source.MappingProfileId;
        if (!source.MappingProfileId.HasValue ||
            !source.MappingProfileVersion.HasValue ||
            !mappingProfileVersionId.HasValue ||
            mappingProfileVersionId != source.MappingProfileId)
        {
            throw Problem(
                SpaceErrorCodes.AiRunStateInvalid,
                422,
                "The mapping profile does not match the confirmed CAD Preview.",
                "reconfirm-cad-mapping");
        }
        if (!request.BasedOnRunId.HasValue &&
            request.RackGenerationProfileVersionId.HasValue)
        {
            throw Problem(
                SpaceErrorCodes.RackProfileRequired,
                422,
                "A requested RackGenerationProfile cannot be pinned until its authoritative version store is available.",
                "omit-unverified-rack-profile");
        }

        var targetFloorLogicalId = ParseTargetFloor(source);
        var floorExists = await context.FloorRevisions.AsNoTracking().AnyAsync(
            item => item.ModelVersionId == versionId &&
                    item.LogicalId == targetFloorLogicalId &&
                    item.LifecycleState == SpaceLifecycleState.Active,
            cancellationToken);
        if (!floorExists)
        {
            throw Problem(
                SpaceErrorCodes.LogicalIdNotFound,
                422,
                "The confirmed CAD target floor is not active in this Draft.",
                "reconfirm-cad-target-floor");
        }

        var preview = await (
                from artifact in context.Artifacts.AsNoTracking()
                join file in context.Files.AsNoTracking()
                    on artifact.FileId equals file.Id
                join job in context.Jobs.AsNoTracking()
                    on artifact.JobId equals (Guid?)job.Id
                where artifact.ModelVersionId == versionId &&
                      artifact.SourceId == source.Id &&
                      artifact.ArtifactType == SpaceArtifactType.PreviewSet &&
                      artifact.SchemaVersion ==
                          SpaceCadPreviewSetVersions.ArtifactSchema &&
                      job.JobType == SpaceJobType.CadParse &&
                      job.SubjectType == SpaceJobSubjectType.ModelSource &&
                      job.SubjectId == source.Id &&
                      job.Status == SpaceJobStatus.Succeeded &&
                      file.State == SpaceFileState.Clean &&
                      !file.IsDeleted &&
                      file.RetentionClass == SpaceFileRetentionClass.Artifact &&
                      file.Sha256 != null
                orderby job.RequestedAtUtc descending, artifact.Id descending
                select new PreviewPin(artifact.Id, file.Sha256!))
            .FirstOrDefaultAsync(cancellationToken) ?? throw Problem(
            SpaceErrorCodes.CadParseArtifactInvalid,
            422,
            "No authoritative CAD PreviewSet is available for generation.",
            "complete-cad-parse");

        return new CreationTarget(
            version,
            source,
            model.SiteId,
            targetFloorLogicalId,
            mappingProfileVersionId,
            RackGenerationProfileVersionId: null,
            preview);
    }

    private async Task RejectUnavailableAiAssistedAsync(
        Guid siteId,
        CancellationToken cancellationToken)
    {
        var policy = await policySource.GetPolicyAsync(
            execution.TenantId,
            cancellationToken);
        if (policy.TenantId != execution.TenantId)
        {
            throw new InvalidOperationException(
                "SPACE_AI_POLICY_TENANT_SCOPE_MISMATCH");
        }
        if (!policy.IsEnabled)
        {
            throw Problem(
                SpaceErrorCodes.AiDisabled,
                403,
                "AI warehouse generation is disabled for this tenant.",
                "use-rule-only-generation");
        }
        if (!policy.AllowsSite(siteId))
        {
            throw Problem(
                SpaceErrorCodes.AiSourcePolicyDenied,
                403,
                "The tenant AI policy does not allow this site.",
                "review-ai-tenant-policy");
        }
        throw Problem(
            SpaceErrorCodes.AiProviderUnavailable,
            503,
            "Provider-backed BuildScene execution is not configured.",
            "use-rule-only-generation");
    }

    private static Guid ParseTargetFloor(SpaceModelSource source)
    {
        try
        {
            var metadata = JsonSerializer.Deserialize<SpaceCadCoordinateMetadataV1>(
                               source.TransformJson!,
                               JsonOptions) ?? throw new JsonException();
            if (metadata.SchemaVersion != SpaceCadCoordinateVersions.SchemaVersion ||
                metadata.TargetFloor.FloorLogicalId == Guid.Empty ||
                !metadata.SourceSha256.Equals(
                    source.Sha256,
                    StringComparison.Ordinal) ||
                metadata.TargetFloor.CoordinateSystem !=
                    SpaceCadCoordinateVersions.TargetCoordinateSystem)
            {
                throw new JsonException();
            }
            return metadata.TargetFloor.FloorLogicalId;
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentNullException)
        {
            throw Problem(
                SpaceErrorCodes.AiSourcePolicyDenied,
                422,
                "The confirmed CAD coordinate metadata is invalid.",
                "reconfirm-cad-coordinate-metadata");
        }
    }

    private static void ValidateRequest(
        Guid versionId,
        CreateSpaceAiGenerationRunRequest request)
    {
        if (versionId == Guid.Empty ||
            request.SourceId == Guid.Empty ||
            request.ExpectedContentRevision < 0 ||
            request.MappingProfileVersionId == Guid.Empty ||
            request.RackGenerationProfileVersionId == Guid.Empty ||
            request.BasedOnRunId == Guid.Empty)
        {
            throw Problem(
                SpaceErrorCodes.RequestInvalid,
                400,
                "A valid version, source and non-negative ContentRevision are required.",
                "correct-generation-request");
        }
    }

    private static string NormalizeMode(string mode, bool recovery)
    {
        var normalized = mode?.Trim();
        if (normalized == SpaceAiGenerationRunContract.RuleOnlyMode ||
            normalized == SpaceAiGenerationRunContract.AiAssistedMode ||
            (recovery && normalized == SpaceAiRunRecoveryContract.SamePolicyMode))
        {
            return normalized;
        }
        throw Problem(
            SpaceErrorCodes.AiRunStateInvalid,
            400,
            recovery
                ? "Recovery mode must be AiAssisted, SamePolicy, or RuleOnly."
                : "Generation mode must be AiAssisted or RuleOnly.",
            "select-generation-mode");
    }

    private static string NormalizeRowVersion(string value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[2..].Trim();
        if (normalized.Length >= 2 &&
            normalized[0] == '"' &&
            normalized[^1] == '"')
        {
            normalized = normalized[1..^1];
        }
        if (normalized == "*")
        {
            throw Problem(
                SpaceErrorCodes.AiReviewConflict,
                400,
                "If-Match must contain the expected Draft RowVersion.",
                "supply-version-row-version");
        }
        return normalized;
    }

    private static void EnsureExpectedVersion(
        SpaceModelVersion version,
        string expected)
    {
        if (!SpaceAiAtomicApplyService.FixedEquals(
                expected,
                Convert.ToBase64String(version.RowVersion)))
        {
            throw Problem(
                SpaceErrorCodes.AiReviewConflict,
                409,
                "The Draft version changed concurrently.",
                "refresh-draft");
        }
    }

    private async Task<SpaceAiGenerationRunAcceptedDto?> ReadReplayAsync(
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await context.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.PrincipalId == execution.ActorId &&
                        item.Operation == CreateOperation &&
                        item.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (record is null)
            return null;
        if (!SpaceAiAtomicApplyService.FixedEquals(
                record.RequestHash,
                requestHash) ||
            record.ReplayUntilUtc < UtcNow())
        {
            throw Problem(
                SpaceErrorCodes.IdempotencyConflict,
                409,
                "The Idempotency-Key was reused with different or expired input.",
                "use-new-idempotency-key");
        }
        return (JsonSerializer.Deserialize<SpaceAiGenerationRunAcceptedDto>(
                    record.ResponseJson,
                    JsonOptions) ?? throw new InvalidOperationException(
                    "The stored generation response is invalid.")) with
        { IdempotentReplay = true };
    }

    private void AddIdempotency(
        string keyHash,
        string requestHash,
        SpaceAiGenerationRunAcceptedDto response,
        DateTime now)
    {
        context.IdempotencyRecords.Add(SpaceIdempotencyRecord.Create(
            execution.TenantId,
            execution.ActorId,
            CreateOperation,
            keyHash,
            requestHash,
            JsonSerializer.Serialize(response, JsonOptions),
            202,
            now.AddHours(24),
            now.AddDays(90)));
    }

    private static SpaceAiGenerationRunAcceptedDto Response(
        SpaceGenerationRun run,
        SpaceJob job,
        string mode,
        bool reused,
        bool idempotentReplay)
    {
        var self = $"/api/space/design/v1/generation-runs/{run.Id}";
        return new SpaceAiGenerationRunAcceptedDto(
            SpaceAiGenerationRunContract.SchemaVersion,
            run.Id,
            job.Id,
            run.Status.ToString(),
            run.BaseContentRevision,
            run.SourceId,
            run.SourceHash,
            mode,
            run.PolicySnapshot.ToString(),
            run.BasedOnRunId,
            new SpaceAiGenerationRunLinksDto(
                self,
                $"{self}/proposals"),
            reused,
            idempotentReplay);
    }

    private string IdempotencyKeyHash(string key)
    {
        var normalized = key?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) ||
            Encoding.UTF8.GetByteCount(normalized) > 128 ||
            normalized.Any(char.IsControl))
        {
            throw Problem(
                SpaceErrorCodes.IdempotencyKeyRequired,
                400,
                "A valid Idempotency-Key is required.",
                "supply-idempotency-key");
        }
        return Hash(
            $"{execution.TenantId:D}\n{CreateOperation}\n{normalized}");
    }

    private void RequireInternalTenant()
    {
        if (execution.IsExternal)
        {
            throw Problem(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot create generation runs.",
                "use-internal-space-editor");
        }
        if (execution.TenantId == Guid.Empty ||
            execution.ActorId == Guid.Empty ||
            context.CurrentTenantId != execution.TenantId)
        {
            throw new SpaceTenantScopeException(
                "A verified internal Space tenant context is required.");
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken) =>
        context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;

    private static Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken) =>
        transaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

    private static async Task RollbackAsync(
        IDbContextTransaction? transaction)
    {
        if (transaction is not null)
            await transaction.RollbackAsync(CancellationToken.None);
    }

    private DateTime UtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private Guid CorrelationId() =>
        execution is ISpaceCorrelationContext correlation &&
        correlation.CorrelationId != Guid.Empty
            ? correlation.CorrelationId
            : Guid.NewGuid();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static SpaceProblemException Problem(
        string code,
        int status,
        string title,
        string recoveryAction) =>
        new(code, status, title, recoveryAction: recoveryAction);

    private sealed record PreviewPin(
        Guid ArtifactId,
        string FileSha256);

    private sealed record CreationTarget(
        SpaceModelVersion Version,
        SpaceModelSource Source,
        Guid SiteId,
        Guid TargetFloorLogicalId,
        Guid? MappingProfileVersionId,
        Guid? RackGenerationProfileVersionId,
        PreviewPin Preview);
}
