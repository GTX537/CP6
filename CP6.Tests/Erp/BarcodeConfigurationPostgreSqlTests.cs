using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<Harness> BarcodeSeed()
    {
        var h=await Seed();var db=h.Db;
        if(!await db.Sys_Menus.AnyAsync(m=>m.MenuId==461))db.Sys_Menus.Add(new(){MenuId=461,MenuKey="wms-mobile",MenuName="Mobile"});
        foreach(var action in new[]{"barcode-manage","scan"})
        {
            if(!await db.Sys_MenuActions.AnyAsync(a=>a.MenuId==461&&a.ActionCode==action))
                db.Sys_MenuActions.Add(new(){MenuId=461,ActionCode=action,ActionName=action});
            db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=461,ActionCode=action});
        }
        db.Stocks.Add(new(){WarehouseCd="W1",LocationCd="SRC",ProductCd="UNRELATED",LotNo="LOT",PhysicalQty=9.12345678m,AvailableQty=9.12345678m,UnitPrice=12.3456m});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();return h;
    }
    private static string BarcodeName(Harness h,string suffix)=>"BC"+h.Tenant.ToString("N")+suffix;
    private static UpsertBarcodeAliasRequest BarcodeAliasInput(Harness h,string suffix)=>new(){Barcode=BarcodeName(h,suffix),BarcodeType="PRODUCT",
        TargetKey="SIMULATED-TARGET",ProductCd="SIMULATED",ConversionRate=1.23456789m};
    private static UpsertBarcodeProfileRequest BarcodeProfileInput(Harness h,string suffix)=>new(){ProfileName=BarcodeName(h,suffix),Format="custom",
        Pattern="^(?<product>[A-Z0-9]+)$",MappingJson="{\"ProductCd\":\"product\"}",Priority=1};
    private static HttpRequestMessage BarcodeWorkbookRequest(Dictionary<string,string> cookies,Harness h,string suffix,bool commit,bool invalid=false)
    {
        using var workbook=new XLWorkbook();var sheet=workbook.Worksheets.Add("Aliases");
        foreach(var (value,column) in new[]{("Barcode",1),("Type",2),("Target",3),("Product",4),("Conversion",8)})sheet.Cell(1,column).Value=value;
        for(var i=0;i<2;i++)
        {
            sheet.Cell(i+2,1).Value=BarcodeName(h,suffix+i);sheet.Cell(i+2,2).Value=invalid&&i==1?"INVALID":"PRODUCT";
            sheet.Cell(i+2,3).Value="SIMULATED-TARGET";sheet.Cell(i+2,4).Value="SIMULATED";sheet.Cell(i+2,8).Value="1.23456789";
        }
        using var bytes=new MemoryStream();workbook.SaveAs(bytes);
        var request=OperationsRequest(cookies,HttpMethod.Post,"api/v2/wms/barcodes/import?commit="+(commit?"true":"false"));
        var form=new MultipartFormDataContent();form.Add(new ByteArrayContent(bytes.ToArray()),"file","aliases.xlsx");request.Content=form;return request;
    }

    [TenantManagementPostgresTheory]
    [InlineData("profile")][InlineData("alias")][InlineData("import")]
    public async Task Barcode_writes_require_current_authority(string operation)
    {
        await using var h=await BarcodeSeed();await using var administrator=await TenantManagementSeed();var db=h.Db;
        var tenant=h.Tenant;var connection=db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var writer=await Login(host,h);var manager=await Login(host,administrator);
        var stock=await PalletStockRows(h);
        if(operation=="import")
        {
            var original=await BarcodeWriteSet(h);using var preview=await host.Send(BarcodeWorkbookRequest(writer,h,"A",false));
            var result=await BarcodeResponse<BarcodeImportResult>(preview);Assert.False(result.Committed);Assert.Equal(2,result.ValidCount);Assert.Equal(original,await BarcodeWriteSet(h));
        }
        using var normal=await Send("A");Assert.True(normal.StatusCode==HttpStatusCode.OK,await OperationsFailure(normal));
        if(operation=="profile")
        {
            var row=await BarcodeResponse<BarcodeProfileDto>(normal);var update=BarcodeProfileInput(h,"A");update.Id=row.Id;update.RowVersion=row.RowVersion;update.Priority=2;
            using var changed=await host.Send(OperationsRequest(writer,HttpMethod.Post,"api/v2/wms/barcode-profiles",update));
            Assert.Equal(2,(await BarcodeResponse<BarcodeProfileDto>(changed)).Priority);
            using var parsed=await host.Send(OperationsRequest(writer,HttpMethod.Post,"api/v2/wms/barcode-profiles/parse",new ParseCompoundBarcodeRequest{RawBarcode="QRA"}));
            var value=await BarcodeResponse<CompoundBarcodeResult>(parsed);Assert.True(value.Matched);Assert.Equal("QRA",value.Values["ProductCd"]);
            Assert.Equal("price-owner",await db.BarcodeProfiles.Where(p=>p.Id==row.Id).Select(p=>p.Modifier).SingleAsync());
        }
        else if(operation=="alias")
        {
            var row=await BarcodeResponse<BarcodeAliasDto>(normal);var update=BarcodeAliasInput(h,"A");update.RowVersion=row.RowVersion;update.TargetKey="SIMULATED-UPDATED";
            using var changed=await host.Send(OperationsRequest(writer,HttpMethod.Post,"api/v2/wms/barcodes",update));
            Assert.Equal("SIMULATED-UPDATED",(await BarcodeResponse<BarcodeAliasDto>(changed)).TargetKey);
            Assert.Equal("price-owner",await db.BarcodeAliases.Where(p=>p.Barcode==row.Barcode).Select(p=>p.Modifier).SingleAsync());
        }
        else{var imported=await BarcodeResponse<BarcodeImportResult>(normal);Assert.True(imported.Committed);Assert.Equal(2,imported.ValidCount);}
        await CheckRows("A");Assert.Equal(stock,await PalletStockRows(h));
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(tenant)));
        var active=await NumberHttpData<TenantManagementSnapshotV1>(current);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();
        await using var target=new NpgsqlConnection(connection);await target.OpenAsync();
        await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        FenceWait pauseWait,commandWait;Task<HttpResponseMessage> pending;TenantManagementReceiptV1 suspended;var before=await BarcodeWriteSet(h);
        await using(var locked=await business.BeginTransactionAsync())
        {
            var table=operation=="profile"?"T_BarcodeProfile":"T_BarcodeAlias";
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\""+table+"\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var held=await target.BeginTransactionAsync();
                await using var tenantLock=new NpgsqlCommand("""SELECT 1 FROM public."Sys_Tenants" WHERE "Id"=@tenant FOR UPDATE""",target,held);
                tenantLock.Parameters.AddWithValue("tenant",tenant);Assert.Equal(1,await tenantLock.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),new TenantManagementCommandV1("barcode-"+operation,active.Revision,"suspend",null)));
                try
                {
                    pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);
                    pending=Send("B");commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"barcode-"+operation,pending);
                }
                finally{await held.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;
        await DeviceEvidence("barcode-"+operation+"-pause.json",new{Operation=operation,Tenant=tenant,pauseWait,commandWait,suspended.CommittedAt,
            AuthorityWait=commandWait.Blocker==pauseWait.Backend,Status=(int)denied.StatusCode,Before=before,After=await BarcodeWriteSet(h)});
        Assert.True(denied.StatusCode==HttpStatusCode.Unauthorized,await OperationsFailure(denied));Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await BarcodeWriteSet(h));
        using var deniedAgain=await Send("B");Assert.Equal(HttpStatusCode.Unauthorized,deniedAgain.StatusCode);Assert.Equal(before,await BarcodeWriteSet(h));
        using var reactivated=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(tenant,"/commands"),new TenantManagementCommandV1("barcode-reactivate-"+operation,suspended.Snapshot.Revision,"reactivate",null)));
        Assert.True((await NumberHttpData<TenantManagementReceiptV1>(reactivated)).Snapshot.Enable);writer=await Login(host,h);
        using var restored=await Send("B");Assert.True(restored.StatusCode==HttpStatusCode.OK,await OperationsFailure(restored));await CheckRows("B");Assert.Equal(stock,await PalletStockRows(h));
        var unchanged=await BarcodeWriteSet(h);
        if(operation=="profile")
        {
            var invalid=BarcodeProfileInput(h,"INVALID");invalid.MappingJson="{}";
            using var invalidResult=await host.Send(OperationsRequest(writer,HttpMethod.Post,"api/v2/wms/barcode-profiles",invalid));Assert.Equal(HttpStatusCode.BadRequest,invalidResult.StatusCode);
            Assert.Contains("WM-BARCODE-PROFILE-SYNTAX",await invalidResult.Content.ReadAsStringAsync());
        }
        else if(operation=="alias")
        {
            var invalid=BarcodeAliasInput(h,"INVALID");invalid.ConversionRate=0;
            using var invalidResult=await host.Send(OperationsRequest(writer,HttpMethod.Post,"api/v2/wms/barcodes",invalid));Assert.Equal(HttpStatusCode.BadRequest,invalidResult.StatusCode);
            Assert.Contains("WM-BARCODE-CONVERSION-INVALID",await invalidResult.Content.ReadAsStringAsync());
        }
        else
        {
            using var invalidResult=await host.Send(BarcodeWorkbookRequest(writer,h,"INVALID",true,true));var invalid=await BarcodeResponse<BarcodeImportResult>(invalidResult);
            Assert.False(invalid.Committed);Assert.Equal(1,invalid.ValidCount);Assert.Equal(1,invalid.InvalidCount);Assert.Contains(invalid.Rows,r=>r.ErrorCode=="WM-BARCODE-TYPE-INVALID");
        }
        Assert.Equal(unchanged,await BarcodeWriteSet(h));

        Task<HttpResponseMessage> Send(string suffix)=>operation=="import"?host.Send(BarcodeWorkbookRequest(writer,h,suffix,true)):
            host.Send(OperationsRequest(writer,HttpMethod.Post,operation=="profile"?"api/v2/wms/barcode-profiles":"api/v2/wms/barcodes",
                operation=="profile"?(object)BarcodeProfileInput(h,suffix):BarcodeAliasInput(h,suffix)));
        async Task CheckRows(string suffix)
        {
            if(operation=="profile")Assert.Equal("price-owner",await db.BarcodeProfiles.Where(p=>p.ProfileName==BarcodeName(h,suffix)).Select(p=>p.Creator).SingleAsync());
            else
            {
                var rows=await db.BarcodeAliases.AsNoTracking().Where(a=>a.Barcode.StartsWith(BarcodeName(h,suffix))).ToListAsync();Assert.Equal(operation=="import"?2:1,rows.Count);
                Assert.All(rows,r=>{Assert.Equal(1.23456789m,r.ConversionRate);Assert.Equal("price-owner",r.Creator);});
            }
        }
    }

    [CurrentPermissionPostgresFact]
    public async Task Barcode_import_late_fault_cannot_be_reflushed_by_logging()
    {
        await using var h=await BarcodeSeed();var connection=h.Db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var writer=await Login(host,h);
        await using var fault=new NpgsqlConnection(connection);await fault.OpenAsync();
        await using var install=new NpgsqlCommand($$"""
            CREATE SEQUENCE public.cp6_test_barcode_once;
            CREATE FUNCTION public.cp6_test_barcode_insert() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN IF nextval('public.cp6_test_barcode_once')=1 THEN RAISE EXCEPTION 'simulated barcode batch late insert failure'; END IF; RETURN NEW; END $fault$;
            CREATE TRIGGER cp6_test_barcode_insert BEFORE INSERT ON public."T_BarcodeAlias" FOR EACH ROW
            WHEN (NEW."TenantId"='{{h.Tenant}}'::uuid AND NEW."Barcode"='{{BarcodeName(h,"FAULT1")}}') EXECUTE FUNCTION public.cp6_test_barcode_insert();
            """,fault);await install.ExecuteNonQueryAsync();
        var before=await BarcodeWriteSet(h);
        try
        {
            using var failed=await host.Send(BarcodeWorkbookRequest(writer,h,"FAULT",true));
            await DeviceEvidence("barcode-import-fault.json",new{Status=(int)failed.StatusCode,Before=before,After=await BarcodeWriteSet(h)});
            Assert.True(failed.StatusCode==HttpStatusCode.ServiceUnavailable,await OperationsFailure(failed));Assert.Equal(before,await BarcodeWriteSet(h));
        }
        finally
        {
            await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_barcode_insert ON public.\"T_BarcodeAlias\"; DROP FUNCTION public.cp6_test_barcode_insert(); DROP SEQUENCE public.cp6_test_barcode_once",fault);await remove.ExecuteNonQueryAsync();
        }
        using var restored=await host.Send(BarcodeWorkbookRequest(writer,h,"FAULT",true));Assert.True((await BarcodeResponse<BarcodeImportResult>(restored)).Committed);
        var rows=await h.Db.BarcodeAliases.ToListAsync();Assert.Equal(2,rows.Count);Assert.All(rows,r=>Assert.Equal(1.23456789m,r.ConversionRate));
    }

    [CurrentPermissionPostgresFact]
    public async Task Barcode_rechecks_actual_revoked_configuration_action()
    {
        await using var h=await BarcodeSeed();var connection=h.Db.Database.GetConnectionString()!;
        await using var host=await AuthenticationApiProcess.Start(connection);var writer=await Login(host,h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();
        await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        var before=await BarcodeWriteSet(h);Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"T_BarcodeProfile\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var revoked=await control.BeginTransactionAsync();
                await using var change=new NpgsqlCommand("""
                    SELECT cp6_authentication_v1.acquire(); DELETE FROM public."Sys_RoleAction"
                    WHERE "TenantId"=@tenant AND "RoleId"=72 AND "MenuId"=461 AND "ActionCode"='barcode-manage'
                    """,control,revoked);change.Parameters.AddWithValue("tenant",h.Tenant);await change.ExecuteNonQueryAsync();
                pending=host.Send(OperationsRequest(writer,HttpMethod.Post,"api/v2/wms/barcode-profiles",BarcodeProfileInput(h,"REVOKED")));
                wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"barcode-revoke",pending);await revoked.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;
        await DeviceEvidence("barcode-revoke.json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,Wait=wait,Before=before,After=await BarcodeWriteSet(h)});
        Assert.True(denied.StatusCode==HttpStatusCode.Forbidden,await OperationsFailure(denied));Assert.Equal(before,await BarcodeWriteSet(h));
    }

    private static async Task<T> BarcodeResponse<T>(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode==HttpStatusCode.OK,await OperationsFailure(response));return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static Task<string> BarcodeWriteSet(Harness h)=>h.Db.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object('Profiles',(SELECT coalesce(jsonb_agg(to_jsonb(p) ORDER BY p."Id"),'[]') FROM public."T_BarcodeProfile" p WHERE p."TenantId"={h.Tenant}),
          'Aliases',(SELECT coalesce(jsonb_agg(to_jsonb(a) ORDER BY a."Id"),'[]') FROM public."T_BarcodeAlias" a WHERE a."TenantId"={h.Tenant}),
          'Stock',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_Stock" s WHERE s."TenantId"={h.Tenant}),
          'StockTransactions',(SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t."Id"),'[]') FROM public."T_StockTransaction" t WHERE t."TenantId"={h.Tenant}),
          'Outbound',(SELECT coalesce(jsonb_agg(to_jsonb(o) ORDER BY o."Id"),'[]') FROM public."T_OutboundOrder" o WHERE o."TenantId"={h.Tenant}))::text AS "Value"
        """).SingleAsync();
}
