using System.Security.Cryptography;
using System.Text;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpacePublishActivityService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceDesignAccessEvaluator access,
    ISpaceCursorCodec cursorCodec) : ISpacePublishActivityService
{
    private const string CursorResource = "publish-attempt-list";
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 100;

    public async Task<SpacePage<SpacePublishAttemptSummaryDto>> GetBySiteAsync(
        Guid siteId,
        string? status,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        EnsureExecutionContext();
        access.EnsureSiteAccess(siteId, write: false);
        if (siteId == Guid.Empty || !await context.Models.AsNoTracking().AnyAsync(
                value => value.SiteId == siteId,
                cancellationToken))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ModelNotFound,
                404,
                "The Space model was not found.",
                recoveryAction: "select-existing-site");
        }

        limit = NormalizeLimit(limit);
        var parsedStatus = ParseStatus(status);
        var normalizedStatus = parsedStatus?.ToString() ?? string.Empty;
        var filterHash = Hash(
            $"site={siteId:D}\nstatus={normalizedStatus}\nlimit={limit}");
        var offset = ReadOffset(cursor, filterHash);

        var query = context.PublishAttempts
            .AsNoTracking()
            .Where(value => value.SiteId == siteId);
        if (parsedStatus.HasValue)
            query = query.Where(value => value.Status == parsedStatus.Value);

        var attempts = await query
            .OrderByDescending(value => value.StartedAtUtc)
            .ThenByDescending(value => value.Id)
            .Skip(offset)
            .Take(limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = attempts.Length > limit;
        attempts = attempts.Take(limit).ToArray();
        var attemptIds = attempts.Select(value => value.Id).ToArray();
        var versionIds = attempts.Select(value => value.TargetVersionId).ToArray();
        var jobIds = attempts
            .Where(value => value.JobId.HasValue)
            .Select(value => value.JobId!.Value)
            .ToArray();

        var versions = await context.Versions.AsNoTracking()
            .Where(value => versionIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var jobs = await context.Jobs.AsNoTracking()
            .Where(value => jobIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var openIssues = await context.ReconciliationIssues.AsNoTracking()
            .Where(value =>
                attemptIds.Contains(value.AttemptId) &&
                value.Status != SpaceReconciliationStatus.Resolved)
            .GroupBy(value => value.AttemptId)
            .Select(group => new { AttemptId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(value => value.AttemptId, value => value.Count, cancellationToken);
        var republishes = await context.HistoricalRepublishes.AsNoTracking()
            .Where(value =>
                value.PublishAttemptId.HasValue &&
                attemptIds.Contains(value.PublishAttemptId.Value))
            .ToDictionaryAsync(value => value.PublishAttemptId!.Value, cancellationToken);

        var items = attempts.Select(attempt =>
        {
            versions.TryGetValue(attempt.TargetVersionId, out var version);
            var job = attempt.JobId.HasValue
                ? jobs.GetValueOrDefault(attempt.JobId.Value)
                : null;
            var republish = republishes.GetValueOrDefault(attempt.Id);
            return new SpacePublishAttemptSummaryDto(
                attempt.Id,
                attempt.SiteId,
                attempt.TargetVersionId,
                version?.VersionNo.ToString() ?? "?",
                version?.Name ?? "Unknown version",
                attempt.BaseVersionId,
                attempt.Status.ToString(),
                attempt.CurrentStep.ToString(),
                attempt.StartedAtUtc,
                attempt.FinishedAtUtc,
                attempt.ApprovalReference,
                attempt.LastErrorCode,
                attempt.Summary,
                attempt.JobId,
                job?.Status.ToString() ?? attempt.Status.ToString(),
                job?.AttemptCount ?? 0,
                job?.MaxAttempts ?? 0,
                job?.Status == SpaceJobStatus.Queued
                    ? job.NextAttemptAtUtc
                    : null,
                openIssues.GetValueOrDefault(attempt.Id),
                republish?.Id,
                republish?.HistoricalVersionId);
        }).ToArray();
        var nextCursor = hasMore
            ? cursorCodec.Encode(new SpaceCursorState(
                CursorResource,
                filterHash,
                checked(offset + limit)))
            : null;
        return new SpacePage<SpacePublishAttemptSummaryDto>(items, nextCursor);
    }

    private SpacePublishAttemptStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;
        if (Enum.TryParse<SpacePublishAttemptStatus>(status.Trim(), true, out var parsed) &&
            Enum.IsDefined(parsed))
            return parsed;
        throw Invalid("status", "status is not a supported publish attempt status.");
    }

    private int NormalizeLimit(int limit)
    {
        if (limit == 0)
            return DefaultPageSize;
        if (limit is < 1 or > MaximumPageSize)
            throw Invalid("limit", $"limit must be between 1 and {MaximumPageSize}.");
        return limit;
    }

    private int ReadOffset(string? cursor, string filterHash)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return 0;
        var state = cursorCodec.Decode(cursor, CursorResource, filterHash);
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

    private void EnsureExecutionContext()
    {
        if (execution.IsExternal)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalSubjectDenied,
                403,
                "External principals cannot read publish activity.",
                recoveryAction: "use-internal-space-principal");
        }
        if (execution.TenantId == Guid.Empty ||
            execution.ActorId == Guid.Empty ||
            execution.TenantId != context.CurrentTenantId)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.TenantScopeDenied,
                403,
                "The Space tenant scope was denied.",
                recoveryAction: "reauthenticate");
        }
    }

    private static SpaceProblemException Invalid(string field, string detail) =>
        new(
            SpaceErrorCodes.VersionStateInvalid,
            400,
            "The publish activity request is invalid.",
            $"{field}: {detail}",
            "correct-publish-activity-request");

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
