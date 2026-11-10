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
    private async Task<Harness> RollSeed(string operation)
    {
        var h=await Seed();var db=h.Db;
        if(!await db.Sys_Menus.AnyAsync(m=>m.MenuKey=="wms-paper-roll"))db.Sys_Menus.Add(new(){MenuId=441,MenuKey="wms-paper-roll",MenuName="Legacy paper roll"});
        await db.SaveChangesAsync();var menu=await db.Sys_Menus.Where(m=>m.MenuKey=="wms-paper-roll").Select(m=>m.MenuId).SingleAsync();
        foreach(var action in new[]{"view","add","consume","slit","dispose"})
        {
            if(!await db.Sys_MenuActions.AnyAsync(a=>a.MenuId==menu&&a.ActionCode==action))db.Sys_MenuActions.Add(new(){MenuId=menu,ActionCode=action,ActionName=action});
            db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu,ActionCode=action});
        }
        db.Stocks.Add(new(){WarehouseCd="W1",LocationCd="SRC",ProductCd="UNRELATED",LotNo="LOT",PhysicalQty=9.12345678m,AvailableQty=9.12345678m,UnitPrice=12.3456m});
        if(operation!="create")foreach(var suffix in new[]{"A","B"})db.PaperRolls.Add(new(){RollNo=RollNo(h,suffix),PaperGrade="SIM-GRADE",WidthMm=300,BasisWeight=210.12m,
            GrainDirection="T",OriginalLengthM=10.12345678m,RemainingLengthM=10.12345678m,CoreDiameterInch=3m,WarehouseCd="W1",LocationCd="SRC",Status=PaperRollStatus.InStock,Creator="price-owner"});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();return h;
    }
    private static string RollNo(Harness h,string suffix)=>"RL"+h.Tenant.ToString("N")[..12]+suffix;
    private static PaperRollDto RollInput()=>new(){PaperGrade="SIM-GRADE",WidthMm=300,BasisWeight=210.12m,GrainDirection="T",OriginalLengthM=10.12345678m,
        CoreDiameterInch=0,WarehouseCd="W1",LocationCd="SRC",DisposeThresholdM=2.12345678m,MfgLotNo="SIMULATED-LOT",SupplierRollNo="SIMULATED-SUPPLIER",Remarks="legacy simulated metadata"};

    [TenantManagementPostgresTheory]
    [InlineData("create")][InlineData("consume")][InlineData("slit")][InlineData("dispose")][InlineData("list")][InlineData("detail")][InlineData("match")][InlineData("match")]
    public async Task Roll_existing_entries_decide_current_authority(string operation)
    {
        await using var h=await RollSeed(operation);await using var administrator=await TenantManagementSeed();var db=h.Db;var tenant=h.Tenant;var connection=db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);
        var stock=await PalletStockRows(h);
        using var normal=await Send("A");Assert.True(normal.StatusCode==HttpStatusCode.OK,await OperationsFailure(normal));
        Assert.Equal(stock,await PalletStockRows(h));
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var target=new NpgsqlConnection(connection);await target.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        FenceWait pauseWait,commandWait;Task<HttpResponseMessage> pending;TenantManagementReceiptV1 suspended;var before=await RollWriteSet(h);
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_PaperRoll\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var held=await target.BeginTransactionAsync();await using var tenantLock=new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""",target,held);
                tenantLock.Parameters.AddWithValue("tenant",tenant);Assert.Equal(1,await tenantLock.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),new TenantManagementCommandV1("roll-"+operation,active.Revision,"suspend",null)));
                try
                {
                    pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);pending=Send("B");
                    commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"roll-"+operation,pending);
                }
                finally{await held.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("roll-"+operation+"-pause.json",new{Operation=operation,AuthorityWait=commandWait.Blocker==pauseWait.Backend,pauseWait,commandWait,
            Status=(int)denied.StatusCode,Before=before,After=await RollWriteSet(h)});
        Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await RollWriteSet(h));
        using var deniedAgain=await Send("A");Assert.Equal(HttpStatusCode.Unauthorized,deniedAgain.StatusCode);Assert.Equal(before,await RollWriteSet(h));
        using var reactivated=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),new TenantManagementCommandV1("roll-reactivate-"+operation,suspended.Snapshot.Revision,"reactivate",null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(reactivated)).Snapshot.Enable);actor=await Login(host,h);
        using var recovered=await Send("B");Assert.True(recovered.StatusCode==HttpStatusCode.OK,await OperationsFailure(recovered));Assert.Equal(stock,await PalletStockRows(h));

        Task<HttpResponseMessage> Send(string suffix)
        {
            const string path="api/wms/paper-roll";
            if(operation=="list")return host.Send(OperationsRequest(actor,HttpMethod.Get,path+"?paperGrade=SIM-GRADE"));
            if(operation=="detail")return host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/"+RollNo(h,suffix)));
            if(operation=="match")return host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/match?paperGrade=SIM-GRADE&widthMm=100&grainDirection=T&requiredLengthM=1"));
            if(operation=="create")return host.Send(OperationsRequest(actor,HttpMethod.Post,path,RollInput()));
            if(operation=="slit")return host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/slit",new SlitRequest{ParentRollNo=RollNo(h,suffix),ChildWidths=new(){100,120},KeepRemnant=true}));
            return host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/"+RollNo(h,suffix)+"/"+operation,operation=="consume"?new{LengthM=1.00000001m}:null));
        }
    }

    [TenantManagementPostgresTheory]
    [InlineData("list")][InlineData("detail")][InlineData("match")]
    public async Task Roll_reads_require_the_current_view_action(string kind)
    {
        await using var h=await RollSeed("list");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="wms-paper-roll").Select(m=>m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a=>a.RoleId==72&&a.MenuId==menu&&a.ActionCode=="view").ExecuteDeleteAsync();
        var actor=await Login(host,h);var before=await RollWriteSet(h);
        using var denied=await host.Send(OperationsRequest(actor,HttpMethod.Get,"api/wms/paper-roll"+(kind=="list"?"":kind=="match"?"/match?paperGrade=SIM-GRADE&widthMm=100&grainDirection=T&requiredLengthM=1":"/"+RollNo(h,"A"))));
        await DeviceEvidence("roll-"+kind+"-action.json",new{Status=(int)denied.StatusCode,Before=before,After=await RollWriteSet(h)});
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(before,await RollWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Roll_current_action_revocation_wins_before_create()
    {
        await using var h=await RollSeed("create");var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="wms-paper-roll").Select(m=>m.MenuId).SingleAsync();
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        var before=await RollWriteSet(h);Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_PaperRoll\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var revoked=await control.BeginTransactionAsync();await using var change=new NpgsqlCommand("""
                    SELECT cp6_authentication_v1.acquire(); DELETE FROM public."Sys_RoleAction" WHERE "TenantId"=@tenant AND "RoleId"=72 AND "MenuId"=@menu AND "ActionCode"='add'
                    """,control,revoked);change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("menu",menu);await change.ExecuteNonQueryAsync();
                pending=host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/paper-roll",RollInput()));
                wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"roll-revoke",pending);await revoked.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("roll-action-revoke.json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,Wait=wait,Before=before,After=await RollWriteSet(h)});
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(before,await RollWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Roll_business_commit_first_keeps_history_before_pause()
    {
        await using var h=await RollSeed("create");await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> writing,pause;FenceWait businessWait;FenceWait? pauseWait=null;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_PaperRoll\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                writing=host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/paper-roll",RollInput()));businessWait=await SerialNativeWait(observer,business.ProcessID,business.ProcessID,"roll-business",writing);
                pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("roll-business-first",active.Revision,"suspend",null)));
                try{pauseWait=await SerialNativeWait(observer,businessWait.Backend,business.ProcessID,"roll-pause-after-business",pause);}
                catch(Xunit.Sdk.XunitException) when(pause.IsCompletedSuccessfully){ }
            }
            finally{await locked.RollbackAsync();}
        }
        using var committed=await writing;using var paused=await pause;var receipt=await NumberHttpData<TenantManagementReceiptV1>(paused);
        await DeviceEvidence("roll-business-first.json",new{BusinessStatus=(int)committed.StatusCode,AuthorityWait=pauseWait?.Blocker==businessWait.Backend,businessWait,pauseWait,receipt.CommittedAt});
        Assert.Equal(HttpStatusCode.OK,committed.StatusCode);Assert.False(receipt.Snapshot.Enable);Assert.NotNull(pauseWait);Assert.Equal(businessWait.Backend,pauseWait!.Blocker);
        Assert.Single(await h.Db.PaperRolls.ToListAsync());
    }

}
