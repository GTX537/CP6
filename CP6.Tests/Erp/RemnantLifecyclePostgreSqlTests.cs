using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<Harness> RemnantSeed(string operation)
    {
        var h=await Seed();var db=h.Db;
        if(!await db.Sys_Menus.AnyAsync(m=>m.MenuKey=="wms-remnant"))db.Sys_Menus.Add(new(){MenuId=442,MenuKey="wms-remnant",MenuName="Legacy remnant"});
        await db.SaveChangesAsync();var menu=await db.Sys_Menus.Where(m=>m.MenuKey=="wms-remnant").Select(m=>m.MenuId).SingleAsync();
        foreach(var action in new[]{"view","add","edit","reserve","use","dispose","del"})
        {
            if(!await db.Sys_MenuActions.AnyAsync(a=>a.MenuId==menu&&a.ActionCode==action))db.Sys_MenuActions.Add(new(){MenuId=menu,ActionCode=action,ActionName=action});
            db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu,ActionCode=action});
        }
        db.Stocks.Add(new(){WarehouseCd="W1",LocationCd="SRC",ProductCd="UNRELATED",LotNo="LOT",PhysicalQty=9.12345678m,AvailableQty=9.12345678m,UnitPrice=12.3456m});
        if(operation!="create")foreach(var suffix in new[]{"A","B"})db.RemnantMaterials.Add(new(){RemnantNo=RemnantNo(h,suffix),MaterialType="PAPER",MaterialGrade="SIM-GRADE",WidthMm=300,LengthMm=500,
            Quantity=1.2345m,UnitCd="SHT",WarehouseCd="W1",LocationCd="SRC",SourceWorkOrderNo="SIM-WORK-ORDER",SourceRollNo="SIM-ROLL",
            Status=operation=="unreserve"?RemnantStatus.Reserved:RemnantStatus.Available,ReservedFor=operation=="unreserve"?"SIM-RESERVED":null,Creator="price-owner"});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();return h;
    }
    private static string RemnantNo(Harness h,string suffix)=>"RM"+h.Tenant.ToString("N")[..12]+suffix;
    private static RemnantDto RemnantInput()=>new(){MaterialType="PAPER",MaterialGrade="SIM-GRADE",WidthMm=300,LengthMm=500,ThicknessUm=200,Quantity=1.2345m,UnitCd="SHT",
        WarehouseCd="W1",LocationCd="SRC",SourceWorkOrderNo="SIM-WORK-ORDER",SourceRollNo="SIM-ROLL",Remarks="legacy simulated metadata"};

    [TenantManagementPostgresTheory]
    [InlineData("create")][InlineData("update")][InlineData("reserve")][InlineData("unreserve")][InlineData("use")][InlineData("dispose")][InlineData("delete")][InlineData("list")][InlineData("detail")][InlineData("match")][InlineData("match")]
    public async Task Remnant_existing_entries_decide_current_authority(string operation)
    {
        await using var h=await RemnantSeed(operation);await using var administrator=await TenantManagementSeed();var db=h.Db;var tenant=h.Tenant;var connection=db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);
        var stock=await PalletStockRows(h);
        using var normal=await Send("A");Assert.True(normal.StatusCode==HttpStatusCode.OK,await OperationsFailure(normal));
        Assert.Equal(stock,await PalletStockRows(h));
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var target=new NpgsqlConnection(connection);await target.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        FenceWait pauseWait,commandWait;Task<HttpResponseMessage> pending;TenantManagementReceiptV1 suspended;var before=await RemnantWriteSet(h);
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_RemnantMaterial\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var held=await target.BeginTransactionAsync();await using var tenantLock=new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""",target,held);
                tenantLock.Parameters.AddWithValue("tenant",tenant);Assert.Equal(1,await tenantLock.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),new TenantManagementCommandV1("remnant-"+operation,active.Revision,"suspend",null)));
                try
                {
                    pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);pending=Send("B");
                    commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"remnant-"+operation,pending);
                }
                finally{await held.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("remnant-"+operation+"-pause.json",new{Operation=operation,AuthorityWait=commandWait.Blocker==pauseWait.Backend,pauseWait,commandWait,
            Status=(int)denied.StatusCode,Before=before,After=await RemnantWriteSet(h)});
        Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await RemnantWriteSet(h));
        using var deniedAgain=await Send("A");Assert.Equal(HttpStatusCode.Unauthorized,deniedAgain.StatusCode);Assert.Equal(before,await RemnantWriteSet(h));
        using var reactivated=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),new TenantManagementCommandV1("remnant-reactivate-"+operation,suspended.Snapshot.Revision,"reactivate",null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(reactivated)).Snapshot.Enable);actor=await Login(host,h);
        using var recovered=await Send("B");Assert.True(recovered.StatusCode==HttpStatusCode.OK,await OperationsFailure(recovered));Assert.Equal(stock,await PalletStockRows(h));

        Task<HttpResponseMessage> Send(string suffix)
        {
            const string path="api/wms/remnant";
            if(operation=="list")return host.Send(OperationsRequest(actor,HttpMethod.Get,path+"?materialType=PAPER&warehouseCd=W1"));
            if(operation=="detail")return host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/"+RemnantNo(h,suffix)));
            if(operation=="match")return host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/match?materialType=PAPER&minWidthMm=100&minLengthMm=100"));
            if(operation=="create")return host.Send(OperationsRequest(actor,HttpMethod.Post,path,RemnantInput()));
            if(operation=="update")return host.Send(OperationsRequest(actor,HttpMethod.Put,path+"/"+RemnantNo(h,suffix),RemnantInput()));
            if(operation=="delete")return host.Send(OperationsRequest(actor,HttpMethod.Delete,path+"/"+RemnantNo(h,suffix)));
            return host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/"+RemnantNo(h,suffix)+"/"+operation,operation=="reserve"?new{ReservedFor="SIM-PURPOSE"}:null));
        }
    }

    [TenantManagementPostgresTheory]
    [InlineData("list")][InlineData("detail")][InlineData("match")]
    public async Task Remnant_reads_require_the_current_view_action(string kind)
    {
        await using var h=await RemnantSeed("list");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="wms-remnant").Select(m=>m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a=>a.RoleId==72&&a.MenuId==menu&&a.ActionCode=="view").ExecuteDeleteAsync();
        var actor=await Login(host,h);var before=await RemnantWriteSet(h);
        using var denied=await host.Send(OperationsRequest(actor,HttpMethod.Get,"api/wms/remnant"+(kind=="list"?"":kind=="match"?"/match?materialType=PAPER&minWidthMm=100&minLengthMm=100":"/"+RemnantNo(h,"A"))));
        await DeviceEvidence("remnant-"+kind+"-action.json",new{Status=(int)denied.StatusCode,Before=before,After=await RemnantWriteSet(h)});
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(before,await RemnantWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Remnant_current_action_revocation_wins_before_create()
    {
        await using var h=await RemnantSeed("create");var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="wms-remnant").Select(m=>m.MenuId).SingleAsync();
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        var before=await RemnantWriteSet(h);Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_RemnantMaterial\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var revoked=await control.BeginTransactionAsync();await using var change=new NpgsqlCommand("""
                    SELECT cp6_authentication_v1.acquire(); DELETE FROM public."Sys_RoleAction" WHERE "TenantId"=@tenant AND "RoleId"=72 AND "MenuId"=@menu AND "ActionCode"='add'
                    """,control,revoked);change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("menu",menu);await change.ExecuteNonQueryAsync();
                pending=host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/remnant",RemnantInput()));
                wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"remnant-revoke",pending);await revoked.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("remnant-action-revoke.json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,Wait=wait,Before=before,After=await RemnantWriteSet(h)});
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(before,await RemnantWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Remnant_business_commit_first_keeps_history_before_pause()
    {
        await using var h=await RemnantSeed("create");await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> writing,pause;FenceWait businessWait;FenceWait? pauseWait=null;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_RemnantMaterial\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                writing=host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/remnant",RemnantInput()));businessWait=await SerialNativeWait(observer,business.ProcessID,business.ProcessID,"remnant-business",writing);
                pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("remnant-business-first",active.Revision,"suspend",null)));
                try{pauseWait=await SerialNativeWait(observer,businessWait.Backend,business.ProcessID,"remnant-pause-after-business",pause);}
                catch(Xunit.Sdk.XunitException) when(pause.IsCompletedSuccessfully){ }
            }
            finally{await locked.RollbackAsync();}
        }
        using var committed=await writing;using var paused=await pause;var receipt=await NumberHttpData<TenantManagementReceiptV1>(paused);
        await DeviceEvidence("remnant-business-first.json",new{BusinessStatus=(int)committed.StatusCode,AuthorityWait=pauseWait?.Blocker==businessWait.Backend,businessWait,pauseWait,receipt.CommittedAt});
        Assert.Equal(HttpStatusCode.OK,committed.StatusCode);Assert.False(receipt.Snapshot.Enable);Assert.NotNull(pauseWait);Assert.Equal(businessWait.Backend,pauseWait!.Blocker);
        Assert.Single(await h.Db.RemnantMaterials.ToListAsync());
    }

}
