using CP6.Core.EFDbContext;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Hubs;

/// <summary>Authenticated MES invalidation channel. Client-supplied business codes never select a delivery group.</summary>
[Authorize]
public class MesHub : Hub
{
    private readonly ILogger<MesHub> logger;
    private readonly CP6Context? db;
    private readonly MesDashboardService? owner;
    private readonly ITenantContext? tenant;
    public MesHub(ILogger<MesHub> logger,CP6Context? db=null,MesDashboardService? owner=null,ITenantContext? tenant=null)
    {this.logger=logger;this.db=db;this.owner=owner;this.tenant=tenant;}
    public static string UpdatesGroup(Guid tenant)=>$"mes:v1:tenant:{tenant:N}";
    private Task AuthorizeUpdates()
    {
        // SignalR creates a fresh DI scope per invocation; TenantMiddleware ran in
        // the transport request scope. Bind this scope from the authenticated
        // connection, then let the Owner revalidate its original session and current authority.
        var identities=Context.User?.Identities.Where(x=>x.IsAuthenticated).ToArray();
        var claims=identities is {Length:1}?identities[0].FindAll("tenant_id").ToArray():[];
        if(claims.Length!=1 || !Guid.TryParse(claims[0].Value,out var tenantId) || tenantId==Guid.Empty)
            throw new HubException("REAUTH_REQUIRED");
        if(tenant!=null)tenant.CurrentTenantId=tenantId;
        if(db?.Database.IsNpgsql()!=true || db.CurrentTenantId!=tenantId || owner==null)
            throw new HubException("AUTHORITY_FENCE_UNAVAILABLE");
        return owner.AuthorizeUpdatesAsync(Context.ConnectionAborted);
    }
    public override async Task OnConnectedAsync()
    {
        await AuthorizeUpdates();
        await Groups.AddToGroupAsync(Context.ConnectionId,UpdatesGroup(db!.CurrentTenantId),Context.ConnectionAborted);
        logger.LogInformation("MES invalidation connected: {ConnectionId}",Context.ConnectionId);
        await base.OnConnectedAsync();
    }
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("MES invalidation disconnected: {ConnectionId}",Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
    // Existing method names remain callable; their arguments are not authority or delivery selectors.
    public Task SubscribeWorkOrder(string workOrderNo)=>AuthorizeUpdates();
    public Task SubscribeMachine(string machineCd)=>AuthorizeUpdates();
    public Task UnsubscribeWorkOrder(string workOrderNo)=>Task.CompletedTask;
    public Task UnsubscribeMachine(string machineCd)=>Task.CompletedTask;
}
