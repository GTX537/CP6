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

public sealed class SpaceExcelCadMatchJobStepExecutor(
    SpaceContext context,
    IServiceProvider services,
    ISpaceExcelWorkbookReader workbookReader,
    ISpaceExcelMappingService mappings)
    : ISpaceExcelCadMatchJobStepExecutor
{
    private const long MaximumArtifactBytes = 200L * 1024L * 1024L;
    private const string ArtifactScanEngine = "cp6-internal-artifact";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        EnsureLease(execution.Lease);
        if (execution.StepCode !=
            SpaceExcelCadMatchJobProcessor.PersistMatchArtifact)
        {
            throw Failure(
                SpaceJobFailureKind.Bug,
                "SPACE_EXCEL_CAD_MATCH_STEP_INVALID",
                "The Excel/CAD match Job step is invalid.");
        }

        try
        {
            return await PersistAsync(execution.Lease, cancellationToken);
        }
        catch (SpaceJobProcessingException)
        {
            throw;
        }
        catch (SpaceExcelWorkbookException exception)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                exception.Code,
                Sanitize(exception.Message,
                    "The Excel workbook could not be read."));
        }
        catch (InvalidDataException exception)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ExcelCadMatchArtifactInvalid,
                Sanitize(exception.Message,
                    "The authoritative matching input is invalid."));
        }
        catch (IOException)
        {
            throw Failure(
                SpaceJobFailureKind.Resource,
                SpaceErrorCodes.JobProcessorFailed,
                "A private matching input or artifact could not be read or written.");
        }
        catch (SpaceProblemException exception)
        {
            throw Failure(
                exception.Retryable
                    ? SpaceJobFailureKind.Resource
                    : SpaceJobFailureKind.Input,
                exception.Code,
                Sanitize(exception.Message,
                    "The authoritative matching input is unavailable."));
        }
    }

    private async Task<SpaceJobStepOutput> PersistAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var input = await LoadInputAsync(lease, cancellationToken);
        var files = services.GetService(typeof(ISpaceFileStore)) as
            ISpaceFileStore ?? throw Failure(
                SpaceJobFailureKind.Resource,
                SpaceErrorCodes.JobProcessorUnavailable,
                "Private Space file storage is not configured.");
        var writes = services.GetService(typeof(ISpaceQuarantineStore)) as
            ISpaceQuarantineStore ?? throw Failure(
                SpaceJobFailureKind.Resource,
                SpaceErrorCodes.JobProcessorUnavailable,
                "Private Space artifact storage is not configured.");

        var existing = await LoadExistingMatchArtifactAsync(
            lease.JobId,
            cancellationToken);
        if (existing is not null)
        {
            var existingValue = await ReadMatchArtifactAsync(
                existing.File,
                files,
                cancellationToken);
            ValidateExisting(input, existing.Artifact, existingValue);
            return Output(Checkpoint(
                existing.Artifact,
                existing.File,
                existingValue));
        }

        var previewSet = await LoadPreviewSetAsync(
            input,
            files,
            cancellationToken);
        SpaceExcelWorkbookData workbook;
        await using (var content = await files.OpenQuarantinedReadAsync(
                         input.ExcelFile.TenantId,
                         input.ExcelFile.Id,
                         input.ExcelFile.StorageKey,
                         cancellationToken))
        {
            workbook = await workbookReader.ReadAsync(content, cancellationToken);
        }

        SpaceExcelMappingProfileDto profile;
        try
        {
            profile = await mappings.GetProfileAsync(
                input.PreflightPayload.MappingProfileId,
                input.PreflightPayload.MappingProfileVersion,
                cancellationToken);
        }
        catch (SpaceProblemException)
        {
            throw new InvalidDataException(
                "The pinned Excel mapping profile no longer exists.");
        }
        if (!profile.DefinitionHash.Equals(
                input.PreflightPayload.MappingDefinitionHash,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The pinned Excel mapping definition hash changed.");
        }

        var editor = await LoadEditorSnapshotAsync(input, cancellationToken);
        var preview = SpaceExcelCadMatching.Build(
            input.Job.TenantId,
            input.Payload.ModelVersionId,
            input.Payload.ExcelSourceId,
            input.Payload.PreflightJobId,
            profile,
            workbook,
            previewSet.Value.SemanticPreview,
            previewSet.Value.DiagnosticIndex,
            editor);
        var value = SpaceExcelCadMatchArtifact.Create(
            input.Job.TenantId,
            input.Job.Id,
            input.Payload,
            previewSet.Artifact.Id,
            input.Job.RequestedBy,
            input.Job.RequestedAtUtc,
            preview);
        var json = SpaceExcelCadMatchArtifact.Serialize(value);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (bytes.LongLength is < 1 or > MaximumArtifactBytes)
        {
            throw new InvalidDataException(
                "The generated Match Artifact exceeds the supported size.");
        }
        var sha256 = Hash(bytes);

        IDbContextTransaction? transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        (Guid FileId, string StorageKey)? committed = null;
        try
        {
            var file = context.Files.Local.FirstOrDefault(item =>
                           item.Sha256 == sha256 &&
                           item.RetentionClass == SpaceFileRetentionClass.Artifact &&
                           item.State == SpaceFileState.Clean) ??
                       await context.Files.SingleOrDefaultAsync(
                           item =>
                               item.Sha256 == sha256 &&
                               item.RetentionClass == SpaceFileRetentionClass.Artifact &&
                               item.State == SpaceFileState.Clean,
                           cancellationToken);
            if (file is null)
            {
                file = await PersistFileAsync(
                    lease.TenantId,
                    bytes,
                    sha256,
                    writes,
                    cancellationToken);
                committed = (file.Id, file.StorageKey);
                context.Files.Add(file);
            }

            var artifact = SpaceArtifact.Create(
                lease.TenantId,
                input.Payload.ModelVersionId,
                input.ExcelSource,
                file,
                SpaceArtifactType.ExcelCadMatchPreview,
                SpaceExcelCadMatchArtifactVersions.ArtifactSchema);
            artifact.AttachToJob(input.Job);
            context.Artifacts.Add(artifact);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return Output(Checkpoint(artifact, file, value));
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            if (committed.HasValue)
            {
                try
                {
                    await files.DeleteAsync(
                        lease.TenantId,
                        committed.Value.FileId,
                        committed.Value.StorageKey,
                        CancellationToken.None);
                }
                catch
                {
                    // Retention cleanup remains the fallback for orphaned objects.
                }
            }
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private async Task<MatchInput> LoadInputAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var job = await context.Jobs.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == lease.JobId,
            cancellationToken) ?? throw Failure(
            SpaceJobFailureKind.Input,
            SpaceErrorCodes.ExcelCadMatchNotFound,
            "The Excel/CAD match Job was not found.");
        var payload = Deserialize<SpaceExcelCadMatchJobPayload>(
            job.PayloadJson,
            SpaceErrorCodes.ExcelCadMatchInvalid,
            "The frozen Excel/CAD match payload is invalid.");
        if (payload.SchemaVersion !=
                SpaceExcelCadMatchArtifactVersions.SchemaVersion ||
            payload.ExcelSourceId != lease.SubjectId ||
            payload.ModelVersionId == Guid.Empty ||
            payload.PreflightJobId == Guid.Empty ||
            payload.CadSourceId == Guid.Empty ||
            payload.CadParseJobId == Guid.Empty ||
            payload.FloorLogicalId == Guid.Empty ||
            payload.ExpectedContentRevision < 0 ||
            !Hash(job.PayloadJson).Equals(lease.InputHash, StringComparison.Ordinal))
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ExcelCadMatchInvalid,
                "The frozen Excel/CAD match payload is invalid.");
        }

        var version = await context.Versions.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == payload.ModelVersionId,
            cancellationToken) ?? throw InputMissing("The model version was not found.");
        if (version.Status != SpaceVersionStatus.Draft ||
            version.ContentRevision != payload.ExpectedContentRevision)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ConcurrencyConflict,
                "The Draft content revision changed before matching executed.");
        }

        var sources = await context.Sources.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == payload.ModelVersionId &&
                (item.Id == payload.ExcelSourceId ||
                 item.Id == payload.CadSourceId))
            .ToArrayAsync(cancellationToken);
        var excel = sources.SingleOrDefault(item => item.Id == payload.ExcelSourceId)
            ?? throw InputMissing("The Excel source was not found.");
        var cad = sources.SingleOrDefault(item => item.Id == payload.CadSourceId)
            ?? throw InputMissing("The CAD source was not found.");
        if (excel.SourceType != SpaceSourceType.Excel ||
            excel.State != SpaceSourceState.PreviewReady ||
            excel.ParserVersion != SpaceExcelPreflightJobProcessor.Version ||
            excel.FileId is null ||
            cad.SourceType is not (SpaceSourceType.Dwg or SpaceSourceType.Dxf) ||
            cad.State != SpaceSourceState.PreviewReady ||
            cad.ParserVersion != SpaceCadParseJobProcessor.Version ||
            cad.FileId is null)
        {
            throw InputInvalid(
                "The selected sources no longer match the frozen authoritative chain.");
        }

        var sourceFiles = await context.Files.AsNoTracking()
            .Where(item => item.Id == excel.FileId || item.Id == cad.FileId)
            .ToArrayAsync(cancellationToken);
        var excelFile = sourceFiles.SingleOrDefault(item => item.Id == excel.FileId)
            ?? throw InputMissing("The Excel source file was not found.");
        var cadFile = sourceFiles.SingleOrDefault(item => item.Id == cad.FileId)
            ?? throw InputMissing("The CAD source file was not found.");
        if (!IsClean(excelFile) || !IsClean(cadFile))
            throw InputInvalid("A frozen source file is no longer clean.");

        var jobs = await context.Jobs.AsNoTracking()
            .Where(item =>
                item.Id == payload.PreflightJobId ||
                item.Id == payload.CadParseJobId)
            .ToArrayAsync(cancellationToken);
        var preflight = jobs.SingleOrDefault(item => item.Id == payload.PreflightJobId)
            ?? throw InputMissing("The Excel preflight Job was not found.");
        var cadParse = jobs.SingleOrDefault(item => item.Id == payload.CadParseJobId)
            ?? throw InputMissing("The CAD parse Job was not found.");
        var preflightPayload = Deserialize<SpaceExcelPreflightJobPayload>(
            preflight.PayloadJson,
            SpaceErrorCodes.ExcelCadMatchInvalid,
            "The frozen Excel preflight payload is invalid.");
        var cadPayload = Deserialize<SpaceCadParseJobPayload>(
            cadParse.PayloadJson,
            SpaceErrorCodes.ExcelCadMatchInvalid,
            "The frozen CAD parse payload is invalid.");
        if (preflight.JobType != SpaceJobType.ExcelPreview ||
            preflight.SubjectId != excel.Id ||
            preflight.Status != SpaceJobStatus.Succeeded ||
            preflightPayload.ModelVersionId != payload.ModelVersionId ||
            preflightPayload.SourceId != excel.Id ||
            excel.MappingProfileId != preflightPayload.MappingProfileId ||
            excel.MappingProfileVersion != preflightPayload.MappingProfileVersion ||
            cadParse.JobType != SpaceJobType.CadParse ||
            cadParse.SubjectId != cad.Id ||
            cadParse.Status != SpaceJobStatus.Succeeded ||
            cadPayload.ModelVersionId != payload.ModelVersionId ||
            cadPayload.SourceId != cad.Id ||
            cadPayload.FileId != cad.FileId ||
            cadPayload.FloorLogicalId != payload.FloorLogicalId ||
            !cadPayload.SourceSha256.Equals(cadFile.Sha256, StringComparison.Ordinal))
        {
            throw InputInvalid(
                "The selected Jobs no longer match the frozen source chain.");
        }
        var blocking = await context.Issues.AsNoTracking().AnyAsync(
            item =>
                item.JobId == preflight.Id &&
                item.Severity == SpaceIssueSeverity.Blocking,
            cancellationToken);
        if (blocking)
            throw InputInvalid("The Excel preflight has blocking issues.");

        return new MatchInput(
            job,
            payload,
            version,
            excel,
            excelFile,
            preflightPayload,
            cad,
            cadParse,
            cadPayload);
    }

    private async Task<PreviewSetInput> LoadPreviewSetAsync(
        MatchInput input,
        ISpaceFileStore files,
        CancellationToken cancellationToken)
    {
        var producerIds = input.CadParseJob.RetryOfJobId.HasValue
            ? new[] { input.CadParseJob.Id, input.CadParseJob.RetryOfJobId.Value }
            : new[] { input.CadParseJob.Id };
        var candidates = await (
                from artifact in context.Artifacts.AsNoTracking()
                join file in context.Files.AsNoTracking()
                    on artifact.FileId equals file.Id
                where artifact.JobId.HasValue &&
                      producerIds.Contains(artifact.JobId.Value) &&
                      artifact.ArtifactType == SpaceArtifactType.PreviewSet
                select new PersistedArtifact(artifact, file))
            .ToArrayAsync(cancellationToken);
        var current = candidates.Where(item =>
            item.Artifact.JobId == input.CadParseJob.Id).ToArray();
        var parent = candidates.Where(item =>
            item.Artifact.JobId != input.CadParseJob.Id).ToArray();
        if (current.Length > 1 || parent.Length > 1)
        {
            throw InputInvalid(
                "The CAD parse has duplicate authoritative PreviewSet artifacts.");
        }
        var persisted = current.SingleOrDefault() ?? parent.SingleOrDefault();
        if (persisted is null ||
            persisted.Artifact.ModelVersionId != input.Payload.ModelVersionId ||
            persisted.Artifact.SourceId != input.CadSource.Id ||
            persisted.Artifact.SchemaVersion != SpaceCadPreviewSetVersions.ArtifactSchema ||
            !IsClean(persisted.File))
        {
            throw InputInvalid(
                "The CAD parse has no valid authoritative PreviewSet artifact.");
        }
        var json = await ReadVerifiedTextAsync(persisted.File, files, cancellationToken);
        var value = SpaceCadPreviewSet.Deserialize(json);
        if (value.TenantId != input.Job.TenantId ||
            value.ModelVersionId != input.Payload.ModelVersionId ||
            value.SourceId != input.Payload.CadSourceId ||
            value.CadParseJobId != persisted.Artifact.JobId ||
            value.FloorLogicalId != input.Payload.FloorLogicalId ||
            !value.SourceSha256.Equals(
                input.CadParsePayload.SourceSha256,
                StringComparison.Ordinal) ||
            !value.CoordinateTransformSha256.Equals(
                input.CadParsePayload.CoordinateTransformSha256,
                StringComparison.Ordinal) ||
            !value.MappingPreviewSha256.Equals(
                input.CadParsePayload.MappingPreviewSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The CAD PreviewSet does not match its frozen parse Job.");
        }
        return new PreviewSetInput(persisted.Artifact, value);
    }

    private async Task<PersistedArtifact?> LoadExistingMatchArtifactAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from artifact in context.Artifacts.AsNoTracking()
                join file in context.Files.AsNoTracking()
                    on artifact.FileId equals file.Id
                where artifact.JobId == jobId &&
                      artifact.ArtifactType ==
                          SpaceArtifactType.ExcelCadMatchPreview
                select new PersistedArtifact(artifact, file))
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (rows.Length > 1)
        {
            throw Failure(
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.ExcelCadMatchArtifactInvalid,
                "The Match Job has duplicate authoritative artifacts.");
        }
        return rows.SingleOrDefault();
    }

    private static void ValidateExisting(
        MatchInput input,
        SpaceArtifact artifact,
        SpaceExcelCadMatchArtifactV1 value)
    {
        if (artifact.ModelVersionId != input.Payload.ModelVersionId ||
            artifact.SourceId != input.Payload.ExcelSourceId ||
            artifact.SchemaVersion !=
                SpaceExcelCadMatchArtifactVersions.ArtifactSchema ||
            value.TenantId != input.Job.TenantId ||
            value.MatchJobId != input.Job.Id ||
            value.ModelVersionId != input.Payload.ModelVersionId ||
            value.ExcelSourceId != input.Payload.ExcelSourceId ||
            value.PreflightJobId != input.Payload.PreflightJobId ||
            value.CadSourceId != input.Payload.CadSourceId ||
            value.CadParseJobId != input.Payload.CadParseJobId ||
            value.FloorLogicalId != input.Payload.FloorLogicalId ||
            value.ExpectedContentRevision != input.Payload.ExpectedContentRevision)
        {
            throw new InvalidDataException(
                "The persisted Match Artifact does not match its frozen Job.");
        }
    }

    private async Task<SpaceExcelEditorSnapshotV1> LoadEditorSnapshotAsync(
        MatchInput input,
        CancellationToken cancellationToken)
    {
        var floor = await context.FloorRevisions.AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.ModelVersionId == input.Payload.ModelVersionId &&
                    item.LogicalId == input.Payload.FloorLogicalId,
                cancellationToken);
        if (floor is null ||
            input.Version.Status != SpaceVersionStatus.Draft ||
            input.Version.ContentRevision != input.Payload.ExpectedContentRevision)
        {
            throw new InvalidDataException(
                "The authoritative editor scene changed before matching executed.");
        }
        var zones = await context.ZoneRevisions.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == input.Payload.ModelVersionId &&
                item.FloorLogicalId == input.Payload.FloorLogicalId)
            .ToDictionaryAsync(
                item => item.LogicalId,
                item => item.ZoneCode,
                cancellationToken);
        var rackRows = await context.RackRevisions.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == input.Payload.ModelVersionId &&
                item.FloorLogicalId == input.Payload.FloorLogicalId)
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
        return SpaceExcelCadMatching.SealEditorSnapshot(
            input.Job.TenantId,
            input.Payload.ModelVersionId,
            input.Payload.FloorLogicalId,
            floor.FloorCode,
            input.Version.ContentRevision,
            input.Version.ContentHash,
            racks);
    }

    private async Task<SpaceExcelCadMatchArtifactV1> ReadMatchArtifactAsync(
        SpaceFile file,
        ISpaceFileStore files,
        CancellationToken cancellationToken) =>
        SpaceExcelCadMatchArtifact.Deserialize(
            await ReadVerifiedTextAsync(file, files, cancellationToken));

    private static async Task<string> ReadVerifiedTextAsync(
        SpaceFile file,
        ISpaceFileStore files,
        CancellationToken cancellationToken)
    {
        if (!IsClean(file) || file.SizeBytes is < 1 or > MaximumArtifactBytes)
            throw new InvalidDataException("The artifact file is not readable.");
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
            throw new InvalidDataException("The artifact file hash or size changed.");
        }
        return json;
    }

    private static async Task<SpaceFile> PersistFileAsync(
        Guid tenantId,
        byte[] bytes,
        string sha256,
        ISpaceQuarantineStore writes,
        CancellationToken cancellationToken)
    {
        var fileId = Guid.NewGuid();
        await using var session = await writes.OpenWriteAsync(
            tenantId,
            fileId,
            cancellationToken);
        try
        {
            await session.Content.WriteAsync(bytes, cancellationToken);
            await session.CommitAsync(cancellationToken);
            var file = SpaceFile.CreateUploading(
                fileId,
                tenantId,
                session.StorageKey,
                $"excel-cad-match-{fileId:N}.json",
                "application/json",
                SpaceFileRetentionClass.Artifact);
            file.CompleteQuarantine(
                "application/json",
                ".json",
                bytes.LongLength,
                sha256);
            file.BeginScanning();
            file.MarkClean(
                ArtifactScanEngine,
                SpaceExcelCadMatchJobProcessor.Version,
                "TRUSTED_GENERATED_ARTIFACT");
            return file;
        }
        catch
        {
            await session.AbortAsync(CancellationToken.None);
            throw;
        }
    }

    private static object Checkpoint(
        SpaceArtifact artifact,
        SpaceFile file,
        SpaceExcelCadMatchArtifactV1 value) => new
        {
            schemaVersion = SpaceExcelCadMatchArtifactVersions.SchemaVersion,
            artifactId = artifact.Id,
            fileId = file.Id,
            value.ArtifactPayloadSha256,
            fileSha256 = file.Sha256,
            value.Preview.MatchPreviewSha256,
            value.Preview.Summary.ExcelRackRowCount,
            value.Preview.Summary.NewCount,
            value.Preview.Summary.UpdateCount,
            value.Preview.Summary.UnchangedCount,
            value.Preview.Summary.UnmatchedCount,
            value.Preview.Summary.ConflictCount,
            value.Preview.Summary.ErrorCount,
            value.Preview.CanConfirm,
        };

    private static SpaceJobStepOutput Output(object checkpoint)
    {
        var json = JsonSerializer.Serialize(checkpoint, JsonOptions);
        return new SpaceJobStepOutput(json, Hash(json));
    }

    private static T Deserialize<T>(string json, string code, string message)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                   ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw Failure(SpaceJobFailureKind.Input, code, message);
        }
    }

    private static void EnsureLease(SpaceJobLease lease)
    {
        if (lease.TenantId == Guid.Empty ||
            lease.JobType != SpaceJobType.ExcelCadMatch ||
            lease.SubjectType != SpaceJobSubjectType.ModelSource ||
            lease.SubjectId == Guid.Empty)
        {
            throw Failure(
                SpaceJobFailureKind.Bug,
                "SPACE_EXCEL_CAD_MATCH_LEASE_INVALID",
                "The Excel/CAD match Job lease is invalid.");
        }
    }

    private static bool IsClean(SpaceFile file) =>
        file.State == SpaceFileState.Clean &&
        !file.IsDeleted &&
        file.SizeBytes >= 0 &&
        IsSha256(file.Sha256);

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character => Uri.IsHexDigit(character) && !char.IsUpper(character));

    private static string Hash(string value) =>
        Hash(Encoding.UTF8.GetBytes(value));

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static string Sanitize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 1000 ? normalized : normalized[..1000];
    }

    private static SpaceJobProcessingException InputMissing(string message) =>
        Failure(
            SpaceJobFailureKind.Input,
            SpaceErrorCodes.ExcelCadMatchNotFound,
            message);

    private static SpaceJobProcessingException InputInvalid(string message) =>
        Failure(
            SpaceJobFailureKind.Input,
            SpaceErrorCodes.ExcelCadMatchInvalid,
            message);

    private static SpaceJobProcessingException Failure(
        SpaceJobFailureKind kind,
        string code,
        string message) => new(kind, code, message);

    private sealed record MatchInput(
        SpaceJob Job,
        SpaceExcelCadMatchJobPayload Payload,
        SpaceModelVersion Version,
        SpaceModelSource ExcelSource,
        SpaceFile ExcelFile,
        SpaceExcelPreflightJobPayload PreflightPayload,
        SpaceModelSource CadSource,
        SpaceJob CadParseJob,
        SpaceCadParseJobPayload CadParsePayload);

    private sealed record PersistedArtifact(
        SpaceArtifact Artifact,
        SpaceFile File);

    private sealed record PreviewSetInput(
        SpaceArtifact Artifact,
        SpaceCadPreviewSetV1 Value);
}
