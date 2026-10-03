using System.Data;
using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Space.Observability;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.BackgroundServices;

internal static class SpaceIntegrationEventOccurredAtUtcBackfill
{
    internal const int BatchSize = 500;
    internal const string LockResource =
        "CP6:SpaceIntegrationEvent:OccurredAtUtc:v1";
    internal const int LockTimeoutMilliseconds = 30_000;
    private const string SourceModule = "SPACE";
    private static readonly SemaphoreSlim NonSqlGate = new(1, 1);

    public static async Task RunAsync(
        CP6Context db,
        SpaceObservabilityOptions options,
        ILogger logger,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var usesDatabaseLock = db.Database.IsSqlServer() || db.Database.IsNpgsql();
        DbConnection? lockConnection = null;
        var openedConnection = false;
        DatabaseSessionResourceLock? sessionLock = null;
        var nonSqlGateHeld = false;

        try
        {
            if (usesDatabaseLock)
            {
                lockConnection = db.Database.GetDbConnection();
                if (lockConnection.State != ConnectionState.Open)
                {
                    await lockConnection.OpenAsync(ct);
                    openedConnection = true;
                }

                try
                {
                    sessionLock = await DatabaseResourceLocks.TryAcquireSessionAsync(
                        lockConnection,
                        db.Database.IsSqlServer() ? DatabaseProvider.SqlServer : DatabaseProvider.PostgreSql,
                        LockResource, LockTimeoutMilliseconds, ct);
                }
                catch (DatabaseResourceLockDeadlockException exception) when (exception.Provider == DatabaseProvider.SqlServer)
                {
                    throw new InvalidOperationException("SPACE_OCCURRED_AT_UTC_BACKFILL_LOCK_UNAVAILABLE", exception);
                }
                if (sessionLock is null)
                {
                    throw new InvalidOperationException(
                        "SPACE_OCCURRED_AT_UTC_BACKFILL_LOCK_UNAVAILABLE");
                }
            }
            else
            {
                await NonSqlGate.WaitAsync(ct);
                nonSqlGateHeld = true;
            }

            var pending = await db.IntegrationEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.SourceModule == SourceModule &&
                        x.OccurredAtUtc == null,
                    ct);
            if (!pending)
                return;

            // Deliberately resolve only after a locked pending-row check.
            // Fresh databases and fully backfilled databases do not require
            // this deployment-specific setting.
            var legacyTimeZone =
                SpaceIntegrationEventUtcNormalizer
                    .ResolveRequiredTimeZone(
                        options.LegacyIntegrationEventTimeZoneId);
            var resolutionCounts =
                new Dictionary<SpaceUtcNormalizationResolution, int>();
            var updatedTotal = 0;

            while (true)
            {
                var rows = await db.IntegrationEvents
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(x =>
                        x.SourceModule == SourceModule &&
                        x.OccurredAtUtc == null)
                    .OrderBy(x => x.Id)
                    .Select(x => new BackfillRow(
                        x.Id,
                        x.CreateDate,
                        x.JobId))
                    .Take(BatchSize)
                    .ToListAsync(ct);
                if (rows.Count == 0)
                    break;

                var normalized = rows
                    .Select(row =>
                    {
                        var result =
                            SpaceIntegrationEventUtcNormalizer
                                .Normalize(
                                    row.CreateDate,
                                    row.Id,
                                    row.JobId,
                                    legacyTimeZone);
                        return new NormalizedRow(
                            row.Id,
                            result.Utc,
                            result.Resolution);
                    })
                    .ToList();

                var affected = db.Database.IsRelational()
                    ? await UpdateRelationalBatchAsync(
                        db,
                        normalized,
                        ct)
                    : await UpdateNonRelationalBatchAsync(
                        db,
                        normalized,
                        ct);

                updatedTotal += affected;
                foreach (var row in normalized)
                {
                    resolutionCounts[row.Resolution] =
                        resolutionCounts.GetValueOrDefault(
                            row.Resolution) + 1;
                }

                db.ChangeTracker.Clear();
            }

            var remaining = await db.IntegrationEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.SourceModule == SourceModule &&
                        x.OccurredAtUtc == null,
                    ct);
            if (remaining)
            {
                throw new InvalidOperationException(
                    "SPACE_OCCURRED_AT_UTC_BACKFILL_INCOMPLETE");
            }

            logger.LogInformation(
                "Space integration UTC backfill completed {UpdatedCount} {TimeZoneId} {ResolutionSummary}",
                updatedTotal,
                legacyTimeZone.Id,
                string.Join(
                    ",",
                    resolutionCounts
                        .OrderBy(x => x.Key)
                        .Select(x => $"{x.Key}={x.Value}")));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex)
            when (ex.Message.StartsWith(
                "SPACE_",
                StringComparison.Ordinal))
        {
            logger.LogError(
                "Space integration UTC backfill failed {ReasonCode} {ErrorType}",
                ex.Message,
                ex.GetType().Name);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Space integration UTC backfill failed {ReasonCode} {ErrorType}",
                "SPACE_OCCURRED_AT_UTC_BACKFILL_FAILED",
                ex.GetType().Name);
            throw new InvalidOperationException(
                "SPACE_OCCURRED_AT_UTC_BACKFILL_FAILED");
        }
        finally
        {
            if (sessionLock is not null)
            {
                try
                {
                    await sessionLock.DisposeAsync();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        "Space integration UTC backfill lock release failed {ReasonCode} {ErrorType}",
                        "SPACE_OCCURRED_AT_UTC_BACKFILL_LOCK_RELEASE_FAILED",
                        ex.GetType().Name);
                }
            }

            if (openedConnection && lockConnection is not null)
                await lockConnection.CloseAsync();
            if (nonSqlGateHeld)
                NonSqlGate.Release();
        }
    }

    private static async Task<int> UpdateRelationalBatchAsync(
        CP6Context db,
        IReadOnlyList<NormalizedRow> rows,
        CancellationToken ct)
    {
        await using var transaction = await db.Database
            .BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var affected = 0;
        foreach (var row in rows)
        {
            affected += await db.IntegrationEvents
                .IgnoreQueryFilters()
                .Where(x =>
                    x.Id == row.Id &&
                    x.SourceModule == SourceModule &&
                    x.OccurredAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        x => x.OccurredAtUtc,
                        row.OccurredAtUtc),
                    ct);
        }

        await transaction.CommitAsync(ct);
        return affected;
    }

    private static async Task<int> UpdateNonRelationalBatchAsync(
        CP6Context db,
        IReadOnlyList<NormalizedRow> rows,
        CancellationToken ct)
    {
        var ids = rows.Select(x => x.Id).ToArray();
        var values = rows.ToDictionary(
            x => x.Id,
            x => x.OccurredAtUtc);
        var entities = await db.IntegrationEvents
            .IgnoreQueryFilters()
            .Where(x =>
                ids.Contains(x.Id) &&
                x.SourceModule == SourceModule &&
                x.OccurredAtUtc == null)
            .ToListAsync(ct);
        foreach (var entity in entities)
            entity.OccurredAtUtc = values[entity.Id];

        await db.SaveChangesAsync(ct);
        return entities.Count;
    }

    private sealed record BackfillRow(
        Guid Id,
        DateTime CreateDate,
        Guid? JobId);

    private sealed record NormalizedRow(
        Guid Id,
        DateTime OccurredAtUtc,
        SpaceUtcNormalizationResolution Resolution);
}
