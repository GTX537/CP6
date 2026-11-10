using System.Net;
using System.Text.Json;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("create")][InlineData("update")]
    public async Task Remnant_native_failure_rolls_back_number_and_record_before_logging(string kind)
    {
        await using var h=await RemnantSeed(kind=="create"?"create":"update");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);
        await using var fault=new NpgsqlConnection(h.Db.Database.GetConnectionString()!);await fault.OpenAsync();
        await using var install=new NpgsqlCommand($$"""
            CREATE SEQUENCE public.cp6_test_remnant_once;
            CREATE FUNCTION public.cp6_test_remnant_failure() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN IF nextval('public.cp6_test_remnant_once')=1 THEN RAISE EXCEPTION 'simulated remnant native failure'; END IF; RETURN NEW; END $fault$;
            CREATE TRIGGER cp6_test_remnant_failure BEFORE {{(kind=="create"?"INSERT":"UPDATE")}} ON public."T_RemnantMaterial" FOR EACH ROW
            WHEN (NEW."TenantId"='{{h.Tenant}}'::uuid) EXECUTE FUNCTION public.cp6_test_remnant_failure();
            """,fault);await install.ExecuteNonQueryAsync();var before=await RemnantWriteSet(h);
        try
        {
            using var failed=await Send();await DeviceEvidence("remnant-"+kind+"-fault.json",new{Status=(int)failed.StatusCode,Before=before,After=await RemnantWriteSet(h)});
            Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.Equal(before,await RemnantWriteSet(h));
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_remnant_failure ON public.\"T_RemnantMaterial\"; DROP FUNCTION public.cp6_test_remnant_failure(); DROP SEQUENCE public.cp6_test_remnant_once",fault);await remove.ExecuteNonQueryAsync();}
        using var restored=await Send();Assert.True(restored.StatusCode==HttpStatusCode.OK,await OperationsFailure(restored));
        if(kind=="create"){Assert.Single(await h.Db.RemnantMaterials.ToListAsync());Assert.Equal(1,await h.Db.WmsSequences.Where(s=>s.Prefix=="REM").Select(s=>s.NextNo).SingleAsync());}
        else Assert.Equal("legacy simulated metadata",await h.Db.RemnantMaterials.Where(s=>s.RemnantNo==RemnantNo(h,"A")).Select(s=>s.Remarks).SingleAsync());
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,kind=="create"?HttpMethod.Post:HttpMethod.Put,kind=="create"?"api/wms/remnant":"api/wms/remnant/"+RemnantNo(h,"A"),RemnantInput()));
    }

    [TenantManagementPostgresTheory]
    [InlineData("width")][InlineData("length")][InlineData("quantity")][InlineData("warehouse")][InlineData("location")]
    public async Task Remnant_update_obeys_the_existing_create_input_invariants_without_effects(string kind)
    {
        await using var h=await RemnantSeed("update");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var input=RemnantInput();
        if(kind=="width")input.WidthMm=0;if(kind=="length")input.LengthMm=0;if(kind=="quantity")input.Quantity=0;if(kind=="warehouse")input.WarehouseCd="";if(kind=="location")input.LocationCd="";
        var before=await RemnantWriteSet(h);using var failed=await host.Send(OperationsRequest(actor,HttpMethod.Put,"api/wms/remnant/"+RemnantNo(h,"A"),input));
        await DeviceEvidence("remnant-invalid-"+kind+".json",new{Status=(int)failed.StatusCode,Before=before,After=await RemnantWriteSet(h)});
        Assert.Equal(HttpStatusCode.BadRequest,failed.StatusCode);Assert.Equal(before,await RemnantWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Remnant_competing_reservations_decide_current_state_and_keep_the_first_purpose()
    {
        await using var h=await RemnantSeed("reserve");var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var stock=await PalletStockRows(h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        await using var install=new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_remnant_gate() RETURNS trigger LANGUAGE plpgsql AS $gate$
            BEGIN PERFORM pg_advisory_xact_lock(726449001); RETURN NEW; END $gate$;
            CREATE TRIGGER cp6_test_remnant_gate BEFORE UPDATE ON public."T_RemnantMaterial" FOR EACH ROW
            WHEN (NEW."TenantId"='{{h.Tenant}}'::uuid) EXECUTE FUNCTION public.cp6_test_remnant_gate();
            """,control);await install.ExecuteNonQueryAsync();
        try
        {
            Task<HttpResponseMessage> first,second;FenceWait firstWait,secondWait;
            await using(var held=await control.BeginTransactionAsync())
            {
                await using var gate=new NpgsqlCommand("SELECT pg_advisory_xact_lock(726449001)",control,held);await gate.ExecuteNonQueryAsync();
                try
                {
                    first=Send("FIRST");firstWait=await WaitForFence(observer,control.ProcessID,"%UPDATE%T_RemnantMaterial%",null,first);
                    second=Send("SECOND");secondWait=await SerialNativeWait(observer,firstWait.Backend,control.ProcessID,"remnant-second-reserve",second);
                }
                finally{await held.RollbackAsync();}
            }
            using var a=await first;using var b=await second;var row=await h.Db.RemnantMaterials.AsNoTracking().SingleAsync(s=>s.RemnantNo==RemnantNo(h,"A"));
            await DeviceEvidence("remnant-two-reserves.json",new{FirstStatus=(int)a.StatusCode,SecondStatus=(int)b.StatusCode,firstWait,secondWait,row.Status,row.ReservedFor,row.Quantity});
            Assert.Equal(HttpStatusCode.OK,a.StatusCode);Assert.Equal(HttpStatusCode.BadRequest,b.StatusCode);Assert.Equal(firstWait.Backend,secondWait.Blocker);
            Assert.Contains("WM-MSG-043",await b.Content.ReadAsStringAsync());Assert.Equal(RemnantStatus.Reserved,row.Status);Assert.Equal("FIRST",row.ReservedFor);Assert.Equal(1.2345m,row.Quantity);Assert.Equal(stock,await PalletStockRows(h));
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_remnant_gate ON public.\"T_RemnantMaterial\"; DROP FUNCTION public.cp6_test_remnant_gate()",control);await remove.ExecuteNonQueryAsync();}
        Task<HttpResponseMessage> Send(string purpose)=>host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/remnant/"+RemnantNo(h,"A")+"/reserve",new{ReservedFor=purpose}));
    }

    [CurrentPermissionPostgresFact]
    public async Task Remnant_legacy_cycle_preserves_quantity_range_state_errors_and_no_stock_effect()
    {
        await using var h=await RemnantSeed("create");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var stock=await PalletStockRows(h);const string path="api/wms/remnant";
        async Task<string> Create(RemnantDto input)
        {using var result=await host.Send(OperationsRequest(actor,HttpMethod.Post,path,input));Assert.True(result.StatusCode==HttpStatusCode.OK,await OperationsFailure(result));using var j=JsonDocument.Parse(await result.Content.ReadAsStringAsync());return j.RootElement.GetProperty("data").GetProperty("remnantNo").GetString()!;}
        var input=RemnantInput();var no=await Create(input);var row=await h.Db.RemnantMaterials.AsNoTracking().SingleAsync();Assert.Equal("price-owner",row.Creator);Assert.Equal(1.2345m,row.Quantity);
        async Task Expect(string suffix,HttpStatusCode status,object? body=null,HttpMethod? method=null)
        {using var result=await host.Send(OperationsRequest(actor,method??HttpMethod.Post,path+"/"+no+suffix,body));Assert.True(result.StatusCode==status,await OperationsFailure(result));}
        using var bad=await host.Send(OperationsRequest(actor,HttpMethod.Post,path,new RemnantDto()));Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        using var match=await host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/match?materialType=PAPER&minWidthMm=300&minLengthMm=500"));Assert.Single(await RemnantHttpData<List<RemnantMaterial>>(match));
        await Expect("/unreserve",HttpStatusCode.BadRequest);await Expect("/reserve",HttpStatusCode.BadRequest,new{ReservedFor=""});
        input.WidthMm=310;input.LengthMm=510;input.Quantity=2.3456m;await Expect("",HttpStatusCode.OK,input,HttpMethod.Put);
        await Expect("/reserve",HttpStatusCode.OK,new{ReservedFor="FIRST"});await Expect("/reserve",HttpStatusCode.BadRequest,new{ReservedFor="SECOND"});
        input.Quantity=3.4567m;await Expect("",HttpStatusCode.OK,input,HttpMethod.Put);
        row=await h.Db.RemnantMaterials.AsNoTracking().SingleAsync();Assert.Equal(RemnantStatus.Reserved,row.Status);Assert.Equal("FIRST",row.ReservedFor);Assert.Equal(3.4567m,row.Quantity);Assert.Equal("price-owner",row.Modifier);
        await Expect("/unreserve",HttpStatusCode.OK);row=await h.Db.RemnantMaterials.AsNoTracking().SingleAsync();Assert.Null(row.ReservedFor);Assert.Equal(RemnantStatus.Available,row.Status);
        await Expect("/use",HttpStatusCode.OK);await Expect("/use",HttpStatusCode.BadRequest);await Expect("",HttpStatusCode.BadRequest,input,HttpMethod.Put);
        input.WidthMm=0;using var ended=await host.Send(OperationsRequest(actor,HttpMethod.Put,path+"/"+no,input));Assert.Equal(HttpStatusCode.BadRequest,ended.StatusCode);Assert.Contains("WM-MSG-043",await ended.Content.ReadAsStringAsync());
        await Expect("/dispose",HttpStatusCode.OK);await Expect("/dispose",HttpStatusCode.BadRequest);await Expect("",HttpStatusCode.OK,method:HttpMethod.Delete);
        using var missing=await host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/"+no));Assert.Equal(HttpStatusCode.NotFound,missing.StatusCode);
        row=await h.Db.RemnantMaterials.IgnoreQueryFilters().SingleAsync(r=>r.TenantId==h.Tenant&&r.RemnantNo==no);Assert.True(row.IsDeleted);Assert.Equal(3.4567m,row.Quantity);
        input=RemnantInput();input.WidthMm=int.MaxValue;input.LengthMm=int.MaxValue;input.Quantity=99999999999999.9999m;no=await Create(input);
        using var maximum=await host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/"+no));row=await RemnantHttpData<RemnantMaterial>(maximum);Assert.Equal(input.Quantity,row.Quantity);Assert.Equal(int.MaxValue,row.WidthMm);Assert.Equal(int.MaxValue,row.LengthMm);
        using var list=await host.Send(OperationsRequest(actor,HttpMethod.Get,path+"?remnantNo="+no+"&materialType=PAPER&materialGrade=SIM-GRADE&status=0&warehouseCd=W1&sourceWorkOrderNo=SIM-WORK-ORDER&sourceRollNo=SIM-ROLL"));Assert.Single(await RemnantHttpData<List<RemnantMaterial>>(list));
        input=RemnantInput();input.Quantity=0.0001m;no=await Create(input);Assert.Equal(0.0001m,await h.Db.RemnantMaterials.Where(r=>r.RemnantNo==no).Select(r=>r.Quantity).SingleAsync());
        Assert.Equal(stock,await PalletStockRows(h));Assert.Empty(await h.Db.StockTransactions.ToListAsync());
    }

    private static async Task<T> RemnantHttpData<T>(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode==HttpStatusCode.OK,await OperationsFailure(response));using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0,document.RootElement.GetProperty("code").GetInt32());return document.RootElement.GetProperty("data").Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static Task<string> RemnantWriteSet(Harness h)=>h.Db.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object('Remnants',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_RemnantMaterial" s WHERE s."TenantId"={h.Tenant}),
          'Numbers',(SELECT coalesce(jsonb_agg(to_jsonb(n) ORDER BY n."Id"),'[]') FROM public."T_WmsSequence" n WHERE n."TenantId"={h.Tenant}),
          'Stock',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_Stock" s WHERE s."TenantId"={h.Tenant}),
          'StockTransactions',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_StockTransaction" s WHERE s."TenantId"={h.Tenant}),
          'Outbound',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_OutboundOrder" s WHERE s."TenantId"={h.Tenant}))::text AS "Value"
        """).SingleAsync();
}
