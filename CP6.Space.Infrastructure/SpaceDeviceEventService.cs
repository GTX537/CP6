using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Space.Infrastructure;

public sealed class SpaceDeviceEventService : ISpaceDeviceEventService
{
    private const string MappingCursorResource = "device-mapping";
    private const int DefaultPageSize = 100;
    private const int MaximumPageSize = 500;
    private const int MaximumBatchSize = 500;
    private const decimal MaximumCoordinateMagnitude = 1_000_000_000m;
    private static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(5);

    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;
    private readonly ISpaceDesignAccessEvaluator _access;
    private readonly ISpaceCursorCodec _cursorCodec;

    public SpaceDeviceEventService(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceClock clock,
        ISpaceDesignAccessEvaluator access,
        ISpaceCursorCodec cursorCodec)
    {
        _context = context;
        _execution = execution;
        _clock = clock;
        _access = access;
        _cursorCodec = cursorCodec;
    }

    public async Task<SpaceDeviceMappingPageDto> GetMappingsAsync(
        Guid siteId,
        string? sourceId,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        _access.EnsureSiteAccess(siteId, write: false);
        await EnsureSiteAsync(siteId, cancellationToken);

        var normalizedSource = string.IsNullOrWhiteSpace(sourceId)
            ? null
            : NormalizeIdentity(sourceId, 100, "sourceId");
        limit = NormalizeLimit(limit);
        var filterHash = Hash(
            $"site={siteId:D}\nsource={normalizedSource ?? ""}\nlimit={limit}");
        var offset = ReadOffset(cursor, filterHash);

        var query = _context.DeviceMappings
            .AsNoTracking()
            .Where(value => value.SiteId == siteId);
        if (normalizedSource is not null)
            query = query.Where(value => value.SourceId == normalizedSource);

        var rows = await query
            .OrderBy(value => value.SourceId)
            .ThenBy(value => value.DeviceExternalId)
            .ThenBy(value => value.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > limit;
        var items = rows.Take(limit).Select(ToDto).ToArray();
        var nextCursor = hasMore
            ? _cursorCodec.Encode(
                new SpaceCursorState(
                    MappingCursorResource,
                    filterHash,
                    checked(offset + limit)))
            : null;
        return new SpaceDeviceMappingPageDto(items, nextCursor);
    }

    public async Task<SpaceDeviceMappingDto> CreateMappingAsync(
        Guid siteId,
        CreateSpaceDeviceMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        _access.EnsureSiteAccess(siteId, write: true);
        var scope = await LoadPublishedScopeAsync(siteId, cancellationToken);

        var sourceId = NormalizeIdentity(request.SourceId, 100, "sourceId");
        var sourceKind = ParseEnum<SpaceDeviceSourceKind>(
            request.SourceKind,
            "sourceKind");
        var deviceExternalId = NormalizeIdentity(
            request.DeviceExternalId,
            200,
            "deviceExternalId");
        var deviceKind = ParseEnum<SpaceDeviceKind>(
            request.DeviceKind,
            "deviceKind");
        var element = await LoadCompatibleElementAsync(
            scope.PublishedVersionId,
            request.ElementLogicalId,
            deviceKind,
            cancellationToken);

        if (await _context.DeviceMappings.AnyAsync(
                value =>
                    value.SiteId == siteId &&
                    value.SourceId == sourceId &&
                    (value.DeviceExternalId == deviceExternalId ||
                     value.ElementLogicalId == element.LogicalId),
                cancellationToken))
        {
            throw Conflict(
                SpaceErrorCodes.DeviceMappingConflict,
                "The source device identity or Space element is already mapped.",
                "use-existing-device-mapping");
        }

        var mapping = SpaceDeviceMapping.Create(
            _execution.TenantId,
            siteId,
            sourceId,
            sourceKind,
            deviceExternalId,
            deviceKind,
            element.LogicalId,
            element.ElementType,
            scope.PublishedVersionId,
            element.FloorLogicalId);
        _context.DeviceMappings.Add(mapping);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw Conflict(
                SpaceErrorCodes.DeviceMappingConflict,
                "The source device identity or Space element was mapped concurrently.",
                "reload-device-mappings",
                exception);
        }
        return ToDto(mapping);
    }

