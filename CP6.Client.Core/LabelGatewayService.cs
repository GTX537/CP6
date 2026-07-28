using CP6.Client.Api;

namespace CP6.Client.Core;

public interface ILabelPrinter
{
    Task PrintAsync(LabelJob job, CancellationToken ct = default);
}

public sealed class LabelGatewayService
{
    private readonly Cp6ApiClient _api;
    private readonly ClientOptions _options;
    private readonly ILabelPrinter _printer;
    private CancellationTokenSource? _loopCts;
    private Task? _loop;

    public LabelGatewayService(
        IHttpClientFactory clients,
        ClientOptions options,
        ILabelPrinter printer)
    {
        _api = new Cp6ApiClient(
            clients.CreateClient(ClientServiceCollectionExtensions.AuthenticatedClient));
        _options = options;
        _printer = printer;
    }

    public event EventHandler<string>? StateChanged;
    public bool IsRunning => _loop is { IsCompleted: false };

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return Task.CompletedTask;
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = RunLoopAsync(_loopCts.Token);
        StateChanged?.Invoke(this, "Running");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_loopCts == null) return;
        await _loopCts.CancelAsync();
        if (_loop != null)
        {
            try { await _loop; }
            catch (OperationCanceledException) { }
        }
        _loopCts.Dispose();
        _loopCts = null;
        _loop = null;
        StateChanged?.Invoke(this, "Stopped");
    }

    public async Task ProcessOnceAsync(CancellationToken ct = default)
    {
        var jobs = await _api.GetLabelJobsAsync(
            status: "Pending", pageSize: 25, ct: ct);
        foreach (var pending in jobs.Items.Where(x =>
                     string.IsNullOrWhiteSpace(x.RequestedDeviceId)
                     || x.RequestedDeviceId == _options.Context.DeviceId))
        {
            ct.ThrowIfCancellationRequested();
            LabelJob claimed;
            try
            {
                claimed = await _api.ClaimLabelJobAsync(pending.JobNo, new LabelJobCommand
                {
                    RowVersion = pending.RowVersion,
                    DeviceId = _options.Context.DeviceId,
                }, ct);
            }
            catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                continue;
            }

            try
            {
                await _printer.PrintAsync(claimed, ct);
                await _api.CompleteLabelJobAsync(claimed.JobNo, new LabelJobCommand
                {
                    RowVersion = claimed.RowVersion,
                    DeviceId = _options.Context.DeviceId,
                    ResultMessage = "Printed by CP6 Windows gateway",
                }, success: true, ct);
            }
            catch (Exception ex)
            {
                await _api.CompleteLabelJobAsync(claimed.JobNo, new LabelJobCommand
                {
                    RowVersion = claimed.RowVersion,
                    DeviceId = _options.Context.DeviceId,
                    ResultMessage = ex.GetType().Name,
                }, success: false, ct);
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(ct);
                StateChanged?.Invoke(this, "Online");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch
            {
                StateChanged?.Invoke(this, "Retrying");
            }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}
