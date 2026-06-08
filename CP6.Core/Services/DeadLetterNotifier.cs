using System.Reflection;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services;

/// <summary>
/// Writes dead-letter alerts to SignalR and Sys_OperLog.
/// </summary>
public class DeadLetterNotifier : IDeadLetterNotifier
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
        await PushSignalRAsync(evt, ct);
        await WriteOperLogAsync(evt, ct);
    }

    private async Task PushSignalRAsync(IntegrationEvent evt, CancellationToken ct)
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
                evt.LastError,
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
            _logger.LogError(ex, "SignalR dead-letter push failed for IntegrationEvent {EventId}", evt.Id);
        }
    }

    private async Task WriteOperLogAsync(IntegrationEvent evt, CancellationToken ct)
    {
        try
        {
            _db.Sys_OperLogs.Add(new Sys_OperLog
            {
                UserName = "system",
                HttpMethod = "BACKGROUND",
                RequestUrl = $"/integration-event/{evt.Id}",
                Controller = "IntegrationEvent",
                Action = evt.HookName,
                RequestBody = $"hook={evt.HookName} source={evt.SourceNo} attempts={evt.Attempts}; lastError={Truncate(evt.LastError, 4000)}",
                StatusCode = 500,
                ElapsedMs = 0,
                ClientIp = null,
                CreateDate = DateTime.Now,
                IsAlert = true,
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OperLog dead-letter write failed for IntegrationEvent {EventId}", evt.Id);
        }
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
