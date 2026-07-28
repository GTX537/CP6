using CP6.Client.Api;
using Microsoft.AspNetCore.SignalR.Client;

namespace CP6.Client.Core;

public sealed class WmsRealtimeService : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ClientAccessGate _accessGate;
    private bool _handlersRegistered;

    public WmsRealtimeService(
        ClientOptions options,
        IClientSessionService sessions,
        ClientAccessGate accessGate)
    {
        _accessGate = accessGate;
        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(options.ApiBaseAddress, "hubs/wms"), config =>
            {
                config.AccessTokenProvider = () => Task.FromResult(sessions.AccessToken);
            })
            .WithAutomaticReconnect(new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30),
            })
            .Build();
        _connection.Reconnecting += _ =>
        {
            ConnectionStateChanged?.Invoke(this, "Reconnecting");
            return Task.CompletedTask;
        };
        _connection.Reconnected += _ =>
        {
            ConnectionStateChanged?.Invoke(this, "Online");
            return Task.CompletedTask;
        };
        _connection.Closed += _ =>
        {
            ConnectionStateChanged?.Invoke(this, "Offline");
            return Task.CompletedTask;
        };
    }

    public HubConnectionState State => _connection.State;
    public event EventHandler<MobileTask>? TaskChanged;
    public event EventHandler<string>? ConnectionStateChanged;

    public async Task StartAsync(CancellationToken ct = default)
    {
        _accessGate.EnsureBusinessAllowed();
        if (!_handlersRegistered)
        {
            foreach (var eventName in new[]
                     {
                         "MobileTaskCreated", "MobileTaskAssigned", "MobileTaskStarted",
                         "MobileTaskCompleted", "MobileTaskCancelled",
                     })
            {
                _connection.On<MobileTask>(eventName, task => TaskChanged?.Invoke(this, task));
            }
            _handlersRegistered = true;
        }
        if (_connection.State == HubConnectionState.Disconnected)
        {
            await _connection.StartAsync(ct);
            ConnectionStateChanged?.Invoke(this, "Online");
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_connection.State != HubConnectionState.Disconnected)
            await _connection.StopAsync(ct);
        ConnectionStateChanged?.Invoke(this, "Offline");
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
