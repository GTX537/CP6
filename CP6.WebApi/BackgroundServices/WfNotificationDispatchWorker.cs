using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wf;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.BackgroundServices;

/// <summary>Dispatches committed workflow outbox rows. A failed delivery never changes workflow state.</summary>
public sealed class WfNotificationDispatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WfNotificationDispatchWorker> _logger;

    public WfNotificationDispatchWorker(IServiceScopeFactory scopes, ILogger<WfNotificationDispatchWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await DispatchBatchAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    internal async Task DispatchBatchAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<NotifyHub>>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var now = DateTime.UtcNow;
        var candidateIds = await db.Wf_Notifications.IgnoreQueryFilters()
            .Where(x => (x.DispatchStatus == 0 || x.DispatchStatus == 2) &&
                        (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now))
            .OrderBy(x => x.CreateDate).Select(x => x.Id).Take(50).ToListAsync(ct);

        foreach (var id in candidateIds)
        {
            var leaseUntil = DateTime.UtcNow.AddMinutes(5);
            var claimed = await db.Wf_Notifications.IgnoreQueryFilters()
                .Where(x => x.Id == id &&
                            (x.DispatchStatus == 0 || x.DispatchStatus == 2) &&
                            (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.DispatchStatus, 2)
                    .SetProperty(x => x.DispatchAttempts, x => x.DispatchAttempts + 1)
                    .SetProperty(x => x.NextAttemptAtUtc, leaseUntil), ct);
            if (claimed != 1) continue;

            var row = await db.Wf_Notifications.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == id, ct);
            try
            {
                if (row.InAppRequested)
                    await hub.Clients.User(row.UserId.ToString()).SendAsync("WfNotification", new
                    {
                        notificationId = row.Id, type = row.Type, userId = row.UserId,
                        instanceId = row.InstanceId, taskId = row.TaskId, flowKey = row.FlowKey
                    }, ct);
                if (row.EmailRequested)
                {
                    var address = await db.Sys_Users.IgnoreQueryFilters()
                        .Where(x => x.Id == row.UserId).Select(x => x.Email).FirstOrDefaultAsync(ct);
                    if (!string.IsNullOrWhiteSpace(address))
                        await email.SendAsync(address, row.Title, row.Body);
                }
                row.DispatchStatus = 1;
                row.DispatchedAtUtc = DateTime.UtcNow;
                row.NextAttemptAtUtc = null;
                row.LastDispatchError = null;
            }
            catch (Exception ex)
            {
                row.DispatchStatus = 0;
                row.LastDispatchError = "dispatch-failed";
                row.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(Math.Min(3600, 5 * Math.Pow(2, row.DispatchAttempts)));
                _logger.LogWarning(ex, "Workflow notification dispatch failed for {NotificationId}", row.Id);
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
