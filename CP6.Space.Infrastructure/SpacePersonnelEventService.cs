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

public sealed class SpacePersonnelEventService : ISpacePersonnelEventService
{
    private const int MaxBatchSize = 500;
    private static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(5);
    private const decimal MaximumCoordinateMagnitude = 1_000_000_000m;

    private readonly SpaceContext _context;
    private readonly ISpaceExecutionContext _execution;
    private readonly ISpaceClock _clock;
    private readonly ISpaceDesignAccessEvaluator _access;

    public SpacePersonnelEventService(
        SpaceContext context,
        ISpaceExecutionContext execution,
        ISpaceClock clock,
        ISpaceDesignAccessEvaluator access)
    {
        _context = context;
        _execution = execution;
        _clock = clock;
        _access = access;
    }

    public async Task<IngestSpacePersonnelEventsResponse> IngestAsync(
        Guid siteId,
        IngestSpacePersonnelEventsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureExecutionContext();
        if (siteId == Guid.Empty)
            throw Invalid("siteId", "siteId must be a non-empty identity.");
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
                throw new SpaceProblemException(
                    SpaceErrorCodes.PersonnelEventConflict,
                    409,
                    "The personnel event stream changed concurrently.",
                    $"Persistence rejected the event batch with {exception.GetType().Name}.",
                    "retry-personnel-event-batch",
                    retryable: true);
            }
        }
    }

    private async Task<IngestSpacePersonnelEventsResponse> IngestCoreAsync(
        Guid siteId,
        NormalizedRequest request,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken)
    {
        if (!await _context.Models.AsNoTracking().AnyAsync(
                value => value.SiteId == siteId,
                cancellationToken))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PersonnelSiteNotFound,
                404,
                "The Space site was not found.",
                recoveryAction: "select-existing-site");
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        if (await _context.PersonnelEvents.AsNoTracking().AnyAsync(
                value =>
                    value.SiteId == siteId &&
                    value.SourceId == request.SourceId &&
                    value.SourceKind != request.SourceKind,
                cancellationToken))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.PersonnelEventConflict,
                409,
                "The personnel source kind conflicts with its existing stream.",
                "A source identity cannot switch between Real and Simulated.",
                "use-distinct-personnel-source-identity");
        }
        var sourceEventIds = request.Events
            .Select(value => value.SourceEventId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var persisted = await _context.PersonnelEvents
            .Where(value =>
                value.SiteId == siteId &&
                value.SourceId == request.SourceId &&
                sourceEventIds.Contains(value.SourceEventId))
            .ToDictionaryAsync(
                value => value.SourceEventId,
                StringComparer.Ordinal,
                cancellationToken);

        var firstBySourceEvent = new Dictionary<string, NormalizedEvent>(
            StringComparer.Ordinal);
        foreach (var item in request.Events)
        {
            if (persisted.TryGetValue(item.SourceEventId, out var stored) &&
                !string.Equals(stored.PayloadHash, item.PayloadHash, StringComparison.Ordinal))
            {
                throw EventConflict(item.SourceEventId);
            }
            if (firstBySourceEvent.TryGetValue(item.SourceEventId, out var first))
            {
                if (!string.Equals(first.PayloadHash, item.PayloadHash, StringComparison.Ordinal))
                    throw EventConflict(item.SourceEventId);
                continue;
            }
            firstBySourceEvent.Add(item.SourceEventId, item);
        }

        var newInputs = firstBySourceEvent.Values
            .Where(value => !persisted.ContainsKey(value.SourceEventId))
            .ToArray();
        var personIds = newInputs
            .Select(value => value.PersonExternalId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var states = await _context.PersonnelStates
            .Where(value =>
                value.SiteId == siteId &&
                value.SourceId == request.SourceId &&
                personIds.Contains(value.PersonExternalId))
            .ToDictionaryAsync(
                value => value.PersonExternalId,
                StringComparer.Ordinal,
                cancellationToken);

        var created = newInputs.ToDictionary(
            value => value.SourceEventId,
            value => value.CreateEntity(
                _execution.TenantId,
                siteId,
                request.SourceId,
                request.SourceKind,
                receivedAtUtc),
            StringComparer.Ordinal);
        var projectionApplied = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var item in newInputs
                     .OrderBy(value => value.PersonExternalId, StringComparer.Ordinal)
                     .ThenBy(value => value.EventKind)
                     .ThenBy(value => value.OccurredAtUtc)
                     .ThenBy(value => value.SourceSequence.HasValue ? 1 : 0)
                     .ThenBy(value => value.SourceSequence)
                     .ThenBy(value => value.SourceEventId, StringComparer.Ordinal))
        {
            var personnelEvent = created[item.SourceEventId];
            try
            {
                if (!states.TryGetValue(item.PersonExternalId, out var state))
                {
                    state = SpacePersonnelCurrentState.Create(personnelEvent);
                    states.Add(item.PersonExternalId, state);
                    _context.PersonnelStates.Add(state);
                    projectionApplied[item.SourceEventId] = true;
                }
                else
                {
                    projectionApplied[item.SourceEventId] = state.Apply(personnelEvent);
                }
            }
            catch (InvalidOperationException exception)
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.PersonnelEventConflict,
                    409,
                    "The personnel identity conflicts with its current binding.",
                    exception.Message,
                    "correct-personnel-identity");
            }
            _context.PersonnelEvents.Add(personnelEvent);
        }

        await _context.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        var firstIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var receipts = new List<SpacePersonnelEventReceipt>(request.Events.Count);
        for (var index = 0; index < request.Events.Count; index++)
        {
            var item = request.Events[index];
            var isFirst = !firstIndex.TryGetValue(item.SourceEventId, out _);
            if (isFirst)
                firstIndex[item.SourceEventId] = index;
            if (persisted.TryGetValue(item.SourceEventId, out var stored) || !isFirst)
            {
                var duplicateId = stored?.Id ?? created[item.SourceEventId].Id;
                receipts.Add(new SpacePersonnelEventReceipt(
                    duplicateId,
                    item.SourceEventId,
                    "Duplicate",
                    false));
                continue;
            }

            var applied = projectionApplied[item.SourceEventId];
            receipts.Add(new SpacePersonnelEventReceipt(
                created[item.SourceEventId].Id,
                item.SourceEventId,
                applied ? "Accepted" : "AcceptedStale",
                applied));
        }

        return new IngestSpacePersonnelEventsResponse(
            SpacePersonnelEventContract.Version,
            siteId,
            request.SourceId,
            request.SourceKind.ToString(),
            new DateTimeOffset(receivedAtUtc),
            receipts.Count,
            receipts.Count(value => value.Outcome != "Duplicate"),
            receipts.Count(value => value.Outcome == "Duplicate"),
            receipts.Count(value => value.Outcome == "AcceptedStale"),
            receipts);
    }

    private static NormalizedRequest Normalize(
        IngestSpacePersonnelEventsRequest request,
        DateTime now)
    {
        if (!string.Equals(
                request.ContractVersion?.Trim(),
                SpacePersonnelEventContract.Version,
                StringComparison.Ordinal))
        {
            throw Invalid(
                "contractVersion",
                $"contractVersion must equal '{SpacePersonnelEventContract.Version}'.");
        }
        var sourceId = NormalizeIdentity(request.SourceId, 100, "sourceId");
        var sourceKind = ParseSourceKind(request.SourceKind);
        if (request.Events is null || request.Events.Count is < 1 or > MaxBatchSize)
        {
            throw Invalid(
                "events",
                $"events must contain between 1 and {MaxBatchSize} items.");
        }

        var events = request.Events
            .Select((value, index) => NormalizeEvent(
                value,
                index,
                now,
                sourceId,
                sourceKind))
            .ToArray();
        return new NormalizedRequest(sourceId, sourceKind, events);
    }

    private static NormalizedEvent NormalizeEvent(
        SpacePersonnelEventInput value,
        int index,
        DateTime now,
        string sourceId,
        SpacePersonnelSourceKind sourceKind)
    {
        if (value is null)
            throw Invalid($"events[{index}]", "An event is required.");
        var prefix = $"events[{index}]";
        var sourceEventId = NormalizeIdentity(
            value.SourceEventId,
            200,
            $"{prefix}.sourceEventId");
        var personExternalId = NormalizeIdentity(
            value.PersonExternalId,
            200,
            $"{prefix}.personExternalId");
        if (value.UserId == Guid.Empty)
            throw Invalid($"{prefix}.userId", "userId cannot be empty.");
        var eventKind = ParseEventKind(value.EventKind, prefix);
        var workState = ParseWorkState(value.WorkState, eventKind, prefix);
        if (value.FloorLogicalId == Guid.Empty || value.LocationLogicalId == Guid.Empty)
            throw Invalid(prefix, "Logical identities cannot be empty.");
        if (value.SourceSequence < 0)
            throw Invalid($"{prefix}.sourceSequence", "sourceSequence cannot be negative.");
        if (value.OccurredAtUtc.Offset != TimeSpan.Zero)
            throw Invalid($"{prefix}.occurredAtUtc", "occurredAtUtc must use UTC offset +00:00.");
        var occurredAtUtc = value.OccurredAtUtc.UtcDateTime;
        if (occurredAtUtc > now + MaximumFutureSkew)
        {
            throw Invalid(
                $"{prefix}.occurredAtUtc",
                "occurredAtUtc cannot be more than five minutes in the future.");
        }

        var coordinates = new[]
        {
            value.XMillimeters,
            value.YMillimeters,
            value.ZMillimeters,
        };
        var coordinateCount = coordinates.Count(item => item.HasValue);
        if (coordinateCount is not (0 or 3) ||
            coordinates.Any(item =>
                item.HasValue && Math.Abs(item.Value) > MaximumCoordinateMagnitude))
        {
            throw Invalid(prefix, "Coordinates must be a bounded XYZ triple.");
        }
        if (value.AccuracyMillimeters < 0 ||
            value.AccuracyMillimeters > MaximumCoordinateMagnitude ||
            value.AccuracyMillimeters.HasValue && coordinateCount == 0)
        {
            throw Invalid(
                $"{prefix}.accuracyMillimeters",
                "accuracyMillimeters requires XYZ and must be non-negative and bounded.");
        }
        if (eventKind == SpacePersonnelEventKind.PositionObserved)
        {
            if (workState.HasValue ||
                !value.LocationLogicalId.HasValue &&
                (!value.FloorLogicalId.HasValue || coordinateCount != 3))
            {
                throw Invalid(
                    prefix,
                    "PositionObserved requires a location or floor plus XYZ, and no workState.");
            }
        }
        else if (!workState.HasValue || value.FloorLogicalId.HasValue ||
                 value.LocationLogicalId.HasValue || coordinateCount != 0 ||
                 value.AccuracyMillimeters.HasValue)
        {
            throw Invalid(
                prefix,
                "WorkStateChanged requires workState and no position fields.");
        }

        var canonical = string.Join(
            "\n",
            SpacePersonnelEventContract.Version,
            sourceId,
            sourceKind.ToString(),
            sourceEventId,
            personExternalId,
            value.UserId?.ToString("D") ?? "",
            eventKind.ToString(),
            workState?.ToString() ?? "",
            value.FloorLogicalId?.ToString("D") ?? "",
            value.LocationLogicalId?.ToString("D") ?? "",
            Format(value.XMillimeters),
            Format(value.YMillimeters),
            Format(value.ZMillimeters),
            Format(value.AccuracyMillimeters),
            value.SourceSequence?.ToString(CultureInfo.InvariantCulture) ?? "",
            occurredAtUtc.ToString("O", CultureInfo.InvariantCulture));
        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        return new NormalizedEvent(
            sourceEventId,
            personExternalId,
            value.UserId,
            eventKind,
            workState,
            value.FloorLogicalId,
            value.LocationLogicalId,
            value.XMillimeters,
            value.YMillimeters,
            value.ZMillimeters,
            value.AccuracyMillimeters,
            value.SourceSequence,
            occurredAtUtc,
            hash);
    }

    private static string NormalizeIdentity(string value, int maximum, string field)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximum ||
            normalized.Any(char.IsControl))
        {
            throw Invalid(field, $"{field} must contain 1 to {maximum} non-control characters.");
        }
        return normalized;
    }

    private static SpacePersonnelSourceKind ParseSourceKind(string value)
    {
        if (string.Equals(value?.Trim(), "Real", StringComparison.OrdinalIgnoreCase))
            return SpacePersonnelSourceKind.Real;
        if (string.Equals(value?.Trim(), "Simulated", StringComparison.OrdinalIgnoreCase))
            return SpacePersonnelSourceKind.Simulated;
        throw Invalid("sourceKind", "sourceKind must be Real or Simulated.");
    }

    private static SpacePersonnelEventKind ParseEventKind(string value, string prefix)
    {
        if (string.Equals(value?.Trim(), "PositionObserved", StringComparison.OrdinalIgnoreCase))
            return SpacePersonnelEventKind.PositionObserved;
        if (string.Equals(value?.Trim(), "WorkStateChanged", StringComparison.OrdinalIgnoreCase))
            return SpacePersonnelEventKind.WorkStateChanged;
        throw Invalid(
            $"{prefix}.eventKind",
            "eventKind must be PositionObserved or WorkStateChanged.");
    }

    private static SpacePersonnelWorkState? ParseWorkState(
        string? value,
        SpacePersonnelEventKind eventKind,
        string prefix)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        foreach (var candidate in Enum.GetValues<SpacePersonnelWorkState>())
        {
            if (string.Equals(value.Trim(), candidate.ToString(), StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        throw Invalid(
            $"{prefix}.workState",
            "workState must be Unknown, Offline, Idle, Busy, or Break.");
    }

    private static string Format(decimal? value) =>
        value?.ToString("G29", CultureInfo.InvariantCulture) ?? "";

    private void EnsureExecutionContext()
    {
        if (_execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot ingest personnel events.",
                recoveryAction: "use-internal-integration-principal");
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

    private static SpaceProblemException Invalid(string field, string detail) =>
        new(
            SpaceErrorCodes.PersonnelEventInvalid,
            422,
            "The personnel event batch is invalid.",
            $"{field}: {detail}",
            "correct-personnel-event-batch");

    private static SpaceProblemException EventConflict(string sourceEventId) =>
        new(
            SpaceErrorCodes.PersonnelEventConflict,
            409,
            "A source event identity was reused with different content.",
            $"Source event '{sourceEventId}' already has another payload.",
            "use-stable-source-event-identity");

    private sealed record NormalizedRequest(
        string SourceId,
        SpacePersonnelSourceKind SourceKind,
        IReadOnlyList<NormalizedEvent> Events);

    private sealed record NormalizedEvent(
        string SourceEventId,
        string PersonExternalId,
        Guid? UserId,
        SpacePersonnelEventKind EventKind,
        SpacePersonnelWorkState? WorkState,
        Guid? FloorLogicalId,
        Guid? LocationLogicalId,
        decimal? XMillimeters,
        decimal? YMillimeters,
        decimal? ZMillimeters,
        decimal? AccuracyMillimeters,
        long? SourceSequence,
        DateTime OccurredAtUtc,
        string PayloadHash)
    {
        public SpacePersonnelEvent CreateEntity(
            Guid tenantId,
            Guid siteId,
            string sourceId,
            SpacePersonnelSourceKind sourceKind,
            DateTime receivedAtUtc) =>
            SpacePersonnelEvent.Create(
                tenantId,
                siteId,
                sourceId,
                sourceKind,
                SourceEventId,
                PersonExternalId,
                UserId,
                EventKind,
                WorkState,
                FloorLogicalId,
                LocationLogicalId,
                XMillimeters,
                YMillimeters,
                ZMillimeters,
                AccuracyMillimeters,
                SourceSequence,
                OccurredAtUtc,
                receivedAtUtc,
                PayloadHash);
    }
}