    public async Task<SpaceDeviceMappingDto> UpdateMappingAsync(
        Guid siteId,
        Guid mappingId,
        UpdateSpaceDeviceMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        _access.EnsureSiteAccess(siteId, write: true);
        var scope = await LoadPublishedScopeAsync(siteId, cancellationToken);
        if (mappingId == Guid.Empty)
            throw Invalid("mappingId", "mappingId must be non-empty.");

        var mapping = await _context.DeviceMappings.SingleOrDefaultAsync(
                          value => value.SiteId == siteId && value.Id == mappingId,
                          cancellationToken)
                      ?? throw NotFound(
                          SpaceErrorCodes.DeviceMappingNotFound,
                          "The device mapping was not found.");
        EnsureExpectedRowVersion(mapping, request.ExpectedRowVersion);
        var deviceKind = ParseEnum<SpaceDeviceKind>(
            request.DeviceKind,
            "deviceKind");
        var element = await LoadCompatibleElementAsync(
            scope.PublishedVersionId,
            request.ElementLogicalId,
            deviceKind,
            cancellationToken);
        if (await _context.DeviceMappings.AnyAsync(
                value =>
                    value.SiteId == siteId &&
                    value.SourceId == mapping.SourceId &&
                    value.ElementLogicalId == element.LogicalId &&
                    value.Id != mapping.Id,
                cancellationToken))
        {
            throw Conflict(
                SpaceErrorCodes.DeviceMappingConflict,
                "The Space element is already mapped for this source.",
                "select-unmapped-device-element");
        }

        mapping.Remap(
            deviceKind,
            element.LogicalId,
            element.ElementType,
            scope.PublishedVersionId,
            element.FloorLogicalId);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw Conflict(
                SpaceErrorCodes.ConcurrencyConflict,
                "The device mapping changed after it was loaded.",
                "reload-device-mapping",
                exception);
        }
        catch (DbUpdateException exception)
        {
            throw Conflict(
                SpaceErrorCodes.DeviceMappingConflict,
                "The Space element was mapped concurrently.",
                "reload-device-mappings",
                exception);
        }
        return ToDto(mapping);
    }

    public async Task<IngestSpaceDeviceEventsResponse> IngestAsync(
        Guid siteId,
        IngestSpaceDeviceEventsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        _access.EnsureSiteAccess(siteId, write: true);
        var now = RequireUtcNow();
        var normalized = Normalize(request, now);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await IngestCoreAsync(
                    siteId,
                    normalized,
                    now,
                    cancellationToken);
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                _context.ChangeTracker.Clear();
            }
            catch (DbUpdateException exception)
            {
                throw Conflict(
                    SpaceErrorCodes.DeviceEventConflict,
                    "The device event stream changed concurrently.",
                    "retry-device-event-batch",
                    exception,
                    retryable: true);
            }
        }
    }

    private async Task<IngestSpaceDeviceEventsResponse> IngestCoreAsync(
        Guid siteId,
        NormalizedRequest request,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken)
    {
        var scope = await LoadPublishedScopeAsync(siteId, cancellationToken);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var sourceEventIds = request.Events
            .Select(value => value.SourceEventId)
            .ToArray();
        var existing = await _context.DeviceEvents
            .AsNoTracking()
            .Where(value =>
                value.SiteId == siteId &&
                value.SourceId == request.SourceId &&
                sourceEventIds.Contains(value.SourceEventId))
            .ToDictionaryAsync(value => value.SourceEventId, cancellationToken);

        foreach (var item in request.Events)
        {
            if (existing.TryGetValue(item.SourceEventId, out var prior) &&
                !string.Equals(
                    prior.PayloadHash,
                    item.PayloadHash,
                    StringComparison.Ordinal))
            {
                throw Conflict(
                    SpaceErrorCodes.DeviceEventConflict,
                    "A source event identity was reused with a different payload.",
                    "use-unique-source-event-id");
            }
        }

        var newInputs = request.Events
            .Where(value => !existing.ContainsKey(value.SourceEventId))
            .ToArray();
        var deviceIds = newInputs
            .Select(value => value.DeviceExternalId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var mappings = deviceIds.Length == 0
            ? new Dictionary<string, SpaceDeviceMapping>(StringComparer.Ordinal)
            : await _context.DeviceMappings
                .Where(value =>
                    value.SiteId == siteId &&
                    value.SourceId == request.SourceId &&
                    deviceIds.Contains(value.DeviceExternalId))
                .ToDictionaryAsync(
                    value => value.DeviceExternalId,
                    StringComparer.Ordinal,
                    cancellationToken);
        if (mappings.Count != deviceIds.Length)
        {
            throw NotFound(
                SpaceErrorCodes.DeviceMappingNotFound,
                "Every device event requires an existing source device mapping.");
        }
        if (mappings.Values.Any(value => value.SourceKind != request.SourceKind))
        {
            throw Conflict(
                SpaceErrorCodes.DeviceMappingConflict,
                "A device source cannot switch between Real and Simulated.",
                "use-matching-device-source-kind");
        }

        var elementIds = mappings.Values
            .Select(value => value.ElementLogicalId)
            .Distinct()
            .ToArray();
        var elements = elementIds.Length == 0
            ? new Dictionary<Guid, SpaceElementRevision>()
            : await _context.ElementRevisions
                .AsNoTracking()
                .Where(value =>
                    value.ModelVersionId == scope.PublishedVersionId &&
                    value.LifecycleState == SpaceLifecycleState.Active &&
                    elementIds.Contains(value.LogicalId))
                .ToDictionaryAsync(value => value.LogicalId, cancellationToken);
        foreach (var mapping in mappings.Values)
        {
            if (!elements.TryGetValue(mapping.ElementLogicalId, out var element) ||
                !IsCompatible(mapping.DeviceKind, element.ElementType))
            {
                throw Conflict(
                    SpaceErrorCodes.DeviceMappingStale,
                    "A device mapping no longer resolves to a compatible element in the current Published version.",
                    "revalidate-device-mapping");
            }
        }
        await ValidateSpatialReferencesAsync(
            scope.PublishedVersionId,
            newInputs,
            cancellationToken);

        var states = deviceIds.Length == 0
            ? new Dictionary<string, SpaceDeviceCurrentState>(StringComparer.Ordinal)
            : await _context.DeviceStates
                .Where(value =>
                    value.SiteId == siteId &&
                    value.SourceId == request.SourceId &&
                    deviceIds.Contains(value.DeviceExternalId))
                .ToDictionaryAsync(
                    value => value.DeviceExternalId,
                    StringComparer.Ordinal,
                    cancellationToken);
        var alarmIds = newInputs
            .Where(value => value.AlarmExternalId is not null)
            .Select(value => value.AlarmExternalId!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var alarmStates = alarmIds.Length == 0
            ? new Dictionary<(string DeviceExternalId, string AlarmExternalId),
                SpaceDeviceAlarmState>()
            : (await _context.DeviceAlarmStates
                .Where(value =>
                    value.SiteId == siteId &&
                    value.SourceId == request.SourceId &&
                    deviceIds.Contains(value.DeviceExternalId) &&
                    alarmIds.Contains(value.AlarmExternalId))
                .ToListAsync(cancellationToken))
                .ToDictionary(value =>
                    (value.DeviceExternalId, value.AlarmExternalId));

        var created = new Dictionary<string, SpaceDeviceEvent>(StringComparer.Ordinal);
        foreach (var item in newInputs)
        {
            var mapping = mappings[item.DeviceExternalId];
            var value = SpaceDeviceEvent.Create(
                _execution.TenantId,
                siteId,
                request.SourceId,
                request.SourceKind,
                item.SourceEventId,
                mapping,
                item.EventKind,
                item.OperatingState,
                item.FloorLogicalId,
                item.LocationLogicalId,
                item.XMillimeters,
                item.YMillimeters,
                item.ZMillimeters,
                item.AccuracyMillimeters,
                item.AlarmExternalId,
                item.AlarmCode,
                item.AlarmSeverity,
                item.AlarmMessage,
                item.SourceSequence,
                item.OccurredAtUtc,
                receivedAtUtc,
                item.PayloadHash);
            created.Add(item.SourceEventId, value);
        }
        var projectionApplied = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var item in newInputs
                     .OrderBy(value => value.DeviceExternalId, StringComparer.Ordinal)
                     .ThenBy(ProjectionChannel)
                     .ThenBy(value => value.AlarmExternalId, StringComparer.Ordinal)
                     .ThenBy(value => value.OccurredAtUtc)
                     .ThenBy(value => value.SourceSequence.HasValue ? 1 : 0)
                     .ThenBy(value => value.SourceSequence)
                     .ThenBy(value => value.SourceEventId, StringComparer.Ordinal))
        {
            var deviceEvent = created[item.SourceEventId];
            if (item.EventKind is SpaceDeviceEventKind.PositionObserved or
                SpaceDeviceEventKind.OperatingStateChanged)
            {
                if (!states.TryGetValue(item.DeviceExternalId, out var state))
                {
                    state = SpaceDeviceCurrentState.Create(deviceEvent);
                    states.Add(item.DeviceExternalId, state);
                    _context.DeviceStates.Add(state);
                    projectionApplied[item.SourceEventId] = true;
                }
                else
                {
                    projectionApplied[item.SourceEventId] = state.Apply(deviceEvent);
                }
            }
            else
            {
                var key = (item.DeviceExternalId, item.AlarmExternalId!);
                if (!alarmStates.TryGetValue(key, out var alarmState))
                {
                    alarmState = SpaceDeviceAlarmState.Create(deviceEvent);
                    alarmStates.Add(key, alarmState);
                    _context.DeviceAlarmStates.Add(alarmState);
                    projectionApplied[item.SourceEventId] = true;
                }
                else
                {
                    projectionApplied[item.SourceEventId] =
                        alarmState.Apply(deviceEvent);
                }
            }
            _context.DeviceEvents.Add(deviceEvent);
        }
        if (created.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }

        var receipts = request.Events.Select(item =>
        {
            if (existing.TryGetValue(item.SourceEventId, out var prior))
            {
                return new SpaceDeviceEventReceipt(
                    prior.Id,
                    item.SourceEventId,
                    item.DeviceExternalId,
                    "Duplicate",
                    false);
            }
            var accepted = created[item.SourceEventId];
            var applied = projectionApplied[item.SourceEventId];
            return new SpaceDeviceEventReceipt(
                accepted.Id,
                item.SourceEventId,
                item.DeviceExternalId,
                applied ? "Accepted" : "AcceptedStale",
                applied);
        }).ToArray();

        return new IngestSpaceDeviceEventsResponse(
            SpaceDeviceEventContract.Version,
            siteId,
            request.SourceId,
            request.SourceKind.ToString(),
            new DateTimeOffset(receivedAtUtc),
            request.Events.Count,
            receipts.Count(value => value.Outcome != "Duplicate"),
            receipts.Count(value => value.Outcome == "Duplicate"),
            receipts.Count(value => value.Outcome == "AcceptedStale"),
            receipts);
    }

    private static int ProjectionChannel(NormalizedEvent value) =>
        value.EventKind switch
        {
            SpaceDeviceEventKind.PositionObserved => 0,
            SpaceDeviceEventKind.OperatingStateChanged => 1,
            _ => 2,
        };

    private NormalizedRequest Normalize(
        IngestSpaceDeviceEventsRequest request,
        DateTime now)
    {
        if (!string.Equals(
                request.ContractVersion,
                SpaceDeviceEventContract.Version,
                StringComparison.Ordinal))
        {
            throw Invalid(
                "contractVersion",
                $"contractVersion must be {SpaceDeviceEventContract.Version}.");
        }
        if (request.Events is null || request.Events.Count is < 1 or > MaximumBatchSize)
        {
            throw Invalid(
                "events",
                $"events must contain 1 to {MaximumBatchSize} items.");
        }

        var sourceId = NormalizeIdentity(request.SourceId, 100, "sourceId");
        var sourceKind = ParseEnum<SpaceDeviceSourceKind>(
            request.SourceKind,
            "sourceKind");
        var events = request.Events.Select((value, index) =>
                Normalize(value, index, sourceId, sourceKind, now))
            .ToArray();
        var duplicate = events
            .GroupBy(value => value.SourceEventId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw Invalid(
                "sourceEventId",
                "sourceEventId values must be unique within a batch.");
        }
        return new NormalizedRequest(sourceId, sourceKind, events);
    }

    private NormalizedEvent Normalize(
        SpaceDeviceEventInput value,
        int index,
        string sourceId,
        SpaceDeviceSourceKind sourceKind,
        DateTime now)
    {
        if (value is null)
            throw Invalid($"events[{index}]", "An event item is required.");
        var sourceEventId = NormalizeIdentity(
            value.SourceEventId,
            200,
            $"events[{index}].sourceEventId");
        var deviceExternalId = NormalizeIdentity(
            value.DeviceExternalId,
            200,
            $"events[{index}].deviceExternalId");
        var eventKind = ParseEnum<SpaceDeviceEventKind>(
            value.EventKind,
            $"events[{index}].eventKind");
        var operatingState = ParseOptionalEnum<SpaceDeviceOperatingState>(
            value.OperatingState,
            $"events[{index}].operatingState");
        var alarmSeverity = ParseOptionalEnum<SpaceDeviceAlarmSeverity>(
            value.AlarmSeverity,
            $"events[{index}].alarmSeverity");
        var alarmExternalId = NormalizeOptionalIdentity(
            value.AlarmExternalId,
            200,
            $"events[{index}].alarmExternalId");
        var alarmCode = NormalizeOptionalIdentity(
            value.AlarmCode,
            100,
            $"events[{index}].alarmCode");
        var alarmMessage = NormalizeOptionalText(
            value.AlarmMessage,
            500,
            $"events[{index}].alarmMessage");

        if (value.OccurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw Invalid(
                $"events[{index}].occurredAtUtc",
                "occurredAtUtc must use UTC offset +00:00.");
        }
        var occurredAtUtc = value.OccurredAtUtc.UtcDateTime;
        if (occurredAtUtc > now + MaximumFutureSkew)
        {
            throw Invalid(
                $"events[{index}].occurredAtUtc",
                "occurredAtUtc cannot be more than five minutes in the future.");
        }
        if (value.SourceSequence < 0)
        {
            throw Invalid(
                $"events[{index}].sourceSequence",
                "sourceSequence cannot be negative.");
        }
        if (value.FloorLogicalId == Guid.Empty ||
            value.LocationLogicalId == Guid.Empty)
        {
            throw Invalid(
                $"events[{index}]",
                "Logical identities cannot be empty.");
        }
        ValidateCoordinates(value, index);

        try
        {
            var probeMapping = SpaceDeviceMapping.Create(
                _execution.TenantId,
                Guid.NewGuid(),
                sourceId,
                sourceKind,
                deviceExternalId,
                SpaceDeviceKind.Other,
                Guid.NewGuid(),
                SpaceElementTypes.Device,
                Guid.NewGuid(),
                Guid.NewGuid());
            _ = SpaceDeviceEvent.Create(
                _execution.TenantId,
                probeMapping.SiteId,
                sourceId,
                sourceKind,
                sourceEventId,
                probeMapping,
                eventKind,
                operatingState,
                value.FloorLogicalId,
                value.LocationLogicalId,
                value.XMillimeters,
                value.YMillimeters,
                value.ZMillimeters,
                value.AccuracyMillimeters,
                alarmExternalId,
                alarmCode,
                alarmSeverity,
                alarmMessage,
                value.SourceSequence,
                occurredAtUtc,
                now,
                new string('0', 64));
        }
        catch (ArgumentException exception)
        {
            throw Invalid($"events[{index}]", exception.Message);
        }

        var payloadHash = Hash(string.Join(
            "\n",
            sourceId,
            sourceKind,
            sourceEventId,
            deviceExternalId,
            eventKind,
            operatingState?.ToString() ?? "",
            value.FloorLogicalId?.ToString("D") ?? "",
            value.LocationLogicalId?.ToString("D") ?? "",
            Decimal(value.XMillimeters),
            Decimal(value.YMillimeters),
            Decimal(value.ZMillimeters),
            Decimal(value.AccuracyMillimeters),
            alarmExternalId ?? "",
            alarmCode ?? "",
            alarmSeverity?.ToString() ?? "",
            alarmMessage ?? "",
            value.SourceSequence?.ToString(CultureInfo.InvariantCulture) ?? "",
            occurredAtUtc.ToString("O", CultureInfo.InvariantCulture)));
        return new NormalizedEvent(
            sourceEventId,
            deviceExternalId,
            eventKind,
            operatingState,
            value.FloorLogicalId,
            value.LocationLogicalId,
            value.XMillimeters,
            value.YMillimeters,
            value.ZMillimeters,
            value.AccuracyMillimeters,
            alarmExternalId,
            alarmCode,
            alarmSeverity,
            alarmMessage,
            value.SourceSequence,
            occurredAtUtc,
            payloadHash);
    }

    private async Task<PublishedScope> LoadPublishedScopeAsync(
        Guid siteId,
        CancellationToken cancellationToken)
    {
        if (siteId == Guid.Empty)
            throw Invalid("siteId", "siteId must be non-empty.");
        var model = await EnsureSiteAsync(siteId, cancellationToken);
        if (!model.CurrentPublishedVersionId.HasValue)
        {
            throw Conflict(
                SpaceErrorCodes.VersionStateInvalid,
                "The Space model has no current Published version.",
                "publish-version");
        }
        var publishedVersionId = model.CurrentPublishedVersionId.Value;
        if (!await _context.Versions.AsNoTracking().AnyAsync(
                value =>
                    value.Id == publishedVersionId &&
                    value.Status == SpaceVersionStatus.Published,
                cancellationToken))
        {
            throw Conflict(
                SpaceErrorCodes.VersionStateInvalid,
                "The current runtime version is not Published.",
                "publish-version");
        }
        return new PublishedScope(publishedVersionId);
    }

    private async Task<SpaceModel> EnsureSiteAsync(
        Guid siteId,
        CancellationToken cancellationToken) =>
        siteId == Guid.Empty
            ? throw NotFound(
                SpaceErrorCodes.DeviceMappingNotFound,
                "The Space site was not found.")
            : await _context.Models.AsNoTracking().SingleOrDefaultAsync(
                  value => value.SiteId == siteId,
                  cancellationToken)
              ?? throw NotFound(
                  SpaceErrorCodes.DeviceMappingNotFound,
                  "The Space site was not found.");

    private async Task<SpaceElementRevision> LoadCompatibleElementAsync(
        Guid publishedVersionId,
        Guid elementLogicalId,
        SpaceDeviceKind deviceKind,
        CancellationToken cancellationToken)
    {
        if (elementLogicalId == Guid.Empty)
            throw Invalid("elementLogicalId", "elementLogicalId must be non-empty.");
        var element = await _context.ElementRevisions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                value =>
                    value.ModelVersionId == publishedVersionId &&
                    value.LogicalId == elementLogicalId &&
                    value.LifecycleState == SpaceLifecycleState.Active,
                cancellationToken);
        if (element is null || !IsCompatible(deviceKind, element.ElementType))
        {
            throw NotFound(
                SpaceErrorCodes.DeviceElementNotFound,
                "A compatible active device element was not found in the current Published version.");
        }
        return element;
    }

    private async Task ValidateSpatialReferencesAsync(
        Guid publishedVersionId,
        IReadOnlyCollection<NormalizedEvent> events,
        CancellationToken cancellationToken)
    {
        var positionEvents = events
            .Where(value =>
                value.EventKind == SpaceDeviceEventKind.PositionObserved)
            .ToArray();
        var floorIds = positionEvents
            .Where(value => value.FloorLogicalId.HasValue)
            .Select(value => value.FloorLogicalId!.Value)
            .Distinct()
            .ToArray();
        var locationIds = positionEvents
            .Where(value => value.LocationLogicalId.HasValue)
            .Select(value => value.LocationLogicalId!.Value)
            .Distinct()
            .ToArray();
        var knownFloors = floorIds.Length == 0
            ? new HashSet<Guid>()
            : (await _context.FloorRevisions
                .AsNoTracking()
                .Where(value =>
                    value.ModelVersionId == publishedVersionId &&
                    value.LifecycleState == SpaceLifecycleState.Active &&
                    floorIds.Contains(value.LogicalId))
                .Select(value => value.LogicalId)
                .ToListAsync(cancellationToken))
                .ToHashSet();
        var knownLocations = locationIds.Length == 0
            ? new Dictionary<Guid, Guid>()
            : await _context.LocationRevisions
                .AsNoTracking()
                .Where(value =>
                    value.ModelVersionId == publishedVersionId &&
                    value.LifecycleState == SpaceLifecycleState.Active &&
                    locationIds.Contains(value.LogicalId))
                .ToDictionaryAsync(
                    value => value.LogicalId,
                    value => value.FloorLogicalId,
                    cancellationToken);

        foreach (var item in positionEvents)
        {
            if (item.FloorLogicalId.HasValue &&
                !knownFloors.Contains(item.FloorLogicalId.Value))
            {
                throw Invalid(
                    "floorLogicalId",
                    "A position floor was not found in the current Published version.");
            }
            if (item.LocationLogicalId.HasValue &&
                (!knownLocations.TryGetValue(
                     item.LocationLogicalId.Value,
                     out var locationFloor) ||
                 item.FloorLogicalId.HasValue &&
                 item.FloorLogicalId.Value != locationFloor))
            {
                throw Invalid(
                    "locationLogicalId",
                    "A position location was not found on the selected Published floor.");
            }
        }
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

    private void EnsureExecutionContext()
    {
        if (_execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot manage device integrations.",
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

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken) =>
        !_context.Database.IsRelational()
            ? null
            : await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

    private int NormalizeLimit(int limit)
    {
        if (limit == 0)
            return DefaultPageSize;
        if (limit < 1 || limit > MaximumPageSize)
        {
            throw Invalid(
                "limit",
                $"limit must be between 1 and {MaximumPageSize}.");
        }
        return limit;
    }

    private int ReadOffset(string? cursor, string filterHash)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        var state = _cursorCodec.Decode(
            cursor,
            MappingCursorResource,
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

    private void EnsureExpectedRowVersion(
        SpaceDeviceMapping mapping,
        string expectedRowVersion)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(expectedRowVersion ?? string.Empty);
        }
        catch (FormatException)
        {
            throw Invalid(
                "expectedRowVersion",
                "expectedRowVersion must be a base64 rowversion.");
        }
        if (!_context.Database.IsRelational() &&
            expected.Length == 0 && mapping.RowVersion.Length == 0)
        {
            return;
        }
        if (expected.Length == 0 ||
            !expected.AsSpan().SequenceEqual(mapping.RowVersion))
        {
            throw Conflict(
                SpaceErrorCodes.ConcurrencyConflict,
                "The device mapping changed after it was loaded.",
                "reload-device-mapping");
        }
    }

    private static T ParseEnum<T>(string value, string field)
        where T : struct, Enum
    {
        var name = Enum.GetNames<T>().SingleOrDefault(candidate =>
            string.Equals(candidate, value?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is not null && Enum.TryParse<T>(name, out var parsed))
            return parsed;
        throw Invalid(
            field,
            $"{field} must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
    }

    private static T? ParseOptionalEnum<T>(string? value, string field)
        where T : struct, Enum =>
        string.IsNullOrWhiteSpace(value) ? null : ParseEnum<T>(value, field);

    private static string NormalizeIdentity(
        string value,
        int maximumLength,
        string field)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            throw Invalid(
                field,
                $"{field} must contain 1 to {maximumLength} non-control characters.");
        }
        return normalized;
    }

    private static string? NormalizeOptionalIdentity(
        string? value,
        int maximumLength,
        string field) =>
        value is null ? null : NormalizeIdentity(value, maximumLength, field);

    private static string? NormalizeOptionalText(
        string? value,
        int maximumLength,
        string field)
    {
        if (value is null)
            return null;
        var normalized = value.Trim();
        if (normalized.Length == 0 || normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            throw Invalid(
                field,
                $"{field} must contain 1 to {maximumLength} non-control characters.");
        }
        return normalized;
    }

    private static void ValidateCoordinates(
        SpaceDeviceEventInput value,
        int index)
    {
        foreach (var coordinate in new[]
                 {
                     value.XMillimeters,
                     value.YMillimeters,
                     value.ZMillimeters,
                 })
        {
            if (coordinate.HasValue && Math.Abs(coordinate.Value) > MaximumCoordinateMagnitude)
            {
                throw Invalid(
                    $"events[{index}]",
                    "Coordinates are outside the supported millimeter range.");
            }
        }
        if (value.AccuracyMillimeters < 0 ||
            value.AccuracyMillimeters > MaximumCoordinateMagnitude)
        {
            throw Invalid(
                $"events[{index}].accuracyMillimeters",
                "accuracyMillimeters must be within the supported non-negative range.");
        }
    }

    private static SpaceDeviceMappingDto ToDto(SpaceDeviceMapping value) =>
        new(
            value.Id,
            value.SiteId,
            value.SourceId,
            value.SourceKind.ToString(),
            value.DeviceExternalId,
            value.DeviceKind.ToString(),
            value.ElementLogicalId,
            value.ElementType,
            value.ValidatedModelVersionId,
            value.ValidatedFloorLogicalId,
            Convert.ToBase64String(value.RowVersion));

    private static string Decimal(decimal? value) =>
        value?.ToString("G29", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static SpaceProblemException Invalid(string field, string detail) =>
        new(
            SpaceErrorCodes.DeviceEventInvalid,
            400,
            "The device integration request is invalid.",
            $"{field}: {detail}",
            "correct-device-integration-request");

    private static SpaceProblemException NotFound(string code, string detail) =>
        new(
            code,
            404,
            detail,
            recoveryAction: "select-existing-device-resource");

    private static SpaceProblemException Conflict(
        string code,
        string detail,
        string recoveryAction,
        Exception? exception = null,
        bool retryable = false) =>
        new(
            code,
            409,
            detail,
            exception?.GetType().Name,
            recoveryAction,
            retryable);

    private sealed record PublishedScope(Guid PublishedVersionId);

    private sealed record NormalizedRequest(
        string SourceId,
        SpaceDeviceSourceKind SourceKind,
        IReadOnlyList<NormalizedEvent> Events);

    private sealed record NormalizedEvent(
        string SourceEventId,
        string DeviceExternalId,
        SpaceDeviceEventKind EventKind,
        SpaceDeviceOperatingState? OperatingState,
        Guid? FloorLogicalId,
        Guid? LocationLogicalId,
        decimal? XMillimeters,
        decimal? YMillimeters,
        decimal? ZMillimeters,
        decimal? AccuracyMillimeters,
        string? AlarmExternalId,
        string? AlarmCode,
        SpaceDeviceAlarmSeverity? AlarmSeverity,
        string? AlarmMessage,
        long? SourceSequence,
        DateTime OccurredAtUtc,
        string PayloadHash);
}
