using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using CP6.WebApi.Localization;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Space.Observability;

public sealed class SpaceAuditQueryService : ISpaceAuditQueryService
{
    internal const int MaxTimelineItems = 1000;
    internal const int EvidenceCharacterLimit = 8192;
    internal const int EvidenceByteLimit =
        EvidenceCharacterLimit * sizeof(char);
    internal const int EvidenceMaxDepth = 8;

    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;
    private const int MaxSafePage = int.MaxValue / MaxPageSize;
    private const string LegacyErrorRedacted =
        "SPACE_LEGACY_ERROR_REDACTED";

    private static readonly JsonSerializerOptions EvidenceJsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            MaxDepth = EvidenceMaxDepth,
        };

    private readonly CP6Context _db;

    public SpaceAuditQueryService(CP6Context db)
    {
        _db = db;
    }

    public async Task<SpaceAuditPageDto> QueryAsync(
        SpaceAuditQueryDto query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var window = NormalizeWindow(query);
        var page = NormalizePage(query.Page);
        var pageSize = NormalizePageSize(query.PageSize);
        var offset = (page - 1) * pageSize;

        var source = _db.SpaceAuditEvents
            .AsNoTracking()
            .Where(x =>
                x.OccurredAtUtc >= window.FromUtc &&
                x.OccurredAtUtc <= window.ToUtc);

        if (!string.IsNullOrWhiteSpace(query.Action))
            source = source.Where(x => x.Action == query.Action);
        if (!string.IsNullOrWhiteSpace(query.Outcome))
            source = source.Where(x => x.Outcome == query.Outcome);
        if (query.CorrelationId.HasValue)
            source = source.Where(
                x => x.CorrelationId == query.CorrelationId.Value);

        var total = await source.CountAsync(ct);
        var rows = await BuildAuditPageRowsQuery(
                source,
                offset,
                pageSize)
            .ToListAsync(ct);

        return new SpaceAuditPageDto(
            rows.Select(ToDto).ToList(),
            page,
            pageSize,
            total);
    }

    public async Task<IReadOnlyList<SpaceAuditTimelineItemDto>>
        GetTimelineAsync(
            Guid correlationId,
            CancellationToken ct = default)
    {
        if (correlationId == Guid.Empty)
            throw new BizException("SPACE_CORRELATION_ID_INVALID");

        var audits = await BuildAuditTimelineRowsQuery(correlationId)
            .ToListAsync(ct);
        var integrations = await BuildIntegrationTimelineRowsQuery(
                correlationId)
            .ToListAsync(ct);

        var candidates = new List<TimelineCandidate>(
            audits.Count + integrations.Count);
        candidates.AddRange(audits.Select(x =>
            new TimelineCandidate(
                x.Id,
                new SpaceAuditTimelineItemDto(
                    "Audit",
                    x.TenantId,
                    EnsureUtcKind(x.OccurredAtUtc),
                    x.Action,
                    x.ResourceType,
                    x.ResourceId,
                    x.Outcome,
                    SafeErrorCode(x.ReasonCode),
                    x.CorrelationId,
                    x.TraceId,
                    x.JobId,
                    x.RunId,
                    x.PublishAttemptId,
                    x.AttemptNo))));
        candidates.AddRange(integrations.Select(x =>
            new TimelineCandidate(
                x.Id,
                new SpaceAuditTimelineItemDto(
                    "IntegrationEvent",
                    x.TenantId,
                    EnsureUtcKind(x.OccurredAtUtc),
                    x.HookName,
                    x.TargetModule,
                    x.SourceNo,
                    x.Status,
                    SafeErrorCode(x.LastError),
                    x.CorrelationId,
                    null,
                    x.JobId,
                    null,
                    x.PublishAttemptId,
                    x.Attempts))));

        // Each source is capped in SQL at max+1. Merge only those bounded
        // candidates, retain the newest max, then return a stable ascending
        // timeline for clients.
        return candidates
            .OrderByDescending(x => x.Item.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .ThenBy(x => x.Item.Kind, StringComparer.Ordinal)
            .Take(MaxTimelineItems)
            .OrderBy(x => x.Item.OccurredAtUtc)
            .ThenBy(x => x.Id)
            .ThenBy(x => x.Item.Kind, StringComparer.Ordinal)
            .Select(x => x.Item)
            .ToList();
    }

    public async Task<IReadOnlyList<SpacePublishEventDto>>
        GetPublishEventsAsync(
            int page,
            int pageSize,
            CancellationToken ct = default)
    {
        page = NormalizePage(page);
        pageSize = NormalizePageSize(pageSize);
        var offset = (page - 1) * pageSize;

        var rows = await _db.IntegrationEvents
            .AsNoTracking()
            .Where(x =>
                x.SourceModule == "SPACE" &&
                x.OccurredAtUtc != null)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(x => new IntegrationProjection(
                x.Id,
                x.TenantId,
                x.OccurredAtUtc!.Value,
                x.HookName,
                x.TargetModule,
                x.SourceNo,
                x.Status,
                x.LastError,
                x.CorrelationId,
                x.JobId,
                x.PublishAttemptId,
                x.Attempts))
            .ToListAsync(ct);

        return rows.Select(x => new SpacePublishEventDto(
                x.Id,
                x.HookName,
                x.SourceNo,
                x.TargetModule,
                x.Status,
                x.Attempts,
                EnsureUtcKind(x.OccurredAtUtc),
                x.CorrelationId,
                x.JobId,
                x.PublishAttemptId,
                SafeErrorCode(x.LastError)))
            .ToList();
    }

    internal IQueryable<AuditProjection> BuildAuditPageRowsQuery(
        IQueryable<Space_AuditEvent> source,
        int offset,
        int pageSize)
    {
        var ordered = source
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip(offset)
            .Take(pageSize);

        if (_db.Database.IsSqlServer())
        {
            return ordered.Select(x => new AuditProjection(
                x.Id,
                x.TenantId,
                x.OccurredAtUtc,
                x.ActorType,
                x.ActorId,
                x.ActorName,
                x.Action,
                x.ResourceType,
                x.ResourceId,
                x.Outcome,
                x.ReasonCode,
                x.CorrelationId,
                x.TraceId,
                x.JobId,
                x.RunId,
                x.PublishAttemptId,
                x.AttemptNo,
                x.AuthorizationEvidenceJson != null &&
                EF.Functions.DataLength(
                    x.AuthorizationEvidenceJson) <= EvidenceByteLimit
                    ? x.AuthorizationEvidenceJson
                    : null));
        }

        return ordered.Select(x => new AuditProjection(
            x.Id,
            x.TenantId,
            x.OccurredAtUtc,
            x.ActorType,
            x.ActorId,
            x.ActorName,
            x.Action,
            x.ResourceType,
            x.ResourceId,
            x.Outcome,
            x.ReasonCode,
            x.CorrelationId,
            x.TraceId,
            x.JobId,
            x.RunId,
            x.PublishAttemptId,
            x.AttemptNo,
            x.AuthorizationEvidenceJson != null &&
            x.AuthorizationEvidenceJson.Length <=
                EvidenceCharacterLimit
                ? x.AuthorizationEvidenceJson
                : null));
    }

    internal IQueryable<AuditTimelineProjection>
        BuildAuditTimelineRowsQuery(Guid correlationId) =>
        _db.SpaceAuditEvents
            .AsNoTracking()
            .Where(x => x.CorrelationId == correlationId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(MaxTimelineItems + 1)
            .Select(x => new AuditTimelineProjection(
                x.Id,
                x.TenantId,
                x.OccurredAtUtc,
                x.Action,
                x.ResourceType,
                x.ResourceId,
                x.Outcome,
                x.ReasonCode,
                x.CorrelationId,
                x.TraceId,
                x.JobId,
                x.RunId,
                x.PublishAttemptId,
                x.AttemptNo));

    internal IQueryable<IntegrationProjection>
        BuildIntegrationTimelineRowsQuery(Guid correlationId) =>
        _db.IntegrationEvents
            .AsNoTracking()
            .Where(x =>
                x.CorrelationId == correlationId &&
                x.SourceModule == "SPACE" &&
                x.OccurredAtUtc != null)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(MaxTimelineItems + 1)
            .Select(x => new IntegrationProjection(
                x.Id,
                x.TenantId,
                x.OccurredAtUtc!.Value,
                x.HookName,
                x.TargetModule,
                x.SourceNo,
                x.Status,
                x.LastError,
                x.CorrelationId,
                x.JobId,
                x.PublishAttemptId,
                x.Attempts));

    private static SpaceAuditEventDto ToDto(AuditProjection x) =>
        new(
            x.EventId,
            x.TenantId,
            EnsureUtcKind(x.OccurredAtUtc),
            x.ActorType,
            x.ActorId,
            x.ActorName,
            x.Action,
            x.ResourceType,
            x.ResourceId,
            x.Outcome,
            x.ReasonCode,
            x.CorrelationId,
            x.TraceId,
            x.JobId,
            x.RunId,
            x.PublishAttemptId,
            x.AttemptNo,
            DeserializeEvidence(x.AuthorizationEvidenceJson));

    private static SpaceAuditEvidenceDto? DeserializeEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) ||
            json.Length > EvidenceCharacterLimit)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SpaceAuditEvidenceDto>(
                json,
                EvidenceJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string? SafeErrorCode(string? lastError)
    {
        if (string.IsNullOrWhiteSpace(lastError))
            return null;

        var separator = lastError.IndexOf(':');
        var candidate = separator < 0
            ? lastError
            : lastError[..separator];
        return candidate.Length > "SPACE_".Length &&
               candidate.StartsWith(
                   "SPACE_",
                   StringComparison.Ordinal) &&
               SpaceErrorSanitizer.IsStableReasonCode(candidate)
            ? candidate
            : LegacyErrorRedacted;
    }

    private static DateTime EnsureUtcKind(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static QueryWindow NormalizeWindow(SpaceAuditQueryDto query)
    {
        if (query.CorrelationId == Guid.Empty)
            throw new BizException("SPACE_CORRELATION_ID_INVALID");
        if (!IsUtc(query.FromUtc) || !IsUtc(query.ToUtc))
            throw QueryRangeInvalid();

        var toUtc = query.ToUtc ?? DateTime.UtcNow;
        DateTime fromUtc;
        if (query.FromUtc.HasValue)
        {
            fromUtc = query.FromUtc.Value;
        }
        else
        {
            if (toUtc.Ticks < TimeSpan.TicksPerDay)
                throw QueryRangeInvalid();

            fromUtc = new DateTime(
                toUtc.Ticks - TimeSpan.TicksPerDay,
                DateTimeKind.Utc);
        }

        if (fromUtc > toUtc ||
            toUtc - fromUtc > TimeSpan.FromDays(31))
        {
            throw QueryRangeInvalid();
        }

        return new QueryWindow(fromUtc, toUtc);
    }

    private static bool IsUtc(DateTime? value) =>
        value is null || value.Value.Kind == DateTimeKind.Utc;

    private static BizException QueryRangeInvalid() =>
        new("SPACE_AUDIT_QUERY_RANGE_INVALID");

    private static int NormalizePage(int page) =>
        Math.Clamp(page, 1, MaxSafePage);

    private static int NormalizePageSize(int pageSize) =>
        Math.Clamp(
            pageSize <= 0 ? DefaultPageSize : pageSize,
            1,
            MaxPageSize);

    private sealed record QueryWindow(DateTime FromUtc, DateTime ToUtc);

    internal sealed record AuditProjection(
        Guid EventId,
        Guid TenantId,
        DateTime OccurredAtUtc,
        string ActorType,
        string ActorId,
        string? ActorName,
        string Action,
        string ResourceType,
        string? ResourceId,
        string Outcome,
        string? ReasonCode,
        Guid CorrelationId,
        string TraceId,
        Guid? JobId,
        Guid? RunId,
        Guid? PublishAttemptId,
        int? AttemptNo,
        string? AuthorizationEvidenceJson);

    internal sealed record IntegrationProjection(
        Guid Id,
        Guid TenantId,
        DateTime OccurredAtUtc,
        string HookName,
        string TargetModule,
        string SourceNo,
        string Status,
        string? LastError,
        Guid CorrelationId,
        Guid? JobId,
        Guid? PublishAttemptId,
        int Attempts);

    internal sealed record AuditTimelineProjection(
        Guid Id,
        Guid TenantId,
        DateTime OccurredAtUtc,
        string Action,
        string ResourceType,
        string? ResourceId,
        string Outcome,
        string? ReasonCode,
        Guid CorrelationId,
        string TraceId,
        Guid? JobId,
        Guid? RunId,
        Guid? PublishAttemptId,
        int? AttemptNo);

    private sealed record TimelineCandidate(
        Guid Id,
        SpaceAuditTimelineItemDto Item);
}
