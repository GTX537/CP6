using System.Data;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed partial class SpaceExcelCadApplyService
{
    public async Task<CompensateSpaceExcelCadApplyResponse> CompensateAsync(
        Guid versionId,
        Guid matchJobId,
        Guid applyJobId,
        CompensateSpaceExcelCadApplyRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        ValidateCompensationRequest(
            versionId,
            matchJobId,
            applyJobId,
            request);
        var operation =
            $"excel-cad-apply-compensation:{applyJobId:N}:{request.Direction}";
        var keyHash = IdempotencyKeyHash(operation, idempotencyKey);
        var requestHash = Hash(JsonSerializer.Serialize(request, JsonOptions));

        IDbContextTransaction? transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken)
            : null;
        try
        {
            var input = await LoadCompensationInputAsync(
                versionId,
                matchJobId,
                applyJobId,
                cancellationToken);
            EnsureWritable(input.Model);
            await AcquireFloorEditLockAsync(
                versionId,
                input.Payload.FloorLogicalId,
                cancellationToken);
            await EnsureActiveEditLeaseAsync(
                versionId,
                input.Payload.FloorLogicalId,
                request.LeaseId,
                request.ClientInstanceId,
                cancellationToken);

            ValidateCompensationHistory(input, request);
            var replay = await ReadCompensationReplayAsync(
                operation,
                keyHash,
                requestHash,
                request,
                input,
                cancellationToken);
            if (replay is not null)
            {
                if (transaction is not null)
                    await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            if (input.Version.ContentRevision != request.ExpectedContentRevision ||
                input.Floor.Revision != request.ExpectedFloorRevision)
            {
                throw CompensationConflict(
                    "The floor or Draft content revision changed before compensation.");
            }
            ValidateSourceState(input, request.Direction);

            var items = input.Records
                .Select(BuildCompensationItem)
                .ToArray();
            var entities = await LoadCompensationEntitiesAsync(
                input.Payload.ModelVersionId,
                items,
                cancellationToken);
            var ordered = request.Direction ==
                          SpaceExcelCadCompensationDirections.Undo
                ? items.Reverse().ToArray()
                : items;
            foreach (var item in ordered)
                EnsureCurrentState(item, entities, request.Direction);

            var now = await ReadAuthoritativeUtcNowAsync(cancellationToken);
            var batch = SpaceElementCommandBatch.Create(
                execution.TenantId,
                request.CommandBatchId,
                versionId,
                input.Payload.FloorLogicalId,
                request.ClientInstanceId,
                request.LeaseId,
                request.ExpectedFloorRevision,
                request.ExpectedContentRevision,
                input.Version.ContentHash,
                request.HistorySha256,
                requestHash,
                execution.ActorId,
                now);
            context.ElementCommandBatches.Add(batch);

            for (var sequence = 0; sequence < ordered.Length; sequence++)
            {
                var item = ordered[sequence];
                var beforeJson = CurrentSnapshot(item, entities);
                ApplyTarget(item, entities, request.Direction);
                var afterJson = CurrentSnapshot(item, entities);
                context.ElementCommandRecords.Add(
                    SpaceElementCommandRecord.Create(
                        execution.TenantId,
                        DeterministicGuid(
                            "space-excel-cad-compensation-command-v1",
                            request.CommandBatchId,
                            item.Record.Id.ToString("N")),
                        batch,
                        sequence,
                        $"ExcelCad{request.Direction}:{item.Record.CommandType}",
                        item.Record.TargetLogicalId,
                        JsonSerializer.Serialize(
                            new
                            {
                                originalCommandBatchId =
                                    input.Payload.CommandBatchId,
                                originalCommandId = item.Record.Id,
                                request.Direction,
                            },
                            JsonOptions),
                        beforeJson,
                        afterJson));
            }

            if (request.Direction == SpaceExcelCadCompensationDirections.Undo)
                input.Source.ReopenImportedPreview(input.Payload.CommandBatchId);
            else
                input.Source.MarkImported(input.Payload.CommandBatchId);
            input.Floor.AdvanceRevision(request.ExpectedFloorRevision);
            input.Version.TouchContent();

            var response = new CompensateSpaceExcelCadApplyResponse(
                SpaceExcelCadApplyVersions.SchemaVersion,
                matchJobId,
                applyJobId,
                request.CommandBatchId,
                request.Direction,
                request.HistorySha256,
                input.Records.Count,
                input.Floor.Revision,
                input.Version.ContentRevision,
                IdempotentReplay: false);
            var responseJson = JsonSerializer.Serialize(response, JsonOptions);
            batch.Complete(
                input.Floor.Revision,
                input.Version.ContentRevision,
                responseJson);
            context.IdempotencyRecords.Add(SpaceIdempotencyRecord.Create(
                execution.TenantId,
                execution.ActorId,
                operation,
                keyHash,
                requestHash,
                responseJson,
                200,
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
            throw CompensationConflict(
                "The reversible Excel/CAD state changed concurrently.");
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

    internal static string ComputeHistorySha256(
        IEnumerable<SpaceElementCommandRecord> records)
    {
        var builder = new StringBuilder();
        var count = 0;
        foreach (var record in records.OrderBy(item => item.SequenceNo))
        {
            builder.Append(record.CommandBatchId.ToString("N"))
                .Append('\u001f').Append(record.Id.ToString("N"))
                .Append('\u001f').Append(record.SequenceNo)
                .Append('\u001f').Append(record.CommandType)
                .Append('\u001f').Append(record.TargetLogicalId.ToString("N"))
                .Append('\u001f').Append(record.BeforeJson)
                .Append('\u001f').Append(record.AfterJson)
                .Append('\n');
            count++;
        }
        if (count == 0)
            throw new InvalidDataException(
                "Excel/CAD Apply did not persist a reversible command history.");
        return Hash(builder.ToString());
    }

    private async Task<CompensationInput> LoadCompensationInputAsync(
        Guid versionId,
        Guid matchJobId,
        Guid applyJobId,
        CancellationToken cancellationToken)
    {
        var applyJob = await context.Jobs.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == applyJobId &&
                        item.JobType == SpaceJobType.ExcelCadApply &&
                        item.SubjectType == SpaceJobSubjectType.ModelSource,
                cancellationToken) ?? throw NotFound();
        var payload = DeserializePayload(applyJob.PayloadJson);
        if (applyJob.Status != SpaceJobStatus.Succeeded ||
            payload.ModelVersionId != versionId ||
            payload.MatchJobId != matchJobId ||
            payload.ExcelSourceId != applyJob.SubjectId)
        {
            throw ArtifactInvalid(
                "The stored Apply Job does not match the compensation chain.");
        }

        var source = await context.Sources.SingleOrDefaultAsync(
            item => item.Id == payload.ExcelSourceId &&
                    item.ModelVersionId == versionId,
            cancellationToken) ?? throw NotFound();
        var version = await context.Versions.SingleOrDefaultAsync(
            item => item.Id == versionId,
            cancellationToken) ?? throw NotFound();
        var model = await context.Models.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == version.ModelId,
            cancellationToken) ?? throw NotFound();
        var floor = await context.FloorRevisions.SingleOrDefaultAsync(
            item => item.ModelVersionId == versionId &&
                    item.LogicalId == payload.FloorLogicalId,
            cancellationToken) ?? throw NotFound();
        var originalBatch = await context.ElementCommandBatches.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == payload.CommandBatchId &&
                        item.ModelVersionId == versionId &&
                        item.FloorLogicalId == payload.FloorLogicalId,
                cancellationToken) ?? throw ArtifactInvalid(
                "The original Excel/CAD command batch is missing.");
        if (originalBatch.ResponseJson is null)
            throw ArtifactInvalid(
                "The original Excel/CAD command batch is incomplete.");
        SpaceExcelCadApplyResultV1 result;
        try
        {
            result = JsonSerializer.Deserialize<SpaceExcelCadApplyResultV1>(
                         originalBatch.ResponseJson,
                         JsonOptions) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw ArtifactInvalid(
                "The original Excel/CAD Apply result is invalid.");
        }
        ValidateResult(result, payload, applyJobId);
        var records = await context.ElementCommandRecords.AsNoTracking()
            .Where(item => item.CommandBatchId == payload.CommandBatchId)
            .OrderBy(item => item.SequenceNo)
            .ToListAsync(cancellationToken);
        return new CompensationInput(
            applyJob,
            payload,
            result,
            records,
            model,
            version,
            floor,
            source);
    }

    private static void ValidateCompensationHistory(
        CompensationInput input,
        CompensateSpaceExcelCadApplyRequest request)
    {
        if (input.Result.SchemaVersion < SpaceExcelCadApplyVersions.SchemaVersion ||
            input.Result.HistoryCommandCount <= 0 ||
            input.Result.HistoryCommandCount != input.Records.Count ||
            input.Result.HistorySha256 != request.HistorySha256 ||
            ComputeHistorySha256(input.Records) != request.HistorySha256)
        {
            throw ArtifactInvalid(
                "The sealed Excel/CAD reversible history is missing or changed.");
        }
    }

    private async Task<CompensateSpaceExcelCadApplyResponse?>
        ReadCompensationReplayAsync(
            string operation,
            string keyHash,
            string requestHash,
            CompensateSpaceExcelCadApplyRequest request,
            CompensationInput input,
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
        if (record.RequestHash != requestHash ||
            record.ReplayUntilUtc < await ReadAuthoritativeUtcNowAsync(
                cancellationToken))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.IdempotencyConflict,
                409,
                "The Idempotency-Key was already used with different or expired input.",
                recoveryAction: "use-new-idempotency-key");
        }
        var response = JsonSerializer
            .Deserialize<CompensateSpaceExcelCadApplyResponse>(
                record.ResponseJson,
                JsonOptions) ?? throw new InvalidOperationException(
                "The compensation idempotency response is invalid.");
        if (response.CommandBatchId != request.CommandBatchId ||
            response.HistorySha256 != request.HistorySha256 ||
            input.Floor.Revision != response.FloorRevision ||
            input.Version.ContentRevision != response.VersionContentRevision)
        {
            throw CompensationConflict(
                "The Draft advanced after the compensated history entry was applied.");
        }
        return response with { IdempotentReplay = true };
    }

    private static void ValidateSourceState(
        CompensationInput input,
        string direction)
    {
        var valid = direction switch
        {
            SpaceExcelCadCompensationDirections.Undo =>
                input.Source.State == SpaceSourceState.Imported &&
                input.Source.ImportedCommandBatchId == input.Payload.CommandBatchId,
            SpaceExcelCadCompensationDirections.Redo =>
                input.Source.State == SpaceSourceState.PreviewReady &&
                input.Source.ImportedCommandBatchId is null,
            _ => false,
        };
        if (!valid)
            throw CompensationConflict(
                "The Excel source is not in the expected reversible state.");
    }

    private static CompensationItem BuildCompensationItem(
        SpaceElementCommandRecord record)
    {
        var kind = record.CommandType switch
        {
            var value when value.StartsWith(
                "ExcelCadApplyRackLevel",
                StringComparison.Ordinal) => CompensationKind.RackLevel,
            var value when value.StartsWith(
                "ExcelCadApplyLocationBinding",
                StringComparison.Ordinal) => CompensationKind.Binding,
            var value when value.StartsWith(
                "ExcelCadApplyDesignAttribute",
                StringComparison.Ordinal) => CompensationKind.Attribute,
            var value when value.StartsWith(
                "ExcelCadApplyLocation",
                StringComparison.Ordinal) => CompensationKind.Location,
            var value when value.StartsWith(
                "ExcelCadApplyRack",
                StringComparison.Ordinal) => CompensationKind.Rack,
            _ => throw ArtifactInvalid(
                $"Unsupported reversible command type '{record.CommandType}'."),
        };
        return new CompensationItem(
            record,
            kind,
            DeserializeSnapshot(kind, record.BeforeJson),
            DeserializeSnapshot(kind, record.AfterJson));
    }

    private async Task<CompensationEntities> LoadCompensationEntitiesAsync(
        Guid versionId,
        IReadOnlyList<CompensationItem> items,
        CancellationToken cancellationToken)
    {
        var ids = items.Select(item => item.EntityId).Distinct().ToArray();
        var racks = await context.RackRevisions.IgnoreQueryFilters()
            .Where(item => item.TenantId == execution.TenantId &&
                           item.ModelVersionId == versionId &&
                           ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var levels = await context.RackLevelRevisions.IgnoreQueryFilters()
            .Where(item => item.TenantId == execution.TenantId &&
                           item.ModelVersionId == versionId &&
                           ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var locations = await context.LocationRevisions.IgnoreQueryFilters()
            .Where(item => item.TenantId == execution.TenantId &&
                           item.ModelVersionId == versionId &&
                           ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var bindings = await context.LocationExternalBindings.IgnoreQueryFilters()
            .Where(item => item.TenantId == execution.TenantId &&
                           item.ModelVersionId == versionId &&
                           ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var attributes = await context.DesignAttributes.IgnoreQueryFilters()
            .Where(item => item.TenantId == execution.TenantId &&
                           item.ModelVersionId == versionId &&
                           ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var sourceIds = items
            .SelectMany(item => new[]
            {
                item.Before.SourceId,
                item.After.SourceId,
            })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var sources = await context.Sources.IgnoreQueryFilters()
            .Where(item => item.TenantId == execution.TenantId &&
                           item.ModelVersionId == versionId &&
                           sourceIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var locationLogicalIds = items
            .SelectMany(item => new[]
            {
                item.Before.LocationLogicalId,
                item.After.LocationLogicalId,
            })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var boundLocations = await context.LocationRevisions.IgnoreQueryFilters()
            .Where(item => item.TenantId == execution.TenantId &&
                           item.ModelVersionId == versionId &&
                           locationLogicalIds.Contains(item.LogicalId))
            .ToDictionaryAsync(item => item.LogicalId, cancellationToken);
        return new CompensationEntities(
            racks,
            levels,
            locations,
            bindings,
            attributes,
            sources,
            boundLocations);
    }

    private static void EnsureCurrentState(
        CompensationItem item,
        CompensationEntities entities,
        string direction)
    {
        var expected = direction == SpaceExcelCadCompensationDirections.Undo
            ? item.After
            : item.Before;
        if (!expected.Exists)
        {
            var removed = item.Kind switch
            {
                CompensationKind.Rack =>
                    entities.Racks.GetValueOrDefault(item.EntityId)?
                        .LifecycleState == SpaceLifecycleState.Disabled,
                CompensationKind.RackLevel =>
                    entities.Levels.GetValueOrDefault(item.EntityId)?
                        .LifecycleState == SpaceLifecycleState.Disabled,
                CompensationKind.Location =>
                    entities.Locations.GetValueOrDefault(item.EntityId)?
                        .LifecycleState == SpaceLifecycleState.Disabled,
                CompensationKind.Binding =>
                    entities.Bindings.GetValueOrDefault(item.EntityId)?.IsDeleted == true,
                CompensationKind.Attribute =>
                    entities.Attributes.GetValueOrDefault(item.EntityId)?.IsDeleted == true,
                _ => false,
            };
            if (removed)
                return;
        }
        else
        {
            var current = CurrentSnapshot(item, entities);
            var currentValue = DeserializeSnapshot(item.Kind, current);
            if (Equals(currentValue.Value, expected.Value))
                return;
        }
        throw CompensationConflict(
            $"The current {item.Kind} state no longer matches sealed history " +
            $"for command {item.Record.Id:D}.");
    }

    private static void ApplyTarget(
        CompensationItem item,
        CompensationEntities entities,
        string direction)
    {
        var target = direction == SpaceExcelCadCompensationDirections.Undo
            ? item.Before
            : item.After;
        switch (item.Kind)
        {
            case CompensationKind.Rack:
            {
                var entity = Require(entities.Racks, item.EntityId, item.Kind);
                if (!target.Exists)
                    entity.ChangeLifecycle(SpaceLifecycleState.Disabled);
                else
                {
                    var value = (RackState)target.Value;
                    entity.RestoreSnapshot(
                        value.FloorLogicalId,
                        value.ZoneLogicalId,
                        value.AisleLogicalId,
                        value.RackCode!,
                        value.Name!,
                        value.RackType,
                        value.TemplateVersionId,
                        value.X,
                        value.Y,
                        value.Z,
                        value.RotationZ,
                        value.Width,
                        value.Depth,
                        value.Height,
                        ParseEnum<SpaceLifecycleState>(value.LifecycleState),
                        Source(entities, value.SourceId),
                        value.SourceRef);
                }
                break;
            }
            case CompensationKind.RackLevel:
            {
                var entity = Require(entities.Levels, item.EntityId, item.Kind);
                if (!target.Exists)
                    entity.ChangeLifecycle(SpaceLifecycleState.Disabled);
                else
                {
                    var value = (RackLevelState)target.Value;
                    entity.RestoreSnapshot(
                        value.RackLogicalId,
                        value.LevelNo,
                        value.BottomZ,
                        value.ClearHeight,
                        value.BinCount,
                        value.DepthCount,
                        value.CellWidth,
                        value.CellDepth,
                        value.BeamHeight,
                        value.MaxLoad,
                        ParseEnum<SpaceLifecycleState>(value.LifecycleState),
                        Source(entities, value.SourceId),
                        value.SourceRef);
                }
                break;
            }
            case CompensationKind.Location:
            {
                var entity = Require(entities.Locations, item.EntityId, item.Kind);
                if (!target.Exists)
                    entity.ChangeLifecycle(SpaceLifecycleState.Disabled);
                else
                {
                    var value = (LocationState)target.Value;
                    entity.RestoreSnapshot(
                        value.FloorLogicalId,
                        value.RackLogicalId,
                        value.LocationCode,
                        value.ColumnNo,
                        value.LevelNo,
                        value.DepthNo,
                        value.Width,
                        value.Height,
                        value.Depth,
                        value.MaxLoad,
                        value.LocationType,
                        ParseEnum<SpaceLocationCodeOrigin>(value.CodeOrigin),
                        ParseEnum<SpaceExternalBindingState>(
                            value.ExternalBindingState),
                        ParseEnum<SpaceLifecycleState>(value.LifecycleState),
                        Source(entities, value.SourceId),
                        value.SourceRef);
                }
                break;
            }
            case CompensationKind.Binding:
            {
                var entity = Require(entities.Bindings, item.EntityId, item.Kind);
                if (!target.Exists || ((BindingState)target.Value).IsDeleted)
                    entity.Remove();
                else
                {
                    var value = (BindingState)target.Value;
                    var location = Require(
                        entities.BoundLocations,
                        value.LocationLogicalId,
                        CompensationKind.Location);
                    entity.Restore(
                        entity.TenantId,
                        location,
                        ParseEnum<SpaceLocationBindingMode>(value.BindingMode),
                        Source(entities, value.SourceId) ?? throw ArtifactInvalid(
                            "A binding history snapshot is missing its source."),
                        value.SourceRef!);
                }
                break;
            }
            case CompensationKind.Attribute:
            {
                var entity = Require(entities.Attributes, item.EntityId, item.Kind);
                if (!target.Exists || ((AttributeState)target.Value).IsDeleted)
                    entity.Remove();
                else
                {
                    var value = (AttributeState)target.Value;
                    entity.Restore(
                        value.Value!,
                        value.Unit,
                        Source(entities, value.SourceId) ?? throw ArtifactInvalid(
                            "An attribute history snapshot is missing its source."),
                        value.SourceRef!);
                }
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(item.Kind));
        }
    }

    private static string CurrentSnapshot(
        CompensationItem item,
        CompensationEntities entities) => item.Kind switch
        {
            CompensationKind.Rack => JsonSerializer.Serialize(
                RackSnapshot(Require(entities.Racks, item.EntityId, item.Kind)),
                JsonOptions),
            CompensationKind.RackLevel => JsonSerializer.Serialize(
                RackLevelSnapshot(Require(
                    entities.Levels,
                    item.EntityId,
                    item.Kind)),
                JsonOptions),
            CompensationKind.Location => JsonSerializer.Serialize(
                LocationSnapshot(Require(
                    entities.Locations,
                    item.EntityId,
                    item.Kind)),
                JsonOptions),
            CompensationKind.Binding => JsonSerializer.Serialize(
                BindingSnapshot(Require(
                    entities.Bindings,
                    item.EntityId,
                    item.Kind)),
                JsonOptions),
            CompensationKind.Attribute => JsonSerializer.Serialize(
                AttributeSnapshot(Require(
                    entities.Attributes,
                    item.EntityId,
                    item.Kind)),
                JsonOptions),
            _ => throw new ArgumentOutOfRangeException(nameof(item.Kind)),
        };

    private static object RackSnapshot(SpaceRackRevision rack) => new
    {
        exists = true,
        rack.Id,
        rack.LogicalId,
        rack.FloorLogicalId,
        rack.ZoneLogicalId,
        rack.AisleLogicalId,
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

    private static object RackLevelSnapshot(SpaceRackLevelRevision level) => new
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
        location.LocationType,
        codeOrigin = location.CodeOrigin.ToString(),
        externalBindingState = location.ExternalBindingState.ToString(),
        location.SourceId,
        location.SourceRef,
        lifecycleState = location.LifecycleState.ToString(),
    };

    private static object BindingSnapshot(SpaceLocationExternalBinding binding) =>
        new
        {
            exists = true,
            binding.Id,
            binding.ModelVersionId,
            binding.LocationLogicalId,
            binding.AdapterId,
            binding.WarehouseCode,
            binding.ExternalLocationId,
            bindingMode = binding.BindingMode.ToString(),
            binding.SourceId,
            binding.SourceRef,
            binding.IsDeleted,
        };

    private static object AttributeSnapshot(SpaceDesignAttribute attribute) => new
    {
        exists = true,
        attribute.Id,
        attribute.ModelVersionId,
        attribute.ObjectType,
        attribute.ObjectLogicalId,
        attribute.Namespace,
        attribute.Key,
        attribute.Value,
        attribute.Unit,
        attribute.SourceId,
        attribute.SourceRef,
        attribute.IsDeleted,
    };

    private static Snapshot DeserializeSnapshot(
        CompensationKind kind,
        string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var exists = document.RootElement.TryGetProperty("exists", out var value) &&
                         value.GetBoolean();
            object snapshot = kind switch
            {
                CompensationKind.Rack =>
                    JsonSerializer.Deserialize<RackState>(json, JsonOptions)!,
                CompensationKind.RackLevel =>
                    JsonSerializer.Deserialize<RackLevelState>(json, JsonOptions)!,
                CompensationKind.Location =>
                    JsonSerializer.Deserialize<LocationState>(json, JsonOptions)!,
                CompensationKind.Binding =>
                    JsonSerializer.Deserialize<BindingState>(json, JsonOptions)!,
                CompensationKind.Attribute =>
                    JsonSerializer.Deserialize<AttributeState>(json, JsonOptions)!,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
            return new Snapshot(exists, json, snapshot);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException)
        {
            throw ArtifactInvalid(
                $"A sealed Excel/CAD history snapshot is invalid: " +
                exception.Message);
        }
    }

    private static T Require<T>(
        IReadOnlyDictionary<Guid, T> values,
        Guid id,
        CompensationKind kind) where T : class =>
        values.GetValueOrDefault(id) ?? throw CompensationConflict(
            $"The {kind} entity {id:D} no longer exists.");

    private static SpaceModelSource? Source(
        CompensationEntities entities,
        Guid? id) => !id.HasValue
            ? null
            : entities.Sources.GetValueOrDefault(id.Value) ??
              throw ArtifactInvalid(
                  $"History source {id.Value:D} no longer exists.");

    private static T ParseEnum<T>(string? value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: false, out var result) &&
        Enum.IsDefined(result)
            ? result
            : throw ArtifactInvalid(
                $"History enum value '{value}' is invalid for {typeof(T).Name}.");

    private static void ValidateCompensationRequest(
        Guid versionId,
        Guid matchJobId,
        Guid applyJobId,
        CompensateSpaceExcelCadApplyRequest request)
    {
        if (versionId == Guid.Empty || matchJobId == Guid.Empty ||
            applyJobId == Guid.Empty ||
            request.SchemaVersion != SpaceExcelCadApplyVersions.SchemaVersion ||
            request.Direction is not (
                SpaceExcelCadCompensationDirections.Undo or
                SpaceExcelCadCompensationDirections.Redo) ||
            request.CommandBatchId == Guid.Empty ||
            request.ClientInstanceId == Guid.Empty ||
            request.LeaseId == Guid.Empty ||
            request.ExpectedFloorRevision is < 0 or long.MaxValue ||
            request.ExpectedContentRevision is < 0 or long.MaxValue ||
            !IsSha256(request.HistorySha256))
        {
            throw Invalid(
                "A valid direction, history identity, edit lease, command batch " +
                "and expected revisions are required.");
        }
    }

    private static SpaceProblemException CompensationConflict(string detail) =>
        new(
            SpaceErrorCodes.ConcurrencyConflict,
            409,
            "The Excel/CAD reversible history can no longer be applied.",
            detail,
            "reload-space-studio",
            retryable: true);

    private enum CompensationKind
    {
        Rack,
        RackLevel,
        Location,
        Binding,
        Attribute,
    }

    private sealed record CompensationInput(
        SpaceJob ApplyJob,
        SpaceExcelCadApplyJobPayload Payload,
        SpaceExcelCadApplyResultV1 Result,
        IReadOnlyList<SpaceElementCommandRecord> Records,
        SpaceModel Model,
        SpaceModelVersion Version,
        SpaceFloorRevision Floor,
        SpaceModelSource Source);

    private sealed record CompensationItem(
        SpaceElementCommandRecord Record,
        CompensationKind Kind,
        Snapshot Before,
        Snapshot After)
    {
        public Guid EntityId => Before.Exists
            ? Before.Id
            : After.Id;
    }

    private sealed record Snapshot(bool Exists, string Json, object Value)
    {
        public Guid Id => Value switch
        {
            RackState state => state.Id,
            RackLevelState state => state.Id,
            LocationState state => state.Id,
            BindingState state => state.Id,
            AttributeState state => state.Id,
            _ => Guid.Empty,
        };

        public Guid? SourceId => Value switch
        {
            RackState state => state.SourceId,
            RackLevelState state => state.SourceId,
            LocationState state => state.SourceId,
            BindingState state => state.SourceId,
            AttributeState state => state.SourceId,
            _ => null,
        };

        public Guid? LocationLogicalId => Value is BindingState state
            ? state.LocationLogicalId
            : null;
    }

    private sealed record CompensationEntities(
        IReadOnlyDictionary<Guid, SpaceRackRevision> Racks,
        IReadOnlyDictionary<Guid, SpaceRackLevelRevision> Levels,
        IReadOnlyDictionary<Guid, SpaceLocationRevision> Locations,
        IReadOnlyDictionary<Guid, SpaceLocationExternalBinding> Bindings,
        IReadOnlyDictionary<Guid, SpaceDesignAttribute> Attributes,
        IReadOnlyDictionary<Guid, SpaceModelSource> Sources,
        IReadOnlyDictionary<Guid, SpaceLocationRevision> BoundLocations);

    private sealed record RackState(
        bool Exists,
        Guid Id,
        Guid LogicalId,
        Guid FloorLogicalId,
        Guid ZoneLogicalId,
        Guid? AisleLogicalId,
        string? RackCode,
        string? Name,
        string? RackType,
        Guid? TemplateVersionId,
        int X,
        int Y,
        int Z,
        decimal RotationZ,
        int Width,
        int Depth,
        int Height,
        Guid? SourceId,
        string? SourceRef,
        string? LifecycleState);

    private sealed record RackLevelState(
        bool Exists,
        Guid Id,
        Guid LogicalId,
        Guid RackLogicalId,
        int LevelNo,
        int BottomZ,
        int ClearHeight,
        int BinCount,
        int DepthCount,
        int CellWidth,
        int CellDepth,
        int BeamHeight,
        decimal? MaxLoad,
        Guid? SourceId,
        string? SourceRef,
        string? LifecycleState);

    private sealed record LocationState(
        bool Exists,
        Guid Id,
        Guid LogicalId,
        Guid FloorLogicalId,
        Guid? RackLogicalId,
        string? LocationCode,
        int ColumnNo,
        int LevelNo,
        int DepthNo,
        int Width,
        int Height,
        int Depth,
        decimal? MaxLoad,
        string? LocationType,
        string? CodeOrigin,
        string? ExternalBindingState,
        Guid? SourceId,
        string? SourceRef,
        string? LifecycleState);

    private sealed record BindingState(
        bool Exists,
        Guid Id,
        Guid ModelVersionId,
        Guid LocationLogicalId,
        string? AdapterId,
        string? WarehouseCode,
        string? ExternalLocationId,
        string? BindingMode,
        Guid? SourceId,
        string? SourceRef,
        bool IsDeleted);

    private sealed record AttributeState(
        bool Exists,
        Guid Id,
        Guid ModelVersionId,
        string? ObjectType,
        Guid ObjectLogicalId,
        string? Namespace,
        string? Key,
        string? Value,
        string? Unit,
        Guid? SourceId,
        string? SourceRef,
        bool IsDeleted);
}
