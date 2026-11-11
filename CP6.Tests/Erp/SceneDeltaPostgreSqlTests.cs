using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Entity.DTOs.Space;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string SceneDeltaBarrier="LOCK TABLE public.\"Space_Floor\", public.\"Space_Zone\", public.\"Space_Rack\", public.\"Space_Location\" IN ACCESS EXCLUSIVE MODE";
    private Task<Harness> SceneDeltaSeed()=>SceneIngressSeed();
    private static Task<Guid> SceneDeltaFloor(Harness h)=>h.Db.Space_Floors.Where(x=>x.FloorCode=="F1").Select(x=>x.Id).SingleAsync();
    private static string SceneDeltaPath(Guid floor)=>SpaceCodePath+"/floor/"+floor+"/scene";
    private static SceneSaveDto SceneDeltaNewZone(Guid floor,string code)=>new(){Zones=[HierarchyZone(floor,code)]};
    private sealed record SceneDeltaHttpInput(HttpMethod Method,string Path,object Body);
    private static async Task<SceneDeltaHttpInput> SceneDeltaInput(Harness h,string operation)
    {
        if(operation!="save")throw new ArgumentOutOfRangeException(nameof(operation));
        var floor=await SceneDeltaFloor(h);return new(HttpMethod.Post,SceneDeltaPath(floor),SceneDeltaNewZone(floor,"DELTA"));
    }
    private static async Task<string> SceneDeltaRows(Harness h)=>JsonSerializer.Serialize(new{Geometry=await HierarchyRows(h),
        Markers=await h.Db.Space_Markers.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),Bins=await h.Db.WmsBins.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),
        Events=await h.Db.IntegrationEvents.AsNoTracking().Where(x=>x.SourceModule=="SPACE").OrderBy(x=>x.Id).ToArrayAsync()});
    private static async Task<int> SceneDeltaSequence(Harness h)=>await h.Db.DocSequences.AsNoTracking().Where(x=>x.FuncCode=="LPB").Select(x=>(int?)x.LastSeq).SingleOrDefaultAsync()??0;
    private static async Task<SceneSaveDto> SceneDeltaRehome(Harness h,Guid floor)
    {
        var rack=await h.Db.Space_Racks.AsNoTracking().SingleAsync(x=>x.RackCode=="R01");var d=HierarchyRackEdit(rack,110);d.AisleId=null;
        return new(){Zones=[HierarchyZone(floor,"REHOME")],Racks=[d]};
    }


    [CurrentPermissionPostgresFact]
    public async Task SceneDelta_save_decides_current_tenant_and_session()
    {
        const string operation="save";
        await using var h=await SceneDeltaSeed();await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);var input=await SceneDeltaInput(h,operation);
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);var before=await SceneDeltaRows(h);var stock=await PalletStockRows(h);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var target=new NpgsqlConnection(connection);await target.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> pending;FenceWait pauseWait,commandWait;TenantManagementReceiptV1 suspended;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand(SceneDeltaBarrier,business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var held=await target.BeginTransactionAsync();await using var tenantLock=new NpgsqlCommand("SELECT 1 FROM public.\"Sys_Tenants\" WHERE \"Id\"=@tenant FOR UPDATE",target,held);tenantLock.Parameters.AddWithValue("tenant",h.Tenant);Assert.Equal(1,await tenantLock.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("scene-delta-"+operation,active.Revision,"suspend",null)));
                try{pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);pending=Send();commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"scene-delta-"+operation,pending);}
                finally{await held.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("scene-delta-"+operation+"-pause.json",new{Operation=operation,Status=(int)denied.StatusCode,AuthorityWait=commandWait.Blocker==pauseWait.Backend,pauseWait,commandWait,Before=before,After=await SceneDeltaRows(h),StockBefore=stock,StockAfter=await PalletStockRows(h)});
        Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await SceneDeltaRows(h));Assert.Equal(stock,await PalletStockRows(h));
        using var restored=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("scene-delta-restore-"+operation,suspended.Snapshot.Revision,"reactivate",null)));Assert.True((await NumberHttpData<TenantManagementReceiptV1>(restored)).Snapshot.Enable);actor=await Login(host,h);using var recovered=await Send();Assert.True(recovered.StatusCode==HttpStatusCode.OK,await OperationsFailure(recovered));
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));
    }

    [CurrentPermissionPostgresFact]
    public async Task SceneDelta_current_action_revocation_wins_before_the_write()
    {
        const string operation="save",resource="space-floor",action="edit";
        await using var h=await SceneDeltaSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var input=await SceneDeltaInput(h,operation);var before=await SceneDeltaRows(h);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey==resource).Select(m=>m.MenuId).SingleAsync();await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand(SceneDeltaBarrier,business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var changed=await control.BeginTransactionAsync();await using var change=new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"MenuId\"=@menu AND \"ActionCode\"=@action",control,changed);change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("menu",menu);change.Parameters.AddWithValue("action",action);await change.ExecuteNonQueryAsync();
                pending=host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"scene-delta-revoke-"+resource+"-"+action,pending);await changed.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("scene-delta-revoke-"+resource+"-"+action+".json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,wait,Before=before,After=await SceneDeltaRows(h)});Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(control.ProcessID,wait.Blocker);Assert.Equal(before,await SceneDeltaRows(h));
    }

}
