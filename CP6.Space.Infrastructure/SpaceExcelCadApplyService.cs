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

public sealed partial class SpaceExcelCadApplyService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access,
    IServiceProvider services,
    ISpaceClock clock) : ISpaceExcelCadApplyService
{
    private const long MaximumArtifactBytes = 200L * 1024L * 1024L;
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<ConfirmSpaceExcelCadMatchResponse> ConfirmAsync(
        Guid versionId,
        Guid matchJobId,
        ConfirmSpaceExcelCadMatchRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        ValidateRequest(versionId, matchJobId, request);

        var operation = $"excel-cad-apply:{versionId:N}:{matchJobId:N}";
        var keyHash = IdempotencyKeyHash(operation, idempotencyKey);
        var requestHash = Hash(JsonSerializer.Serialize(request, JsonOptions));
        var authority = await LoadAuthorityAsync(
            versionId,
            matchJobId,
            request,
            write: true,
            cancellationToken);
        var commandBatchId = DeterministicGuid(
            "space-excel-cad-apply-batch-v1",
            matchJobId,
            request.ArtifactPayloadSha256);
        var payload = new SpaceExcelCadApplyJobPayload(
            SpaceExcelCadApplyVersions.PayloadSchemaVersion,
            versionId,
            matchJobId,
            request.ArtifactId,
            request.ArtifactPayloadSha256,
            authority.Artifact.ExcelSourceId,
            authority.Artifact.FloorLogicalId,
            request.ClientInstanceId,
            request.LeaseId,
            request.ExpectedFloorRevision,
            request.ExpectedContentRevision,
            commandBatchId);
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var inputHash = Hash(payloadJson);
        var enqueue = new SpaceJobEnqueueRequest(
            SpaceJobType.ExcelCadApply,
            SpaceJobSubjectType.ModelSource,
            authority.Artifact.ExcelSourceId,
            inputHash,
            SpaceExcelCadApplyJobProcessor.Version,
            VariantKey: $"{versionId:N}:{matchJobId:N}:{commandBatchId:N}",
            MaxAttempts: 5,
            PayloadJson: payloadJson);
        var businessKey = SpaceJobBusinessKey.Create(enqueue);

        IDbContextTransaction? transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        try
        {
            await AcquireFloorEditLockAsync(
                versionId,
                authority.Artifact.FloorLogicalId,
                cancellationToken);
            authority = await LoadAuthorityAsync(
                versionId,
                matchJobId,
                request,
                write: true,
                cancellationToken);
            await EnsureActiveEditLeaseAsync(
                versionId,
                authority.Artifact.FloorLogicalId,
                request.LeaseId,
                request.ClientInstanceId,
                cancellationToken);
            ValidateAuthorityRevisions(
                authority,
                request,
                commandBatchId);
            var concurrentReplay = await ReadReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return concurrentReplay;
            }
            var prior = await FindPriorApplyAsync(
                matchJobId,
                commandBatchId,
                cancellationToken);
            var job = prior?.Job;
            var reused = job is not null;
            if (job is null)
            {
                job = await context.Jobs.SingleOrDefaultAsync(
                    item =>
                        item.JobType == SpaceJobType.ExcelCadApply &&
                        item.BusinessKey == businessKey &&
                        (item.Status == SpaceJobStatus.Queued ||
                         item.Status == SpaceJobStatus.Running),
                    cancellationToken);
                reused = job is not null;
            }

            var now = RequireUtcNow();
            if (job is null)
            {
                job = SpaceJob.CreateQueued(
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
                await context.SaveChangesAsync(cancellationToken);
            }

            var response = Response(
                versionId,
                matchJobId,
                job,
                commandBatchId,
                reused);
            context.IdempotencyRecords.Add(SpaceIdempotencyRecord.Create(
                execution.TenantId,
                execution.ActorId,
                operation,
                keyHash,
                requestHash,
                JsonSerializer.Serialize(response, JsonOptions),
                202,
                now.AddHours(24),
                now.AddDays(90)));
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
            var concurrentReplay = await ReadReplayAsync(
                operation,
                keyHash,
                requestHash,
                cancellationToken);
            if (concurrentReplay is not null)
                return concurrentReplay;
            throw Conflict(
                "The Draft or confirmation ledger changed while Apply was queued.");
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

    public async Task<SpaceExcelCadApplyDto> GetAsync(
        Guid versionId,
        Guid matchJobId,
        Guid applyJobId,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        if (versionId == Guid.Empty || matchJobId == Guid.Empty ||
            applyJobId == Guid.Empty)
        {
            throw NotFound();
        }

        var scope = await (
                from job in context.Jobs.AsNoTracking()
                join source in context.Sources.AsNoTracking()
                    on job.SubjectId equals source.Id
                join version in context.Versions.AsNoTracking()
                    on source.ModelVersionId equals version.Id
                join model in context.Models.AsNoTracking()
                    on version.ModelId equals model.Id
                where job.Id == applyJobId &&
                      job.JobType == SpaceJobType.ExcelCadApply &&
                      job.SubjectType == SpaceJobSubjectType.ModelSource &&
                      version.Id == versionId
                select new { Job = job, Model = model })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound();
        EnsureReadable(scope.Model);
        var payload = DeserializePayload(scope.Job.PayloadJson);
        if (payload.ModelVersionId != versionId ||
            payload.MatchJobId != matchJobId ||
            payload.ExcelSourceId != scope.Job.SubjectId)
        {
            throw ArtifactInvalid(
                "The stored Apply Job does not match the requested chain.");
        }

        var checkpoint = await (
                from step in context.JobSteps.AsNoTracking()
                join attempt in context.JobAttempts.AsNoTracking()
                    on step.AttemptId equals attempt.Id
                where attempt.JobId == applyJobId &&
                      step.StepCode ==
                          SpaceExcelCadApplyJobProcessor.ApplyConfirmedArtifact &&
                      (step.Status == SpaceJobStepStatus.Succeeded ||
                       step.Status == SpaceJobStepStatus.Reused) &&
                      step.CheckpointJson != null
                orderby step.FinishedAtUtc descending
                select step.CheckpointJson)
            .FirstOrDefaultAsync(cancellationToken);
        SpaceExcelCadApplyResultV1? result = null;
        if (checkpoint is not null)
        {
            try
            {
                result = JsonSerializer.Deserialize<SpaceExcelCadApplyResultV1>(
                    checkpoint,
                    JsonOptions) ?? throw new JsonException();
            }
            catch (JsonException exception)
            {
                throw ArtifactInvalid(
                    $"The stored Apply checkpoint is invalid: {exception.Message}");
            }
            ValidateResult(result, payload, applyJobId);
        }

        return new SpaceExcelCadApplyDto(
            matchJobId,
            applyJobId,
            payload.CommandBatchId,
            scope.Job.Status.ToString(),
            payload.ExpectedContentRevision,
            result,
            IdempotentReplay: scope.Job.AttemptCount > 1,
            scope.Job.LastErrorCode,
            scope.Job.LastErrorSummary);
    }

    private async Task<ApplyAuthority> LoadAuthorityAsync(
        Guid versionId,
        Guid matchJobId,
        ConfirmSpaceExcelCadMatchRequest request,
        bool write,
        CancellationToken cancellationToken)
    {
        var scope = await (
                from match in context.Jobs.AsNoTracking()
                join source in context.Sources.AsNoTracking()
                    on match.SubjectId equals source.Id
                join version in context.Versions.AsNoTracking()
                    on source.ModelVersionId equals version.Id
                join model in context.Models.AsNoTracking()
                    on version.ModelId equals model.Id
                where match.Id == matchJobId &&
                      match.JobType == SpaceJobType.ExcelCadMatch &&
                      match.SubjectType == SpaceJobSubjectType.ModelSource &&
                      version.Id == versionId
                select new
                {
                    Match = match,
                    Source = source,
                    Version = version,
                    Model = model,
                })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound();
        if (write)
            EnsureWritable(scope.Model);
        else
            EnsureReadable(scope.Model);
        var matchPayload = DeserializeMatch(scope.Match.PayloadJson);
        if (scope.Match.Status != SpaceJobStatus.Succeeded ||
            scope.Source.SourceType != SpaceSourceType.Excel ||
            matchPayload.ModelVersionId != versionId ||
            matchPayload.ExcelSourceId != scope.Source.Id ||
            matchPayload.ExpectedContentRevision != request.ExpectedContentRevision)
        {
            throw Conflict(
                "The Match Artifact is no longer attached to the current Draft revision.");
        }

        var persisted = await (
                from artifact in context.Artifacts.AsNoTracking()
                join file in context.Files.AsNoTracking()
                    on artifact.FileId equals file.Id
                where artifact.JobId == matchJobId &&
                      artifact.ArtifactType ==
                          SpaceArtifactType.ExcelCadMatchPreview
                select new { Artifact = artifact, File = file })
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (persisted.Length != 1)
            throw ArtifactInvalid(
                "The Match Job does not have exactly one authoritative artifact.");
        var stored = persisted[0];
        if (stored.Artifact.Id != request.ArtifactId ||
            stored.Artifact.ModelVersionId != versionId ||
            stored.Artifact.SourceId != scope.Source.Id ||
            stored.Artifact.SchemaVersion !=
                SpaceExcelCadMatchArtifactVersions.ArtifactSchema ||
            stored.File.State != SpaceFileState.Clean ||
            stored.File.SizeBytes is < 1 or > MaximumArtifactBytes)
        {
            throw ArtifactInvalid(
                "The selected authoritative Match Artifact identity is invalid.");
        }

        var value = await ReadArtifactAsync(stored.File, cancellationToken);
        if (value.MatchJobId != matchJobId ||
            value.ModelVersionId != versionId ||
            value.ExcelSourceId != scope.Source.Id ||
            value.FloorLogicalId != matchPayload.FloorLogicalId ||
            value.ArtifactPayloadSha256 != request.ArtifactPayloadSha256 ||
            !value.Preview.CanConfirm)
        {
            throw ArtifactInvalid(
                "The Match Artifact is not eligible for confirmation.");
        }

        var floor = await context.FloorRevisions.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.ModelVersionId == versionId &&
                        item.LogicalId == value.FloorLogicalId,
                cancellationToken)
            ?? throw NotFound();
        return new ApplyAuthority(scope.Version, scope.Source, floor, value);
    }

    private static void ValidateAuthorityRevisions(
        ApplyAuthority authority,
        ConfirmSpaceExcelCadMatchRequest request,
        Guid commandBatchId)
    {
        var initialConfirmation =
            authority.Version.Status == SpaceVersionStatus.Draft &&
            authority.Source.State == SpaceSourceState.PreviewReady &&
            authority.Version.ContentRevision == request.ExpectedContentRevision;
        var alreadyApplied =
            authority.Source.State == SpaceSourceState.Imported &&
            authority.Source.ImportedCommandBatchId == commandBatchId &&
            authority.Version.ContentRevision >= request.ExpectedContentRevision + 1;
        if (!initialConfirmation && !alreadyApplied)
        {
            throw Conflict(
                "The Match Artifact is no longer attached to the current Draft revision.");
        }

        var expectedFloorRevision = alreadyApplied
            ? request.ExpectedFloorRevision + 1
            : request.ExpectedFloorRevision;
        if (authority.Floor.Revision != expectedFloorRevision)
        {
            throw Conflict(
                "The floor revision changed before Excel/CAD Apply confirmation.");
        }
    }

    private async Task<SpaceExcelCadMatchArtifactV1> ReadArtifactAsync(
        SpaceFile file,
        CancellationToken cancellationToken)
    {
        var store = services.GetService(typeof(ISpaceFileStore)) as
            ISpaceFileStore ?? throw new SpaceProblemException(
                SpaceErrorCodes.JobProcessorUnavailable,
                503,
                "Private Space artifact storage is not configured.",
                recoveryAction: "configure-space-file-storage",
                retryable: true);
        try
        {
            await using var stream = await store.OpenQuarantinedReadAsync(
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
                    "The Match Artifact file hash or size changed.");
            }
            return SpaceExcelCadMatchArtifact.Deserialize(json);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException)
        {
            throw ArtifactInvalid(exception.Message);
        }
    }

    private async Task<PriorApply?> FindPriorApplyAsync(
        Guid matchJobId,
        Guid commandBatchId,
        CancellationToken cancellationToken)
    {
        var candidates = await context.Jobs.AsNoTracking()
            .Where(item => item.JobType == SpaceJobType.ExcelCadApply)
            .OrderByDescending(item => item.RequestedAtUtc)
            .ToArrayAsync(cancellationToken);
        foreach (var job in candidates)
        {
            SpaceExcelCadApplyJobPayload payload;
            try
            {
                payload = DeserializePayload(job.PayloadJson);
            }
            catch (SpaceProblemException)
            {
                continue;
            }
            if (payload.SchemaVersion <
                    SpaceExcelCadApplyVersions.PayloadSchemaVersion &&
                job.Status != SpaceJobStatus.Succeeded)
            {
                continue;
            }
            if (payload.MatchJobId == matchJobId &&
                payload.CommandBatchId == commandBatchId &&
                job.Status is SpaceJobStatus.Queued or
                    SpaceJobStatus.Running or SpaceJobStatus.Succeeded)
            {
                return new PriorApply(job, payload);
            }
        }
        return null;
    }

    private async Task<ConfirmSpaceExcelCadMatchResponse?> ReadReplayAsync(
        string operation,
        string keyHash,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var record = await context.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.PrincipalId == execution.ActorId &&
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
        return (JsonSerializer.Deserialize<ConfirmSpaceExcelCadMatchResponse>(
                    record.ResponseJson,
                    JsonOptions) ?? throw new InvalidOperationException(
                    "The Apply Job idempotency response is invalid.")) with
        {
            IdempotentReplay = true,
        };
    }

    private static SpaceExcelCadMatchJobPayload DeserializeMatch(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SpaceExcelCadMatchJobPayload>(
                       json,
                       JsonOptions) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw ArtifactInvalid("The stored Match Job payload is invalid.");
        }
    }

    private static SpaceExcelCadApplyJobPayload DeserializePayload(string json)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<SpaceExcelCadApplyJobPayload>(
                              json,
                              JsonOptions) ?? throw new JsonException();
            if (payload.SchemaVersion is < 1 or >
                    SpaceExcelCadApplyVersions.PayloadSchemaVersion ||
                payload.ModelVersionId == Guid.Empty ||
                payload.MatchJobId == Guid.Empty ||
                payload.ArtifactId == Guid.Empty ||
                !IsSha256(payload.ArtifactPayloadSha256) ||
                payload.ExcelSourceId == Guid.Empty ||
                payload.FloorLogicalId == Guid.Empty ||
                (payload.SchemaVersion >=
                    SpaceExcelCadApplyVersions.PayloadSchemaVersion &&
                 (payload.ClientInstanceId == Guid.Empty ||
                  payload.LeaseId == Guid.Empty)) ||
                payload.ExpectedFloorRevision < 0 ||
                payload.ExpectedContentRevision < 0 ||
                payload.CommandBatchId == Guid.Empty)
            {
                throw new JsonException();
            }
            return payload;
        }
        catch (JsonException)
        {
            throw ArtifactInvalid("The stored Apply Job payload is invalid.");
        }
    }

    private static void ValidateResult(
        SpaceExcelCadApplyResultV1 result,
        SpaceExcelCadApplyJobPayload payload,
        Guid applyJobId)
    {
        if (result.SchemaVersion is < SpaceExcelCadApplyVersions.LegacySchemaVersion or
                > SpaceExcelCadApplyVersions.SchemaVersion ||
            result.MatchJobId != payload.MatchJobId ||
            result.ApplyJobId != applyJobId ||
            result.ArtifactId != payload.ArtifactId ||
            result.ArtifactPayloadSha256 != payload.ArtifactPayloadSha256 ||
            result.ModelVersionId != payload.ModelVersionId ||
            result.ExcelSourceId != payload.ExcelSourceId ||
            result.FloorLogicalId != payload.FloorLogicalId ||
            result.CommandBatchId != payload.CommandBatchId ||
            result.ExpectedFloorRevision != payload.ExpectedFloorRevision ||
            result.ExpectedContentRevision != payload.ExpectedContentRevision ||
            result.ResultFloorRevision != payload.ExpectedFloorRevision + 1 ||
            result.ResultContentRevision != payload.ExpectedContentRevision + 1 ||
            result.ConfirmedBy == Guid.Empty ||
            result.ConfirmedAtUtc.Kind != DateTimeKind.Utc ||
            result.AppliedAtUtc.Kind != DateTimeKind.Utc ||
            !IsSha256(result.ApplyPlanSha256) ||
            (result.SchemaVersion >= SpaceExcelCadApplyVersions.SchemaVersion &&
             (!IsSha256(result.HistorySha256) ||
              result.HistoryCommandCount <= 0)))
        {
            throw ArtifactInvalid("The stored Apply result identity is invalid.");
        }
    }

    private static void ValidateRequest(
        Guid versionId,
        Guid matchJobId,
        ConfirmSpaceExcelCadMatchRequest request)
    {
        if (versionId == Guid.Empty || matchJobId == Guid.Empty ||
            !request.Confirmed || request.ArtifactId == Guid.Empty ||
            !IsSha256(request.ArtifactPayloadSha256) ||
            request.ExpectedContentRevision is < 0 or long.MaxValue ||
            request.ExpectedFloorRevision is < 0 or long.MaxValue ||
            request.ClientInstanceId == Guid.Empty ||
            request.LeaseId == Guid.Empty)
        {
            throw Invalid(
                "Explicit confirmation, artifact identity, edit lease and expected revisions are required.");
        }
    }

    private async Task EnsureActiveEditLeaseAsync(
        Guid versionId,
        Guid floorLogicalId,
        Guid leaseId,
        Guid clientInstanceId,
        CancellationToken cancellationToken)
    {
        var now = await ReadAuthoritativeUtcNowAsync(cancellationToken);
        var lease = await context.EditLeases.AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.ModelVersionId == versionId &&
                candidate.FloorLogicalId == floorLogicalId,
                cancellationToken);
        if (lease is null ||
            lease.LeaseId != leaseId ||
            lease.OwnerUserId != execution.ActorId ||
            lease.ClientInstanceId != clientInstanceId ||
            lease.IsExpired(now))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.EditLeaseLost,
                409,
                "The floor edit lease is missing, expired, or owned by another session.",
                recoveryAction: "acquire-edit-lease");
        }
    }

    private async Task<DateTime> ReadAuthoritativeUtcNowAsync(
        CancellationToken cancellationToken)
    {
        var now = context.Database.IsSqlServer()
            ? await context.Database
                .SqlQueryRaw<DateTime>("SELECT SYSUTCDATETIME() AS [Value]")
                .SingleAsync(cancellationToken)
            : clock.UtcNow;
        return now.Kind == DateTimeKind.Utc
            ? now
            : DateTime.SpecifyKind(now, DateTimeKind.Utc);
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
                SpaceErrorCodes.EditLeaseHeld,
                409,
                "The floor edit session is busy.",
                recoveryAction: "retry-excel-cad-confirmation",
                retryable: true);
        }
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

    private void EnsureWritable(SpaceModel model)
    {
        EnsureReadable(model);
        access.EnsureSiteAccess(model.SiteId, write: true);
    }

    private void EnsureExecutionContext()
    {
        if (execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot confirm Excel/CAD Match Artifacts.",
                recoveryAction: "use-internal-space-principal");
        }
        if (execution.TenantId == Guid.Empty ||
            execution.ActorId == Guid.Empty ||
            execution.TenantId != context.CurrentTenantId)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.TenantScopeDenied,
                403,
                "The Space tenant scope was denied.",
                recoveryAction: "reauthenticate");
        }
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

    private static ConfirmSpaceExcelCadMatchResponse Response(
        Guid versionId,
        Guid matchJobId,
        SpaceJob job,
        Guid commandBatchId,
        bool replay) => new(
        matchJobId,
        job.Id,
        commandBatchId,
        job.Status.ToString(),
        $"/api/space/design/v1/versions/{versionId:D}/excel-cad-matches/" +
        $"{matchJobId:D}/confirmations/{job.Id:D}",
        replay);

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

    internal static Guid DeterministicGuid(
        string purpose,
        Guid identity,
        string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{purpose}\n{identity:N}\n{value}"));
        var guidBytes = bytes[..16];
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    internal static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    internal static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            Uri.IsHexDigit(character) && !char.IsUpper(character));

    private static SpaceProblemException Invalid(string detail) => new(
        SpaceErrorCodes.ExcelCadApplyInvalid,
        422,
        "The Excel/CAD confirmation request is invalid.",
        detail,
        "review-match-and-confirm-again");

    private static SpaceProblemException NotFound() => new(
        SpaceErrorCodes.ExcelCadApplyNotFound,
        404,
        "The Excel/CAD confirmation was not found.",
        recoveryAction: "reload-match");

    private static SpaceProblemException Conflict(string detail) => new(
        SpaceErrorCodes.ConcurrencyConflict,
        409,
        "The Draft changed before the Excel/CAD match could be applied.",
        detail,
        "rebuild-match-artifact",
        retryable: true);

    private static SpaceProblemException ArtifactInvalid(string detail) => new(
        SpaceErrorCodes.ExcelCadApplyArtifactInvalid,
        422,
        "The authoritative Excel/CAD Match Artifact cannot be applied.",
        detail,
        "rebuild-match-artifact");

    private sealed record ApplyAuthority(
        SpaceModelVersion Version,
        SpaceModelSource Source,
        SpaceFloorRevision Floor,
        SpaceExcelCadMatchArtifactV1 Artifact);

    private sealed record PriorApply(
        SpaceJob Job,
        SpaceExcelCadApplyJobPayload Payload);
}
