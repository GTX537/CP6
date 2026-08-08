using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class SpaceExcelCadApplyJobStepExecutor(
    SpaceContext context,
    IServiceProvider services,
    ISpaceExcelWorkbookReader workbookReader,
    ISpaceExcelMappingService mappings,
    ISpaceClock clock) : ISpaceExcelCadApplyJobStepExecutor
{
    private const long MaximumArtifactBytes = 200L * 1024L * 1024L;
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
            return await ExecuteCoreAsync(execution, cancellationToken);
        }
        catch (SpaceJobProcessingException)
        {
            throw;
        }
        catch (DbUpdateException exception)
        {
            throw Failure(
                SpaceJobFailureKind.Resource,
                SpaceErrorCodes.ConcurrencyConflict,
                Sanitize(exception.InnerException?.Message ?? exception.Message,
                    "A concurrent editor write prevented Excel/CAD Apply."));
        }
        catch (Exception exception) when (
            exception is InvalidDataException or ArgumentException or
                InvalidOperationException or SpaceVersionConflictException or
                SpaceVersionStateException or SpaceFileStateException)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ExcelCadApplyInvalid,
                Sanitize(exception.Message,
                    "The confirmed Excel/CAD Match Artifact is invalid."));
        }
    }

    private async Task<SpaceJobStepOutput> ExecuteCoreAsync(
        SpaceJobStepExecution execution,
        CancellationToken cancellationToken)
    {
        var files = services.GetService(typeof(ISpaceFileStore)) as
            ISpaceFileStore ?? throw Failure(
                SpaceJobFailureKind.Resource,
                SpaceErrorCodes.JobProcessorUnavailable,
                "Private Space file storage is not configured.");
        var input = await LoadImmutableInputAsync(
            execution.Lease,
            files,
            cancellationToken);
        ValidateConfirmable(input);

        SpaceExcelWorkbookData workbook;
        await using (var content = await files.OpenQuarantinedReadAsync(
                         input.ExcelFile.TenantId,
                         input.ExcelFile.Id,
                         input.ExcelFile.StorageKey,
                         cancellationToken))
        {
            workbook = await workbookReader.ReadAsync(content, cancellationToken);
        }
        var profile = await LoadMappingAsync(input, cancellationToken);
        var projection = SpaceExcelCadMatching.ProjectWorkbook(profile, workbook);
        if (!projection.WorkbookProjectionSha256.Equals(
                input.Artifact.Preview.WorkbookProjectionSha256,
                StringComparison.Ordinal) ||
            projection.Inspection.Validation.Findings.Any(item =>
                item.Severity == SpaceIssueSeverity.Blocking))
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ExcelCadApplyArtifactInvalid,
                "The authoritative Excel workbook projection changed or is now blocked.");
        }
        var unsupportedSheets = projection.CanonicalRows
            .Where(item => item.TargetSheet is "Bindings" or "Attributes")
            .Select(item => item.TargetSheet)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        if (unsupportedSheets.Length != 0)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ExcelCadApplyScopeUnsupported,
                $"The populated {string.Join(", ", unsupportedSheets)} sheet is not safely resolvable by this Apply contract.");
        }
        if (projection.CanonicalRows.Any(item =>
                item.TargetSheet == "Locations" &&
                !string.IsNullOrWhiteSpace(Value(item, "LocationType"))))
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ExcelCadApplyScopeUnsupported,
                "LocationType is populated but the versioned Location model has no authoritative persistence field.");
        }
        if (projection.CanonicalRows.Any(item => item.TargetSheet is not (
                "Racks" or "RackLevels" or "Locations" or
                "Bindings" or "Attributes")))
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ExcelCadApplyScopeUnsupported,
                "The workbook contains a target sheet outside the frozen Apply contract.");
        }
        if (projection.CanonicalRows.Count(item => item.TargetSheet == "Racks") !=
            input.Artifact.Preview.Rows.Count)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ExcelCadApplyArtifactInvalid,
                "The Match Artifact row set no longer covers the workbook rack projection exactly.");
        }

        var plan = BuildStablePlan(input, profile, projection);
        var planJson = JsonSerializer.Serialize(plan, JsonOptions);
        var planSha256 = Hash(planJson);
        var existing = await ReadBatchReplayAsync(
            input,
            planSha256,
            cancellationToken);
        if (existing is not null)
            return Output(existing);

        IDbContextTransaction? transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        try
        {
            existing = await ReadBatchReplayAsync(
                input,
                planSha256,
                cancellationToken);
            if (existing is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return Output(existing);
            }

            var state = await LoadWritableStateAsync(
                input,
                projection,
                cancellationToken);
            ValidateEditorSnapshot(input, state);
            var applyRows = BuildApplyRows(input, state);
            ValidateFinalRackKeys(state.Racks, applyRows);
            var hierarchy = BuildHierarchyRows(
                input,
                profile,
                projection,
                state,
                applyRows);

            var appliedAtUtc = RequireUtcNow();
            var batch = SpaceElementCommandBatch.Create(
                execution.Lease.TenantId,
                input.Payload.CommandBatchId,
                input.Payload.ModelVersionId,
                input.Payload.FloorLogicalId,
                input.Payload.MatchJobId,
                input.Payload.ExpectedFloorRevision,
                planSha256,
                input.Job.RequestedBy,
                appliedAtUtc);
            context.ElementCommandBatches.Add(batch);

            var created = 0L;
            var updated = 0L;
            var unchanged = 0L;
            for (var index = 0; index < applyRows.Count; index++)
            {
                var item = applyRows[index];
                SpaceRackRevision rack;
                object before;
                if (item.Row.Disposition == SpaceExcelCadMatchDisposition.New)
                {
                    before = new { exists = false };
                    rack = SpaceRackRevision.Create(
                        execution.Lease.TenantId,
                        input.Payload.ModelVersionId,
                        item.LogicalId,
                        input.Payload.FloorLogicalId,
                        item.ZoneLogicalId,
                        item.RackCode);
                    context.RackRevisions.Add(rack);
                    created++;
                }
                else
                {
                    rack = state.Racks.Single(candidate =>
                        candidate.LogicalId == item.LogicalId);
                    before = RackSnapshot(rack);
                    if (item.Row.Disposition == SpaceExcelCadMatchDisposition.Update)
                        updated++;
                    else
                        unchanged++;
                }

                rack.UpdateDefinition(
                    input.Payload.FloorLogicalId,
                    item.ZoneLogicalId,
                    item.RackCode);
                rack.ConfigureGeometry(
                    item.X,
                    item.Y,
                    item.Z,
                    item.RotationZ,
                    item.Width,
                    item.Depth,
                    item.Height,
                    item.TemplateVersionId);
                rack.ChangeLifecycle(item.LifecycleState);
                rack.AttachSource(input.ExcelSource, item.Row.MatchedSourceRef);
                var after = RackSnapshot(rack);
                context.ElementCommandRecords.Add(
                    SpaceElementCommandRecord.Create(
                        execution.Lease.TenantId,
                        SpaceExcelCadApplyService.DeterministicGuid(
                            "space-excel-cad-apply-command-v1",
                            input.Payload.CommandBatchId,
                            item.Row.ExcelRowId),
                        batch,
                        index,
                        $"ExcelCadApplyRack{item.Row.Disposition}",
                        item.LogicalId,
                        JsonSerializer.Serialize(item.Row, JsonOptions),
                        JsonSerializer.Serialize(before, JsonOptions),
                        JsonSerializer.Serialize(after, JsonOptions)));
            }

            ApplyHierarchy(
                execution.Lease.TenantId,
                input,
                batch,
                hierarchy,
                applyRows.Count);

            state.Floor.AdvanceRevision(input.Payload.ExpectedFloorRevision);
            state.Version.TouchContent();
            input.ExcelSource.MarkImported(input.Payload.CommandBatchId);
            var result = new SpaceExcelCadApplyResultV1(
                SpaceExcelCadApplyVersions.SchemaVersion,
                input.Payload.MatchJobId,
                input.Job.Id,
                input.Payload.ArtifactId,
                input.Payload.ArtifactPayloadSha256,
                input.Payload.ModelVersionId,
                input.Payload.ExcelSourceId,
                input.Payload.FloorLogicalId,
                input.Payload.CommandBatchId,
                input.Payload.ExpectedFloorRevision,
                state.Floor.Revision,
                input.Payload.ExpectedContentRevision,
                state.Version.ContentRevision,
                created,
                updated,
                unchanged,
                input.Job.RequestedBy,
                input.Job.RequestedAtUtc,
                appliedAtUtc,
                planSha256);
            var responseJson = JsonSerializer.Serialize(result, JsonOptions);
            batch.Complete(
                state.Floor.Revision,
                state.Version.ContentRevision,
                responseJson);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
            return Output(result);
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

    private async Task<ImmutableInput> LoadImmutableInputAsync(
        SpaceJobLease lease,
        ISpaceFileStore files,
        CancellationToken cancellationToken)
    {
        var job = await context.Jobs.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == lease.JobId,
            cancellationToken) ?? throw Missing("The Apply Job was not found.");
        var payload = Deserialize<SpaceExcelCadApplyJobPayload>(
            job.PayloadJson,
            "The frozen Apply payload is invalid.");
        if (payload.SchemaVersion != SpaceExcelCadApplyVersions.SchemaVersion ||
            payload.ExcelSourceId != lease.SubjectId ||
            payload.ModelVersionId == Guid.Empty ||
            payload.MatchJobId == Guid.Empty ||
            payload.ArtifactId == Guid.Empty ||
            !IsSha256(payload.ArtifactPayloadSha256) ||
            payload.FloorLogicalId == Guid.Empty ||
            payload.ExpectedFloorRevision < 0 ||
            payload.ExpectedContentRevision < 0 ||
            payload.CommandBatchId == Guid.Empty ||
            !Hash(job.PayloadJson).Equals(lease.InputHash, StringComparison.Ordinal))
        {
            throw Invalid("The frozen Apply payload identity is invalid.");
        }

        var matchJob = await context.Jobs.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == payload.MatchJobId,
            cancellationToken) ?? throw Missing("The Match Job was not found.");
        var matchPayload = Deserialize<SpaceExcelCadMatchJobPayload>(
            matchJob.PayloadJson,
            "The frozen Match payload is invalid.");
        if (matchJob.JobType != SpaceJobType.ExcelCadMatch ||
            matchJob.SubjectType != SpaceJobSubjectType.ModelSource ||
            matchJob.SubjectId != payload.ExcelSourceId ||
            matchJob.Status != SpaceJobStatus.Succeeded ||
            matchPayload.ModelVersionId != payload.ModelVersionId ||
            matchPayload.ExcelSourceId != payload.ExcelSourceId ||
            matchPayload.FloorLogicalId != payload.FloorLogicalId ||
            matchPayload.ExpectedContentRevision != payload.ExpectedContentRevision)
        {
            throw Invalid("The Match Job does not match the frozen Apply chain.");
        }

        var persisted = await (
                from storedArtifact in context.Artifacts.AsNoTracking()
                join file in context.Files.AsNoTracking()
                    on storedArtifact.FileId equals file.Id
                where storedArtifact.Id == payload.ArtifactId &&
                      storedArtifact.JobId == payload.MatchJobId &&
                      storedArtifact.ArtifactType ==
                          SpaceArtifactType.ExcelCadMatchPreview
                select new { Artifact = storedArtifact, File = file })
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (persisted.Length != 1)
            throw Invalid("The authoritative Match Artifact was not found uniquely.");
        var stored = persisted[0];
        if (stored.Artifact.ModelVersionId != payload.ModelVersionId ||
            stored.Artifact.SourceId != payload.ExcelSourceId ||
            stored.Artifact.SchemaVersion !=
                SpaceExcelCadMatchArtifactVersions.ArtifactSchema ||
            !IsClean(stored.File) ||
            stored.File.SizeBytes is < 1 or > MaximumArtifactBytes)
        {
            throw Invalid("The authoritative Match Artifact identity is invalid.");
        }
        var artifact = SpaceExcelCadMatchArtifact.Deserialize(
            await ReadVerifiedTextAsync(stored.File, files, cancellationToken));
        if (artifact.TenantId != lease.TenantId ||
            artifact.MatchJobId != payload.MatchJobId ||
            artifact.ModelVersionId != payload.ModelVersionId ||
            artifact.ExcelSourceId != payload.ExcelSourceId ||
            artifact.FloorLogicalId != payload.FloorLogicalId ||
            artifact.ExpectedContentRevision != payload.ExpectedContentRevision ||
            artifact.ArtifactPayloadSha256 != payload.ArtifactPayloadSha256)
        {
            throw Invalid("The Match Artifact does not match the frozen Apply input.");
        }

        var source = await context.Sources.SingleOrDefaultAsync(
            item => item.Id == payload.ExcelSourceId &&
                    item.ModelVersionId == payload.ModelVersionId,
            cancellationToken) ?? throw Missing("The Excel source was not found.");
        if (source.SourceType != SpaceSourceType.Excel ||
            source.FileId is null ||
            source.ParserVersion != SpaceExcelPreflightJobProcessor.Version ||
            source.State is not (SpaceSourceState.PreviewReady or
                SpaceSourceState.Imported))
        {
            throw Invalid("The Excel source is not an authoritative parsed source.");
        }
        var excelFile = await context.Files.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == source.FileId,
            cancellationToken) ?? throw Missing("The Excel source file was not found.");
        if (!IsClean(excelFile))
            throw Invalid("The Excel source file is no longer clean.");

        var preflight = await context.Jobs.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == artifact.PreflightJobId,
            cancellationToken) ?? throw Missing("The Excel preflight Job was not found.");
        var preflightPayload = Deserialize<SpaceExcelPreflightJobPayload>(
            preflight.PayloadJson,
            "The Excel preflight payload is invalid.");
        if (preflight.JobType != SpaceJobType.ExcelPreview ||
            preflight.SubjectId != source.Id ||
            preflight.Status != SpaceJobStatus.Succeeded ||
            preflightPayload.ModelVersionId != payload.ModelVersionId ||
            preflightPayload.SourceId != source.Id ||
            source.MappingProfileId != preflightPayload.MappingProfileId ||
            source.MappingProfileVersion != preflightPayload.MappingProfileVersion)
        {
            throw Invalid("The Excel preflight no longer matches the source chain.");
        }
        return new ImmutableInput(
            job,
            payload,
            artifact,
            source,
            excelFile,
            preflightPayload);
    }

    private async Task<SpaceExcelMappingProfileDto> LoadMappingAsync(
        ImmutableInput input,
        CancellationToken cancellationToken)
    {
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
            throw Invalid("The pinned Excel mapping profile no longer exists.");
        }
        if (!profile.DefinitionHash.Equals(
                input.PreflightPayload.MappingDefinitionHash,
                StringComparison.Ordinal) ||
            !profile.DefinitionHash.Equals(
                input.Artifact.Preview.MappingDefinitionSha256,
                StringComparison.Ordinal))
        {
            throw Invalid("The pinned Excel mapping definition changed.");
        }
        return profile;
    }

    private static void ValidateConfirmable(ImmutableInput input)
    {
        var preview = input.Artifact.Preview;
        if (!preview.CanConfirm || preview.Rows.Count == 0 ||
            preview.ExcelBlockingFindingCount != 0 ||
            preview.CadBlockingCount != 0 ||
            preview.Rows.Any(item => item.Disposition is not (
                SpaceExcelCadMatchDisposition.New or
                SpaceExcelCadMatchDisposition.Update or
                SpaceExcelCadMatchDisposition.Unchanged) ||
                item.ErrorCodes.Count != 0 ||
                item.CadConfidenceBand is not (null or
                    SpaceCadConfidenceBand.High or
                    SpaceCadConfidenceBand.Review)))
        {
            throw Invalid("The Match Artifact is not eligible for confirmation.");
        }
    }

    private static object BuildStablePlan(
        ImmutableInput input,
        SpaceExcelMappingProfileDto profile,
        SpaceExcelWorkbookProjectionV1 projection) => new
        {
            schemaVersion = SpaceExcelCadApplyVersions.SchemaVersion,
            hierarchySchemaVersion = 1,
            input.Payload.ModelVersionId,
            input.Payload.MatchJobId,
            input.Payload.ArtifactId,
            input.Payload.ArtifactPayloadSha256,
            input.Payload.ExcelSourceId,
            input.Payload.FloorLogicalId,
            input.Payload.ExpectedFloorRevision,
            input.Payload.ExpectedContentRevision,
            input.Payload.CommandBatchId,
            input.Artifact.Preview.MatchPreviewSha256,
            input.Artifact.Preview.EditorSnapshotSha256,
            input.Artifact.Preview.WorkbookProjectionSha256,
            authoritativeSheets = profile.Definition.Sheets
            .Select(item => item.TargetSheet)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray(),
            projectedRowCounts = projection.CanonicalRows
            .GroupBy(item => item.TargetSheet, StringComparer.Ordinal)
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Count()),
            rows = input.Artifact.Preview.Rows.Select(item => new
            {
                item.ExcelRowId,
                item.Disposition,
                item.EditorLogicalId,
                item.MatchedSourceRef,
                item.Values,
                item.MatchEvidenceSha256,
            }).ToArray(),
        };

    private async Task<WritableState> LoadWritableStateAsync(
        ImmutableInput input,
        SpaceExcelWorkbookProjectionV1 projection,
        CancellationToken cancellationToken)
    {
        var version = await context.Versions.SingleOrDefaultAsync(
            item => item.Id == input.Payload.ModelVersionId,
            cancellationToken) ?? throw Missing("The Draft version was not found.");
        var floor = await context.FloorRevisions.SingleOrDefaultAsync(
            item => item.ModelVersionId == input.Payload.ModelVersionId &&
                    item.LogicalId == input.Payload.FloorLogicalId,
            cancellationToken) ?? throw Missing("The target floor was not found.");
        if (version.Status != SpaceVersionStatus.Draft ||
            version.ContentRevision != input.Payload.ExpectedContentRevision ||
            floor.Revision != input.Payload.ExpectedFloorRevision ||
            input.ExcelSource.State != SpaceSourceState.PreviewReady)
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ConcurrencyConflict,
                "The Draft, floor, or Excel source changed before Apply executed.");
        }
        var zones = await context.ZoneRevisions
            .Where(item =>
                item.ModelVersionId == input.Payload.ModelVersionId &&
                item.FloorLogicalId == input.Payload.FloorLogicalId)
            .ToArrayAsync(cancellationToken);
        var racks = await context.RackRevisions
            .Where(item =>
                item.ModelVersionId == input.Payload.ModelVersionId &&
                item.FloorLogicalId == input.Payload.FloorLogicalId)
            .ToArrayAsync(cancellationToken);
        var rackIds = racks.Select(item => item.LogicalId).ToArray();
        var levels = rackIds.Length == 0
            ? []
            : await context.RackLevelRevisions
                .Where(item =>
                    item.ModelVersionId == input.Payload.ModelVersionId &&
                    rackIds.Contains(item.RackLogicalId))
                .ToArrayAsync(cancellationToken);
        var locations = await context.LocationRevisions
            .Where(item =>
                item.ModelVersionId == input.Payload.ModelVersionId)
            .ToArrayAsync(cancellationToken);
        var templateVersions = await ResolveTemplateVersionsAsync(
            projection,
            cancellationToken);
        return new WritableState(
            version,
            floor,
            zones,
            racks,
            levels,
            locations,
            templateVersions);
    }

    private async Task<IReadOnlyDictionary<string, Guid>>
        ResolveTemplateVersionsAsync(
            SpaceExcelWorkbookProjectionV1 projection,
            CancellationToken cancellationToken)
    {
        var codes = projection.CanonicalRows
            .Where(item => item.TargetSheet == "Racks")
            .Select(item => Value(item, "RackTemplateCode"))
            .Where(item => item is not null)
            .Select(item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (codes.Length == 0)
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        var assets = await context.Assets.AsNoTracking()
            .Where(item => item.Status == SpaceAssetStatus.Active)
            .ToArrayAsync(cancellationToken);
        var selected = new Dictionary<string, SpaceAsset>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var code in codes)
        {
            var matches = assets
                .Where(item => item.AssetCode.Equals(
                    code,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var tenant = matches
                .Where(item => item.Scope == SpaceAssetScope.Tenant)
                .ToArray();
            var system = matches
                .Where(item => item.Scope == SpaceAssetScope.System)
                .ToArray();
            var preferred = tenant.Length == 0 ? system : tenant;
            if (preferred.Length != 1)
            {
                throw Invalid(
                    $"RackTemplateCode '{code}' does not resolve to one visible active asset.");
            }
            selected[code] = preferred[0];
        }

        var assetIds = selected.Values.Select(item => item.Id).ToArray();
        var versions = await context.AssetVersions.AsNoTracking()
            .Where(item =>
                assetIds.Contains(item.AssetId) &&
                item.Status == SpaceAssetVersionStatus.Ready)
            .ToArrayAsync(cancellationToken);
        var result = new Dictionary<string, Guid>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in selected)
        {
            var latest = versions
                .Where(version => version.AssetId == item.Value.Id)
                .OrderByDescending(version => version.VersionNo)
                .ThenBy(version => version.Id)
                .FirstOrDefault() ?? throw Invalid(
                    $"RackTemplateCode '{item.Key}' has no Ready immutable version.");
            result[item.Key] = latest.Id;
        }
        return result;
    }

    private static void ValidateEditorSnapshot(
        ImmutableInput input,
        WritableState state)
    {
        var zones = state.Zones.ToDictionary(
            item => item.LogicalId,
            item => item.ZoneCode);
        var snapshot = SpaceExcelCadMatching.SealEditorSnapshot(
            input.Job.TenantId,
            input.Payload.ModelVersionId,
            input.Payload.FloorLogicalId,
            state.Floor.FloorCode,
            state.Version.ContentRevision,
            state.Version.ContentHash,
            state.Racks.Select(item =>
            {
                if (!zones.TryGetValue(item.ZoneLogicalId, out var zoneCode))
                    throw new InvalidDataException(
                        "An editor rack has no authoritative zone.");
                return new SpaceExcelEditorRackSnapshotV1(
                    item.LogicalId,
                    item.Id,
                    item.RackCode,
                    item.SourceRef,
                    state.Floor.FloorCode,
                    zoneCode,
                    item.X,
                    item.Y,
                    item.Z,
                    item.Width,
                    item.Depth,
                    item.Height,
                    item.RotationZ,
                    item.LifecycleState.ToString());
            }).ToArray());
        if (!snapshot.SnapshotSha256.Equals(
                input.Artifact.Preview.EditorSnapshotSha256,
                StringComparison.Ordinal))
        {
            throw Failure(
                SpaceJobFailureKind.Input,
                SpaceErrorCodes.ConcurrencyConflict,
                "The authoritative editor rack snapshot changed before Apply.");
        }
    }

    private static IReadOnlyList<ApplyRow> BuildApplyRows(
        ImmutableInput input,
        WritableState state)
    {
        var zonesByCode = state.Zones
            .GroupBy(item => item.ZoneCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                item => item.Key,
                item => item.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var rows = new List<ApplyRow>(input.Artifact.Preview.Rows.Count);
        foreach (var row in input.Artifact.Preview.Rows)
        {
            var values = row.Values;
            if (string.IsNullOrWhiteSpace(values.FloorCode) ||
                !values.FloorCode.Equals(
                    state.Floor.FloorCode,
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(values.ZoneCode) ||
                !zonesByCode.TryGetValue(values.ZoneCode, out var zones) ||
                zones.Length != 1 ||
                string.IsNullOrWhiteSpace(values.RackCode))
            {
                throw Invalid("A confirmed rack has an invalid floor, zone, or rack code.");
            }
            var logicalId = row.Disposition == SpaceExcelCadMatchDisposition.New
                ? SpaceExcelCadApplyService.DeterministicGuid(
                    "space-excel-cad-apply-rack-v1",
                    input.Payload.CommandBatchId,
                    row.ExcelRowId)
                : row.EditorLogicalId ?? throw new InvalidDataException(
                    "An existing rack match has no editor logical identity.");
            if (row.Disposition == SpaceExcelCadMatchDisposition.New &&
                row.EditorLogicalId.HasValue)
            {
                throw Invalid("A new rack match unexpectedly targets an editor rack.");
            }
            Guid? templateVersionId = null;
            if (!string.IsNullOrWhiteSpace(values.RackTemplateCode))
            {
                if (!state.TemplateVersions.TryGetValue(
                        values.RackTemplateCode,
                        out var resolvedTemplateVersionId))
                {
                    throw Invalid(
                        $"RackTemplateCode '{values.RackTemplateCode}' was not resolved authoritatively.");
                }
                templateVersionId = resolvedTemplateVersionId;
            }
            rows.Add(new ApplyRow(
                row,
                logicalId,
                zones[0].LogicalId,
                values.RackCode,
                Integer(values.XMillimeters, "XMillimeters"),
                Integer(values.YMillimeters, "YMillimeters"),
                Integer(values.ZMillimeters, "ZMillimeters"),
                values.RotationZDegrees ?? throw new InvalidDataException(
                    "RotationZDegrees is required."),
                PositiveInteger(values.WidthMillimeters, "WidthMillimeters"),
                PositiveInteger(values.DepthMillimeters, "DepthMillimeters"),
                PositiveInteger(values.HeightMillimeters, "HeightMillimeters"),
                templateVersionId,
                Lifecycle(values.LifecycleStatus)));
        }
        if (rows.Select(item => item.LogicalId).Distinct().Count() != rows.Count ||
            rows.Select(item => item.RackCode)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Count)
        {
            throw Invalid("The confirmed rack targets contain duplicate identities or codes.");
        }
        return rows;
    }

    private static HierarchyApply BuildHierarchyRows(
        ImmutableInput input,
        SpaceExcelMappingProfileDto profile,
        SpaceExcelWorkbookProjectionV1 projection,
        WritableState state,
        IReadOnlyList<ApplyRow> racks)
    {
        var levelsAreAuthoritative = profile.Definition.Sheets.Any(item =>
            item.TargetSheet.Equals("RackLevels", StringComparison.Ordinal));
        var locationsAreAuthoritative = profile.Definition.Sheets.Any(item =>
            item.TargetSheet.Equals("Locations", StringComparison.Ordinal));
        var rackByCode = racks.ToDictionary(
            item => item.RackCode,
            StringComparer.OrdinalIgnoreCase);
        var existingLevels = state.Levels
            .GroupBy(
                item => LevelKey(item.RackLogicalId, item.LevelNo),
                StringComparer.Ordinal)
            .ToDictionary(
                item => item.Key,
                item => item.Single(),
                StringComparer.Ordinal);
        var levels = new List<LevelApply>();
        foreach (var row in projection.CanonicalRows
                     .Where(item => item.TargetSheet == "RackLevels")
                     .OrderBy(item => item.SourceSheet, StringComparer.Ordinal)
                     .ThenBy(item => item.RowNumber))
        {
            var rackCode = RequiredValue(row, "RackCode");
            if (!rackByCode.TryGetValue(rackCode, out var rack))
                throw Invalid("A RackLevel row targets an unapplied rack.");
            var levelNo = PositiveInteger(Value(row, "LevelNo"), "LevelNo");
            var key = LevelKey(rack.LogicalId, levelNo);
            existingLevels.TryGetValue(key, out var existing);
            var logicalId = existing?.LogicalId ??
                SpaceExcelCadApplyService.DeterministicGuid(
                    "space-excel-cad-apply-rack-level-v1",
                    input.Payload.CommandBatchId,
                    CanonicalIdentity(row));
            var binCount = PositiveInteger(Value(row, "BinCount"), "BinCount");
            var depthCount = PositiveInteger(
                Value(row, "DepthCount"),
                "DepthCount");
            levels.Add(new LevelApply(
                row,
                existing,
                logicalId,
                rack.LogicalId,
                levelNo,
                NonNegativeInteger(Value(row, "BottomZMm"), "BottomZMm"),
                PositiveInteger(
                    Value(row, "ClearHeightMm"),
                    "ClearHeightMm"),
                binCount,
                depthCount,
                CellDimension(rack.Width, binCount, "Rack width / BinCount"),
                CellDimension(rack.Depth, depthCount, "Rack depth / DepthCount"),
                OptionalDecimal(Value(row, "LoadCapacityKg")),
                Lifecycle(Value(row, "LifecycleStatus"))));
        }
        if (levels.Select(item => item.LogicalId).Distinct().Count() != levels.Count)
            throw Invalid("The RackLevel projection contains duplicate identities.");

        var levelByBusinessKey = levels.ToDictionary(
            item => LevelBusinessKey(
                racks.Single(rack => rack.LogicalId == item.RackLogicalId).RackCode,
                item.LevelNo),
            StringComparer.OrdinalIgnoreCase);
        var existingLocationsByCode = state.Locations
            .Where(item => !string.IsNullOrWhiteSpace(item.LocationCode))
            .GroupBy(item => item.LocationCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                item => item.Key,
                item => item.Single(),
                StringComparer.OrdinalIgnoreCase);
        var locations = new List<LocationApply>();
        foreach (var row in projection.CanonicalRows
                     .Where(item => item.TargetSheet == "Locations")
                     .OrderBy(item => item.SourceSheet, StringComparer.Ordinal)
                     .ThenBy(item => item.RowNumber))
        {
            var rackCode = RequiredValue(row, "RackCode");
            if (!rackByCode.TryGetValue(rackCode, out var rack))
                throw Invalid("A Location row targets an unapplied rack.");
            var levelNo = PositiveInteger(Value(row, "LevelNo"), "LevelNo");
            if (!levelByBusinessKey.TryGetValue(
                    LevelBusinessKey(rackCode, levelNo),
                    out var level))
            {
                throw Invalid("A Location row targets an unapplied rack level.");
            }
            var locationCode = RequiredValue(row, "LocationCode");
            existingLocationsByCode.TryGetValue(locationCode, out var existing);
            if (existing is not null &&
                existing.FloorLogicalId != input.Payload.FloorLogicalId)
            {
                throw Invalid(
                    $"LocationCode '{locationCode}' already belongs to another floor.");
            }
            var logicalId = existing?.LogicalId ??
                SpaceExcelCadApplyService.DeterministicGuid(
                    "space-excel-cad-apply-location-v1",
                    input.Payload.CommandBatchId,
                    CanonicalIdentity(row));
            locations.Add(new LocationApply(
                row,
                existing,
                logicalId,
                rack.LogicalId,
                locationCode,
                PositiveInteger(Value(row, "ColumnNo"), "ColumnNo"),
                levelNo,
                PositiveInteger(Value(row, "DepthNo"), "DepthNo"),
                level.CellWidth,
                level.ClearHeight,
                level.CellDepth,
                level.MaxLoad,
                Lifecycle(Value(row, "LifecycleStatus"))));
        }
        if (locations.Select(item => item.LogicalId).Distinct().Count() !=
            locations.Count)
        {
            throw Invalid("The Location projection contains duplicate identities.");
        }

        var rackIds = racks.Select(item => item.LogicalId).ToHashSet();
        var expectedLevelIds = levels.Select(item => item.LogicalId).ToHashSet();
        var obsoleteLevels = levelsAreAuthoritative
            ? state.Levels
                .Where(item =>
                    rackIds.Contains(item.RackLogicalId) &&
                    !expectedLevelIds.Contains(item.LogicalId) &&
                    item.LifecycleState != SpaceLifecycleState.Disabled)
                .OrderBy(item => item.LogicalId)
                .ToArray()
            : [];
        var expectedLocationIds = locations
            .Select(item => item.LogicalId)
            .ToHashSet();
        var obsoleteLocations = locationsAreAuthoritative
            ? state.Locations
                .Where(item =>
                    item.RackLogicalId.HasValue &&
                    rackIds.Contains(item.RackLogicalId.Value) &&
                    !expectedLocationIds.Contains(item.LogicalId) &&
                    item.LifecycleState != SpaceLifecycleState.Disabled)
                .OrderBy(item => item.LogicalId)
                .ToArray()
            : [];
        if (obsoleteLocations.Any(item =>
                item.ExternalBindingState != SpaceExternalBindingState.Unbound))
        {
            throw Invalid(
                "Apply cannot disable a WMS-bound Location omitted from the authoritative workbook.");
        }
        if (!locationsAreAuthoritative && obsoleteLevels.Any(level =>
                state.Locations.Any(location =>
                    location.RackLogicalId == level.RackLogicalId &&
                    location.LevelNo == level.LevelNo &&
                    location.LifecycleState == SpaceLifecycleState.Active)))
        {
            throw Invalid(
                "Apply cannot disable a RackLevel while Locations are outside workbook authority.");
        }

        return new HierarchyApply(
            levels,
            obsoleteLevels,
            locations,
            obsoleteLocations);
    }

    private static void ValidateFinalRackKeys(
        IReadOnlyList<SpaceRackRevision> existing,
        IReadOnlyList<ApplyRow> applyRows)
    {
        var byLogicalId = applyRows.ToDictionary(item => item.LogicalId);
        foreach (var item in applyRows.Where(item =>
                     item.Row.Disposition != SpaceExcelCadMatchDisposition.New))
        {
            if (!existing.Any(candidate => candidate.LogicalId == item.LogicalId))
                throw Invalid("A matched editor rack no longer exists.");
        }
        foreach (var item in applyRows.Where(item =>
                     item.Row.Disposition == SpaceExcelCadMatchDisposition.New))
        {
            if (existing.Any(candidate => candidate.LogicalId == item.LogicalId))
                throw Invalid("A deterministic new rack identity already exists.");
        }

        var final = existing.Select(item => byLogicalId.TryGetValue(
                    item.LogicalId,
                    out var replacement)
                ? (replacement.ZoneLogicalId, replacement.RackCode)
                : (item.ZoneLogicalId, item.RackCode))
            .Concat(applyRows
                .Where(item => item.Row.Disposition ==
                    SpaceExcelCadMatchDisposition.New)
                .Select(item => (item.ZoneLogicalId, item.RackCode)))
            .ToArray();
        if (final.GroupBy(
                    item => $"{item.ZoneLogicalId:N}\u001f{item.RackCode}",
                    StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
        {
            throw Invalid("Apply would create a duplicate rack code in one zone.");
        }
    }

    private void ApplyHierarchy(
        Guid tenantId,
        ImmutableInput input,
        SpaceElementCommandBatch batch,
        HierarchyApply hierarchy,
        int sequence)
    {
        foreach (var item in hierarchy.Levels)
        {
            var level = item.Existing;
            object before;
            string action;
            if (level is null)
            {
                before = new { exists = false };
                level = SpaceRackLevelRevision.Create(
                    tenantId,
                    input.Payload.ModelVersionId,
                    item.LogicalId,
                    item.RackLogicalId,
                    item.LevelNo,
                    item.BottomZ,
                    item.ClearHeight,
                    item.BinCount,
                    item.DepthCount,
                    item.CellWidth,
                    item.CellDepth,
                    item.MaxLoad);
                context.RackLevelRevisions.Add(level);
                action = "Create";
            }
            else
            {
                before = LevelSnapshot(level);
                level.UpdateSpecification(
                    item.LevelNo,
                    item.BottomZ,
                    item.ClearHeight,
                    item.BinCount,
                    item.DepthCount,
                    item.CellWidth,
                    item.CellDepth,
                    item.MaxLoad);
                action = "Update";
            }
            level.ChangeLifecycle(item.LifecycleState);
            level.AttachSource(input.ExcelSource, SourceRef(item.Row));
            AddCommand(
                tenantId,
                input,
                batch,
                sequence++,
                "space-excel-cad-apply-rack-level-command-v1",
                CanonicalIdentity(item.Row),
                $"ExcelCadApplyRackLevel{action}",
                item.LogicalId,
                item.Row,
                before,
                LevelSnapshot(level));
        }

        foreach (var item in hierarchy.Locations)
        {
            var location = item.Existing;
            object before;
            string action;
            if (location is null)
            {
                before = new { exists = false };
                location = SpaceLocationRevision.Create(
                    tenantId,
                    input.Payload.ModelVersionId,
                    item.LogicalId,
                    input.Payload.FloorLogicalId,
                    item.RackLogicalId,
                    item.LocationCode,
                    item.ColumnNo,
                    item.LevelNo,
                    item.DepthNo,
                    item.Width,
                    item.Height,
                    item.Depth,
                    item.MaxLoad,
                    SpaceLocationCodeOrigin.Imported,
                    SpaceExternalBindingState.Unbound);
                context.LocationRevisions.Add(location);
                action = "Create";
            }
            else
            {
                before = LocationSnapshot(location);
                location.UpdateImportedSpecification(
                    input.Payload.FloorLogicalId,
                    item.RackLogicalId,
                    item.LocationCode,
                    item.ColumnNo,
                    item.LevelNo,
                    item.DepthNo,
                    item.Width,
                    item.Height,
                    item.Depth,
                    item.MaxLoad);
                action = "Update";
            }
            location.ChangeLifecycle(item.LifecycleState);
            location.AttachSource(input.ExcelSource, SourceRef(item.Row));
            AddCommand(
                tenantId,
                input,
                batch,
                sequence++,
                "space-excel-cad-apply-location-command-v1",
                CanonicalIdentity(item.Row),
                $"ExcelCadApplyLocation{action}",
                item.LogicalId,
                item.Row,
                before,
                LocationSnapshot(location));
        }

        foreach (var location in hierarchy.ObsoleteLocations)
        {
            var before = LocationSnapshot(location);
            location.ChangeLifecycle(SpaceLifecycleState.Disabled);
            AddCommand(
                tenantId,
                input,
                batch,
                sequence++,
                "space-excel-cad-apply-location-disable-command-v1",
                location.LogicalId.ToString("N"),
                "ExcelCadApplyLocationDisable",
                location.LogicalId,
                new { reason = "omitted-from-authoritative-workbook" },
                before,
                LocationSnapshot(location));
        }

        foreach (var level in hierarchy.ObsoleteLevels)
        {
            var before = LevelSnapshot(level);
            level.ChangeLifecycle(SpaceLifecycleState.Disabled);
            AddCommand(
                tenantId,
                input,
                batch,
                sequence++,
                "space-excel-cad-apply-rack-level-disable-command-v1",
                level.LogicalId.ToString("N"),
                "ExcelCadApplyRackLevelDisable",
                level.LogicalId,
                new { reason = "omitted-from-authoritative-workbook" },
                before,
                LevelSnapshot(level));
        }
    }

    private void AddCommand(
        Guid tenantId,
        ImmutableInput input,
        SpaceElementCommandBatch batch,
        int sequence,
        string purpose,
        string identity,
        string commandType,
        Guid logicalId,
        object request,
        object before,
        object after) =>
        context.ElementCommandRecords.Add(
            SpaceElementCommandRecord.Create(
                tenantId,
                SpaceExcelCadApplyService.DeterministicGuid(
                    purpose,
                    input.Payload.CommandBatchId,
                    identity),
                batch,
                sequence,
                commandType,
                logicalId,
                JsonSerializer.Serialize(request, JsonOptions),
                JsonSerializer.Serialize(before, JsonOptions),
                JsonSerializer.Serialize(after, JsonOptions)));

    private async Task<SpaceExcelCadApplyResultV1?> ReadBatchReplayAsync(
        ImmutableInput input,
        string planSha256,
        CancellationToken cancellationToken)
    {
        var batch = await context.ElementCommandBatches.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == input.Payload.CommandBatchId,
                cancellationToken);
        if (batch is null)
            return null;
        if (batch.ModelVersionId != input.Payload.ModelVersionId ||
            batch.FloorLogicalId != input.Payload.FloorLogicalId ||
            batch.ClientInstanceId != input.Payload.MatchJobId ||
            batch.ExpectedFloorRevision != input.Payload.ExpectedFloorRevision ||
            !batch.RequestHash.Equals(planSha256, StringComparison.Ordinal) ||
            batch.ResponseJson is null ||
            batch.ResultFloorRevision != input.Payload.ExpectedFloorRevision + 1 ||
            batch.ResultVersionContentRevision !=
                input.Payload.ExpectedContentRevision + 1 ||
            input.ExcelSource.State != SpaceSourceState.Imported ||
            input.ExcelSource.ImportedCommandBatchId != input.Payload.CommandBatchId)
        {
            throw Invalid("The deterministic Apply command batch conflicts with stored state.");
        }
        var result = Deserialize<SpaceExcelCadApplyResultV1>(
            batch.ResponseJson,
            "The stored Apply command batch response is invalid.");
        ValidateResult(input, result, planSha256);
        return result;
    }

    private static void ValidateResult(
        ImmutableInput input,
        SpaceExcelCadApplyResultV1 result,
        string planSha256)
    {
        if (result.SchemaVersion != SpaceExcelCadApplyVersions.SchemaVersion ||
            result.MatchJobId != input.Payload.MatchJobId ||
            result.ApplyJobId != input.Job.Id ||
            result.ArtifactId != input.Payload.ArtifactId ||
            result.ArtifactPayloadSha256 !=
                input.Payload.ArtifactPayloadSha256 ||
            result.ModelVersionId != input.Payload.ModelVersionId ||
            result.ExcelSourceId != input.Payload.ExcelSourceId ||
            result.FloorLogicalId != input.Payload.FloorLogicalId ||
            result.CommandBatchId != input.Payload.CommandBatchId ||
            result.ExpectedFloorRevision != input.Payload.ExpectedFloorRevision ||
            result.ResultFloorRevision != input.Payload.ExpectedFloorRevision + 1 ||
            result.ExpectedContentRevision !=
                input.Payload.ExpectedContentRevision ||
            result.ResultContentRevision !=
                input.Payload.ExpectedContentRevision + 1 ||
            !result.ApplyPlanSha256.Equals(planSha256, StringComparison.Ordinal))
        {
            throw Invalid("The stored Apply result does not match its frozen input.");
        }
    }

    private static object RackSnapshot(SpaceRackRevision rack) => new
    {
        exists = true,
        rack.Id,
        rack.LogicalId,
        rack.FloorLogicalId,
        rack.ZoneLogicalId,
        rack.RackCode,
        rack.Name,
        rack.RackType,
        rack.TemplateVersionId,
        rack.X,
        rack.Y,
        rack.Z,
        rack.RotationZ,
        rack.Width,
        rack.Depth,
        rack.Height,
        rack.SourceId,
        rack.SourceRef,
        lifecycleState = rack.LifecycleState.ToString(),
    };

    private static object LevelSnapshot(SpaceRackLevelRevision level) => new
    {
        exists = true,
        level.Id,
        level.LogicalId,
        level.RackLogicalId,
        level.LevelNo,
        level.BottomZ,
        level.ClearHeight,
        level.BinCount,
        level.DepthCount,
        level.CellWidth,
        level.CellDepth,
        level.BeamHeight,
        level.MaxLoad,
        level.SourceId,
        level.SourceRef,
        lifecycleState = level.LifecycleState.ToString(),
    };

    private static object LocationSnapshot(SpaceLocationRevision location) => new
    {
        exists = true,
        location.Id,
        location.LogicalId,
        location.FloorLogicalId,
        location.RackLogicalId,
        location.LocationCode,
        location.ColumnNo,
        location.LevelNo,
        location.DepthNo,
        location.Width,
        location.Height,
        location.Depth,
        location.MaxLoad,
        codeOrigin = location.CodeOrigin.ToString(),
        externalBindingState = location.ExternalBindingState.ToString(),
        location.SourceId,
        location.SourceRef,
        lifecycleState = location.LifecycleState.ToString(),
    };

    private static string LevelKey(Guid rackLogicalId, int levelNo) =>
        $"{rackLogicalId:N}\u001f{levelNo}";

    private static string LevelBusinessKey(string rackCode, int levelNo) =>
        $"{rackCode}\u001f{levelNo}";

    private static string CanonicalIdentity(SpaceExcelCanonicalRow row) =>
        $"{row.TargetSheet}\n{row.SourceSheet}\n{row.RowNumber}";

    private static string SourceRef(SpaceExcelCanonicalRow row) =>
        $"{row.SourceSheet}!{row.RowNumber}";

    private static string? Value(
        SpaceExcelCanonicalRow row,
        string field) => row.Values.GetValueOrDefault(field);

    private static string RequiredValue(
        SpaceExcelCanonicalRow row,
        string field) =>
        string.IsNullOrWhiteSpace(Value(row, field))
            ? throw new InvalidDataException($"{field} is required.")
            : Value(row, field)!.Trim();

    private static int Integer(string? value, string field)
    {
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var result))
        {
            throw new InvalidDataException($"{field} must be a 32-bit integer.");
        }
        return result;
    }

    private static int PositiveInteger(string? value, string field)
    {
        var result = Integer(value, field);
        if (result <= 0)
            throw new InvalidDataException($"{field} must be positive.");
        return result;
    }

    private static int NonNegativeInteger(string? value, string field)
    {
        var result = Integer(value, field);
        if (result < 0)
            throw new InvalidDataException($"{field} cannot be negative.");
        return result;
    }

    private static decimal? OptionalDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!decimal.TryParse(
                value,
                NumberStyles.Number | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out var result) || result < 0)
        {
            throw new InvalidDataException(
                "LoadCapacityKg must be a non-negative decimal.");
        }
        return result;
    }

    private static int CellDimension(int dimension, int count, string field)
    {
        var result = dimension / count;
        if (result <= 0)
            throw new InvalidDataException($"{field} must produce at least 1 mm.");
        return result;
    }

    private static int Integer(decimal? value, string field)
    {
        if (!value.HasValue || decimal.Truncate(value.Value) != value.Value ||
            value.Value is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidDataException($"{field} must be a 32-bit integer.");
        }
        return decimal.ToInt32(value.Value);
    }

    private static int PositiveInteger(decimal? value, string field)
    {
        var result = Integer(value, field);
        if (result <= 0)
            throw new InvalidDataException($"{field} must be positive.");
        return result;
    }

    private static SpaceLifecycleState Lifecycle(string? value)
    {
        if (Enum.TryParse<SpaceLifecycleState>(value, true, out var parsed) &&
            parsed is SpaceLifecycleState.Active or SpaceLifecycleState.Disabled)
        {
            return parsed;
        }
        throw new InvalidDataException(
            "LifecycleStatus must be Active or Disabled.");
    }

    private DateTime RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static async Task<string> ReadVerifiedTextAsync(
        SpaceFile file,
        ISpaceFileStore files,
        CancellationToken cancellationToken)
    {
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
                "The authoritative artifact file hash or size changed.");
        }
        return json;
    }

    private static SpaceJobStepOutput Output(SpaceExcelCadApplyResultV1 result)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return new SpaceJobStepOutput(json, Hash(json));
    }

    private static T Deserialize<T>(string json, string message)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                   ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw Invalid(message);
        }
    }

    private static void EnsureLease(SpaceJobLease lease)
    {
        if (lease.TenantId == Guid.Empty ||
            lease.JobType != SpaceJobType.ExcelCadApply ||
            lease.SubjectType != SpaceJobSubjectType.ModelSource ||
            lease.SubjectId == Guid.Empty)
        {
            throw Failure(
                SpaceJobFailureKind.Bug,
                "SPACE_EXCEL_CAD_APPLY_LEASE_INVALID",
                "The Excel/CAD Apply Job lease is invalid.");
        }
    }

    private static bool IsClean(SpaceFile file) =>
        file.State == SpaceFileState.Clean &&
        !file.IsDeleted && file.SizeBytes >= 0 && IsSha256(file.Sha256);

    private static bool IsSha256(string? value) =>
        SpaceExcelCadApplyService.IsSha256(value);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static string Sanitize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 1000 ? normalized : normalized[..1000];
    }

    private static SpaceJobProcessingException Missing(string message) =>
        Failure(
            SpaceJobFailureKind.Input,
            SpaceErrorCodes.ExcelCadApplyNotFound,
            message);

    private static SpaceJobProcessingException Invalid(string message) =>
        Failure(
            SpaceJobFailureKind.Input,
            SpaceErrorCodes.ExcelCadApplyArtifactInvalid,
            message);

    private static SpaceJobProcessingException Failure(
        SpaceJobFailureKind kind,
        string code,
        string message) => new(kind, code, message);

    private sealed record ImmutableInput(
        SpaceJob Job,
        SpaceExcelCadApplyJobPayload Payload,
        SpaceExcelCadMatchArtifactV1 Artifact,
        SpaceModelSource ExcelSource,
        SpaceFile ExcelFile,
        SpaceExcelPreflightJobPayload PreflightPayload);

    private sealed record WritableState(
        SpaceModelVersion Version,
        SpaceFloorRevision Floor,
        IReadOnlyList<SpaceZoneRevision> Zones,
        IReadOnlyList<SpaceRackRevision> Racks,
        IReadOnlyList<SpaceRackLevelRevision> Levels,
        IReadOnlyList<SpaceLocationRevision> Locations,
        IReadOnlyDictionary<string, Guid> TemplateVersions);

    private sealed record ApplyRow(
        SpaceExcelCadRackMatchV1 Row,
        Guid LogicalId,
        Guid ZoneLogicalId,
        string RackCode,
        int X,
        int Y,
        int Z,
        decimal RotationZ,
        int Width,
        int Depth,
        int Height,
        Guid? TemplateVersionId,
        SpaceLifecycleState LifecycleState);

    private sealed record LevelApply(
        SpaceExcelCanonicalRow Row,
        SpaceRackLevelRevision? Existing,
        Guid LogicalId,
        Guid RackLogicalId,
        int LevelNo,
        int BottomZ,
        int ClearHeight,
        int BinCount,
        int DepthCount,
        int CellWidth,
        int CellDepth,
        decimal? MaxLoad,
        SpaceLifecycleState LifecycleState);

    private sealed record LocationApply(
        SpaceExcelCanonicalRow Row,
        SpaceLocationRevision? Existing,
        Guid LogicalId,
        Guid RackLogicalId,
        string LocationCode,
        int ColumnNo,
        int LevelNo,
        int DepthNo,
        int Width,
        int Height,
        int Depth,
        decimal? MaxLoad,
        SpaceLifecycleState LifecycleState);

    private sealed record HierarchyApply(
        IReadOnlyList<LevelApply> Levels,
        IReadOnlyList<SpaceRackLevelRevision> ObsoleteLevels,
        IReadOnlyList<LocationApply> Locations,
        IReadOnlyList<SpaceLocationRevision> ObsoleteLocations);
}
