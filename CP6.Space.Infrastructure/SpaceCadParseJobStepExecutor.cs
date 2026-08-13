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

public sealed class UnavailableSpaceCadParseProvider : ISpaceCadParseProvider
{
    public Task<IReadOnlyList<SpaceCadGeneratedArtifact>> GenerateAsync(
        SpaceCadParseProviderRequest request,
        Stream source,
        CancellationToken cancellationToken = default) =>
        throw new SpaceJobProcessingException(
            SpaceJobFailureKind.Resource,
            SpaceErrorCodes.JobProcessorUnavailable,
            "A production CAD conversion provider is not configured.");
}

public sealed class SpaceCadParseJobStepExecutor(
    SpaceContext context,
    IServiceProvider services,
    ISpaceCadParseProvider provider) : ISpaceCadParseJobStepExecutor
{
    private const long MaximumArtifactBytes = 200L * 1024L * 1024L;
    private const string ArtifactScanEngine = "cp6-internal-artifact";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly SpaceArtifactType[] RequiredArtifactTypes =
    [
        SpaceArtifactType.CadIr,
        SpaceArtifactType.LayerInventory,
        SpaceArtifactType.PreviewSet,
    ];

    public async Task<SpaceJobStepOutput> ExecuteAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);
        EnsureLease(execution.Lease);
        return execution.StepCode switch
        {
            SpaceCadParseJobProcessor.GenerateArtifacts =>
                await GenerateArtifactsAsync(execution, cancellationToken),
            SpaceCadParseJobProcessor.FinalizePreview =>
                await FinalizePreviewAsync(execution, cancellationToken),
            _ => throw Failure(
                SpaceJobFailureKind.Bug,
                "SPACE_CAD_PARSE_STEP_INVALID",
                "The CAD parse Job step is invalid."),
        };
    }

    private async Task<SpaceJobStepOutput> GenerateArtifactsAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken)
    {
        var input = await LoadInputAsync(execution.Lease, cancellationToken);
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
        var existing = await LoadArtifactsAsync(
            execution.Lease.JobId,
            input.Job.RetryOfJobId,
            cancellationToken);
        if (existing.Count != 0)
            return Output(ValidateExistingArtifacts(input.Payload, existing));

        await using var source = await files.OpenQuarantinedReadAsync(
            input.File.TenantId,
            input.File.Id,
            input.File.StorageKey,
            cancellationToken);
        IReadOnlyList<SpaceCadGeneratedArtifact> generated;
        try
        {
            generated = await provider.GenerateAsync(
                new SpaceCadParseProviderRequest(
                    execution.Lease.TenantId,
                    execution.Lease.JobId,
                    input.Payload),
                source,
                cancellationToken);
        }
        catch (SpaceJobProcessingException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ParseFailed,
                Sanitize(exception.Message, "The CAD source could not be parsed."));
        }
        catch (IOException)
        {
            throw Failure(
                SpaceJobFailureKind.Resource,
                SpaceErrorCodes.JobProcessorFailed,
                "The CAD source or generated artifacts could not be read.");
        }

        ValidateGeneratedArtifacts(generated);
        IDbContextTransaction? transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        var committedObjects = new List<(Guid FileId, string StorageKey)>();
        try
        {
            var rows = new List<PersistedArtifact>(generated.Count);
            foreach (var artifact in generated.OrderBy(item => item.ArtifactType))
            {
                var file = context.Files.Local.FirstOrDefault(item =>
                               item.Sha256 == artifact.Sha256 &&
                               item.RetentionClass ==
                                   SpaceFileRetentionClass.Artifact &&
                               item.State == SpaceFileState.Clean) ??
                           await context.Files.SingleOrDefaultAsync(
                               item =>
                                   item.Sha256 == artifact.Sha256 &&
                                   item.RetentionClass ==
                                       SpaceFileRetentionClass.Artifact &&
                                   item.State == SpaceFileState.Clean,
                               cancellationToken);
                if (file is null)
                {
                    file = await PersistFileAsync(
                        execution.Lease.TenantId,
                        artifact,
                        writes,
                        committedObjects,
                        cancellationToken);
                    context.Files.Add(file);
                }

                var row = SpaceArtifact.Create(
                    execution.Lease.TenantId,
                    input.Source.ModelVersionId,
                    input.Source,
                    file,
                    artifact.ArtifactType,
                    artifact.SchemaVersion);
                row.AttachToJob(input.Job);
                context.Artifacts.Add(row);
                rows.Add(new PersistedArtifact(row, file));
            }

            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return Output(Checkpoint(input.Payload, rows));
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            foreach (var item in committedObjects)
            {
                try
                {
                    await files.DeleteAsync(
                        execution.Lease.TenantId,
                        item.FileId,
                        item.StorageKey,
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

    private async Task<SpaceJobStepOutput> FinalizePreviewAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken)
    {
        var input = await LoadInputAsync(execution.Lease, cancellationToken);
        var generateStep = await context.JobSteps.AsNoTracking()
            .SingleOrDefaultAsync(
                step =>
                    step.AttemptId == execution.Lease.AttemptId &&
                    step.StepCode == SpaceCadParseJobProcessor.GenerateArtifacts &&
                    (step.Status == SpaceJobStepStatus.Succeeded ||
                     step.Status == SpaceJobStepStatus.Reused),
                cancellationToken)
            ?? throw Failure(
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.CadParseArtifactInvalid,
                "The CAD artifact checkpoint is missing.");
        var checkpoint = DeserializeCheckpoint(generateStep.CheckpointJson);
        await ValidateCheckpointArtifactsAsync(checkpoint, cancellationToken);

        if (input.Source.State == SpaceSourceState.Ready)
        {
            input.Source.BeginParsing();
            input.Source.MarkPreviewReady();
            await context.SaveChangesAsync(cancellationToken);
        }
        else if (input.Source.State != SpaceSourceState.PreviewReady)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.CadParseInvalid,
                "The CAD source is no longer in a parseable state.");
        }

        return new SpaceJobStepOutput(
            generateStep.CheckpointJson!,
            generateStep.OutputHash!);
    }

    private async Task<ParseInput> LoadInputAsync(
        SpaceJobLease lease,
        CancellationToken cancellationToken)
    {
        var job = await context.Jobs.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == lease.JobId, cancellationToken)
            ?? throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.CadParseNotFound,
                "The CAD parse Job was not found.");
        var payload = DeserializePayload(job.PayloadJson);
        if (payload.SchemaVersion != SpaceCadParsePayloadVersions.Current ||
            payload.SourceId != lease.SubjectId ||
            payload.ModelVersionId == Guid.Empty ||
            payload.FileId == Guid.Empty ||
            payload.FloorLogicalId == Guid.Empty ||
            !IsSha256(payload.SourceSha256) ||
            !IsSha256(payload.CoordinateTransformSha256) ||
            payload.MappingProfileId == Guid.Empty ||
            payload.MappingProfileVersion <= 0 ||
            !IsSha256(payload.MappingDefinitionSha256) ||
            !IsSha256(payload.MappingPreviewSha256) ||
            payload.BaseContentRevision < 0 ||
            payload.BaseContentHash is not null &&
                !IsSha256(payload.BaseContentHash) ||
            !Hash(job.PayloadJson).Equals(lease.InputHash, StringComparison.Ordinal))
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.CadParseInvalid,
                "The frozen CAD parse payload is invalid.");
        }

        var source = await context.Sources.SingleOrDefaultAsync(
            item =>
                item.Id == payload.SourceId &&
                item.ModelVersionId == payload.ModelVersionId,
            cancellationToken)
            ?? throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.SourceNotFound,
                "The CAD source was not found.");
        var expectedType = payload.SourceFormat == SpaceCadSourceFormat.Dwg
            ? SpaceSourceType.Dwg
            : SpaceSourceType.Dxf;
        if (source.SourceType != expectedType ||
            source.FileId != payload.FileId ||
            !source.Sha256.Equals(payload.SourceSha256, StringComparison.Ordinal) ||
            source.ParserVersion != SpaceCadParseJobProcessor.Version ||
            source.MappingProfileId != payload.MappingProfileId ||
            source.MappingProfileVersion != payload.MappingProfileVersion ||
            source.Unit != payload.ConfirmedUnit.ToString() ||
            source.ScaleToMillimeters != payload.ConfirmedScaleToMillimeters ||
            source.TransformJson != payload.CoordinateMetadataJson ||
            source.State is not (SpaceSourceState.Ready or SpaceSourceState.PreviewReady))
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.CadParseInvalid,
                "The CAD source no longer matches the frozen parse input.");
        }

        var file = await context.Files.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == payload.FileId,
            cancellationToken)
            ?? throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.FileNotFound,
                "The CAD source file was not found.");
        if (file.State != SpaceFileState.Clean || file.IsDeleted ||
            !string.Equals(file.Sha256, payload.SourceSha256, StringComparison.Ordinal))
        {
            throw Failure(
                SpaceJobFailureKind.Security,
                SpaceErrorCodes.SourceUnsafe,
                "The CAD source file is not clean or no longer matches its hash.");
        }
        return new ParseInput(job, payload, source, file);
    }

    private async Task<SpaceFile> PersistFileAsync(
        Guid tenantId,
        SpaceCadGeneratedArtifact artifact,
        ISpaceQuarantineStore writes,
        ICollection<(Guid FileId, string StorageKey)> committedObjects,
        CancellationToken cancellationToken)
    {
        var fileId = Guid.NewGuid();
        await using var session = await writes.OpenWriteAsync(
            tenantId,
            fileId,
            cancellationToken);
        try
        {
            await using var content = await artifact.OpenReadAsync(cancellationToken);
            if (content is null || !content.CanRead)
                throw new InvalidDataException("The generated CAD artifact is unreadable.");
            var (size, sha256) = await CopyAndHashAsync(
                content,
                session.Content,
                cancellationToken);
            if (size != artifact.SizeBytes ||
                !sha256.Equals(artifact.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The generated CAD artifact does not match its declared hash and size.");
            }
            await session.CommitAsync(cancellationToken);
            committedObjects.Add((fileId, session.StorageKey));

            var file = SpaceFile.CreateUploading(
                fileId,
                tenantId,
                session.StorageKey,
                artifact.FileName,
                artifact.ContentType,
                SpaceFileRetentionClass.Artifact);
            file.CompleteQuarantine(
                artifact.ContentType,
                artifact.Extension,
                size,
                sha256);
            file.BeginScanning();
            file.MarkClean(
                ArtifactScanEngine,
                SpaceCadParseJobProcessor.Version,
                "TRUSTED_GENERATED_ARTIFACT");
            return file;
        }
        catch
        {
            await session.AbortAsync(CancellationToken.None);
            throw;
        }
    }

    private Task<List<PersistedArtifact>> LoadArtifactsAsync(
        Guid jobId,
        Guid? retryOfJobId,
        CancellationToken cancellationToken) =>
        (from artifact in context.Artifacts.AsNoTracking()
         join file in context.Files.AsNoTracking()
             on artifact.FileId equals file.Id
         where artifact.JobId == jobId ||
               (retryOfJobId.HasValue && artifact.JobId == retryOfJobId.Value)
         select new PersistedArtifact(artifact, file))
        .ToListAsync(cancellationToken);

    private static CadParseCheckpoint ValidateExistingArtifacts(
        SpaceCadParseJobPayload payload,
        IReadOnlyList<PersistedArtifact> artifacts)
    {
        if (artifacts.Count != RequiredArtifactTypes.Length ||
            artifacts.Select(item => item.Artifact.ArtifactType)
                .OrderBy(item => item)
                .SequenceEqual(RequiredArtifactTypes.OrderBy(item => item)) == false ||
            artifacts.Any(item =>
                item.File.State != SpaceFileState.Clean ||
                !IsSha256(item.File.Sha256 ?? string.Empty)))
        {
            throw Failure(
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.CadParseArtifactInvalid,
                "The persisted CAD artifact set is incomplete or inconsistent.");
        }
        return Checkpoint(payload, artifacts);
    }

    private static void ValidateGeneratedArtifacts(
        IReadOnlyList<SpaceCadGeneratedArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        if (artifacts.Count != RequiredArtifactTypes.Length ||
            artifacts.Select(item => item.ArtifactType).Distinct().Count() !=
                RequiredArtifactTypes.Length ||
            !artifacts.Select(item => item.ArtifactType).OrderBy(item => item)
                .SequenceEqual(RequiredArtifactTypes.OrderBy(item => item)))
        {
            throw Failure(
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.CadParseArtifactInvalid,
                "The CAD provider did not return the required artifact set.");
        }
        foreach (var artifact in artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.SchemaVersion) ||
                artifact.SchemaVersion.Length > 50 ||
                string.IsNullOrWhiteSpace(artifact.FileName) ||
                artifact.FileName.Length > 260 ||
                string.IsNullOrWhiteSpace(artifact.ContentType) ||
                artifact.ContentType.Length > 200 ||
                string.IsNullOrWhiteSpace(artifact.Extension) ||
                artifact.Extension.Length > 20 ||
                artifact.SizeBytes < 0 ||
                artifact.SizeBytes > MaximumArtifactBytes ||
                !IsSha256(artifact.Sha256))
            {
                throw Failure(
                    SpaceJobFailureKind.Bug,
                    SpaceErrorCodes.CadParseArtifactInvalid,
                    "The CAD provider returned invalid artifact metadata.");
            }
        }
    }

    private async Task ValidateCheckpointArtifactsAsync(
        CadParseCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        if (checkpoint.SchemaVersion != 1 ||
            checkpoint.Artifacts.Count != RequiredArtifactTypes.Length)
        {
            throw Failure(
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.CadParseArtifactInvalid,
                "The CAD artifact checkpoint is invalid.");
        }
        var ids = checkpoint.Artifacts.Select(item => item.ArtifactId).ToArray();
        var persisted = await (
                from artifact in context.Artifacts.AsNoTracking()
                join file in context.Files.AsNoTracking()
                    on artifact.FileId equals file.Id
                where ids.Contains(artifact.Id)
                select new { artifact, file })
            .ToListAsync(cancellationToken);
        if (persisted.Count != ids.Length || persisted.Any(item =>
                item.file.State != SpaceFileState.Clean ||
                !checkpoint.Artifacts.Any(expected =>
                    expected.ArtifactId == item.artifact.Id &&
                    expected.FileId == item.file.Id &&
                    expected.ArtifactType == item.artifact.ArtifactType &&
                    expected.Sha256 == item.file.Sha256)))
        {
            throw Failure(
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.CadParseArtifactInvalid,
                "The CAD artifact checkpoint no longer matches private storage metadata.");
        }
    }

    private static CadParseCheckpoint Checkpoint(
        SpaceCadParseJobPayload payload,
        IReadOnlyList<PersistedArtifact> artifacts) =>
        new(
            1,
            payload.SourceSha256,
            payload.CoordinateTransformSha256,
            payload.MappingDefinitionSha256,
            payload.MappingPreviewSha256,
            artifacts.OrderBy(item => item.Artifact.ArtifactType)
                .Select(item => new CadParseCheckpointArtifact(
                    item.Artifact.Id,
                    item.File.Id,
                    item.Artifact.ArtifactType,
                    item.Artifact.SchemaVersion,
                    item.File.Sha256!,
                    item.File.SizeBytes))
                .ToArray());

    private static SpaceJobStepOutput Output(CadParseCheckpoint checkpoint)
    {
        var json = JsonSerializer.Serialize(checkpoint, JsonOptions);
        return new SpaceJobStepOutput(json, Hash(json));
    }

    private static SpaceCadParseJobPayload DeserializePayload(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SpaceCadParseJobPayload>(
                       json,
                       JsonOptions) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.CadParseInvalid,
                "The frozen CAD parse payload is invalid.");
        }
    }

    private static CadParseCheckpoint DeserializeCheckpoint(string? json)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new JsonException();
            return JsonSerializer.Deserialize<CadParseCheckpoint>(
                       json,
                       JsonOptions) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw Failure(
                SpaceJobFailureKind.Bug,
                SpaceErrorCodes.CadParseArtifactInvalid,
                "The CAD artifact checkpoint is invalid.");
        }
    }

    private static async Task<(long Size, string Sha256)> CopyAndHashAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        long size = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            size = checked(size + read);
            if (size > MaximumArtifactBytes)
                throw new InvalidDataException("The generated CAD artifact is too large.");
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await destination.FlushAsync(cancellationToken);
        return (
            size,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static void EnsureLease(SpaceJobLease lease)
    {
        if (lease.TenantId == Guid.Empty ||
            lease.JobType != SpaceJobType.CadParse ||
            lease.SubjectType != SpaceJobSubjectType.ModelSource ||
            lease.SubjectId == Guid.Empty)
        {
            throw Failure(
                SpaceJobFailureKind.Bug,
                "SPACE_CAD_PARSE_LEASE_INVALID",
                "The CAD parse Job lease is invalid.");
        }
    }

    private static bool IsSha256(string value) =>
        value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string Sanitize(string value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= 1000 ? normalized : normalized[..1000];
    }

    private static SpaceJobProcessingException Failure(
        SpaceJobFailureKind kind,
        string code,
        string message) => new(kind, code, message);

    private sealed record ParseInput(
        SpaceJob Job,
        SpaceCadParseJobPayload Payload,
        SpaceModelSource Source,
        SpaceFile File);

    private sealed record PersistedArtifact(
        SpaceArtifact Artifact,
        SpaceFile File);

    private sealed record CadParseCheckpoint(
        int SchemaVersion,
        string SourceSha256,
        string CoordinateTransformSha256,
        string MappingDefinitionSha256,
        string MappingPreviewSha256,
        IReadOnlyList<CadParseCheckpointArtifact> Artifacts);

    private sealed record CadParseCheckpointArtifact(
        Guid ArtifactId,
        Guid FileId,
        SpaceArtifactType ArtifactType,
        string SchemaVersion,
        string Sha256,
        long SizeBytes);
}
