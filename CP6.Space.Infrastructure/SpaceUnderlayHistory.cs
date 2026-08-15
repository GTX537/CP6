using System.Data;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed partial class SpaceUnderlayV1Service
{
    public async Task<CompensateSpaceUnderlayResponse> CompensateAsync(
        Guid versionId,
        Guid floorLogicalId,
        CompensateSpaceUnderlayRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        ValidateCompensationRequest(versionId, floorLogicalId, request);
        var operation =
            $"underlay-compensation:{request.OriginalCommandBatchId:N}:" +
            request.Direction;
        var requestHash = Hash(JsonSerializer.Serialize(request, JsonOptions));
        var keyHash = IdempotencyKeyHash(operation, idempotencyKey);

        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await AcquireFloorEditLockAsync(
                versionId,
                floorLogicalId,
                cancellationToken);
            var version = await _context.Versions.SingleOrDefaultAsync(
                              item => item.Id == versionId,
                              cancellationToken)
                          ?? throw NotFound(
                              SpaceErrorCodes.VersionNotFound,
                              "Space version");
            var model = await _context.Models.AsNoTracking()
                            .SingleOrDefaultAsync(
                                item => item.Id == version.ModelId,
                                cancellationToken)
                        ?? throw NotFound(
                            SpaceErrorCodes.ModelNotFound,
                            "Space model");
            EnsureWritable(model);
            var floor = await _context.FloorRevisions.SingleOrDefaultAsync(
                            item => item.ModelVersionId == versionId &&
                                    item.LogicalId == floorLogicalId,
                            cancellationToken)
                        ?? throw NotFound(
                            SpaceErrorCodes.LogicalIdNotFound,
                            "Space floor logical identity");
            await EnsureActiveEditLeaseAsync(
                versionId,
                floorLogicalId,
                request.LeaseId,
                request.ClientInstanceId,
                cancellationToken);
            var replay = await ReadCompensationReplayAsync(
                operation,
                keyHash,
                requestHash,
                request,
                version,
                floor,
                cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }
            EnsureUnderlayRevisions(
                version,
                floor,
                request.ExpectedContentRevision,
                request.ExpectedFloorRevision);

            var originalBatch = await _context.ElementCommandBatches.AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == request.OriginalCommandBatchId &&
                            item.ModelVersionId == versionId &&
                            item.FloorLogicalId == floorLogicalId,
                    cancellationToken) ?? throw InvalidUnderlayHistory(
                    "The original underlay command batch is missing.");
            var originalRecords = await _context.ElementCommandRecords.AsNoTracking()
                .Where(item => item.CommandBatchId == request.OriginalCommandBatchId)
                .OrderBy(item => item.SequenceNo)
                .ToArrayAsync(cancellationToken);
            if (originalBatch.ResponseJson is null ||
                originalRecords.Length != 1 ||
                originalRecords[0].TargetLogicalId != floorLogicalId ||
                originalRecords[0].CommandType is not (
                    "UnderlaySet" or "UnderlayCalibrate") ||
                ComputeUnderlayHistorySha256(originalRecords[0]) !=
                request.HistorySha256)
            {
                throw InvalidUnderlayHistory(
                    "The sealed underlay history is missing, unsupported or changed.");
            }

            var original = originalRecords[0];
            var before = DeserializeUnderlaySnapshot(original.BeforeJson);
            var after = DeserializeUnderlaySnapshot(original.AfterJson);
            var expected = request.Direction ==
                           SpaceUnderlayCompensationDirections.Undo
                ? after
                : before;
            var target = request.Direction ==
                         SpaceUnderlayCompensationDirections.Undo
                ? before
                : after;
            if (CurrentUnderlaySnapshot(floor) != expected)
            {
                throw UnderlayConflict(
                    "The current floor underlay no longer matches sealed history.");
            }

            var now = await ReadAuthoritativeUtcNowAsync(cancellationToken);
            var batch = SpaceElementCommandBatch.Create(
                _execution.TenantId,
                request.CommandBatchId,
                versionId,
                floorLogicalId,
                request.ClientInstanceId,
                request.LeaseId,
                request.ExpectedFloorRevision,
                request.ExpectedContentRevision,
                version.ContentHash,
                request.HistorySha256,
                requestHash,
                _execution.ActorId,
                now);
            var beforeJson = SerializeUnderlaySnapshot(floor);
            await RestoreUnderlaySnapshotAsync(
                floor,
                target,
                cancellationToken);
            floor.AdvanceRevision(request.ExpectedFloorRevision);
            version.TouchContent();
            var record = SpaceElementCommandRecord.Create(
                _execution.TenantId,
                Guid.NewGuid(),
                batch,
                0,
                $"Underlay{request.Direction}:{original.CommandType}",
                floorLogicalId,
                JsonSerializer.Serialize(
                    new
                    {
                        request.OriginalCommandBatchId,
                        request.Direction,
                    },
                    JsonOptions),
                beforeJson,
                SerializeUnderlaySnapshot(floor));
            var response = new CompensateSpaceUnderlayResponse(
                SpaceUnderlayHistoryVersions.SchemaVersion,
                request.OriginalCommandBatchId,
                request.CommandBatchId,
                request.Direction,
                request.HistorySha256,
                ToDto(floor),
                version.ContentRevision,
                IdempotentReplay: false);
            var responseJson = JsonSerializer.Serialize(response, JsonOptions);
            batch.Complete(floor.Revision, version.ContentRevision, responseJson);
            _context.ElementCommandBatches.Add(batch);
            _context.ElementCommandRecords.Add(record);
            _context.IdempotencyRecords.Add(NewIdempotencyRecord(
                operation,
                keyHash,
                requestHash,
                responseJson,
                now));
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw UnderlayConflict(
                "The floor underlay changed concurrently.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<CompensateSpaceUnderlayResponse?>
        ReadCompensationReplayAsync(
            string operation,
            string keyHash,
            string requestHash,
            CompensateSpaceUnderlayRequest request,
            SpaceModelVersion version,
            SpaceFloorRevision floor,
            CancellationToken cancellationToken)
    {
        var record = await _context.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.PrincipalId == _execution.ActorId &&
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
            .Deserialize<CompensateSpaceUnderlayResponse>(
                record.ResponseJson,
                JsonOptions) ?? throw InvalidUnderlayHistory(
                "The stored underlay compensation response is invalid.");
        if (response.CommandBatchId != request.CommandBatchId ||
            response.OriginalCommandBatchId != request.OriginalCommandBatchId ||
            response.HistorySha256 != request.HistorySha256 ||
            floor.Revision != response.Floor.RevisionNumber ||
            version.ContentRevision != response.VersionContentRevision)
        {
            throw UnderlayConflict(
                "The Draft advanced after this underlay history action.");
        }
        return response with { IdempotentReplay = true };
    }

    private async Task EnsureUnderlayReplayCurrentAsync(
        Guid versionId,
        Guid floorLogicalId,
        SpaceSceneFloorDto responseFloor,
        long responseContentRevision,
        CancellationToken cancellationToken)
    {
        var floorState = await _context.FloorRevisions.AsNoTracking()
            .Where(item =>
                item.ModelVersionId == versionId &&
                item.LogicalId == floorLogicalId)
            .Select(item => new
            {
                item.ModelVersionId,
                item.LogicalId,
                item.Revision,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (floorState is null ||
            responseFloor.Revision.LogicalId != floorLogicalId ||
            floorState.Revision != responseFloor.RevisionNumber)
        {
            throw UnderlayConflict(
                "The floor advanced after this underlay request was applied.");
        }
        var contentRevision = await _context.Versions.AsNoTracking()
            .Where(item => item.Id == floorState.ModelVersionId)
            .Select(item => (long?)item.ContentRevision)
            .SingleOrDefaultAsync(cancellationToken);
        if (contentRevision != responseContentRevision)
        {
            throw UnderlayConflict(
                "The Draft advanced after this underlay request was applied.");
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
        var lease = await _context.EditLeases.AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.ModelVersionId == versionId &&
                        item.FloorLogicalId == floorLogicalId,
                cancellationToken);
        if (lease is null || lease.LeaseId != leaseId ||
            lease.OwnerUserId != _execution.ActorId ||
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
        var now = _context.Database.IsSqlServer()
            ? await _context.Database
                .SqlQueryRaw<DateTime>("SELECT SYSUTCDATETIME() AS [Value]")
                .SingleAsync(cancellationToken)
            : RequireUtcNow();
        return now.Kind == DateTimeKind.Utc
            ? now
            : DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }

    private async Task AcquireFloorEditLockAsync(
        Guid versionId,
        Guid floorLogicalId,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsSqlServer())
            return;
        var result = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output,
        };
        var resource = new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        {
            Value = $"cp6:space:floor-edit:{_execution.TenantId:N}:" +
                    $"{versionId:N}:{floorLogicalId:N}",
        };
        await _context.Database.ExecuteSqlRawAsync(
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
                recoveryAction: "retry-underlay-write",
                retryable: true);
        }
    }

    private static void EnsureUnderlayRevisions(
        SpaceModelVersion version,
        SpaceFloorRevision floor,
        long expectedContentRevision,
        long expectedFloorRevision)
    {
        if (floor.Revision != expectedFloorRevision)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.FloorRevisionConflict,
                409,
                "The floor revision changed before the underlay write.",
                recoveryAction: "reload-floor-scene");
        }
        if (version.ContentRevision != expectedContentRevision)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.VersionConflict,
                409,
                "The Draft content revision changed before the underlay write.",
                recoveryAction: "reload-floor-scene");
        }
    }

    private static string SerializeUnderlaySnapshot(SpaceFloorRevision floor) =>
        JsonSerializer.Serialize(CurrentUnderlaySnapshot(floor), JsonOptions);

    private static UnderlaySnapshot CurrentUnderlaySnapshot(
        SpaceFloorRevision floor) =>
        new(
            floor.UnderlaySourceId,
            floor.UnderlayCalibrationId,
            floor.UnderlayScale,
            floor.UnderlayOffsetX,
            floor.UnderlayOffsetY,
            floor.UnderlayRotationZ);

    private static UnderlaySnapshot DeserializeUnderlaySnapshot(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<UnderlaySnapshot>(json, JsonOptions)
                   ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw InvalidUnderlayHistory(
                "A sealed underlay history snapshot is invalid.");
        }
    }

    private async Task RestoreUnderlaySnapshotAsync(
        SpaceFloorRevision floor,
        UnderlaySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        SpaceModelSource? source = null;
        if (snapshot.SourceId.HasValue)
        {
            source = await _context.Sources.SingleOrDefaultAsync(
                         item => item.Id == snapshot.SourceId &&
                                 item.ModelVersionId == floor.ModelVersionId,
                         cancellationToken)
                     ?? throw InvalidUnderlayHistory(
                         "The underlay source referenced by history is missing.");
            EnsureUnderlayType(source.SourceType);
        }
        SpaceUnderlayCalibration? calibration = null;
        if (snapshot.CalibrationId.HasValue)
        {
            calibration = await _context.UnderlayCalibrations.AsNoTracking()
                              .SingleOrDefaultAsync(
                                  item => item.Id == snapshot.CalibrationId &&
                                          item.ModelVersionId == floor.ModelVersionId &&
                                          item.FloorLogicalId == floor.LogicalId &&
                                          item.SourceId == snapshot.SourceId,
                                  cancellationToken)
                          ?? throw InvalidUnderlayHistory(
                              "The underlay calibration referenced by history is missing.");
        }
        floor.RestoreUnderlaySnapshot(
            source,
            calibration,
            snapshot.Scale,
            snapshot.OffsetX,
            snapshot.OffsetY,
            snapshot.RotationZ);
    }

    private static SpaceUnderlayHistoryDto SealUnderlayHistory(
        SpaceElementCommandBatch batch,
        SpaceElementCommandRecord record) =>
        new(
            SpaceUnderlayHistoryVersions.SchemaVersion,
            batch.Id,
            record.CommandType,
            ComputeUnderlayHistorySha256(record));

    private static string ComputeUnderlayHistorySha256(
        SpaceElementCommandRecord record)
    {
        var value = new StringBuilder()
            .Append(record.CommandBatchId.ToString("N"))
            .Append('\u001f').Append(record.Id.ToString("N"))
            .Append('\u001f').Append(record.SequenceNo)
            .Append('\u001f').Append(record.CommandType)
            .Append('\u001f').Append(record.TargetLogicalId.ToString("N"))
            .Append('\u001f').Append(record.BeforeJson)
            .Append('\u001f').Append(record.AfterJson)
            .ToString();
        return Hash(value);
    }

    private static void ValidateCompensationRequest(
        Guid versionId,
        Guid floorLogicalId,
        CompensateSpaceUnderlayRequest request)
    {
        if (versionId == Guid.Empty || floorLogicalId == Guid.Empty ||
            request.SchemaVersion != SpaceUnderlayHistoryVersions.SchemaVersion ||
            request.Direction is not (
                SpaceUnderlayCompensationDirections.Undo or
                SpaceUnderlayCompensationDirections.Redo) ||
            request.OriginalCommandBatchId == Guid.Empty ||
            request.CommandBatchId == Guid.Empty ||
            request.ClientInstanceId == Guid.Empty ||
            request.LeaseId == Guid.Empty ||
            request.ExpectedFloorRevision < 0 ||
            request.ExpectedContentRevision < 0 ||
            !IsSha256(request.HistorySha256))
        {
            throw InvalidUnderlayHistory(
                "A valid history identity, direction, edit lease and revisions are required.");
        }
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static SpaceProblemException InvalidUnderlayHistory(string detail) =>
        new(
            SpaceErrorCodes.UnderlayHistoryInvalid,
            422,
            "The underlay reversible history is invalid.",
            detail,
            "reload-space-studio");

    private static SpaceProblemException UnderlayConflict(string detail) =>
        new(
            SpaceErrorCodes.ConcurrencyConflict,
            409,
            "The underlay reversible history can no longer be applied.",
            detail,
            "reload-floor-scene");

    private sealed record UnderlaySnapshot(
        Guid? SourceId,
        Guid? CalibrationId,
        decimal? Scale,
        int OffsetX,
        int OffsetY,
        decimal RotationZ);
}
