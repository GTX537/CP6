using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class SpaceCadParseService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access,
    SpaceFileUploadService uploads,
    SpaceSourceCoordinator sources,
    ISpaceClock clock,
    ISpaceFileStore? files = null,
    ISpaceDesignV1Service? design = null) : ISpaceCadParseService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<UploadSpaceCadSourceResponse> UploadAsync(
        Guid versionId,
        SpaceCadSourceFormat sourceFormat,
        string originalName,
        string? declaredContentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        EnsureExecutionContext();
        var (version, _) = await LoadWritableVersionAsync(versionId, cancellationToken);
        var sourceType = sourceFormat == SpaceCadSourceFormat.Dwg
            ? SpaceSourceType.Dwg
            : SpaceSourceType.Dxf;
        var upload = await uploads.UploadAsync(
            new SpaceFileUploadRequest(
                sourceType,
                originalName,
                declaredContentType,
                SpaceFileRetentionClass.Source),
            content,
            cancellationToken);
        var existing = await context.Sources.SingleOrDefaultAsync(
            item =>
                item.ModelVersionId == versionId &&
                item.SourceType == sourceType &&
                item.Sha256 == upload.File.Sha256,
            cancellationToken);
        SpaceModelSource source;
        if (existing is not null)
        {
            source = existing;
            if (source.State == SpaceSourceState.Rejected)
                throw Conflict("The same CAD source was previously rejected.");
        }
        else
        {
            source = upload.File.State == SpaceFileState.Clean
                ? sources.AddFileSource(
                    version,
                    upload.File,
                    sourceType,
                    upload.File.OriginalName)
                : sources.AddPendingFileSource(
                    version,
                    upload.File,
                    sourceType,
                    upload.File.OriginalName);
            context.Sources.Add(source);
            await context.SaveChangesAsync(cancellationToken);
        }

        await SynchronizeTerminalFileStateAsync(
            source,
            upload.File.Id,
            cancellationToken);
        var currentFile = await context.Files.AsNoTracking().SingleAsync(
            item => item.Id == upload.File.Id,
            cancellationToken);
        var scanJobId = upload.ScanJobId ??
            await FindScanJobIdAsync(upload.File.Id, cancellationToken);
        return new UploadSpaceCadSourceResponse(
            ToDto(currentFile),
            ToDto(source),
            scanJobId,
            scanJobId.HasValue ? $"/api/space/design/v1/jobs/{scanJobId:D}" : null,
            upload.Reused || existing is not null);
    }

    public async Task<StartSpaceCadParseResponse> StartAsync(
        Guid versionId,
        Guid sourceId,
        StartSpaceCadParseRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        ValidateRequest(request);
        var operation = $"cad-parse:{sourceId:N}";
        var keyHash = IdempotencyKeyHash(operation, idempotencyKey);
        var requestHash = Hash(JsonSerializer.Serialize(request, JsonOptions));
        var replay = await ReadReplayAsync<StartSpaceCadParseResponse>(
            operation,
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay with { IdempotentReplay = true };

        IDbContextTransaction? transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        try
        {
            await AcquireCadParseLockAsync(sourceId, cancellationToken);
            context.ChangeTracker.Clear();
            var concurrentReplay = await ReadReplayAsync<StartSpaceCadParseResponse>(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return concurrentReplay with { IdempotentReplay = true };
            }

            var (version, _) = await LoadWritableVersionAsync(
                versionId,
                cancellationToken);
            var source = await context.Sources.SingleOrDefaultAsync(
                item => item.Id == sourceId && item.ModelVersionId == versionId,
                cancellationToken) ?? throw NotFound();
            if (source.SourceType is not (SpaceSourceType.Dwg or SpaceSourceType.Dxf) ||
                source.FileId is null ||
                source.State is not (SpaceSourceState.Ready or SpaceSourceState.PreviewReady))
            {
                throw Conflict("The selected source is not a ready file-backed CAD source.");
            }
            var file = await context.Files.AsNoTracking().SingleOrDefaultAsync(
                item => item.Id == source.FileId,
                cancellationToken) ?? throw NotFound();
            if (file.State != SpaceFileState.Clean || file.IsDeleted ||
                !string.Equals(file.Sha256, source.Sha256, StringComparison.Ordinal))
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.SourceUnsafe,
                    409,
                    "The CAD source is not ready for parsing.",
                    "Wait for a clean scan result and retry.",
                    "wait-for-source-ready",
                    retryable: true);
            }
            ValidateCoordinateMetadata(request, source.Sha256);
            var preparation = await context.CadParsePreparations
                .SingleOrDefaultAsync(
                    item => item.Id == request.PreparationId &&
                            item.ModelVersionId == versionId &&
                            item.SourceId == sourceId,
                    cancellationToken) ?? throw new SpaceProblemException(
                        SpaceErrorCodes.CadPreparationNotFound,
                        404,
                        "The confirmed CAD preparation was not found.",
                        recoveryAction: "reopen-cad-wizard");
            ValidatePreparation(
                preparation,
                version,
                source,
                request,
                RequireUtcNow());
            var payload = new SpaceCadParseJobPayload(
                SpaceCadParsePayloadVersions.Current,
                versionId,
                sourceId,
                file.Id,
                source.Sha256,
                source.SourceType == SpaceSourceType.Dwg
                    ? SpaceCadSourceFormat.Dwg
                    : SpaceCadSourceFormat.Dxf,
                request.FloorLogicalId,
                request.ConfirmedUnit,
                request.ConfirmedScaleToMillimeters,
                request.CoordinateMetadataJson,
                NormalizeHash(request.CoordinateTransformSha256),
                request.MappingProfileId,
                request.MappingProfileVersion,
                NormalizeHash(request.MappingDefinitionSha256),
                NormalizeHash(request.MappingPreviewSha256),
                version.ContentRevision,
                version.ContentHash,
                preparation.ProviderKey,
                preparation.SemanticPreviewSha256,
                preparation.MappingReplaySnapshotJson,
                preparation.ProviderVersion);
            var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
            var inputHash = Hash(payloadJson);
            var enqueue = Enqueue(payload, inputHash, payloadJson);
            var businessKey = SpaceJobBusinessKey.Create(enqueue);
            var active = await context.Jobs.SingleOrDefaultAsync(
                job =>
                    job.JobType == SpaceJobType.CadParse &&
                    job.SubjectType == SpaceJobSubjectType.ModelSource &&
                    job.SubjectId == sourceId &&
                    (job.Status == SpaceJobStatus.Queued ||
                     job.Status == SpaceJobStatus.Running),
                cancellationToken);
            if (active is not null)
            {
                if (!active.BusinessKey.Equals(businessKey, StringComparison.Ordinal))
                    throw Conflict("A different CAD parse is already active for this source.");
                var activeResponse = Response(active, source, replay: true);
                AddIdempotency(
                    operation,
                    keyHash,
                    requestHash,
                    activeResponse,
                    RequireUtcNow());
                await context.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return activeResponse;
            }

            source.ConfigureImport(
                SpaceCadParseJobProcessor.Version,
                request.MappingProfileId,
                request.MappingProfileVersion,
                request.ConfirmedUnit.ToString(),
                request.ConfirmedScaleToMillimeters,
                request.CoordinateMetadataJson);
            var now = RequireUtcNow();
            var job = SpaceJob.CreateQueued(
                execution.TenantId,
                enqueue.JobType,
                enqueue.SubjectType,
                enqueue.SubjectId,
                businessKey,
                enqueue.InputHash,
                enqueue.Priority,
                enqueue.MaxAttempts,
                execution.ActorId,
                now,
                CorrelationId(),
                enqueue.PayloadJson);
            context.Jobs.Add(job);
            var response = Response(job, source, replay: false);
            AddIdempotency(operation, keyHash, requestHash, response, now);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            var concurrentReplay = await ReadReplayAsync<StartSpaceCadParseResponse>(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay with { IdempotentReplay = true };
            throw Conflict("The CAD source changed while parsing was being started.");
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

    public async Task<SpaceCadParseDto> GetAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        var input = await LoadReadableAsync(
            versionId,
            sourceId,
            jobId,
            tracked: false,
            cancellationToken);
        var artifacts = await (
                from artifact in context.Artifacts.AsNoTracking()
                join file in context.Files.AsNoTracking()
                    on artifact.FileId equals file.Id
                where artifact.JobId == jobId ||
                      (input.Job.RetryOfJobId.HasValue &&
                       artifact.JobId == input.Job.RetryOfJobId.Value)
                orderby artifact.ArtifactType
                select new SpaceCadParseArtifactDto(
                    artifact.Id,
                    file.Id,
                    artifact.ArtifactType.ToString(),
                    artifact.SchemaVersion,
                    file.Sha256!,
                    file.SizeBytes))
            .ToArrayAsync(cancellationToken);
        return new SpaceCadParseDto(
            input.Job.Id,
            input.Source.ModelVersionId,
            input.Source.Id,
            input.Job.Status.ToString(),
            input.Source.State.ToString(),
            SpaceCadParseJobProcessor.Version,
            input.Payload.FloorLogicalId,
            input.Payload.CoordinateTransformSha256,
            input.Payload.MappingProfileId,
            input.Payload.MappingProfileVersion,
            input.Payload.MappingDefinitionSha256,
            input.Payload.MappingPreviewSha256,
            input.Job.RetryOfJobId,
            input.Job.CancellationRequestedAtUtc.HasValue,
            input.Job.LastErrorCode,
            input.Job.LastErrorSummary,
            artifacts);
    }

    public async Task<SpaceCadReviewCandidateListDto> ListReviewCandidatesAsync(
        Guid versionId,
        Guid floorLogicalId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        if (versionId == Guid.Empty || floorLogicalId == Guid.Empty)
            throw NotFound();
        if (limit is < 1 or > 100)
            throw Invalid("The candidate limit must be between 1 and 100.");

        var scope = await (
                from version in context.Versions.AsNoTracking()
                join model in context.Models.AsNoTracking()
                    on version.ModelId equals model.Id
                where version.Id == versionId
                select new { Version = version, Model = model })
            .SingleOrDefaultAsync(cancellationToken);
        if (scope is null)
            throw NotFound();
        EnsureReadable(scope.Model);

        var floorExists = await context.FloorRevisions.AsNoTracking().AnyAsync(
            floor =>
                floor.ModelVersionId == versionId &&
                floor.LogicalId == floorLogicalId &&
                floor.LifecycleState == SpaceLifecycleState.Active,
            cancellationToken);
        if (!floorExists)
            throw NotFound();

        var query =
            from job in context.Jobs.AsNoTracking()
            join source in context.Sources.AsNoTracking()
                on job.SubjectId equals source.Id
            where source.ModelVersionId == versionId &&
                  (source.SourceType == SpaceSourceType.Dwg ||
                   source.SourceType == SpaceSourceType.Dxf) &&
                  job.JobType == SpaceJobType.CadParse &&
                  job.SubjectType == SpaceJobSubjectType.ModelSource &&
                  job.Status == SpaceJobStatus.Succeeded
            orderby job.FinishedAtUtc descending, job.RequestedAtUtc descending
            select new
            {
                SourceId = source.Id,
                source.DisplayName,
                source.SourceType,
                source.Sha256,
                SourceState = source.State,
                JobId = job.Id,
                JobStatus = job.Status,
                job.RequestedAtUtc,
                job.FinishedAtUtc,
                job.PayloadJson,
                HasPreviewSet = context.Artifacts.Any(
                    artifact =>
                        artifact.ArtifactType == SpaceArtifactType.PreviewSet &&
                        (artifact.JobId == job.Id ||
                         (job.RetryOfJobId.HasValue &&
                          artifact.JobId == job.RetryOfJobId.Value))),
            };

        var candidates = new List<SpaceCadReviewCandidateDto>(limit + 1);
        await foreach (var row in query.AsAsyncEnumerable()
                           .WithCancellation(cancellationToken))
        {
            if (!TryDeserializeCandidatePayload(row.PayloadJson, out var payload) ||
                payload.ModelVersionId != versionId ||
                payload.SourceId != row.SourceId ||
                payload.FloorLogicalId != floorLogicalId ||
                !SourceFormatMatches(payload.SourceFormat, row.SourceType))
            {
                continue;
            }

            var isCurrentRevision =
                payload.BaseContentRevision == scope.Version.ContentRevision &&
                string.Equals(
                    payload.BaseContentHash,
                    scope.Version.ContentHash,
                    StringComparison.Ordinal);
            var canLoadReview =
                isCurrentRevision &&
                row.SourceState == SpaceSourceState.PreviewReady &&
                row.HasPreviewSet;

            candidates.Add(new SpaceCadReviewCandidateDto(
                row.SourceId,
                row.DisplayName,
                row.SourceType.ToString(),
                row.Sha256,
                row.JobId,
                row.JobStatus.ToString(),
                row.SourceState.ToString(),
                payload.FloorLogicalId,
                payload.BaseContentRevision,
                payload.BaseContentHash,
                isCurrentRevision,
                canLoadReview,
                row.RequestedAtUtc,
                row.FinishedAtUtc,
                payload.PreferredProviderKey,
                payload.PreferredProviderVersion,
                payload.MappingProfileId,
                payload.MappingProfileVersion));
            if (candidates.Count > limit)
                break;
        }

        return new SpaceCadReviewCandidateListDto(
            versionId,
            floorLogicalId,
            scope.Version.ContentRevision,
            scope.Version.ContentHash,
            candidates.Count > limit,
            candidates.Take(limit).ToArray());
    }

    public async Task<SpaceCadReviewWorkspaceV1> GetReviewWorkspaceAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        if (files is null)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.JobProcessorUnavailable,
                503,
                "Private Space artifact storage is not configured.",
                recoveryAction: "configure-space-artifact-storage",
                retryable: true);
        }

        var input = await LoadReadableAsync(
            versionId,
            sourceId,
            jobId,
            tracked: false,
            cancellationToken);
        if (input.Job.Status != SpaceJobStatus.Succeeded ||
            input.Source.State != SpaceSourceState.PreviewReady)
        {
            throw Conflict(
                "The CAD parse has not produced a reviewable PreviewSet yet.");
        }

        var artifactInput = await (
                from artifact in context.Artifacts.AsNoTracking()
                join file in context.Files.AsNoTracking()
                    on artifact.FileId equals file.Id
                where artifact.ArtifactType == SpaceArtifactType.PreviewSet &&
                      (artifact.JobId == jobId ||
                       (input.Job.RetryOfJobId.HasValue &&
                        artifact.JobId == input.Job.RetryOfJobId.Value))
                select new { Artifact = artifact, File = file })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound();
        if (artifactInput.File.State != SpaceFileState.Clean ||
            artifactInput.File.IsDeleted ||
            artifactInput.File.SizeBytes is < 1 or > 100L * 1024L * 1024L ||
            string.IsNullOrWhiteSpace(artifactInput.File.Sha256))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.SourceUnsafe,
                409,
                "The CAD PreviewSet artifact is not readable.",
                recoveryAction: "start-new-cad-parse");
        }

        string json;
        await using (var stream = await files.OpenQuarantinedReadAsync(
                         artifactInput.File.TenantId,
                         artifactInput.File.Id,
                         artifactInput.File.StorageKey,
                         cancellationToken))
        using (var reader = new StreamReader(
                   stream,
                   Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: false,
                   leaveOpen: false))
        {
            json = await reader.ReadToEndAsync(cancellationToken);
        }
        if (!Hash(json).Equals(artifactInput.File.Sha256, StringComparison.Ordinal))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.SourceUnsafe,
                409,
                "The CAD PreviewSet artifact failed integrity verification.",
                recoveryAction: "start-new-cad-parse");
        }
        SpaceCadPreviewSetV2 previewSet;
        try
        {
            previewSet = SpaceCadPreviewSet.Deserialize(json);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException or ArgumentException)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.SourceUnsafe,
                409,
                "The CAD PreviewSet artifact failed schema verification.",
                recoveryAction: "start-new-cad-parse");
        }
        var artifactJobMatches = previewSet.CadParseJobId == jobId ||
            previewSet.CadParseJobId == input.Job.RetryOfJobId;
        if (previewSet.TenantId != execution.TenantId ||
            previewSet.ModelVersionId != versionId ||
            previewSet.SourceId != sourceId ||
            !artifactJobMatches ||
            previewSet.FloorLogicalId != input.Payload.FloorLogicalId ||
            !previewSet.SourceSha256.Equals(
                input.Payload.SourceSha256,
                StringComparison.Ordinal) ||
            !previewSet.CoordinateTransformSha256.Equals(
                input.Payload.CoordinateTransformSha256,
                StringComparison.Ordinal) ||
            !previewSet.MappingPreviewSha256.Equals(
                input.Payload.MappingPreviewSha256,
                StringComparison.Ordinal) ||
            input.Payload.ExpectedSemanticPreviewSha256 is not null &&
            !previewSet.SemanticPreview.SemanticPreviewSha256.Equals(
                input.Payload.ExpectedSemanticPreviewSha256,
                StringComparison.Ordinal) ||
            previewSet.BaseContentRevision != input.Payload.BaseContentRevision ||
            !string.Equals(
                previewSet.BaseContentHash,
                input.Payload.BaseContentHash,
                StringComparison.Ordinal))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CadParseArtifactInvalid,
                409,
                "The CAD PreviewSet no longer matches the selected parse chain.",
                recoveryAction: "start-new-cad-parse");
        }

        var version = await context.Versions.AsNoTracking()
            .SingleAsync(item => item.Id == versionId, cancellationToken);
        if (version.ContentRevision != input.Payload.BaseContentRevision ||
            !string.Equals(
                version.ContentHash,
                input.Payload.BaseContentHash,
                StringComparison.Ordinal))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ParseChangesetStale,
                409,
                "The CAD review changeset was produced from an older Draft revision.",
                recoveryAction: "start-new-cad-parse");
        }
        var floor = await context.FloorRevisions.AsNoTracking()
            .SingleOrDefaultAsync(item =>
                item.ModelVersionId == versionId &&
                item.LogicalId == input.Payload.FloorLogicalId,
                cancellationToken)
            ?? throw NotFound();
        var zones = await context.ZoneRevisions.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == versionId &&
                item.FloorLogicalId == floor.LogicalId)
            .ToDictionaryAsync(item => item.LogicalId, item => item.ZoneCode, cancellationToken);
        var rackRows = await context.RackRevisions.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == versionId &&
                item.FloorLogicalId == floor.LogicalId)
            .ToArrayAsync(cancellationToken);
        var currentElements = await context.ElementRevisions.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == versionId &&
                item.FloorLogicalId == floor.LogicalId &&
                item.SourceId == sourceId)
            .ToArrayAsync(cancellationToken);
        var racks = rackRows.Select(item =>
        {
            if (!zones.TryGetValue(item.ZoneLogicalId, out var zoneCode))
                throw new InvalidDataException("An editor rack has no authoritative zone.");
            return new SpaceExcelEditorRackSnapshotV1(
                item.LogicalId,
                item.Id,
                item.RackCode,
                item.SourceRef,
                floor.FloorCode,
                zoneCode,
                item.X,
                item.Y,
                item.Z,
                item.Width,
                item.Depth,
                item.Height,
                item.RotationZ,
                item.LifecycleState.ToString());
        }).ToArray();
        var snapshot = SpaceExcelCadMatching.SealEditorSnapshot(
            execution.TenantId,
            versionId,
            floor.LogicalId,
            floor.FloorCode,
            input.Payload.BaseContentRevision,
            input.Payload.BaseContentHash,
            racks);
        try
        {
            return SpaceCadReviewWorkspace.Build(
                previewSet.DiagnosticIndex,
                snapshot,
                sourceId: sourceId,
                cadParseJobId: jobId,
                semanticPreviewSha256:
                    previewSet.SemanticPreview.SemanticPreviewSha256,
                changes: BuildChanges(
                    versionId,
                    previewSet.SemanticPreview,
                    currentElements));
        }
        catch (InvalidDataException)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CadParseArtifactInvalid,
                409,
                "The CAD PreviewSet is incompatible with the current editor snapshot.",
                recoveryAction: "start-new-cad-parse");
        }
    }

    public async Task<ApplySpaceCadChangesetResponse> ApplyReviewChangesAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        ApplySpaceCadChangesetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        if (design is null)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.JobProcessorUnavailable,
                503,
                "The CAD changeset Apply service is not configured.",
                recoveryAction: "configure-space-design-service",
                retryable: true);
        }
        if (request.CommandBatchId == Guid.Empty ||
            request.ClientInstanceId == Guid.Empty ||
            request.LeaseId == Guid.Empty ||
            request.ExpectedFloorRevision < 0 ||
            request.ExpectedContentRevision < 0 ||
            !IsSha256(request.WorkspaceSha256) ||
            request.ExpectedContentHash is not null &&
                !IsSha256(request.ExpectedContentHash) ||
            request.ChangeIds is null ||
            request.ChangeIds.Count is < 1 or >
                SpaceCadReviewWorkspaceVersions.MaximumApplyChanges ||
            request.ChangeIds.Any(string.IsNullOrWhiteSpace) ||
            request.ChangeIds.Distinct(StringComparer.Ordinal).Count() !=
                request.ChangeIds.Count)
        {
            throw Invalid("CAD changeset Apply request is invalid.");
        }
        var parse = await LoadReadableAsync(
            versionId,
            sourceId,
            jobId,
            tracked: false,
            cancellationToken);
        var applyFingerprint = CadApplyFingerprint(
            versionId,
            sourceId,
            jobId,
            request);
        var replay = await ReadCadApplyReplayWithFenceAsync(
            versionId,
            parse.Payload.FloorLogicalId,
            request,
            applyFingerprint,
            cancellationToken);
        if (replay is not null)
            return replay;

        var workspace = await GetReviewWorkspaceAsync(
            versionId,
            sourceId,
            jobId,
            cancellationToken);
        if (!workspace.WorkspaceSha256.Equals(
                request.WorkspaceSha256,
                StringComparison.Ordinal) ||
            workspace.EditorContentRevision != request.ExpectedContentRevision ||
            !string.Equals(
                workspace.EditorContentHash,
                request.ExpectedContentHash,
                StringComparison.Ordinal))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ParseChangesetStale,
                409,
                "The selected CAD changeset no longer matches the current review workspace.",
                recoveryAction: "start-new-cad-parse");
        }

        var byId = (workspace.Changes ?? [])
            .ToDictionary(item => item.ChangeId, StringComparer.Ordinal);
        if (request.ChangeIds.Any(id =>
                !byId.TryGetValue(id, out var change) ||
                !change.CanApply))
        {
            throw Invalid(
                "Only selected Add, Modify or Delete CAD changes can be applied.");
        }
        var selected = request.ChangeIds.Select(id => byId[id]).ToArray();
        var previewSet = await ReadPreviewSetAsync(
            versionId,
            sourceId,
            jobId,
            cancellationToken);
        var previewByRef = previewSet.SemanticPreview.Items.ToDictionary(
            item => item.Source.SourceRef,
            StringComparer.Ordinal);
        var existing = await context.ElementRevisions.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == versionId &&
                item.FloorLogicalId == workspace.FloorLogicalId &&
                selected.Select(change => change.LogicalId)
                    .Contains(item.LogicalId))
            .ToDictionaryAsync(item => item.LogicalId, cancellationToken);
        var existingElementIds = existing.Values.Select(item => item.Id).ToArray();
        var existingAttributes = existingElementIds.Length == 0
            ? new Dictionary<Guid, IReadOnlyList<SpaceElementAttributeWriteDto>>()
            : (await context.ElementAttributes.AsNoTracking()
                .Where(item =>
                    item.ModelVersionId == versionId &&
                    existingElementIds.Contains(item.ElementRevisionId))
                .ToListAsync(cancellationToken))
                .GroupBy(item => item.ElementRevisionId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<SpaceElementAttributeWriteDto>)group
                        .Select(item => new SpaceElementAttributeWriteDto(
                            item.Namespace,
                            item.Key,
                            item.ValueType,
                            item.Value,
                            item.Unit))
                        .ToArray());
        var commands = selected.Select(change => change.Kind switch
        {
            SpaceCadChangeKind.Add => CreateCadCommand(
                CadCommandId(request.CommandBatchId, change.ChangeId),
                change,
                previewByRef[change.SourceRef],
                sourceId),
            SpaceCadChangeKind.Modify => UpdateCadCommand(
                CadCommandId(request.CommandBatchId, change.ChangeId),
                change,
                previewByRef[change.SourceRef],
                existing[change.LogicalId],
                existingAttributes.GetValueOrDefault(
                    existing[change.LogicalId].Id,
                    [])),
            SpaceCadChangeKind.Delete => new SpaceElementCommandDto(
                CadCommandId(request.CommandBatchId, change.ChangeId),
                SpaceElementCommandContract.DeleteObject,
                change.LogicalId,
                null),
            _ => throw Invalid("The selected CAD change is not applyable."),
        }).ToArray();
        var applied = await design.ApplyCadElementCommandsAsync(
            versionId,
            workspace.FloorLogicalId,
            new ApplySpaceElementCommandBatchRequest(
                SpaceElementCommandContract.SchemaVersion,
                request.CommandBatchId,
                request.ClientInstanceId,
                request.LeaseId,
                request.ExpectedFloorRevision,
                commands,
                request.ExpectedContentRevision,
                request.ExpectedContentHash,
                applyFingerprint),
            cancellationToken);
        var history = BuildCadApplyHistory(applied);
        return new ApplySpaceCadChangesetResponse(
            applied.CommandBatchId,
            applied.FloorRevision,
            applied.VersionContentRevision,
            selected.LongLength,
            request.WorkspaceSha256,
            applied.IdempotentReplay,
            history.UndoCommands,
            history.RedoCommands);
    }

    private async Task<SpaceCadPreviewSetV2> ReadPreviewSetAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (files is null)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.JobProcessorUnavailable,
                503,
                "Private Space artifact storage is not configured.",
                recoveryAction: "configure-space-artifact-storage",
                retryable: true);
        }
        var input = await LoadReadableAsync(
            versionId,
            sourceId,
            jobId,
            tracked: false,
            cancellationToken);
        var artifactInput = await (
                from artifact in context.Artifacts.AsNoTracking()
                join file in context.Files.AsNoTracking()
                    on artifact.FileId equals file.Id
                where artifact.ArtifactType == SpaceArtifactType.PreviewSet &&
                      (artifact.JobId == jobId ||
                       (input.Job.RetryOfJobId.HasValue &&
                        artifact.JobId == input.Job.RetryOfJobId.Value))
                select new { Artifact = artifact, File = file })
            .SingleOrDefaultAsync(cancellationToken) ?? throw NotFound();
        await using var stream = await files.OpenQuarantinedReadAsync(
            artifactInput.File.TenantId,
            artifactInput.File.Id,
            artifactInput.File.StorageKey,
            cancellationToken);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: false);
        var json = await reader.ReadToEndAsync(cancellationToken);
        if (!Hash(json).Equals(artifactInput.File.Sha256, StringComparison.Ordinal))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.SourceUnsafe,
                409,
                "The CAD PreviewSet artifact failed integrity verification.",
                recoveryAction: "start-new-cad-parse");
        }
        return SpaceCadPreviewSet.Deserialize(json);
    }

    private static IReadOnlyList<SpaceCadChangeV1> BuildChanges(
        Guid versionId,
        SpaceCadSemanticPreviewV1 preview,
        IReadOnlyList<SpaceElementRevision> currentElements)
    {
        var currentByRef = currentElements
            .Where(item => item.SourceRef is not null)
            .GroupBy(item => item.SourceRef!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(),
                StringComparer.Ordinal);
        var changes = new List<SpaceCadChangeV1>();
        var seenRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in preview.Items)
        {
            var sourceRef = item.Source.SourceRef;
            seenRefs.Add(sourceRef);
            var logicalId = CadLogicalId(versionId, preview.SourceSha256, sourceRef);
            var supportedType = ElementType(item.Target);
            var existing = currentByRef.GetValueOrDefault(sourceRef) ?? [];
            var (kind, canApply, reason) = item switch
            {
                { Disposition: SpaceCadSemanticDisposition.Rejected } =>
                    (SpaceCadChangeKind.Unrecognized, false,
                        "SPACE_CAD_UNRECOGNIZED"),
                { IsConfirmable: false } =>
                    (SpaceCadChangeKind.Conflict, false,
                        "SPACE_CAD_CHANGE_NOT_CONFIRMABLE"),
                { Disposition: SpaceCadSemanticDisposition.Candidate } =>
                    (SpaceCadChangeKind.LowConfidence, false,
                        "SPACE_CAD_LOW_CONFIDENCE"),
                _ when supportedType is null =>
                    (SpaceCadChangeKind.Conflict, false,
                        "SPACE_CAD_REQUIRES_RULE_ONLY_REVIEW"),
                _ when existing.Length > 1 =>
                    (SpaceCadChangeKind.Conflict, false,
                        "SPACE_CAD_SOURCE_REF_CONFLICT"),
                _ when existing.Length == 1 &&
                       existing[0].IsManualCorrectionLocked =>
                    (SpaceCadChangeKind.Conflict, false,
                        SpaceErrorCodes.CadManualCorrectionLocked),
                _ when existing.Length == 1 &&
                       !existing[0].ElementType.Equals(
                           supportedType,
                           StringComparison.Ordinal) =>
                    (SpaceCadChangeKind.Conflict, false,
                        "SPACE_CAD_OBJECT_TYPE_CONFLICT"),
                _ when existing.Length == 1 =>
                    (SpaceCadChangeKind.Modify, true, (string?)null),
                _ => (SpaceCadChangeKind.Add, true, (string?)null),
            };
            if (existing.Length == 1)
                logicalId = existing[0].LogicalId;
            changes.Add(new SpaceCadChangeV1(
                ChangeId(sourceRef, logicalId),
                kind,
                logicalId,
                sourceRef,
                item.PreviewObjectId,
                supportedType ?? item.Target.ToString(),
                item.Confidence,
                item.IsSelected && canApply,
                canApply,
                reason,
                existing.Length == 1 &&
                    existing[0].IsManualCorrectionLocked,
                existing.Length == 1
                    ? existing[0].UserCorrectionVersion
                    : 0,
                existing.Length == 1
                    ? new SpaceCadMillimeterBoundsV1(
                        existing[0].X,
                        existing[0].Y,
                        checked(existing[0].X + existing[0].Width),
                        checked(existing[0].Y + existing[0].Depth))
                    : null,
                item.Geometry?.Bounds));
        }
        foreach (var element in currentElements.Where(item =>
                     item.SourceRef is not null &&
                     !seenRefs.Contains(item.SourceRef) &&
                     item.LifecycleState == SpaceLifecycleState.Active))
        {
            var isLocked = element.IsManualCorrectionLocked;
            changes.Add(new SpaceCadChangeV1(
                ChangeId(element.SourceRef!, element.LogicalId),
                isLocked
                    ? SpaceCadChangeKind.Conflict
                    : SpaceCadChangeKind.Delete,
                element.LogicalId,
                element.SourceRef!,
                null,
                element.ElementType,
                null,
                IsSelected: false,
                CanApply: !isLocked,
                BlockingReasonCode: isLocked
                    ? SpaceErrorCodes.CadManualCorrectionLocked
                    : null,
                IsManualCorrectionLocked: isLocked,
                UserCorrectionVersion: element.UserCorrectionVersion,
                new SpaceCadMillimeterBoundsV1(
                    element.X,
                    element.Y,
                    checked(element.X + element.Width),
                    checked(element.Y + element.Depth)),
                AfterBounds: null));
        }
        return changes;
    }

    private static SpaceElementCommandDto CreateCadCommand(
        Guid commandId,
        SpaceCadChangeV1 change,
        SpaceCadSemanticPreviewItemV1 preview,
        Guid sourceId)
    {
        var placement = CadPlacement(preview);
        return new SpaceElementCommandDto(
            commandId,
            SpaceElementCommandContract.CreateElement,
            change.LogicalId,
            null,
            CreateElement: new SpaceCreateElementDto(
                change.ObjectType,
                placement.GeometryJson,
                placement.X,
                placement.Y,
                placement.Z,
                placement.RotationZ,
                placement.Width,
                placement.Height,
                placement.Depth,
                BusinessCode: null,
                ParentLogicalId: null,
                sourceId,
                change.SourceRef,
                SemanticAttributes(preview)));
    }

    private static SpaceElementCommandDto UpdateCadCommand(
        Guid commandId,
        SpaceCadChangeV1 change,
        SpaceCadSemanticPreviewItemV1 preview,
        SpaceElementRevision existing,
        IReadOnlyList<SpaceElementAttributeWriteDto> existingAttributes)
    {
        var placement = CadPlacement(preview);
        return new SpaceElementCommandDto(
            commandId,
            SpaceElementCommandContract.UpdateProperties,
            change.LogicalId,
            new SpaceUpdateElementPropertiesDto(
                placement.GeometryJson,
                placement.X,
                placement.Y,
                placement.Z,
                placement.RotationZ,
                placement.Width,
                placement.Height,
                placement.Depth,
                existing.BusinessCode,
                existing.LinkedEntityType,
                existing.LinkedLogicalId,
                MergeSemanticAttributes(existingAttributes, preview)));
    }

    private static IReadOnlyList<SpaceElementAttributeWriteDto>
        MergeSemanticAttributes(
            IReadOnlyList<SpaceElementAttributeWriteDto> existing,
            SpaceCadSemanticPreviewItemV1 preview)
    {
        var attributes = existing.ToDictionary(
            item => $"{item.Namespace.Trim()}\u001f{item.Key.Trim()}",
            StringComparer.OrdinalIgnoreCase);
        foreach (var semantic in SemanticAttributes(preview))
        {
            attributes[$"{semantic.Namespace.Trim()}\u001f{semantic.Key.Trim()}"] =
                semantic;
        }
        return attributes.Values
            .OrderBy(item => item.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SpaceElementAttributeWriteDto>
        SemanticAttributes(SpaceCadSemanticPreviewItemV1 preview) =>
        preview.TargetSubtype is null
            ? []
            : [new SpaceElementAttributeWriteDto(
                SpaceElementAttributeNamespaces.Design,
                "semanticLabel",
                SpaceElementAttributeValueTypes.String,
                preview.TargetSubtype,
                null)];

    private static CadElementPlacement CadPlacement(
        SpaceCadSemanticPreviewItemV1 item)
    {
        var geometry = item.Geometry ?? throw Invalid(
            "A CAD change requires deterministic geometry.");
        var bounds = geometry.Bounds;
        var width = Math.Max(1, bounds.MaxX - bounds.MinX);
        var defaultDepth = item.AppliedMapping.DefaultThicknessMillimeters is > 0
            ? decimal.ToInt32(item.AppliedMapping.DefaultThicknessMillimeters.Value)
            : 200;
        var depth = Math.Max(1, bounds.MaxY - bounds.MinY);
        if (geometry.Kind is SpaceCadSemanticGeometryKind.Path or
            SpaceCadSemanticGeometryKind.Point)
            depth = defaultDepth;
        var height = item.AppliedMapping.DefaultHeightMillimeters is > 0
            ? decimal.ToInt32(item.AppliedMapping.DefaultHeightMillimeters.Value)
            : 2_000;
        string geometryJson;
        if (geometry.Kind == SpaceCadSemanticGeometryKind.Polygon &&
            geometry.Points.Count >= 3)
        {
            geometryJson = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                kind = "polygon",
                outer = geometry.Points.Select(point => new
                {
                    x = point.X - bounds.MinX,
                    y = point.Y - bounds.MinY,
                    z = 0,
                }),
                holes = Array.Empty<object>(),
                height,
            }, JsonOptions);
        }
        else
        {
            geometryJson = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                kind = "box",
                width,
                height,
                depth,
            }, JsonOptions);
        }
        return new CadElementPlacement(
            geometryJson,
            bounds.MinX,
            bounds.MinY,
            0,
            0,
            width,
            height,
            depth);
    }

    private static string? ElementType(SpaceCadSemanticTarget target) =>
        target switch
        {
            SpaceCadSemanticTarget.Wall => SpaceElementTypes.Wall,
            SpaceCadSemanticTarget.Column => SpaceElementTypes.Column,
            SpaceCadSemanticTarget.Door => SpaceElementTypes.Door,
            SpaceCadSemanticTarget.Dock => SpaceElementTypes.Dock,
            SpaceCadSemanticTarget.Equipment => SpaceElementTypes.StaticEquipment,
            SpaceCadSemanticTarget.VerticalCirculation =>
                SpaceElementTypes.StaticEquipment,
            SpaceCadSemanticTarget.Annotation => SpaceElementTypes.Annotation,
            SpaceCadSemanticTarget.Guide => SpaceElementTypes.Guide,
            SpaceCadSemanticTarget.RestrictedArea =>
                SpaceElementTypes.RestrictedArea,
            _ => null,
        };

    private static Guid CadLogicalId(
        Guid versionId,
        string sourceSha256,
        string sourceRef)
    {
        var sourceKey =
            $"source-{Hash(JsonSerializer.Serialize(new { sourceSha256, value = sourceRef }, JsonOptions))}";
        return WarehouseDeterministicIdentity.CreateObjectLogicalId(
            versionId,
            sourceSha256,
            sourceKey);
    }

    private static string ChangeId(string sourceRef, Guid logicalId) =>
        $"cad-change-{Hash($"{sourceRef}\n{logicalId:D}")[..32]}";

    private static CadApplyHistory BuildCadApplyHistory(
        ApplySpaceElementCommandBatchResponse applied)
    {
        var undo = new List<SpaceSavedElementCommandDto>();
        var redo = new List<SpaceSavedElementCommandDto>();
        foreach (var result in applied.AffectedObjects)
        {
            switch (result.Type)
            {
                case SpaceElementCommandContract.CreateElement:
                    undo.Add(new SpaceSavedElementCommandDto(
                        SpaceElementCommandContract.DeleteObject,
                        result.TargetLogicalId));
                    redo.Add(new SpaceSavedElementCommandDto(
                        SpaceElementCommandContract.RestoreLogicalObject,
                        result.TargetLogicalId));
                    break;
                case SpaceElementCommandContract.UpdateProperties:
                    if (result.BeforeElement is null ||
                        result.BeforeAttributes is null)
                    {
                        throw new InvalidDataException(
                            "A CAD modify result is missing its pre-apply snapshot.");
                    }
                    undo.Add(new SpaceSavedElementCommandDto(
                        SpaceElementCommandContract.UpdateProperties,
                        result.TargetLogicalId,
                        UpdateFromSnapshot(
                            result.BeforeElement,
                            result.BeforeAttributes)));
                    redo.Add(new SpaceSavedElementCommandDto(
                        SpaceElementCommandContract.UpdateProperties,
                        result.TargetLogicalId,
                        UpdateFromSnapshot(result.Element, result.Attributes)));
                    break;
                case SpaceElementCommandContract.DeleteObject:
                    undo.Add(new SpaceSavedElementCommandDto(
                        SpaceElementCommandContract.RestoreLogicalObject,
                        result.TargetLogicalId));
                    redo.Add(new SpaceSavedElementCommandDto(
                        SpaceElementCommandContract.DeleteObject,
                        result.TargetLogicalId));
                    break;
                default:
                    throw new InvalidDataException(
                        $"CAD Apply returned unsupported history command '{result.Type}'.");
            }
        }
        if (undo.Count == 0 || undo.Count != redo.Count)
        {
            throw new InvalidDataException(
                "CAD Apply did not return a complete reversible command set.");
        }
        undo.Reverse();
        return new CadApplyHistory(undo.ToArray(), redo.ToArray());
    }

    private static SpaceUpdateElementPropertiesDto UpdateFromSnapshot(
        SpaceSceneElementDto element,
        IReadOnlyList<SpaceSceneElementAttributeDto> attributes)
    {
        if (element.IsManualCorrectionLocked)
        {
            throw new InvalidDataException(
                "A locked manual correction cannot enter CAD Apply history.");
        }
        return new SpaceUpdateElementPropertiesDto(
            element.GeometryJson,
            element.X,
            element.Y,
            element.Z,
            element.RotationZ,
            element.Width,
            element.Height,
            element.Depth,
            element.BusinessCode,
            element.LinkedEntityType,
            element.LinkedLogicalId,
            attributes.Select(attribute =>
                new SpaceElementAttributeWriteDto(
                    attribute.Namespace,
                    attribute.Key,
                    attribute.ValueType,
                    attribute.Value,
                    attribute.Unit))
                .ToArray(),
            element.ElementType);
    }

    private sealed record CadApplyHistory(
        IReadOnlyList<SpaceSavedElementCommandDto> UndoCommands,
        IReadOnlyList<SpaceSavedElementCommandDto> RedoCommands);

    private static string CadApplyFingerprint(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        ApplySpaceCadChangesetRequest request) =>
        Hash(JsonSerializer.Serialize(new
        {
            versionId,
            sourceId,
            jobId,
            request.CommandBatchId,
            request.ClientInstanceId,
            request.LeaseId,
            request.ExpectedFloorRevision,
            request.ExpectedContentRevision,
            request.ExpectedContentHash,
            request.WorkspaceSha256,
            changeIds = request.ChangeIds
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray(),
        }, JsonOptions));

    private async Task<ApplySpaceCadChangesetResponse?> ReadCadApplyReplayAsync(
        Guid versionId,
        SpaceModelVersion currentVersion,
        ApplySpaceCadChangesetRequest request,
        string applyFingerprint,
        CancellationToken cancellationToken)
    {
        var batch = await context.ElementCommandBatches.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.CommandBatchId,
                cancellationToken);
        if (batch is null)
            return null;
        if (batch.ModelVersionId != versionId ||
            batch.AppliedBy != execution.ActorId ||
            batch.ClientInstanceId != request.ClientInstanceId ||
            batch.LeaseId != request.LeaseId ||
            batch.ExpectedFloorRevision != request.ExpectedFloorRevision ||
            batch.ExpectedContentRevision != request.ExpectedContentRevision ||
            !string.Equals(
                batch.ExpectedContentHash,
                request.ExpectedContentHash,
                StringComparison.Ordinal) ||
            !string.Equals(
                batch.ChangesetSha256,
                applyFingerprint,
                StringComparison.Ordinal))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CommandConflict,
                409,
                "The commandBatchId was already used with different CAD input.",
                recoveryAction: "create-new-command-batch");
        }
        if (batch.ResponseJson is null)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CommandConflict,
                409,
                "The CAD command batch has not reached a replayable state.",
                recoveryAction: "reload-floor-scene");
        }
        var applied = JsonSerializer.Deserialize<ApplySpaceElementCommandBatchResponse>(
                          batch.ResponseJson,
                          JsonOptions)
                      ?? throw new InvalidDataException(
                          "The CAD command batch response is invalid.");
        if (currentVersion.ContentRevision != applied.VersionContentRevision)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ParseChangesetStale,
                409,
                "The CAD changeset replay no longer matches the current Draft revision.",
                recoveryAction: "start-new-cad-parse");
        }
        var floorRevision = await context.FloorRevisions.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == versionId &&
                item.LogicalId == batch.FloorLogicalId)
            .Select(item => (long?)item.Revision)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound();
        if (floorRevision != applied.FloorRevision)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.FloorRevisionConflict,
                409,
                "The CAD changeset replay no longer matches the current floor revision.",
                recoveryAction: "reload-floor-scene");
        }
        var history = BuildCadApplyHistory(applied);
        return new ApplySpaceCadChangesetResponse(
            applied.CommandBatchId,
            applied.FloorRevision,
            applied.VersionContentRevision,
            request.ChangeIds.Count,
            request.WorkspaceSha256,
            IdempotentReplay: true,
            history.UndoCommands,
            history.RedoCommands);
    }

    private async Task<ApplySpaceCadChangesetResponse?>
        ReadCadApplyReplayWithFenceAsync(
            Guid versionId,
            Guid floorLogicalId,
            ApplySpaceCadChangesetRequest request,
            string applyFingerprint,
            CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        try
        {
            await AcquireFloorEditLockAsync(
                versionId,
                floorLogicalId,
                cancellationToken);
            var currentVersion = await LoadWritableVersionSnapshotAsync(
                versionId,
                cancellationToken);
            await EnsureActiveEditLeaseAsync(
                versionId,
                floorLogicalId,
                request.LeaseId,
                request.ClientInstanceId,
                cancellationToken);
            var replay = await ReadCadApplyReplayAsync(
                versionId,
                currentVersion,
                request,
                applyFingerprint,
                cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return replay;
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

    private async Task EnsureActiveEditLeaseAsync(
        Guid versionId,
        Guid floorLogicalId,
        Guid leaseId,
        Guid clientInstanceId,
        CancellationToken cancellationToken)
    {
        var now = context.Database.IsSqlServer()
            ? await context.Database
                .SqlQueryRaw<DateTime>("SELECT SYSUTCDATETIME() AS [Value]")
                .SingleAsync(cancellationToken)
            : RequireUtcNow();
        if (now.Kind != DateTimeKind.Utc)
            now = DateTime.SpecifyKind(now, DateTimeKind.Utc);
        var lease = await context.EditLeases.AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.ModelVersionId == versionId &&
                candidate.FloorLogicalId == floorLogicalId &&
                candidate.LeaseId == leaseId,
                cancellationToken);
        if (lease is null ||
            lease.OwnerUserId != execution.ActorId ||
            lease.ClientInstanceId != clientInstanceId ||
            lease.IsExpired(now))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.EditLeaseLost,
                409,
                "The edit lease is no longer valid.",
                recoveryAction: "export-recovery-draft-or-reacquire",
                retryable: true);
        }
    }

    private async Task AcquireFloorEditLockAsync(
        Guid versionId,
        Guid floorLogicalId,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsSqlServer())
            return;

        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
        };
        var resource = new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        {
            Value = $"cp6:space:floor-edit:{execution.TenantId:N}:" +
                    $"{versionId:N}:{floorLogicalId:N}",
        };
        await context.Database.ExecuteSqlRawAsync(
            """
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 15000;
            """,
            [result, resource],
            cancellationToken);
        if (Convert.ToInt32(result.Value) < 0)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CommandConflict,
                409,
                "The floor edit session is busy.",
                recoveryAction: "retry-cad-changeset-apply",
                retryable: true);
        }
    }

    private static Guid CadCommandId(Guid commandBatchId, string changeId) =>
        WarehouseDeterministicIdentity.CreateObjectLogicalId(
            commandBatchId,
            Hash(changeId),
            "cad-command");

    private sealed record CadElementPlacement(
        string GeometryJson,
        int X,
        int Y,
        int Z,
        decimal RotationZ,
        int Width,
        int Height,
        int Depth);

    public async Task<SpaceCadParseActionResponse> CancelAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        var input = await LoadReadableAsync(
            versionId,
            sourceId,
            jobId,
            tracked: true,
            cancellationToken);
        access.EnsureSiteAccess(input.Model.SiteId, write: true);
        input.Job.RequestCancellation(execution.ActorId, RequireUtcNow());
        await context.SaveChangesAsync(cancellationToken);
        return Action(input.Job, versionId, sourceId);
    }

    public async Task<SpaceCadParseActionResponse> RetryAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        var operation = $"cad-parse-retry:{jobId:N}";
        var keyHash = IdempotencyKeyHash(operation, idempotencyKey);
        var requestHash = Hash($"{versionId:N}\n{sourceId:N}\n{jobId:N}");
        var replay = await ReadReplayAsync<SpaceCadParseActionResponse>(
            operation,
            keyHash,
            requestHash,
            cancellationToken);
        if (replay is not null)
            return replay with { IdempotentReplay = true };

        IDbContextTransaction? transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        try
        {
            await AcquireCadParseLockAsync(sourceId, cancellationToken);
            context.ChangeTracker.Clear();
            var concurrentReplay =
                await ReadReplayAsync<SpaceCadParseActionResponse>(
                    operation,
                    keyHash,
                    requestHash,
                    cancellationToken);
            if (concurrentReplay is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return concurrentReplay with { IdempotentReplay = true };
            }

            var input = await LoadReadableAsync(
                versionId,
                sourceId,
                jobId,
                tracked: true,
                cancellationToken);
            access.EnsureSiteAccess(input.Model.SiteId, write: true);
            var enqueue = Enqueue(
                input.Payload,
                input.Job.InputHash,
                input.Job.PayloadJson);
            var businessKey = SpaceJobBusinessKey.Create(enqueue);
            var active = await context.Jobs.SingleOrDefaultAsync(
                item =>
                    item.JobType == SpaceJobType.CadParse &&
                    item.BusinessKey == businessKey &&
                    (item.Status == SpaceJobStatus.Queued ||
                     item.Status == SpaceJobStatus.Running),
                cancellationToken);
            var retry = active ?? input.Job.CreateExplicitRetry(
                businessKey,
                input.Job.InputHash,
                execution.ActorId,
                RequireUtcNow(),
                CorrelationId(),
                input.Job.PayloadJson);
            if (active is null)
                context.Jobs.Add(retry);
            var response = Action(retry, versionId, sourceId);
            AddIdempotency(
                operation,
                keyHash,
                requestHash,
                response,
                RequireUtcNow());
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            context.ChangeTracker.Clear();
            var concurrentReplay =
                await ReadReplayAsync<SpaceCadParseActionResponse>(
                    operation,
                    keyHash,
                    requestHash,
                    cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay with { IdempotentReplay = true };
            throw Conflict("The CAD parse changed while retry was being queued.");
        }
        catch (SpaceJobNotRetryableException exception)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw new SpaceProblemException(
                SpaceErrorCodes.JobNotRetryable,
                409,
                "The CAD parse Job cannot be retried.",
                exception.Message,
                "correct-input-or-start-new-parse");
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

    private async Task<ReadableParse> LoadReadableAsync(
        Guid versionId,
        Guid sourceId,
        Guid jobId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        if (versionId == Guid.Empty || sourceId == Guid.Empty || jobId == Guid.Empty)
            throw NotFound();
        var sourcesQuery = tracked ? context.Sources : context.Sources.AsNoTracking();
        var versionsQuery = tracked ? context.Versions : context.Versions.AsNoTracking();
        var modelsQuery = tracked ? context.Models : context.Models.AsNoTracking();
        var jobsQuery = tracked ? context.Jobs : context.Jobs.AsNoTracking();
        var result = await (
                from source in sourcesQuery
                join version in versionsQuery on source.ModelVersionId equals version.Id
                join model in modelsQuery on version.ModelId equals model.Id
                join job in jobsQuery on source.Id equals job.SubjectId
                where version.Id == versionId &&
                      source.Id == sourceId &&
                      job.Id == jobId &&
                      job.JobType == SpaceJobType.CadParse &&
                      job.SubjectType == SpaceJobSubjectType.ModelSource
                select new { source, model, job })
            .SingleOrDefaultAsync(cancellationToken);
        if (result is null)
            throw NotFound();
        EnsureReadable(result.model);
        var payload = DeserializePayload(result.job.PayloadJson);
        if (payload.SchemaVersion is not (
                SpaceCadParsePayloadVersions.LegacyBaseRevision or
                SpaceCadParsePayloadVersions.LegacyProviderRouting or
                SpaceCadParsePayloadVersions.LegacyMappingReplay or
                SpaceCadParsePayloadVersions.Current))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CadParseInvalid,
                409,
                "The stored CAD parse contract is no longer supported.",
                "Start a new parse to bind the current Draft revision.",
                "start-new-cad-parse");
        }
        if (payload.ModelVersionId != versionId || payload.SourceId != sourceId)
            throw Invalid("The stored CAD parse payload does not match its route.");
        return new ReadableParse(result.source, result.model, result.job, payload);
    }

    private async Task<(SpaceModelVersion Version, SpaceModel Model)>
        LoadWritableVersionAsync(
            Guid versionId,
            CancellationToken cancellationToken)
    {
        var result = await (
                from version in context.Versions
                join model in context.Models on version.ModelId equals model.Id
                where version.Id == versionId
                select new { version, model })
            .SingleOrDefaultAsync(cancellationToken);
        if (result is null)
            throw NotFound();
        EnsureReadable(result.model);
        access.EnsureSiteAccess(result.model.SiteId, write: true);
        if (result.version.Status != SpaceVersionStatus.Draft)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.VersionStateInvalid,
                409,
                "Only a Draft version can accept a CAD parse.",
                recoveryAction: "open-or-create-draft");
        }
        return (result.version, result.model);
    }

    private async Task<SpaceModelVersion> LoadWritableVersionSnapshotAsync(
        Guid versionId,
        CancellationToken cancellationToken)
    {
        var result = await (
                from version in context.Versions.AsNoTracking()
                join model in context.Models.AsNoTracking() on version.ModelId equals model.Id
                where version.Id == versionId
                select new { version, model })
            .SingleOrDefaultAsync(cancellationToken);
        if (result is null)
            throw NotFound();
        EnsureReadable(result.model);
        access.EnsureSiteAccess(result.model.SiteId, write: true);
        if (result.version.Status != SpaceVersionStatus.Draft)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.VersionStateInvalid,
                409,
                "Only a Draft version can accept a CAD changeset.",
                recoveryAction: "open-or-create-draft");
        }
        return result.version;
    }

    private void EnsureReadable(SpaceModel model)
    {
        if (model.Mode != SpaceModelMode.DesignV1 ||
            model.CutoverState != SpaceModelCutoverState.DesignV1)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.DesignApiDisabled,
                404,
                "The Design API is not enabled for this Site.",
                recoveryAction: "use-legacy-api");
        }
        access.EnsureSiteAccess(model.SiteId, write: false);
    }

    private static SpaceJobEnqueueRequest Enqueue(
        SpaceCadParseJobPayload payload,
        string inputHash,
        string payloadJson) =>
        new(
            SpaceJobType.CadParse,
            SpaceJobSubjectType.ModelSource,
            payload.SourceId,
            inputHash,
            SpaceCadParseJobProcessor.Version,
            VariantKey:
                $"{payload.FloorLogicalId:N}:{payload.CoordinateTransformSha256}:" +
                $"{payload.MappingProfileId:N}:{payload.MappingProfileVersion}:" +
                payload.MappingDefinitionSha256,
            MaxAttempts: 3,
            PayloadJson: payloadJson);

    private void AddIdempotency<T>(
        string operation,
        string keyHash,
        string requestHash,
        T response,
        DateTime nowUtc) =>
        context.IdempotencyRecords.Add(SpaceIdempotencyRecord.Create(
            execution.TenantId,
            execution.ActorId,
            operation,
            keyHash,
            requestHash,
            JsonSerializer.Serialize(response, JsonOptions),
            202,
            nowUtc.AddHours(24),
            nowUtc.AddDays(90)));

    private async Task<T?> ReadReplayAsync<T>(
        string operation,
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken) where T : class
    {
        var record = await context.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.PrincipalId == execution.ActorId &&
                    item.Operation == operation &&
                    item.IdempotencyKeyHash == keyHash,
                cancellationToken);
        if (record is null)
            return null;
        if (!record.RequestHash.Equals(requestHash, StringComparison.Ordinal) ||
            record.ReplayUntilUtc < RequireUtcNow())
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyConflict,
                409,
                "The Idempotency-Key was already used with different or expired input.",
                recoveryAction: "use-new-idempotency-key");
        }
        return JsonSerializer.Deserialize<T>(record.ResponseJson, JsonOptions)
               ?? throw new InvalidOperationException(
                   "The CAD parse idempotency response is invalid.");
    }

    private async Task AcquireCadParseLockAsync(
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        if (context.Database.ProviderName !=
            "Microsoft.EntityFrameworkCore.SqlServer")
        {
            return;
        }

        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
        };
        var resource = new SqlParameter(
            "@resource",
            SqlDbType.NVarChar,
            255)
        {
            Value = $"cp6:space:cad-parse:{execution.TenantId:N}:{sourceId:N}",
        };
        await context.Database.ExecuteSqlRawAsync(
            """
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 15000;
            """,
            [result, resource],
            cancellationToken);
        if (Convert.ToInt32(result.Value) < 0)
        {
            throw Conflict(
                "Another CAD parse request is currently updating this source.");
        }
    }

    private static void ValidateRequest(StartSpaceCadParseRequest request)
    {
        if (request.PreparationId == Guid.Empty ||
            request.FloorLogicalId == Guid.Empty ||
            request.ConfirmedUnit == SpaceCadUnit.Unknown ||
            request.ConfirmedScaleToMillimeters <= 0 ||
            string.IsNullOrWhiteSpace(request.CoordinateMetadataJson) ||
            request.CoordinateMetadataJson.Length > 8_000 ||
            !IsSha256(request.CoordinateTransformSha256) ||
            request.MappingProfileId == Guid.Empty ||
            request.MappingProfileVersion <= 0 ||
            !IsSha256(request.MappingDefinitionSha256) ||
            !IsSha256(request.MappingPreviewSha256))
        {
            throw Invalid("CAD coordinate and mapping confirmations are required.");
        }
    }

    private static void ValidatePreparation(
        SpaceCadParsePreparation preparation,
        SpaceModelVersion version,
        SpaceModelSource source,
        StartSpaceCadParseRequest request,
        DateTime now)
    {
        if (preparation.ExpiresAtUtc <= now)
            throw new SpaceProblemException(
                SpaceErrorCodes.CadPreparationExpired,
                409,
                "The CAD preparation has expired.",
                "Run the preparation preview again before starting parsing.",
                "restart-cad-preparation");
        if (!preparation.ReadyForParsing)
            throw new SpaceProblemException(
                SpaceErrorCodes.CadPreparationInvalid,
                422,
                "The CAD preparation still has Blocking issues.",
                recoveryAction: "resolve-cad-preparation-blockers");
        if (preparation.BaseContentRevision != version.ContentRevision ||
            !string.Equals(
                preparation.BaseContentHash,
                version.ContentHash,
                StringComparison.Ordinal))
            throw new SpaceProblemException(
                SpaceErrorCodes.ParseChangesetStale,
                409,
                "The Draft changed after CAD preparation.",
                "Run the preparation preview again against the current Draft.",
                "restart-cad-preparation");
        if (string.IsNullOrWhiteSpace(preparation.ProviderKey) ||
            string.IsNullOrWhiteSpace(preparation.ProviderVersion) ||
            !preparation.SourceSha256.Equals(source.Sha256, StringComparison.Ordinal) ||
            preparation.FloorLogicalId != request.FloorLogicalId ||
            !preparation.ConfirmedUnit.Equals(request.ConfirmedUnit.ToString(), StringComparison.Ordinal) ||
            preparation.ConfirmedScaleToMillimeters != request.ConfirmedScaleToMillimeters ||
            !preparation.CoordinateMetadataJson.Equals(request.CoordinateMetadataJson, StringComparison.Ordinal) ||
            !preparation.CoordinateTransformSha256.Equals(
                request.CoordinateTransformSha256,
                StringComparison.Ordinal) ||
            preparation.MappingProfileId != request.MappingProfileId ||
            preparation.MappingProfileVersion != request.MappingProfileVersion ||
            !preparation.MappingDefinitionSha256.Equals(
                request.MappingDefinitionSha256,
                StringComparison.Ordinal) ||
            !preparation.MappingPreviewSha256.Equals(
                request.MappingPreviewSha256,
                StringComparison.Ordinal) ||
            !IsValidMappingReplaySnapshot(
                preparation.MappingReplaySnapshotJson,
                preparation.TenantId,
                preparation.SourceSha256,
                preparation.MappingProfileId,
                preparation.MappingProfileVersion,
                preparation.MappingDefinitionSha256,
                preparation.MappingPreviewSha256))
            throw new SpaceProblemException(
                SpaceErrorCodes.CadPreparationInvalid,
                422,
                "The CAD parse request does not match its server preparation.",
                recoveryAction: "restart-cad-preparation");
    }

    private static bool IsValidMappingReplaySnapshot(
        string json,
        Guid tenantId,
        string sourceSha256,
        Guid profileId,
        int profileVersion,
        string profileDefinitionSha256,
        string mappingPreviewSha256)
    {
        try
        {
            var snapshot = SpaceCadMappingReplaySnapshot.Deserialize(json);
            return snapshot.TenantId == tenantId &&
                   snapshot.ProfileId == profileId &&
                   snapshot.ProfileVersion == profileVersion &&
                   snapshot.ProfileDefinitionSha256.Equals(
                       profileDefinitionSha256,
                       StringComparison.Ordinal) &&
                   snapshot.SourceSha256.Equals(
                       sourceSha256,
                       StringComparison.Ordinal) &&
                   snapshot.ExpectedMappingPreviewSha256.Equals(
                       mappingPreviewSha256,
                       StringComparison.Ordinal);
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static void ValidateCoordinateMetadata(
        StartSpaceCadParseRequest request,
        string sourceSha256)
    {
        try
        {
            var metadata = JsonSerializer.Deserialize<SpaceCadCoordinateMetadataV1>(
                               request.CoordinateMetadataJson,
                               JsonOptions) ?? throw new JsonException();
            if (metadata.SchemaVersion != SpaceCadCoordinateVersions.SchemaVersion ||
                !metadata.SourceSha256.Equals(sourceSha256, StringComparison.Ordinal) ||
                !metadata.UnitConfirmed ||
                metadata.ConfirmedUnit != request.ConfirmedUnit ||
                metadata.ConfirmedScaleToMillimeters !=
                    request.ConfirmedScaleToMillimeters ||
                metadata.TargetFloor.FloorLogicalId != request.FloorLogicalId ||
                metadata.TargetFloor.CoordinateSystem !=
                    SpaceCadCoordinateVersions.TargetCoordinateSystem ||
                !metadata.TransformSha256.Equals(
                    request.CoordinateTransformSha256,
                    StringComparison.Ordinal))
            {
                throw new JsonException();
            }
        }
        catch (JsonException)
        {
            throw Invalid(
                "CAD coordinate metadata does not match the source and confirmation.");
        }
    }

    private async Task SynchronizeTerminalFileStateAsync(
        SpaceModelSource source,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        if (source.State != SpaceSourceState.Scanning)
            return;
        var file = await context.Files.SingleAsync(
            item => item.Id == fileId,
            cancellationToken);
        await context.Entry(file).ReloadAsync(cancellationToken);
        if (file.State is not (SpaceFileState.Clean or SpaceFileState.Rejected))
            return;
        source.CompleteFileScan(file);
        await context.SaveChangesAsync(cancellationToken);
    }

    private Task<Guid?> FindScanJobIdAsync(
        Guid fileId,
        CancellationToken cancellationToken) =>
        context.Jobs.AsNoTracking()
            .Where(job =>
                job.SubjectType == SpaceJobSubjectType.File &&
                job.SubjectId == fileId &&
                job.JobType == SpaceJobType.FileScan)
            .OrderByDescending(job => job.RequestedAtUtc)
            .Select(job => (Guid?)job.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static SpaceCadParseJobPayload DeserializePayload(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SpaceCadParseJobPayload>(json, JsonOptions)
                   ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw Invalid("The stored CAD parse payload is invalid.");
        }
    }

    private static bool TryDeserializeCandidatePayload(
        string json,
        out SpaceCadParseJobPayload payload)
    {
        try
        {
            payload = JsonSerializer.Deserialize<SpaceCadParseJobPayload>(
                          json,
                          JsonOptions)!;
            return payload is not null &&
                   payload.SchemaVersion is (
                       SpaceCadParsePayloadVersions.LegacyBaseRevision or
                       SpaceCadParsePayloadVersions.LegacyProviderRouting or
                       SpaceCadParsePayloadVersions.LegacyMappingReplay or
                       SpaceCadParsePayloadVersions.Current);
        }
        catch (JsonException)
        {
            payload = null!;
            return false;
        }
    }

    private static bool SourceFormatMatches(
        SpaceCadSourceFormat format,
        SpaceSourceType sourceType) =>
        (format == SpaceCadSourceFormat.Dwg && sourceType == SpaceSourceType.Dwg) ||
        (format == SpaceCadSourceFormat.Dxf && sourceType == SpaceSourceType.Dxf);

    private static StartSpaceCadParseResponse Response(
        SpaceJob job,
        SpaceModelSource source,
        bool replay) =>
        new(
            job.Id,
            job.Status.ToString(),
            $"/api/space/design/v1/jobs/{job.Id:D}",
            ParseUrl(source.ModelVersionId, source.Id, job.Id),
            ToDto(source),
            replay);

    private static SpaceCadParseActionResponse Action(
        SpaceJob job,
        Guid versionId,
        Guid sourceId) =>
        new(
            job.Id,
            job.Status.ToString(),
            $"/api/space/design/v1/jobs/{job.Id:D}",
            ParseUrl(versionId, sourceId, job.Id));

    private static SpaceFileDto ToDto(SpaceFile file) =>
        new(
            file.Id,
            file.OriginalName,
            file.DetectedContentType ?? file.DeclaredContentType,
            file.Extension,
            file.SizeBytes,
            file.Sha256,
            file.State.ToString(),
            file.ScanResultCode,
            Convert.ToBase64String(file.RowVersion));

    private static SpaceSourceDto ToDto(SpaceModelSource source) =>
        new(
            source.Id,
            source.ModelVersionId,
            source.SourceType.ToString(),
            source.FileId,
            source.DisplayName,
            source.Sha256,
            source.State.ToString(),
            source.ParserVersion,
            source.MappingProfileId,
            source.MappingProfileVersion,
            source.Unit,
            source.ScaleToMillimeters,
            Convert.ToBase64String(source.RowVersion));

    private void EnsureExecutionContext()
    {
        if (execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot access CAD sources or parse artifacts.",
                recoveryAction: "use-published-runtime");
        }
        if (execution.TenantId == Guid.Empty || execution.ActorId == Guid.Empty)
            throw new SpaceTenantScopeException(
                "A verified Space tenant and actor are required.");
    }

    private DateTime RequireUtcNow()
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

    private static string ParseUrl(Guid versionId, Guid sourceId, Guid jobId) =>
        $"/api/space/design/v1/versions/{versionId:D}/sources/{sourceId:D}/" +
        $"cad-parses/{jobId:D}";

    private static string IdempotencyKeyHash(string operation, string key)
    {
        var normalized = key?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 200)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyKeyRequired,
                400,
                "A valid Idempotency-Key is required.",
                recoveryAction: "supply-idempotency-key");
        }
        return Hash($"{operation}\n{normalized}");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool IsSha256(string value) =>
        value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string NormalizeHash(string value) => value.ToLowerInvariant();

    private static SpaceProblemException Invalid(string detail) =>
        new(
            SpaceErrorCodes.CadParseInvalid,
            422,
            "The CAD parse request is invalid.",
            detail,
            "correct-cad-parse-request");

    private static SpaceProblemException NotFound() =>
        new(
            SpaceErrorCodes.CadParseNotFound,
            404,
            "The CAD parse was not found.",
            recoveryAction: "reload-cad-sources");

    private static SpaceProblemException Conflict(string detail) =>
        new(
            SpaceErrorCodes.SourceConflict,
            409,
            "The CAD source is not available for this operation.",
            detail,
            "reload-source-and-retry",
            retryable: true);

    private sealed record ReadableParse(
        SpaceModelSource Source,
        SpaceModel Model,
        SpaceJob Job,
        SpaceCadParseJobPayload Payload);
}
