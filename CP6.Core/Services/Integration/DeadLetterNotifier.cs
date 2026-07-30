using System.Reflection;
using System.Data;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Space.Observability;
using CP6.Entity.DomainModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Integration;

/// <summary>
/// Writes dead-letter alerts to SignalR and Sys_OperLog.
/// </summary>
public class DeadLetterNotifier :
    IDeadLetterNotifier,
    ISpaceDeadLetterNotifier
{
    private const string HubTypeName = "CP6.WebApi.Hubs.WmsHub, CP6.WebApi";
    private const string HubContextTypeName = "Microsoft.AspNetCore.SignalR.IHubContext`1, Microsoft.AspNetCore.SignalR.Core";

    private readonly IServiceProvider _serviceProvider;
    private readonly CP6Context _db;
    private readonly ILogger<DeadLetterNotifier> _logger;

    public DeadLetterNotifier(
        IServiceProvider serviceProvider,
        CP6Context db,
        ILogger<DeadLetterNotifier> logger)
    {
        _serviceProvider = serviceProvider;
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyAsync(IntegrationEvent evt, CancellationToken ct = default)
    {
        await PushSignalRAsync(
            evt,
            ct,
            spaceOutbox: false);
        await WriteOperLogAsync(
            evt,
            ct,
            spaceOutbox: false);
    }

    /// <inheritdoc />
    public async Task<bool> TryNotifyDurablyAsync(
        IntegrationEvent evt,
        Guid notificationLeaseId,
        CancellationToken ct = default)
    {
        if (evt.Id == Guid.Empty ||
            notificationLeaseId == Guid.Empty)
        {
            return false;
        }

        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                ct)
            : null;
        var owned = await LockSpaceNotificationAsync(
            evt.Id,
            notificationLeaseId,
            DateTime.UtcNow,
            ct);
        if (!owned)
            return false;

        var durable = await WriteOperLogAsync(
            evt,
            ct,
            spaceOutbox: true);
        if (!durable)
            return false;
        if (transaction is not null)
            await transaction.CommitAsync(ct);

        // Realtime delivery is intentionally best effort. Durable success is
        // defined exclusively by the committed Sys_OperLog row above.
        await PushSignalRAsync(
            evt,
            ct,
            spaceOutbox: true);
        return true;
    }

    private async Task PushSignalRAsync(
        IntegrationEvent evt,
        CancellationToken ct,
        bool spaceOutbox)
    {
        try
        {
            var hubContext = ResolveWmsHubContext();
            if (hubContext == null)
            {
                _logger.LogWarning("WMS HubContext was not available for IntegrationEvent {EventId}", evt.Id);
                return;
            }

            var clients = hubContext.GetType().GetProperty("Clients")?.GetValue(hubContext);
            var all = clients?.GetType().GetProperty("All")?.GetValue(clients);
            if (all == null)
            {
                _logger.LogWarning("WMS HubContext clients were not available for IntegrationEvent {EventId}", evt.Id);
                return;
            }

            var payload = new
            {
                EventId = evt.Id,
                evt.HookName,
                evt.SourceNo,
                evt.Attempts,
                LastError = string.Equals(
                    evt.SourceModule,
                    "SPACE",
                    StringComparison.Ordinal)
                        ? SafeSpaceLastError(evt.LastError)
                        : evt.LastError,
                OccurredAt = DateTime.UtcNow,
            };

            var method = all.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m =>
                    m.Name == "SendCoreAsync"
                    && m.GetParameters().Length == 3);
            if (method == null)
            {
                _logger.LogWarning("SendCoreAsync was not found for IntegrationEvent {EventId}", evt.Id);
                return;
            }

            var task = method.Invoke(all, new object?[]
            {
                "IntegrationDeadLetter",
                new object?[] { payload },
                ct,
            }) as Task;
            if (task != null)
            {
                await task;
            }
        }
        catch (Exception ex)
        {
            if (spaceOutbox)
            {
                var safe = SpaceErrorSanitizer.Classify(
                    ex,
                    "SPACE_DEAD_LETTER_SIGNALR_FAILED");
                _logger.LogError(
                    "Space dead-letter SignalR push failed {ReasonCode} {ExceptionType} {Fingerprint} {EventId}",
                    safe.ReasonCode,
                    safe.ExceptionType,
                    safe.Fingerprint,
                    evt.Id);
                return;
            }

            _logger.LogError(
                ex,
                "SignalR dead-letter push failed for IntegrationEvent {EventId}",
                evt.Id);
        }
    }

    private async Task<bool> WriteOperLogAsync(
        IntegrationEvent evt,
        CancellationToken ct,
        bool spaceOutbox)
    {
        try
        {
            var requestUrl = spaceOutbox
                ? NotificationKey(evt.Id)
                : $"/integration-event/{evt.Id}";
            if (spaceOutbox &&
                await _db.Sys_OperLogs.AnyAsync(
                    log =>
                        log.IsAlert &&
                        log.Controller == "IntegrationEvent" &&
                        log.RequestUrl == requestUrl,
                    ct))
            {
                return true;
            }

            _db.Sys_OperLogs.Add(new Sys_OperLog
            {
                UserName = "system",
                HttpMethod = "BACKGROUND",
                RequestUrl = requestUrl,
                Controller = "IntegrationEvent",
                Action = evt.HookName,
                RequestBody = spaceOutbox
                    ? $"hook={Truncate(evt.HookName, 100)} source={Truncate(evt.SourceNo, 100)} attempts={evt.Attempts}; lastError={SafeSpaceLastError(evt.LastError)}"
                    : $"hook={evt.HookName} source={evt.SourceNo} attempts={evt.Attempts}; lastError={Truncate(evt.LastError, 4000)}",
                StatusCode = 500,
                ElapsedMs = 0,
                ClientIp = null,
                CreateDate = DateTime.Now,
                IsAlert = true,
            });
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            if (spaceOutbox)
            {
                var safe = SpaceErrorSanitizer.Classify(
                    ex,
                    "SPACE_DEAD_LETTER_OPERLOG_FAILED");
                _logger.LogError(
                    "Space dead-letter OperLog write failed {ReasonCode} {ExceptionType} {Fingerprint} {EventId}",
                    safe.ReasonCode,
                    safe.ExceptionType,
                    safe.Fingerprint,
                    evt.Id);
            }
            else
            {
                _logger.LogError(
                    ex,
                    "OperLog dead-letter write failed for IntegrationEvent {EventId}",
                    evt.Id);
            }
            return false;
        }
    }

    private async Task<bool> LockSpaceNotificationAsync(
        Guid eventId,
        Guid notificationLeaseId,
        DateTime lockNow,
        CancellationToken ct)
    {
        var tenantId = _db.CurrentTenantId;
        if (_db.Database.ProviderName?.Contains(
                "SqlServer",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return await _db.IntegrationEvents
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM [T_IntegrationEvent] WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                    WHERE [Id] = {eventId}
                      AND [TenantId] = {tenantId}
                      AND [SourceModule] = {"SPACE"}
                      AND [Status] = {IntegrationEventStatus.DeadLetter}
                      AND [DeadLetterNotifiedAtUtc] IS NULL
                      AND [DeadLetterNotificationLeaseId] = {notificationLeaseId}
                      AND [DeadLetterNotificationLeaseUntilUtc] > {lockNow}
                    """)
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(ct);
        }

        var owned = _db.IntegrationEvents
            .IgnoreQueryFilters()
            .Where(e =>
                e.Id == eventId &&
                e.TenantId == tenantId &&
                e.SourceModule == "SPACE" &&
                e.Status == IntegrationEventStatus.DeadLetter &&
                e.DeadLetterNotifiedAtUtc == null &&
                e.DeadLetterNotificationLeaseId ==
                    notificationLeaseId &&
                e.DeadLetterNotificationLeaseUntilUtc >
                    lockNow);
        if (!_db.Database.IsRelational())
            return await owned.AnyAsync(ct);

        var affected = await owned.ExecuteUpdateAsync(
            setters => setters.SetProperty(
                e => e.DeadLetterNotificationLeaseId,
                notificationLeaseId),
            ct);
        return affected == 1;
    }

    private static string NotificationKey(Guid eventId) =>
        $"/integration-event/{eventId}/dead-letter";

    private static string SafeSpaceLastError(string? value)
    {
        const string fallback =
            "SPACE_RETRY_DEAD_LETTER";
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var reasonCode = value.Split(':', 2)[0];
        if (reasonCode.Length is 0 or > 100 ||
            !reasonCode.StartsWith(
                "SPACE_",
                StringComparison.Ordinal) ||
            reasonCode.Any(character =>
                character is not (>= 'A' and <= 'Z') and
                    not (>= '0' and <= '9') and
                    not '_'))
        {
            return fallback;
        }
        return reasonCode;
    }

    private object? ResolveWmsHubContext()
    {
        var hubType = Type.GetType(HubTypeName);
        var openHubContextType = Type.GetType(HubContextTypeName);
        if (hubType == null || openHubContextType == null)
        {
            return null;
        }

        var hubContextType = openHubContextType.MakeGenericType(hubType);
        return _serviceProvider.GetService(hubContextType);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value == null || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
