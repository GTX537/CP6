using System.Security.Cryptography;
using System.Text;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceDeviceRuntimeService : ISpaceDeviceRuntimeService
{
    private const string CurrentCursorResource = "device-current";

    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;
    private readonly ISpaceDesignAccessEvaluator _access;
    private readonly ISpaceCursorCodec _cursorCodec;
    private readonly SpaceDeviceRuntimeOptions _options;

    public SpaceDeviceRuntimeService(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceClock clock,
        ISpaceDesignAccessEvaluator access,
        ISpaceCursorCodec cursorCodec,
        SpaceDeviceRuntimeOptions options)
    {
        _context = context;
        _execution = execution;
        _clock = clock;
        _access = access;
        _cursorCodec = cursorCodec;
        _options = options;
        _options.Validate();
    }

    public async Task<SpaceDeviceCurrentPageDto> GetCurrentAsync(
        Guid siteId,
        string? sourceKind,
        string? deviceKind,
        string? operatingState,
        Guid? floorLogicalId,
        bool? hasActiveAlarm,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        _access.EnsureSiteAccess(siteId, write: false);
        var publishedVersionId = await LoadPublishedVersionAsync(
            siteId,
            cancellationToken);

        limit = NormalizeLimit(limit);
        var parsedSourceKind = ParseOptionalEnum<SpaceDeviceSourceKind>(
            sourceKind,
            "sourceKind");
        var parsedDeviceKind = ParseOptionalEnum<SpaceDeviceKind>(
            deviceKind,
            "deviceKind");
        var parsedOperatingState = ParseOptionalEnum<SpaceDeviceOperatingState>(
            operatingState,
            "operatingState");
        if (floorLogicalId == Guid.Empty)
            throw Invalid("floorLogicalId", "floorLogicalId cannot be empty.");

        var filterHash = Hash(
            $"site={siteId:D}\nsourceKind={Normalize(sourceKind)}" +
            $"\ndeviceKind={Normalize(deviceKind)}" +
            $"\noperatingState={Normalize(operatingState)}" +
            $"\nfloor={floorLogicalId?.ToString("D") ?? ""}" +
            $"\nhasAlarm={hasActiveAlarm?.ToString() ?? ""}\nlimit={limit}");
        var offset = ReadOffset(cursor, filterHash);

        var currentElements = _context.ElementRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == publishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active);
        var currentLocations = _context.LocationRevisions
            .AsNoTracking()
            .Where(value =>
                value.ModelVersionId == publishedVersionId &&
                value.LifecycleState == SpaceLifecycleState.Active);
        var query =
            from mapping in _context.DeviceMappings.AsNoTracking()
            where mapping.SiteId == siteId
            join stateValue in _context.DeviceStates.AsNoTracking()
                on new
                {
                    mapping.SiteId,
                    mapping.SourceId,
                    mapping.DeviceExternalId,
                }
                equals new
                {
                    stateValue.SiteId,
                    stateValue.SourceId,
                    stateValue.DeviceExternalId,
                }
                into stateValues
            from state in stateValues.DefaultIfEmpty()
            join elementValue in currentElements
                on mapping.ElementLogicalId equals elementValue.LogicalId
                into elementValues
            from element in elementValues.DefaultIfEmpty()
            join locationValue in currentLocations
                on (state == null ? null : state.LocationLogicalId)
                equals (Guid?)locationValue.LogicalId
                into locationValues
            from location in locationValues.DefaultIfEmpty()
            select new { Mapping = mapping, State = state, Element = element, Location = location };

        if (parsedSourceKind.HasValue)
            query = query.Where(value =>
                value.Mapping.SourceKind == parsedSourceKind.Value);
        if (parsedDeviceKind.HasValue)
            query = query.Where(value =>
                value.Mapping.DeviceKind == parsedDeviceKind.Value);
        if (parsedOperatingState.HasValue)
        {
            query = parsedOperatingState.Value == SpaceDeviceOperatingState.Unknown
                ? query.Where(value =>
                    value.State == null ||
                    value.State.OperatingState == SpaceDeviceOperatingState.Unknown)
                : query.Where(value =>
                    value.State != null &&
                    value.State.OperatingState == parsedOperatingState.Value);
        }
        if (floorLogicalId.HasValue)
        {
            query = query.Where(value =>
                value.State != null &&
                value.State.FloorLogicalId == floorLogicalId.Value ||
                (value.State == null || !value.State.FloorLogicalId.HasValue) &&
                value.Location != null &&
                value.Location.FloorLogicalId == floorLogicalId.Value ||
                (value.State == null ||
                 !value.State.FloorLogicalId.HasValue &&
                 !value.State.LocationLogicalId.HasValue) &&
                value.Element != null &&
                value.Element.FloorLogicalId == floorLogicalId.Value);
        }
        if (hasActiveAlarm.HasValue)
        {
            query = hasActiveAlarm.Value
                ? query.Where(value => _context.DeviceAlarmStates.Any(alarm =>
                    alarm.SiteId == siteId &&
                    alarm.DeviceMappingId == value.Mapping.Id &&
                    alarm.IsActive))
                : query.Where(value => !_context.DeviceAlarmStates.Any(alarm =>
                    alarm.SiteId == siteId &&
                    alarm.DeviceMappingId == value.Mapping.Id &&
                    alarm.IsActive));
        }

        var rows = await query
            .OrderBy(value => value.Mapping.SourceId)
            .ThenBy(value => value.Mapping.DeviceExternalId)
            .ThenBy(value => value.Mapping.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > limit;
        var pageRows = rows.Take(limit).ToArray();
        var mappingIds = pageRows.Select(value => value.Mapping.Id).ToArray();
        var alarms = mappingIds.Length == 0
            ? new List<SpaceDeviceAlarmState>()
            : await _context.DeviceAlarmStates
                .AsNoTracking()
                .Where(value =>
                    value.SiteId == siteId &&
                    mappingIds.Contains(value.DeviceMappingId) &&
                    value.IsActive)
                .OrderByDescending(value => value.AlarmSeverity)
                .ThenBy(value => value.OccurredAtUtc)
                .ThenBy(value => value.AlarmExternalId)
                .ToListAsync(cancellationToken);
        var alarmsByMapping = alarms
            .GroupBy(value => value.DeviceMappingId)
            .ToDictionary(value => value.Key, value => value.ToArray());
        var now = RequireUtcNow();
        var items = pageRows.Select(value => ToDto(
            value.Mapping,
            value.State,
            value.Element,
            value.Location,
            alarmsByMapping.GetValueOrDefault(value.Mapping.Id, []),
            now)).ToArray();
        var nextCursor = hasMore
            ? _cursorCodec.Encode(new SpaceCursorState(
                CurrentCursorResource,
                filterHash,
                checked(offset + limit)))
            : null;

        return new SpaceDeviceCurrentPageDto(
            siteId,
            publishedVersionId,
            new DateTimeOffset(now),
            checked((int)_options.CurrentFreshness.TotalSeconds),
            items,
            nextCursor);
    }

    private SpaceDeviceCurrentDto ToDto(
        SpaceDeviceMapping mapping,
        SpaceDeviceCurrentState? state,
        SpaceElementRevision? element,
        SpaceLocationRevision? location,
        IReadOnlyList<SpaceDeviceAlarmState> alarms,
        DateTime now)
    {
        var positionAge = AgeMilliseconds(now, state?.PositionOccurredAtUtc);
        var operatingStateAge = AgeMilliseconds(
            now,
            state?.OperatingStateOccurredAtUtc);
        var freshnessMilliseconds = _options.CurrentFreshness.TotalMilliseconds;
        var mappingIsCurrent = element is not null &&
                               IsCompatible(
                                   mapping.DeviceKind,
                                   element.ElementType);
        var activeAlarmDtos = alarms.Select(value =>
            new SpaceDeviceActiveAlarmDto(
                value.AlarmExternalId,
                value.AlarmCode!,
                value.AlarmSeverity!.Value.ToString(),
                value.AlarmMessage,
                ToOffset(value.OccurredAtUtc),
                ToOffset(value.ReceivedAtUtc),
                value.EventId,
                value.SourceEventId,
                AgeMilliseconds(now, value.OccurredAtUtc)))
            .ToArray();
        var maximumSeverity = alarms
            .Where(value => value.AlarmSeverity.HasValue)
            .Select(value => value.AlarmSeverity)
            .Max();

        return new SpaceDeviceCurrentDto(
            mapping.Id,
            mapping.SourceId,
            mapping.SourceKind.ToString(),
            mapping.DeviceExternalId,
            mapping.DeviceKind.ToString(),
            mapping.ElementLogicalId,
            mapping.ElementType,
            mappingIsCurrent,
            mappingIsCurrent ? element!.FloorLogicalId : null,
            mappingIsCurrent ? element!.X : null,
            mappingIsCurrent ? element!.Y : null,
            mappingIsCurrent ? element!.Z : null,
            (state?.OperatingState ?? SpaceDeviceOperatingState.Unknown).ToString(),
            state?.FloorLogicalId ?? location?.FloorLogicalId,
            state?.LocationLogicalId,
            state?.XMillimeters,
            state?.YMillimeters,
            state?.ZMillimeters,
            state?.AccuracyMillimeters,
            ToOffset(state?.PositionOccurredAtUtc),
            ToOffset(state?.PositionReceivedAtUtc),
            state?.PositionEventId,
            state?.PositionSourceEventId,
            ToOffset(state?.OperatingStateOccurredAtUtc),
            ToOffset(state?.OperatingStateReceivedAtUtc),
            state?.OperatingStateEventId,
            state?.OperatingStateSourceEventId,
            positionAge,
            operatingStateAge,
            state?.PositionOccurredAtUtc.HasValue == true,
            !positionAge.HasValue || positionAge.Value > freshnessMilliseconds,
            !operatingStateAge.HasValue ||
            operatingStateAge.Value > freshnessMilliseconds,
            mapping.SourceKind == SpaceDeviceSourceKind.Simulated,
            activeAlarmDtos.Length > 0,
            activeAlarmDtos.Length,
            maximumSeverity?.ToString(),
            activeAlarmDtos);
    }

    private async Task<Guid> LoadPublishedVersionAsync(
        Guid siteId,
        CancellationToken cancellationToken)
    {
        if (siteId == Guid.Empty)
            throw SiteNotFound();
        var versionId = await _context.Models
            .AsNoTracking()
            .Where(value => value.SiteId == siteId)
            .Select(value => value.CurrentPublishedVersionId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!versionId.HasValue)
            throw SiteNotFound();
        if (!await _context.Versions.AsNoTracking().AnyAsync(
                value =>
                    value.Id == versionId.Value &&
                    value.Status == SpaceVersionStatus.Published,
                cancellationToken))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.VersionStateInvalid,
                409,
                "The current runtime version is not Published.",
                recoveryAction: "publish-version");
        }
        return versionId.Value;
    }

    private void EnsureExecutionContext()
    {
        if (_execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot read device runtime data.",
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

    private int ReadOffset(string? cursor, string filterHash)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        var state = _cursorCodec.Decode(
            cursor,
            CurrentCursorResource,
            filterHash);
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
            string.Equals(
                candidate,
                value.Trim(),
                StringComparison.OrdinalIgnoreCase));
        if (name is not null && Enum.TryParse<T>(name, out var parsed))
            return parsed;
        throw Invalid(
            field,
            $"{field} must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
    }

    private static bool IsCompatible(
        SpaceDeviceKind deviceKind,
        string elementType) =>
        deviceKind switch
        {
            SpaceDeviceKind.Conveyor =>
                elementType == SpaceElementTypes.Conveyor,
            SpaceDeviceKind.Lift =>
                elementType is SpaceElementTypes.Device or
                    SpaceElementTypes.StaticEquipment or
                    SpaceElementTypes.Elevator,
            SpaceDeviceKind.Sorter =>
                elementType is SpaceElementTypes.Device or
                    SpaceElementTypes.StaticEquipment or
                    SpaceElementTypes.Conveyor,
            SpaceDeviceKind.Workstation =>
                elementType is SpaceElementTypes.Device or
                    SpaceElementTypes.StaticEquipment or
                    SpaceElementTypes.Workstation,
            _ => elementType is SpaceElementTypes.Device or
                SpaceElementTypes.StaticEquipment,
        };

    private static long? AgeMilliseconds(DateTime now, DateTime? occurredAtUtc) =>
        occurredAtUtc.HasValue
            ? Math.Max(
                0,
                checked((long)(now - occurredAtUtc.Value).TotalMilliseconds))
            : null;

    private static long AgeMilliseconds(DateTime now, DateTime occurredAtUtc) =>
        Math.Max(
            0,
            checked((long)(now - occurredAtUtc).TotalMilliseconds));

    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value.HasValue ? ToOffset(value.Value) : null;

    private static string Normalize(string? value) =>
        value?.Trim().ToUpperInvariant() ?? "";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static SpaceProblemException Invalid(string field, string detail) =>
        new(
            SpaceErrorCodes.DeviceQueryInvalid,
            400,
            "The device runtime query is invalid.",
            $"{field}: {detail}",
            "correct-device-query");

    private static SpaceProblemException SiteNotFound() =>
        new(
            SpaceErrorCodes.DeviceSiteNotFound,
            404,
            "The Space site was not found.",
            recoveryAction: "select-existing-site");
}
