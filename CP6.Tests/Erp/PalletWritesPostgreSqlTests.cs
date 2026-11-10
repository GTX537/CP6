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
    private async Task<Harness> PalletSeed(string operation)
    {
        var h=await Seed();var db=h.Db;
        if(!await db.Sys_Menus.AnyAsync(m=>m.MenuId==445))db.Sys_Menus.Add(new(){MenuId=445,MenuKey="wms-pallet",MenuName="Pallet"});
        foreach(var action in new[]{"add","edit","complete","move","ship","del"})
        {
            if(!await db.Sys_MenuActions.AnyAsync(a=>a.MenuId==445&&a.ActionCode==action))
                db.Sys_MenuActions.Add(new(){MenuId=445,ActionCode=action,ActionName=action});
            db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=445,ActionCode=action});
        }
        db.WmsRoleScopes.Add(new(){RoleId=72,WarehouseCd="W1"});
        db.Warehouses.Add(new(){WarehouseCd="W1",WarehouseName="Simulated pallet metadata"});
        db.Locations.AddRange(new(){WarehouseCd="W1",LocationCd="SRC",AreaCd="SOURCE"},new(){WarehouseCd="W1",LocationCd="DST",AreaCd="DESTINATION"});
        db.Stocks.Add(new(){WarehouseCd="W1",LocationCd="SRC",ProductCd="UNRELATED",LotNo="LOT",PhysicalQty=9.12345678m,AvailableQty=9.12345678m,UnitPrice=12.3456m});
        if(operation!="create")
            foreach(var suffix in new[]{"A","B"})
                db.Pallets.Add(new(){PalletNo=PalletNumber(h,suffix),ProductCd="META-P",ProductName="Original",LotNo="META-LOT",CartonQty=5,WeightKg=12.345m,
                    HeightMm=1200,MaxStackLayers=3,WarehouseCd="W1",LocationCd="SRC",Creator="price-owner",Remarks="original",
                    Status=operation=="move"?PalletStatus.InStock:operation=="ship"?PalletStatus.WaitingShip:PalletStatus.Building});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();return h;
    }
    private static string PalletNumber(Harness h,string suffix)=>"PT"+h.Tenant.ToString("N")[..12]+suffix;
    private static PalletDto PalletInput(string location="SRC")=>new(){ProductCd="META-P",ProductName="Updated",LotNo="META-LOT",CartonQty=7,
        WeightKg=23.456m,HeightMm=1300,MaxStackLayers=4,WarehouseCd="W1",LocationCd=location,Remarks="updated"};

    [TenantManagementPostgresTheory]
    [InlineData("create")][InlineData("update")][InlineData("complete")][InlineData("move")][InlineData("ship")][InlineData("delete")]
    public async Task Pallet_metadata_writes_require_current_authority(string operation)
    {
        await using var h=await PalletSeed(operation);await using var administrator=await TenantManagementSeed();
        var db=h.Db;var tenant=h.Tenant;var connection=db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);
        var writer=await Login(host,h);var manager=await Login(host,administrator);
        var number=PalletNumber(h,"A");var pendingNumber=PalletNumber(h,"B");
        var initialStock=await PalletStockRows(h);
        using var normal=await Send(number);Assert.True(normal.StatusCode==HttpStatusCode.OK,await OperationsFailure(normal));
        if(operation=="create")
        {
            using var result=JsonDocument.Parse(await normal.Content.ReadAsStringAsync());
            number=result.RootElement.GetProperty("data").GetProperty("palletNo").GetString()!;
        }
        var row=await db.Pallets.IgnoreQueryFilters().AsNoTracking().SingleAsync(p=>p.TenantId==tenant&&p.PalletNo==number);
        Assert.Equal("price-owner",operation=="create"?row.Creator:row.Modifier);
        if(operation is "create" or "update"){Assert.Equal(23.456m,row.WeightKg);Assert.Equal(7,row.CartonQty);}
        if(operation=="delete")Assert.True(row.IsDeleted);
        else Assert.Equal(operation switch{"complete"=>PalletStatus.InStock,"move"=>PalletStatus.WaitingShip,"ship"=>PalletStatus.Shipped,_=>PalletStatus.Building},row.Status);
        if(operation is "move" or "update")Assert.Equal("DST",row.LocationCd);
        if(operation=="ship")Assert.Equal("OUT-METADATA",row.ShippedOutboundNo);
        Assert.Equal(initialStock,await PalletStockRows(h));

        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(tenant)));
        var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();
        await using var target=new NpgsqlConnection(connection);await target.OpenAsync();
        await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        FenceWait pauseWait,commandWait;Task<HttpResponseMessage> pending;TenantManagementReceiptV1 suspended;
        var before=await PalletWriteSet(h);
        await using(var businessLock=await business.BeginTransactionAsync())
        {
            await using var lockBusiness=new NpgsqlCommand("LOCK TABLE public.\"T_Pallet\" IN ACCESS EXCLUSIVE MODE",business,businessLock);await lockBusiness.ExecuteNonQueryAsync();
            try
            {
                await using var tenantLock=await target.BeginTransactionAsync();
                await using var lockTenant=new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""",target,tenantLock);
                lockTenant.Parameters.AddWithValue("tenant",tenant);Assert.Equal(1,await lockTenant.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),new TenantManagementCommandV1("pallet-"+operation,active.Revision,"suspend",null)));
                try
                {
                    pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);
                    pending=Send(pendingNumber);
                    commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"pallet-"+operation,pending);
                }
                finally{await tenantLock.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await businessLock.RollbackAsync();}
        }
        using var denied=await pending;
        await DeviceEvidence("pallet-"+operation+"-pause.json",new{Operation=operation,Tenant=tenant,pauseWait,commandWait,suspended.CommittedAt,
            AuthorityWait=commandWait.Blocker==pauseWait.Backend,Status=(int)denied.StatusCode,Before=before,After=await PalletWriteSet(h)});
        Assert.True(denied.StatusCode==HttpStatusCode.Unauthorized,await OperationsFailure(denied));
        Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await PalletWriteSet(h));
        using var deniedAgain=await Send(pendingNumber);Assert.Equal(HttpStatusCode.Unauthorized,deniedAgain.StatusCode);Assert.Equal(before,await PalletWriteSet(h));
        using var reactivated=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),
            new TenantManagementCommandV1("pallet-reactivate-"+operation,suspended.Snapshot.Revision,"reactivate",null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(reactivated)).Snapshot.Enable);writer=await Login(host,h);
        await db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(g=>g.AreaCd,"NONE"));writer=await Login(host,h);
        await Reject(()=>Send(pendingNumber),HttpStatusCode.Forbidden,"FORBIDDEN");
        if(operation is "update" or "move")
        {
            foreach(var area in new[]{"SOURCE","DESTINATION"})
            {
                await db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(g=>g.AreaCd,area));writer=await Login(host,h);
                await Reject(()=>Send(pendingNumber),HttpStatusCode.Forbidden,"FORBIDDEN");
                if(area=="SOURCE")
                {
                    await db.Locations.Where(l=>l.LocationCd=="DST").ExecuteUpdateAsync(s=>s.SetProperty(l=>l.IsBlocked,true));
                    await Reject(()=>Send(pendingNumber),HttpStatusCode.Forbidden,"FORBIDDEN");
                    await db.Locations.Where(l=>l.LocationCd=="DST").ExecuteUpdateAsync(s=>s.SetProperty(l=>l.IsBlocked,false));
                }
            }
        }
        await db.WmsRoleScopes.ExecuteUpdateAsync(s=>s.SetProperty(g=>g.AreaCd,""));writer=await Login(host,h);
        if(operation is "create" or "update")
        {
            db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="wms-pallet",FieldName="Remarks",Access=2});await db.SaveChangesAsync();db.ChangeTracker.Clear();writer=await Login(host,h);
            await Reject(()=>Send(pendingNumber),HttpStatusCode.Forbidden,"FIELD_WRITE_FORBIDDEN");
            if(operation=="update")
            {
                using var unchanged=await Send(number);Assert.True(unchanged.StatusCode==HttpStatusCode.OK,await OperationsFailure(unchanged));
            }
            await db.Sys_RoleFieldPerms.Where(p=>p.ResourceKey=="wms-pallet").ExecuteDeleteAsync();writer=await Login(host,h);
        }
        if(operation is "update" or "move")
        {
            await db.Locations.Where(l=>l.LocationCd=="DST").ExecuteUpdateAsync(s=>s.SetProperty(l=>l.IsBlocked,true));
            await Reject(()=>Send(pendingNumber),HttpStatusCode.Conflict,"LOCATION_OPERATION_BLOCKED");
            await db.Locations.Where(l=>l.LocationCd=="DST").ExecuteUpdateAsync(s=>s.SetProperty(l=>l.IsBlocked,false));
        }
        using var recovered=await Send(pendingNumber);Assert.True(recovered.StatusCode==HttpStatusCode.OK,await OperationsFailure(recovered));
        Assert.Equal(initialStock,await PalletStockRows(h));
        if(operation=="complete")await Reject(()=>Send(pendingNumber),HttpStatusCode.BadRequest,"WM-MSG-043");
        if(operation=="move")await Reject(()=>Send(pendingNumber),HttpStatusCode.BadRequest,"WM-MSG-043");
        if(operation=="ship")
        {
            await Reject(()=>host.Send(OperationsRequest(writer,HttpMethod.Delete,"api/wms/pallet/"+pendingNumber)),HttpStatusCode.BadRequest,"WM-MSG-043");
            await Reject(()=>host.Send(OperationsRequest(writer,HttpMethod.Put,"api/wms/pallet/"+pendingNumber,PalletInput())),HttpStatusCode.BadRequest,"WM-MSG-043");
        }
        if(operation=="delete")await Reject(()=>Send(pendingNumber),HttpStatusCode.BadRequest,"WM-MSG-070");
        if(operation=="create")
        {
            var invalid=PalletInput();invalid.CartonQty=0;
            await Reject(()=>host.Send(OperationsRequest(writer,HttpMethod.Post,"api/wms/pallet",invalid)),HttpStatusCode.BadRequest,"CartonQty");
        }

        Task<HttpResponseMessage> Send(string no)=>host.Send(OperationsRequest(writer,operation=="create"?HttpMethod.Post:operation=="update"?HttpMethod.Put:operation=="delete"?HttpMethod.Delete:HttpMethod.Post,
            "api/wms/pallet"+(operation=="create"?"":"/"+no)+(operation switch{"complete"=>"/complete-building","move"=>"/move-to-shipping","ship"=>"/mark-shipped",_=>""}),
            operation is "create" or "update"?PalletInput(operation=="update"?"DST":"SRC"):operation=="move"?new{ToLocationCd="DST"}:operation=="ship"?new{OutboundNo="OUT-METADATA"}:null));
        async Task Reject(Func<Task<HttpResponseMessage>> request,HttpStatusCode status,string error)
        {
            var original=await PalletWriteSet(h);using var response=await request();Assert.True(response.StatusCode==status,await OperationsFailure(response));
            Assert.Contains(error,await response.Content.ReadAsStringAsync());Assert.Equal(original,await PalletWriteSet(h));
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Pallet_create_failure_rolls_back_number_and_record_before_logging()
    {
        await using var h=await PalletSeed("create");var db=h.Db;
        db.WmsSequences.Add(new(){Prefix="PLT",DateKey="*",NextNo=100});await db.SaveChangesAsync();db.ChangeTracker.Clear();
        await using var host=await AuthenticationApiProcess.Start(db.Database.GetConnectionString()!);var writer=await Login(host,h);
        await using var fault=new NpgsqlConnection(db.Database.GetConnectionString());await fault.OpenAsync();
        await using var install=new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_pallet_insert() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN RAISE EXCEPTION 'simulated pallet insert failure after numbering'; END $fault$;
            CREATE TRIGGER cp6_test_pallet_insert BEFORE INSERT ON public."T_Pallet" FOR EACH ROW
            WHEN (NEW."TenantId"='{{h.Tenant}}'::uuid) EXECUTE FUNCTION public.cp6_test_pallet_insert();
            """,fault);await install.ExecuteNonQueryAsync();
        var before=await PalletWriteSet(h);
        try
        {
            using var failed=await host.Send(OperationsRequest(writer,HttpMethod.Post,"api/wms/pallet",PalletInput()));
            await DeviceEvidence("pallet-create-fault.json",new{Status=(int)failed.StatusCode,Before=before,After=await PalletWriteSet(h)});
            Assert.True(failed.StatusCode==HttpStatusCode.ServiceUnavailable,await OperationsFailure(failed));Assert.Equal(before,await PalletWriteSet(h));
        }
        finally
        {
            await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_pallet_insert ON public.\"T_Pallet\"; DROP FUNCTION public.cp6_test_pallet_insert()",fault);await remove.ExecuteNonQueryAsync();
        }
        using var restored=await host.Send(OperationsRequest(writer,HttpMethod.Post,"api/wms/pallet",PalletInput()));
        Assert.True(restored.StatusCode==HttpStatusCode.OK,await OperationsFailure(restored));
        Assert.Equal(101,await db.WmsSequences.Where(s=>s.Prefix=="PLT").Select(s=>s.NextNo).SingleAsync());
        Assert.Single(await db.Pallets.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Pallet_rechecks_action_after_actual_native_revocation_commits()
    {
        await using var h=await PalletSeed("update");var connection=h.Db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var writer=await Login(host,h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();
        await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        var before=await PalletWriteSet(h);Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var businessLock=await business.BeginTransactionAsync())
        {
            await using var fence=new NpgsqlCommand("LOCK TABLE public.\"T_Pallet\" IN ACCESS EXCLUSIVE MODE",business,businessLock);await fence.ExecuteNonQueryAsync();
            try
            {
                await using var revoked=await control.BeginTransactionAsync();
                await using var change=new NpgsqlCommand("""
                    SELECT cp6_authentication_v1.acquire(); DELETE FROM public."Sys_RoleAction"
                    WHERE "TenantId"=@tenant AND "RoleId"=72 AND "MenuId"=445 AND "ActionCode"='edit'
                    """,control,revoked);change.Parameters.AddWithValue("tenant",h.Tenant);await change.ExecuteNonQueryAsync();
                pending=host.Send(OperationsRequest(writer,HttpMethod.Put,"api/wms/pallet/"+PalletNumber(h,"A"),PalletInput("DST")));
                wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"pallet-revoke",pending);
                await revoked.CommitAsync();
            }
            finally{await businessLock.RollbackAsync();}
        }
        using var denied=await pending;
        await DeviceEvidence("pallet-revoke.json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,Wait=wait,Before=before,After=await PalletWriteSet(h)});
        Assert.True(denied.StatusCode==HttpStatusCode.Forbidden,await OperationsFailure(denied));Assert.Equal(before,await PalletWriteSet(h));
    }

    private static Task<string> PalletStockRows(Harness h)=>h.Db.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object('Stock',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_Stock" s WHERE s."TenantId"={h.Tenant}),
          'Transactions',(SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t."Id"),'[]') FROM public."T_StockTransaction" t WHERE t."TenantId"={h.Tenant}),
          'Outbound',(SELECT coalesce(jsonb_agg(to_jsonb(o) ORDER BY o."Id"),'[]') FROM public."T_OutboundOrder" o WHERE o."TenantId"={h.Tenant}))::text AS "Value"
        """).SingleAsync();
    private static Task<string> PalletWriteSet(Harness h)=>h.Db.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object('Pallets',(SELECT coalesce(jsonb_agg(to_jsonb(p) ORDER BY p."Id"),'[]') FROM public."T_Pallet" p WHERE p."TenantId"={h.Tenant}),
          'Sequences',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_WmsSequence" s WHERE s."TenantId"={h.Tenant}),
          'Stock',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_Stock" s WHERE s."TenantId"={h.Tenant}),
          'StockTransactions',(SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t."Id"),'[]') FROM public."T_StockTransaction" t WHERE t."TenantId"={h.Tenant}),
          'Outbound',(SELECT coalesce(jsonb_agg(to_jsonb(o) ORDER BY o."Id"),'[]') FROM public."T_OutboundOrder" o WHERE o."TenantId"={h.Tenant}))::text AS "Value"
        """).SingleAsync();
}
