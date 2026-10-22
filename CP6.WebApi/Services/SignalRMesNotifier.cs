using CP6.Core.EFDbContext;
using CP6.Core.Services.Mes;
using CP6.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

/// <summary>Volatile invalidation only. Business facts must be read through current authorized HTTP Owners.</summary>
public class SignalRMesNotifier : IMesNotifier
{
    private readonly IHubContext<MesHub> hub;
    private readonly CP6Context? db;
    public SignalRMesNotifier(IHubContext<MesHub> hub,ILogger<SignalRMesNotifier> logger,CP6Context? db=null)
    {this.hub=hub;this.db=db;}

    private Task Invalidate()
    {
        if(db?.Database.IsNpgsql()!=true || db.CurrentTenantId==Guid.Empty)
            throw new InvalidOperationException("Trusted PostgreSQL MES tenant required.");
        // No document, machine, quantity, actor, timestamp or event kind is disclosed.
        return hub.Clients.Group(MesHub.UpdatesGroup(db.CurrentTenantId)).SendAsync("MesInvalidatedV1");
    }
    public Task NotifyProductionReportedAsync(string workOrderNo,string processCd,decimal goodQty,decimal defectQty)=>Invalidate();
    public Task NotifyDefectIssuedAsync(string defectNo,string workOrderNo,string categoryCd)=>Invalidate();
    public Task NotifyMachineStatusChangedAsync(string machineCd,int newStatus)=>Invalidate();
    public Task NotifyWorkOrderStatusChangedAsync(string workOrderNo,int newStatus)=>Invalidate();
    public Task NotifyDowntimeRegisteredAsync(string downtimeNo,string machineCd,int downtimeType)=>Invalidate();
}
