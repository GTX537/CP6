using System.Data;
using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Space.Observability;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.BackgroundServices;

internal static class SpaceIntegrationEventOccurredAtUtcBackfill
{
    internal const int BatchSize = 500;
    internal const string LockResource =
        "CP6:SpaceIntegrationEvent:OccurredAtUtc:v1";
    internal const string AcquireLockCommandText =
        """
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock
            @Resource = @resource,
            @LockMode = N'Exclusive',
            @LockOwner = N'Session',
            @LockTimeout = @timeoutMilliseconds,
            @DbPrincipal = N'public';
        SELECT @result;
        """;
    internal const string ReleaseLockCommandText =
        """
        DECLARE @result int;
        EXEC @result = sys.sp_releaseapplock
            @Resource = @resource,
            @LockOwner = N'Session',
            @DbPrincipal = N'public';
        SELECT @result;
        """;

    private const int LockTimeoutMilliseconds = 30_000;
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

        var isSqlServer = db.Database.IsSqlServer();
        DbConnection? sqlConnection = null;
        var openedConnection = false;
        var sqlLockHeld = false;
        var nonSqlGateHeld = false;

        try
        {
            if (isSqlServer)
            {
                sqlConnection = db.Database.GetDbConnection();
                if (sqlConnection.State != ConnectionState.Open)
                {
                    await sqlConnection.OpenAsync(ct);
                    openedConnection = true;
                }

                var lockResult = await ExecuteAppLockCommandAsync(
                    sqlConnection,
                    AcquireLockCommandText,
                    LockTimeoutMilliseconds,
                    ct);
                if (lockResult < 0)
                {
                    throw new InvalidOperationException(
                        "SPACE_OCCURRED_AT_UTC_BACKFILL_LOCK_UNAVAILABLE");
                }

                sqlLockHeld = true;
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
            if (sqlLockHeld && sqlConnection is not null)
            {
                try
                {
                    await ExecuteAppLockCommandAsync(
                        sqlConnection,
                        ReleaseLockCommandText,
                        LockTimeoutMilliseconds,
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        "Space integration UTC backfill lock release failed {ReasonCode} {ErrorType}",
                        "SPACE_OCCURRED_AT_UTC_BACKFILL_LOCK_RELEASE_FAILED",
                        ex.GetType().Name);
                }
            }

            if (openedConnection && sqlConnection is not null)
                await sqlConnection.CloseAsync();
            if (nonSqlGateHeld)
                NonSqlGate.Release();
        }
    }

    internal static void ConfigureAppLockCommand(
        DbCommand command,
        string commandText,
        int timeoutMilliseconds)
    {
        command.CommandText = commandText;
        command.CommandType = CommandType.Text;

        var resource = command.CreateParameter();
        resource.ParameterName = "@resource";
        resource.DbType = DbType.String;
        resource.Size = 255;
        resource.Value = LockResource;
        command.Parameters.Add(resource);

        var timeout = command.CreateParameter();
        timeout.ParameterName = "@timeoutMilliseconds";
        timeout.DbType = DbType.Int32;
        timeout.Value = timeoutMilliseconds;
        command.Parameters.Add(timeout);
    }

    private static async Task<int> ExecuteAppLockCommandAsync(
        DbConnection connection,
        string commandText,
        int timeoutMilliseconds,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        ConfigureAppLockCommand(
            command,
            commandText,
            timeoutMilliseconds);
        var result = await command.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull)
        {
            throw new InvalidOperationException(
                "SPACE_OCCURRED_AT_UTC_BACKFILL_LOCK_UNAVAILABLE");
        }

        return Convert.ToInt32(result);
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
