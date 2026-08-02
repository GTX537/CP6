using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpacePersonnelRuntimeService : ISpacePersonnelRuntimeService
{
    private const string CurrentCursorResource = "personnel-current";
    private const string TrajectoryCursorResource = "personnel-trajectory";

    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;
    private readonly ISpaceDesignAccessEvaluator _access;
    private readonly ISpaceCursorCodec _cursorCodec;
    private readonly SpacePersonnelRuntimeOptions _options;

    public SpacePersonnelRuntimeService(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceClock clock,
        ISpaceDesignAccessEvaluator access,
        ISpaceCursorCodec cursorCodec,
        SpacePersonnelRuntimeOptions options)
    {
        _context = context;
        _execution = execution;
        _clock = clock;
        _access = access;
        _cursorCodec = cursorCodec;
        _options = options;
        _options.Validate();
    }

    public async Task<SpacePersonnelCurrentPageDto> GetCurrentAsync(
        Guid siteId,
        string? sourceKind,
        string? workState,
        Guid? floorLogicalId,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        _access.EnsureSiteAccess(siteId, write: false);
        await EnsureSiteAsync(siteId, cancellationToken);

        limit = NormalizeLimit(limit);
        var parsedSourceKind = ParseOptionalEnum<SpacePersonnelSourceKind>(
            sourceKind,
            "sourceKind");
        var parsedWorkState = ParseOptionalEnum<SpacePersonnelWorkState>(
            workState,
            "workState");
        if (floorLogicalId == Guid.Empty)
            throw Invalid("floorLogicalId", "floorLogicalId cannot be empty.");

        var filterHash = Hash(
            $"site={siteId:D}\nsourceKind={Normalize(sourceKind)}" +
            $"\nworkState={Normalize(workState)}" +
            $"\nfloor={floorLogicalId?.ToString("D") ?? ""}\nlimit={limit}");
        var offset = ReadOffset(
            cursor,
            CurrentCursorResource,
            filterHash);

        var query = _context.PersonnelStates
            .AsNoTracking()
            .Where(value => value.SiteId == siteId);
        if (parsedSourceKind.HasValue)
            query = query.Where(value => value.SourceKind == parsedSourceKind.Value);
        if (parsedWorkState.HasValue)
            query = query.Where(value => value.WorkState == parsedWorkState.Value);
        if (floorLogicalId.HasValue)
            query = query.Where(value => value.FloorLogicalId == floorLogicalId.Value);

        var rows = await query
            .OrderBy(value => value.SourceId)
            .ThenBy(value => value.PersonExternalId)
            .ThenBy(value => value.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > limit;
        var now = RequireUtcNow();
        var items = rows
            .Take(limit)
            .Select(value => ToCurrentDto(value, now))
            .ToArray();
        var nextCursor = hasMore
            ? _cursorCodec.Encode(
                new SpaceCursorState(
                    CurrentCursorResource,
                    filterHash,
                    checked(offset + limit)))
            : null;

        return new SpacePersonnelCurrentPageDto(
            siteId,
            new DateTimeOffset(now),
            checked((int)_options.CurrentFreshness.TotalSeconds),
            items,
            nextCursor);
    }

    public async Task<SpacePersonnelTrajectoryResponse> GetTrajectoryAsync(
        Guid siteId,
        string sourceId,
        string personExternalId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        _access.EnsureSiteAccess(siteId, write: false);
        await EnsureSiteAsync(siteId, cancellationToken);

        sourceId = NormalizeIdentity(sourceId, 100, "sourceId");
        personExternalId = NormalizeIdentity(
            personExternalId,
            200,
            "personExternalId");
        limit = NormalizeLimit(limit);
        var now = RequireUtcNow();
        var retentionCutoff = now - _options.TrajectoryRetention;
        ValidateTrajectoryWindow(fromUtc, toUtc, now, retentionCutoff);

        var state = await _context.PersonnelStates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.SiteId == siteId &&
                    value.SourceId == sourceId &&
                    value.PersonExternalId == personExternalId,
                cancellationToken);
        if (state is null)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PersonnelNotFound,
                404,
                "The personnel identity was not found.",
                recoveryAction: "select-existing-personnel-identity");
        }

        var filterHash = Hash(
            $"site={siteId:D}\nsource={sourceId}\nperson={personExternalId}" +
            $"\nfrom={fromUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}" +
            $"\nto={toUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}" +
            $"\nlimit={limit}");
        var offset = ReadOffset(
            cursor,
            TrajectoryCursorResource,
            filterHash);
        var from = fromUtc.UtcDateTime;
        var to = toUtc.UtcDateTime;
        var rows = await _context.PersonnelEvents
            .AsNoTracking()
            .Where(value =>
                value.SiteId == siteId &&
                value.SourceId == sourceId &&
                value.PersonExternalId == personExternalId &&
                value.EventKind == SpacePersonnelEventKind.PositionObserved &&
                value.OccurredAtUtc >= from &&
                value.OccurredAtUtc < to)
            .OrderBy(value => value.OccurredAtUtc)
            .ThenBy(value => value.SourceSequence)
            .ThenBy(value => value.SourceEventId)
            .ThenBy(value => value.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > limit;
        var items = rows
            .Take(limit)
            .Select(ToTrajectoryPoint)
            .ToArray();
        var nextCursor = hasMore
            ? _cursorCodec.Encode(
                new SpaceCursorState(
                    TrajectoryCursorResource,
                    filterHash,
                    checked(offset + limit)))
            : null;

        return new SpacePersonnelTrajectoryResponse(
            siteId,
            sourceId,
            state.SourceKind.ToString(),
            personExternalId,
            fromUtc,
            toUtc,
            new DateTimeOffset(retentionCutoff),
            items,
            nextCursor);
    }

    private SpacePersonnelCurrentDto ToCurrentDto(
        SpacePersonnelCurrentState value,
        DateTime now)
    {
        var positionAge = AgeMilliseconds(now, value.PositionOccurredAtUtc);
        var workStateAge = AgeMilliseconds(now, value.WorkStateOccurredAtUtc);
        var freshnessMilliseconds = _options.CurrentFreshness.TotalMilliseconds;
        return new SpacePersonnelCurrentDto(
            value.SourceId,
            value.SourceKind.ToString(),
            value.PersonExternalId,
            value.WorkState.ToString(),
            value.FloorLogicalId,
            value.LocationLogicalId,
            value.XMillimeters,
            value.YMillimeters,
            value.ZMillimeters,
            value.AccuracyMillimeters,
            ToOffset(value.PositionOccurredAtUtc),
            ToOffset(value.PositionReceivedAtUtc),
            value.PositionEventId,
            value.PositionSourceEventId,
            ToOffset(value.WorkStateOccurredAtUtc),
            ToOffset(value.WorkStateReceivedAtUtc),
            value.WorkStateEventId,
            value.WorkStateSourceEventId,
            positionAge,
            workStateAge,
            value.PositionOccurredAtUtc.HasValue,
            !positionAge.HasValue || positionAge.Value > freshnessMilliseconds,
            !workStateAge.HasValue || workStateAge.Value > freshnessMilliseconds,
            value.SourceKind == SpacePersonnelSourceKind.Simulated);
    }

    private static SpacePersonnelTrajectoryPointDto ToTrajectoryPoint(
        SpacePersonnelEvent value) =>
        new(
            value.Id,
            value.SourceEventId,
            value.FloorLogicalId,
            value.LocationLogicalId,
            value.XMillimeters,
            value.YMillimeters,
            value.ZMillimeters,
            value.AccuracyMillimeters,
            value.SourceSequence,
            ToOffset(value.OccurredAtUtc),
            ToOffset(value.ReceivedAtUtc),
            Math.Max(
                0,
                checked((long)(value.ReceivedAtUtc - value.OccurredAtUtc)
                    .TotalMilliseconds)));

    private async Task EnsureSiteAsync(
        Guid siteId,
        CancellationToken cancellationToken)
    {
        if (siteId == Guid.Empty || !await _context.Models
                .AsNoTracking()
                .AnyAsync(value => value.SiteId == siteId, cancellationToken))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PersonnelSiteNotFound,
                404,
                "The Space site was not found.",
                recoveryAction: "select-existing-site");
        }
    }

    private void ValidateTrajectoryWindow(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        DateTime now,
        DateTime retentionCutoff)
    {
        if (fromUtc.Offset != TimeSpan.Zero || toUtc.Offset != TimeSpan.Zero)
            throw Invalid("timeWindow", "fromUtc and toUtc must use UTC offset +00:00.");
        if (fromUtc >= toUtc)
            throw Invalid("timeWindow", "fromUtc must be earlier than toUtc.");
        if (toUtc.UtcDateTime > now)
            throw Invalid("toUtc", "toUtc cannot be in the future.");
        if (fromUtc.UtcDateTime < retentionCutoff)
        {
            throw Invalid(
                "fromUtc",
                $"fromUtc must be within the {_options.TrajectoryRetention.TotalDays:G} day retention window.");
        }
        if (toUtc - fromUtc > _options.MaximumTrajectoryWindow)
        {
            throw Invalid(
                "timeWindow",
                $"The trajectory window cannot exceed {_options.MaximumTrajectoryWindow.TotalHours:G} hours.");
        }
    }

    private void EnsureExecutionContext()
    {
        if (_execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot read personnel runtime data.",
                recoveryAction: "use-internal-space-principal");
        }
        if (_execution.TenantId == Guid.Empty ||
            _execution.ActorId == Guid.Empty ||
            _execution.TenantId != _context.CurrentTenantId)
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
        var now = _clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private int NormalizeLimit(int limit)
    {
        if (limit == 0)
            return _options.DefaultPageSize;
        if (limit < 1 || limit > _options.MaximumPageSize)
        {
            throw Invalid(
                "limit",
                $"limit must be between 1 and {_options.MaximumPageSize}.");
        }
        return limit;
    }

    private int ReadOffset(
        string? cursor,
        string resource,
        string filterHash)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        var state = _cursorCodec.Decode(cursor, resource, filterHash);
        if (state.Offset < 0)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.CursorInvalid,
                400,
                "The cursor is invalid.",
                recoveryAction: "restart-pagination");
        }
        return state.Offset;
    }

    private static T? ParseOptionalEnum<T>(string? value, string field)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var name = Enum.GetNames<T>().SingleOrDefault(candidate =>
            string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is not null && Enum.TryParse<T>(name, out var parsed))
        {
            return parsed;
        }
        throw Invalid(
            field,
            $"{field} must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
    }

    private static string NormalizeIdentity(
        string value,
        int maximum,
        string field)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maximum ||
            normalized.Any(char.IsControl))
        {
            throw Invalid(
                field,
                $"{field} must contain 1 to {maximum} non-control characters.");
        }
        return normalized;
    }

    private static long? AgeMilliseconds(DateTime now, DateTime? occurredAtUtc) =>
        occurredAtUtc.HasValue
            ? Math.Max(
                0,
                checked((long)(now - occurredAtUtc.Value).TotalMilliseconds))
            : null;

    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value.HasValue ? ToOffset(value.Value) : null;

    private static string Normalize(string? value) =>
        value?.Trim().ToUpperInvariant() ?? "";

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static SpaceProblemException Invalid(string field, string detail) =>
        new(
            SpaceErrorCodes.PersonnelQueryInvalid,
            400,
            "The personnel runtime query is invalid.",
            $"{field}: {detail}",
            "correct-personnel-query");
}
