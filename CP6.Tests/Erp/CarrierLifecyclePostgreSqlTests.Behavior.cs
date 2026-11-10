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
    [InlineData("insert")][InlineData("update")]
    public async Task Carrier_native_failure_rolls_back_number_status_and_history_before_logging(string kind)
    {
        await using var h=await CarrierSeed(kind=="insert"?"create":"pickup");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);
        await using var fault=new NpgsqlConnection(h.Db.Database.GetConnectionString()!);await fault.OpenAsync();
        await using var install=new NpgsqlCommand($$"""
            CREATE SEQUENCE public.cp6_test_carrier_once;
            CREATE FUNCTION public.cp6_test_carrier_failure() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN IF nextval('public.cp6_test_carrier_once')=1 THEN RAISE EXCEPTION 'simulated carrier native failure'; END IF; RETURN NEW; END $fault$;
            CREATE TRIGGER cp6_test_carrier_failure BEFORE {{kind.ToUpperInvariant()}} ON public."T_CarrierShipment" FOR EACH ROW
            WHEN (NEW."TenantId"='{{h.Tenant}}'::uuid) EXECUTE FUNCTION public.cp6_test_carrier_failure();
            """,fault);await install.ExecuteNonQueryAsync();var before=await CarrierWriteSet(h);
        try
        {
            using var failed=await Send();await DeviceEvidence("carrier-"+kind+"-fault.json",new{Status=(int)failed.StatusCode,Before=before,After=await CarrierWriteSet(h)});
            Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.Equal(before,await CarrierWriteSet(h));
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_carrier_failure ON public.\"T_CarrierShipment\"; DROP FUNCTION public.cp6_test_carrier_failure(); DROP SEQUENCE public.cp6_test_carrier_once",fault);await remove.ExecuteNonQueryAsync();}
        using var restored=await Send();Assert.True(restored.StatusCode==HttpStatusCode.OK,await OperationsFailure(restored));
        if(kind=="insert")
        {Assert.Single(await h.Db.CarrierShipments.ToListAsync());Assert.Equal(1,await h.Db.WmsSequences.Where(s=>s.Prefix=="SHIP").Select(s=>s.NextNo).SingleAsync());}
        else
        {
            var row=await h.Db.CarrierShipments.AsNoTracking().SingleAsync(s=>s.ShipmentNo==CarrierNo(h,"A"));
            Assert.Equal(CarrierShipmentStatus.PickedUp,row.Status);Assert.Single(JsonSerializer.Deserialize<List<CarrierEventDto>>(row.EventsJson!)!);
        }
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,HttpMethod.Post,kind=="insert"?"api/wms/carrier":"api/wms/carrier/"+CarrierNo(h,"A")+"/pickup",kind=="insert"?CarrierInput():null));
    }

    [CurrentPermissionPostgresFact]
    public async Task Carrier_two_native_blocked_event_appends_preserve_both_accepted_observations()
    {
        await using var h=await CarrierSeed("event");var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        var stock=await PalletStockRows(h);await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        await using var install=new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_carrier_gate() RETURNS trigger LANGUAGE plpgsql AS $gate$
            BEGIN PERFORM pg_advisory_xact_lock(726447001); RETURN NEW; END $gate$;
            CREATE TRIGGER cp6_test_carrier_gate BEFORE UPDATE ON public."T_CarrierShipment" FOR EACH ROW
            WHEN (NEW."TenantId"='{{h.Tenant}}'::uuid) EXECUTE FUNCTION public.cp6_test_carrier_gate();
            """,control);await install.ExecuteNonQueryAsync();
        try
        {
            Task<HttpResponseMessage> first,second;FenceWait firstWait,secondWait;
            await using(var held=await control.BeginTransactionAsync())
            {
                await using var gate=new NpgsqlCommand("SELECT pg_advisory_xact_lock(726447001)",control,held);await gate.ExecuteNonQueryAsync();
                try
                {
                    first=Send("first");firstWait=await WaitForFence(observer,control.ProcessID,"%UPDATE%T_CarrierShipment%",null,first);
                    second=Send("second");secondWait=await SerialNativeWait(observer,firstWait.Backend,control.ProcessID,"carrier-second-append",second);
                }
                finally{await held.RollbackAsync();}
            }
            using var a=await first;using var b=await second;
            var row=await h.Db.CarrierShipments.AsNoTracking().SingleAsync(s=>s.ShipmentNo==CarrierNo(h,"A"));var events=JsonSerializer.Deserialize<List<CarrierEventDto>>(row.EventsJson!)!;
            await DeviceEvidence("carrier-two-appends.json",new{FirstStatus=(int)a.StatusCode,SecondStatus=(int)b.StatusCode,firstWait,secondWait,Events=events});
            Assert.Equal(HttpStatusCode.OK,a.StatusCode);Assert.Equal(HttpStatusCode.OK,b.StatusCode);Assert.Equal(firstWait.Backend,secondWait.Blocker);
            Assert.Equal(2,events.Count);Assert.Contains(events,e=>e.Message=="first");Assert.Contains(events,e=>e.Message=="second");Assert.Equal(stock,await PalletStockRows(h));
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_carrier_gate ON public.\"T_CarrierShipment\"; DROP FUNCTION public.cp6_test_carrier_gate()",control);await remove.ExecuteNonQueryAsync();}
        Task<HttpResponseMessage> Send(string message)=>host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/carrier/"+CarrierNo(h,"A")+"/event",new CarrierEventDto{Status="MANUAL",Location="SIMULATED",Message=message}));
    }

    [CurrentPermissionPostgresFact]
    public async Task Carrier_manual_lifecycle_preserves_history_precision_errors_and_no_stock_effect()
    {
        await using var h=await CarrierSeed("create");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var stock=await PalletStockRows(h);
        const string path="api/wms/carrier";
        using var bad=await host.Send(OperationsRequest(actor,HttpMethod.Post,path,new CarrierShipmentDto{PackageNo=""}));Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        using var badCarrier=await host.Send(OperationsRequest(actor,HttpMethod.Post,path,new CarrierShipmentDto{PackageNo="SIM",CarrierCd=""}));Assert.Equal(HttpStatusCode.BadRequest,badCarrier.StatusCode);
        using var created=await host.Send(OperationsRequest(actor,HttpMethod.Post,path,CarrierInput()));Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        using var json=JsonDocument.Parse(await created.Content.ReadAsStringAsync());var no=json.RootElement.GetProperty("data").GetProperty("shipmentNo").GetString()!;
        async Task Expect(string suffix,HttpStatusCode status,object? input=null)
        {using var result=await host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/"+no+suffix,input));Assert.True(result.StatusCode==status,await OperationsFailure(result));}
        await Expect("/event",HttpStatusCode.OK,new CarrierEventDto{Status="Delivered",Message="SIMULATED MANUAL TEXT",Location="SIMULATED"});
        var row=await h.Db.CarrierShipments.AsNoTracking().SingleAsync();Assert.Equal(CarrierShipmentStatus.Created,row.Status);Assert.Equal("price-owner",row.Creator);Assert.Equal(3.456m,row.WeightKg);Assert.Equal(98.76m,row.CarrierFee);Assert.Equal("SIM-TRACKING",row.TrackingNo);
        await Expect("/in-transit",HttpStatusCode.BadRequest);await Expect("/pickup",HttpStatusCode.OK);await Expect("/pickup",HttpStatusCode.BadRequest);
        await Expect("/in-transit",HttpStatusCode.OK);await Expect("/delivered",HttpStatusCode.OK);await Expect("/delivered",HttpStatusCode.BadRequest);await Expect("/fail",HttpStatusCode.BadRequest,new{Reason="late"});
        row=await h.Db.CarrierShipments.AsNoTracking().SingleAsync();Assert.Equal(CarrierShipmentStatus.Delivered,row.Status);Assert.NotNull(row.PickedUpAt);Assert.NotNull(row.DeliveredAt);Assert.Equal("price-owner",row.Modifier);
        Assert.Equal(4,JsonSerializer.Deserialize<List<CarrierEventDto>>(row.EventsJson!)!.Count);
        using var list=await host.Send(OperationsRequest(actor,HttpMethod.Get,path+"?shipmentNo="+no+"&packageNo=SIM-PACKAGE&trackingNo=SIM-TRACKING&carrierCd=YAMATO&customerCd=SIM-CUSTOMER&status=3"));
        Assert.Single(await CarrierHttpData<List<CarrierShipment>>(list));
        using var detail=await host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/"+no));Assert.Equal(no,(await CarrierHttpData<CarrierShipment>(detail)).ShipmentNo);
        var automatic=CarrierInput();automatic.TrackingNo=null;
        using var second=await host.Send(OperationsRequest(actor,HttpMethod.Post,path,automatic));Assert.Equal(HttpStatusCode.OK,second.StatusCode);
        row=await h.Db.CarrierShipments.AsNoTracking().SingleAsync(s=>s.Status==CarrierShipmentStatus.Created);no=row.ShipmentNo;Assert.Matches("^YAMATO-[0-9]{8}-[0-9]{6}$",row.TrackingNo);
        await Expect("/fail",HttpStatusCode.OK,new{Reason="first failure"});await Expect("/fail",HttpStatusCode.OK,new{Reason="second failure"});
        row=await h.Db.CarrierShipments.AsNoTracking().SingleAsync(s=>s.ShipmentNo==no);Assert.Equal(CarrierShipmentStatus.Failed,row.Status);Assert.Equal(2,JsonSerializer.Deserialize<List<CarrierEventDto>>(row.EventsJson!)!.Count);
        using var third=await host.Send(OperationsRequest(actor,HttpMethod.Post,path,CarrierInput()));Assert.Equal(HttpStatusCode.OK,third.StatusCode);
        no=(await h.Db.CarrierShipments.AsNoTracking().SingleAsync(s=>s.Status==CarrierShipmentStatus.Created)).ShipmentNo;
        await Expect("/pickup",HttpStatusCode.OK);await Expect("/delivered",HttpStatusCode.OK);
        using var missing=await host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/MISSING"));Assert.Equal(HttpStatusCode.NotFound,missing.StatusCode);
        Assert.Equal(stock,await PalletStockRows(h));Assert.Empty(await h.Db.StockTransactions.ToListAsync());
    }

    private static async Task<T> CarrierHttpData<T>(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode==HttpStatusCode.OK,await OperationsFailure(response));using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0,document.RootElement.GetProperty("code").GetInt32());return document.RootElement.GetProperty("data").Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static Task<string> CarrierWriteSet(Harness h)=>h.Db.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object('Shipments',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_CarrierShipment" s WHERE s."TenantId"={h.Tenant}),
          'Numbers',(SELECT coalesce(jsonb_agg(to_jsonb(n) ORDER BY n."Id"),'[]') FROM public."T_WmsSequence" n WHERE n."TenantId"={h.Tenant}),
          'Stock',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_Stock" s WHERE s."TenantId"={h.Tenant}),
          'StockTransactions',(SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t."Id"),'[]') FROM public."T_StockTransaction" t WHERE t."TenantId"={h.Tenant}),
          'Outbound',(SELECT coalesce(jsonb_agg(to_jsonb(o) ORDER BY o."Id"),'[]') FROM public."T_OutboundOrder" o WHERE o."TenantId"={h.Tenant}))::text AS "Value"
        """).SingleAsync();
}
