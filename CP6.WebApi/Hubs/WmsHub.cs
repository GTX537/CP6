using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wms;
using CP6.Core.Services.Common;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Hubs;

/// <summary>
/// WMS SignalR Hub — 倉庫リアルタイムイベント
/// </summary>
/// <remarks>
/// 推送事件：
/// - StockChanged：在庫変動（IN/OUT/MOVE/ADJ/RSV/UNRSV）
/// - InboundReceived：入庫実績確定
/// - OutboundShipped：出庫確定 + 梱包NO
/// - StockTakeCompleted：棚卸完了
///
/// クライアント接続：
///   const conn = new HubConnectionBuilder().withUrl('/hubs/wms').build()
///   conn.on('StockChanged', payload => { ... })
///
/// 購読グループ：
///   - wh:{warehouseCd}      倉庫別の絞り込み
///   - product:{productCd}    製品別の絞り込み
/// </remarks>
[Authorize]
public class WmsHub : Hub
{
    public const string GeneralGroup = "wms:all";
    private const string FilterGroupsKey = "wms:filter-groups";
    private readonly ILogger<WmsHub> _logger;
    private readonly CP6Context? _db;
    private readonly StockOperationsService? _owner;
    private readonly ITenantContext? _tenant;
    public WmsHub(ILogger<WmsHub> logger,CP6Context? db=null,StockOperationsService? owner=null,ITenantContext? tenant=null)
    { _logger=logger;_db=db;_owner=owner;_tenant=tenant; }
    public static string InventoryGroup(Guid tenantId)=>$"inventory:v1:tenant:{tenantId:N}";
    private Task AuthorizeInventory()
    {
        // SignalR resolves each Hub in a separate scope from TenantMiddleware.
        // This selects the authenticated connection's tenant, not a client filter;
        // the Owner still reconsumes its original session and current permissions.
        var identities=Context.User?.Identities.Where(x=>x.IsAuthenticated).ToArray();
        var claims=identities is {Length:1}?identities[0].FindAll("tenant_id").ToArray():[];
        if(claims.Length!=1 || !Guid.TryParse(claims[0].Value,out var tenantId) || tenantId==Guid.Empty)
            throw new HubException("REAUTH_REQUIRED");
        if(_tenant!=null)_tenant.CurrentTenantId=tenantId;
        if(_db?.Database.IsNpgsql()!=true || _db.CurrentTenantId!=tenantId || _owner==null)
            throw new HubException("AUTHORITY_FENCE_UNAVAILABLE");
        return _owner.AuthorizeUpdatesAsync(Context.ConnectionAborted);
    }

    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId:N}";

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("WMS Hub 接続: {ConnectionId}", Context.ConnectionId);
        if(_db?.Database.IsNpgsql()==true)
        {
            await AuthorizeInventory();
            await Groups.AddToGroupAsync(Context.ConnectionId,InventoryGroup(_db.CurrentTenantId));
            await base.OnConnectedAsync();return;
        }
        var tenantClaim = Context.User?.FindFirst("tenant_id")?.Value;
        if (Guid.TryParse(tenantClaim, out var tenantId))
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));
        await Groups.AddToGroupAsync(Context.ConnectionId, GeneralGroup);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("WMS Hub 切断: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>クライアントが特定倉庫の更新を購読</summary>
    public Task SubscribeWarehouse(string warehouseCd)
        => SubscribeFilterAsync($"wh:{warehouseCd}");

    public Task UnsubscribeWarehouse(string warehouseCd)
        => UnsubscribeFilterAsync($"wh:{warehouseCd}");

    /// <summary>クライアントが特定製品の更新を購読</summary>
    public Task SubscribeProduct(string productCd)
        => SubscribeFilterAsync($"product:{productCd}");

    public Task UnsubscribeProduct(string productCd)
        => UnsubscribeFilterAsync($"product:{productCd}");

    private HashSet<string> FilterGroups
    {
        get
        {
            if (Context.Items.TryGetValue(FilterGroupsKey, out var value)
                && value is HashSet<string> groups)
                return groups;
            var created = new HashSet<string>(StringComparer.Ordinal);
            Context.Items[FilterGroupsKey] = created;
            return created;
        }
    }

    private async Task SubscribeFilterAsync(string group)
    {
        if(_db?.Database.IsNpgsql()==true){await AuthorizeInventory();return;}
        var groups = FilterGroups;
        if (!groups.Add(group)) return;
        if (groups.Count == 1)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GeneralGroup);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
    }

    private async Task UnsubscribeFilterAsync(string group)
    {
        if(_db?.Database.IsNpgsql()==true)return;
        var groups = FilterGroups;
        if (!groups.Remove(group)) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        if (groups.Count == 0)
            await Groups.AddToGroupAsync(Context.ConnectionId, GeneralGroup);
    }
}
