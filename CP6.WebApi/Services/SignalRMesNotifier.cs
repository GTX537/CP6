using CP6.Core.Services.Mes;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CP6.WebApi.Services;

/// <summary>
/// IMesNotifier の SignalR 実装。
/// </summary>
/// <remarks>
/// CP6.Core からは IMesNotifier 経由で呼び出され、SignalR への依存を WebApi 層に閉じ込める。
/// </remarks>
public class SignalRMesNotifier : IMesNotifier
{
    private readonly IHubContext<MesHub> _hub;
    private readonly ILogger<SignalRMesNotifier> _logger;

    public SignalRMesNotifier(IHubContext<MesHub> hub, ILogger<SignalRMesNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyProductionReportedAsync(string workOrderNo, string processCd, decimal goodQty, decimal defectQty)
    {
        var payload = new { workOrderNo, processCd, goodQty, defectQty, at = DateTime.Now };
        await _hub.Clients.All.SendAsync("ProductionReported", payload);
        await _hub.Clients.Group($"wo:{workOrderNo}").SendAsync("ProductionReported", payload);
    }

    public async Task NotifyDefectIssuedAsync(string defectNo, string workOrderNo, string categoryCd)
    {
        var payload = new { defectNo, workOrderNo, categoryCd, at = DateTime.Now };
        await _hub.Clients.All.SendAsync("DefectIssued", payload);
    }

    public async Task NotifyMachineStatusChangedAsync(string machineCd, int newStatus)
    {
        var payload = new { machineCd, newStatus, at = DateTime.Now };
        await _hub.Clients.All.SendAsync("MachineStatusChanged", payload);
        await _hub.Clients.Group($"machine:{machineCd}").SendAsync("MachineStatusChanged", payload);
    }

    public async Task NotifyWorkOrderStatusChangedAsync(string workOrderNo, int newStatus)
    {
        var payload = new { workOrderNo, newStatus, at = DateTime.Now };
        await _hub.Clients.All.SendAsync("WorkOrderStatusChanged", payload);
        await _hub.Clients.Group($"wo:{workOrderNo}").SendAsync("WorkOrderStatusChanged", payload);
    }

    public async Task NotifyDowntimeRegisteredAsync(string downtimeNo, string machineCd, int downtimeType)
    {
        var payload = new { downtimeNo, machineCd, downtimeType, at = DateTime.Now };
        await _hub.Clients.All.SendAsync("DowntimeRegistered", payload);
        await _hub.Clients.Group($"machine:{machineCd}").SendAsync("DowntimeRegistered", payload);
    }
}
