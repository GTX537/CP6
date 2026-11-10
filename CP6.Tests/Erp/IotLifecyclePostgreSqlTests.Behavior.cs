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
    [InlineData("list")][InlineData("detail")][InlineData("readings")][InlineData("alerts")]
    public async Task Iot_reads_require_the_current_view_action(string kind)
    {
        await using var h=await IotSeed("ingest");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="wms-iot").Select(m=>m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a=>a.RoleId==72&&a.MenuId==menu&&a.ActionCode=="view").ExecuteDeleteAsync();
        var actor=await Login(host,h);var before=await IotWriteSet(h);
        var suffix=kind switch{"list"=>"sensors","detail"=>"sensors/"+IotNo(h,"A"),"readings"=>"sensors/"+IotNo(h,"A")+"/readings",_=>"alerts"};
        using var denied=await host.Send(OperationsRequest(actor,HttpMethod.Get,"api/wms/iot/"+suffix));
        await DeviceEvidence("iot-"+kind+"-action.json",new{Status=(int)denied.StatusCode,Before=before,After=await IotWriteSet(h)});
        Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(before,await IotWriteSet(h));
    }

    [TenantManagementPostgresTheory]
    [InlineData("sensor")][InlineData("reading")][InlineData("batch")]
    public async Task Iot_late_native_insert_failure_cannot_be_resaved_by_logging(string kind)
    {
        await using var h=await IotSeed(kind=="sensor"?"create":"ingest");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);
        var table=kind=="sensor"?"T_IotSensor":"T_IotSensorReading";var hit=kind=="batch"?2:1;
        await using var fault=new NpgsqlConnection(h.Db.Database.GetConnectionString()!);await fault.OpenAsync();
        await using var install=new NpgsqlCommand($$"""
            CREATE SEQUENCE public.cp6_test_iot_once;
            CREATE FUNCTION public.cp6_test_iot_insert() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN IF nextval('public.cp6_test_iot_once')={{hit}} THEN RAISE EXCEPTION 'simulated IoT late insert failure'; END IF; RETURN NEW; END $fault$;
            CREATE TRIGGER cp6_test_iot_insert BEFORE INSERT ON public."{{table}}" FOR EACH ROW
            WHEN (NEW."TenantId"='{{h.Tenant}}'::uuid) EXECUTE FUNCTION public.cp6_test_iot_insert();
            """,fault);await install.ExecuteNonQueryAsync();var before=await IotWriteSet(h);
        try
        {
            using var failed=await Send();await DeviceEvidence("iot-"+kind+"-fault.json",new{Status=(int)failed.StatusCode,Before=before,After=await IotWriteSet(h)});
            Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.Equal(before,await IotWriteSet(h));
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_iot_insert ON public.\""+table+"\"; DROP FUNCTION public.cp6_test_iot_insert(); DROP SEQUENCE public.cp6_test_iot_once",fault);await remove.ExecuteNonQueryAsync();}
        using var restored=await Send();Assert.True(restored.StatusCode==HttpStatusCode.OK,await OperationsFailure(restored));
        if(kind=="sensor")
        {Assert.Single(await h.Db.IotSensors.ToListAsync());Assert.Equal(1,await h.Db.WmsSequences.Where(s=>s.Prefix=="SEN").Select(s=>s.NextNo).SingleAsync());}
        else Assert.Equal(kind=="reading"?3:4,await h.Db.IotSensorReadings.CountAsync());

        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,HttpMethod.Post,kind=="sensor"?"api/wms/iot/sensors":kind=="batch"?"api/wms/iot/simulate?count=1":"api/wms/iot/sensors/"+IotNo(h,"A")+"/readings",
            kind=="sensor"?IotInput():kind=="reading"?new{Value=18.2345m,ReadAt=IotAt().AddMinutes(5)}:null));
    }

    [CurrentPermissionPostgresFact]
    public async Task Iot_late_history_does_not_regress_the_latest_recorded_projection()
    {
        await using var h=await IotSeed("ingest");var id=IotNo(h,"A");var actual=await h.Db.IotSensorReadings.AsNoTracking().SingleAsync(r=>r.SensorId==id);
        await h.Db.IotSensors.Where(s=>s.SensorId==id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LastReadAt,actual.ReadAt.AddMinutes(-10)).SetProperty(x=>x.LastValue,1m));
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var stock=await PalletStockRows(h);
        using var late=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/iot/sensors/"+id+"/readings",new{Value=11.1234m,ReadAt=actual.ReadAt.AddMinutes(-5)}));Assert.Equal(HttpStatusCode.OK,late.StatusCode);
        var head=await h.Db.IotSensors.AsNoTracking().SingleAsync(s=>s.SensorId==id);
        await DeviceEvidence("iot-late-projection.json",new{head.LastValue,head.LastReadAt,ExpectedValue=actual.Value,ExpectedAt=actual.ReadAt,History=await h.Db.IotSensorReadings.AsNoTracking().Where(r=>r.SensorId==id).OrderBy(r=>r.ReadAt).Select(r=>new{r.ReadAt,r.Value}).ToListAsync()});
        Assert.Equal(actual.ReadAt,head.LastReadAt);Assert.Equal(actual.Value,head.LastValue);Assert.Equal(2,await h.Db.IotSensorReadings.CountAsync(r=>r.SensorId==id));
        var next=actual.ReadAt.AddMinutes(5);
        using var newer=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/iot/sensors/"+id+"/readings",new{Value=18.4321m,ReadAt=next}));Assert.Equal(HttpStatusCode.OK,newer.StatusCode);
        head=await h.Db.IotSensors.AsNoTracking().SingleAsync(s=>s.SensorId==id);Assert.Equal(next,head.LastReadAt);Assert.Equal(18.4321m,head.LastValue);
        using var sameTime=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/iot/sensors/"+id+"/readings",new{Value=19.4321m,ReadAt=next}));Assert.Equal(HttpStatusCode.OK,sameTime.StatusCode);
        Assert.Equal(2,await h.Db.IotSensorReadings.CountAsync(r=>r.SensorId==id&&r.ReadAt==next));Assert.Equal(stock,await PalletStockRows(h));
    }

    [TenantManagementPostgresTheory]
    [InlineData("head")][InlineData("history")][InlineData("count0")][InlineData("count101")][InlineData("sensors101")][InlineData("unsupported")]
    public async Task Iot_simulation_is_bounded_explicit_and_projects_one_actual_latest_row(string scenario)
    {
        await using var h=await IotSeed("ingest");var stock=await PalletStockRows(h);
        if(scenario=="history")await h.Db.IotSensorReadings.Where(r=>r.SensorId==IotNo(h,"A")).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.ReadAt,IotAt().AddHours(1)));
        if(scenario=="unsupported")await h.Db.IotSensors.Where(s=>s.SensorId==IotNo(h,"B")).ExecuteUpdateAsync(s=>s.SetProperty(r=>r.SensorType,"CUSTOM").SetProperty(r=>r.Unit,"BARS"));
        if(scenario=="sensors101")
        {
            for(var i=0;i<99;i++)h.Db.IotSensors.Add(new(){SensorId=IotNo(h,"X"+i.ToString("D3")),SensorType="TEMP",WarehouseCd="W1",Unit="C",IsEnabled=true});
            await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        }
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var before=await IotWriteSet(h);
        var count=scenario=="count0"?0:scenario=="count101"?101:scenario=="sensors101"?1:3;
        using var result=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/iot/simulate?count="+count));
        if(scenario is "head" or "history")
        {
            Assert.Equal(HttpStatusCode.OK,result.StatusCode);
            var sensors=await h.Db.IotSensors.AsNoTracking().OrderBy(s=>s.SensorId).ToListAsync();var evidence=new List<object>();
            foreach(var sensor in sensors)
            {
                var latest=await h.Db.IotSensorReadings.AsNoTracking().Where(r=>r.SensorId==sensor.SensorId).OrderByDescending(r=>r.ReadAt).FirstAsync();
                evidence.Add(new{sensor.SensorId,sensor.LastValue,sensor.LastReadAt,ExpectedValue=latest.Value,ExpectedAt=latest.ReadAt});
            }
            await DeviceEvidence("iot-simulation-"+scenario+".json",new{Status=(int)result.StatusCode,Rows=evidence});
            foreach(var sensor in sensors)
            {
                var latest=await h.Db.IotSensorReadings.AsNoTracking().Where(r=>r.SensorId==sensor.SensorId).OrderByDescending(r=>r.ReadAt).FirstAsync();
                Assert.Equal(latest.ReadAt,sensor.LastReadAt);Assert.Equal(latest.Value,sensor.LastValue);
            }
            Assert.Equal(8,await h.Db.IotSensorReadings.CountAsync());
        }
        else
        {
            var expected=scenario=="unsupported"?HttpStatusCode.ServiceUnavailable:HttpStatusCode.BadRequest;
            await DeviceEvidence("iot-simulation-"+scenario+".json",new{Status=(int)result.StatusCode,Expected=(int)expected,Before=before,After=await IotWriteSet(h)});
            Assert.Equal(expected,result.StatusCode);Assert.Equal(before,await IotWriteSet(h));
            if(scenario=="count101")
            {
                using var boundary=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/iot/simulate?count=100"));Assert.Equal(HttpStatusCode.OK,boundary.StatusCode);Assert.Equal(202,await h.Db.IotSensorReadings.CountAsync());
            }
            if(scenario=="sensors101")
            {
                await h.Db.IotSensors.Where(s=>s.SensorId==IotNo(h,"X098")).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsEnabled,false));
                using var maximum=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/iot/simulate?count=100"));Assert.True(maximum.StatusCode==HttpStatusCode.OK,await OperationsFailure(maximum));Assert.Equal(10002,await h.Db.IotSensorReadings.CountAsync());
                await DeviceEvidence("iot-maximum-10000.json",new{Status=(int)maximum.StatusCode,Generated=10000,HistoryCount=await h.Db.IotSensorReadings.CountAsync()});
            }
            if(scenario=="unsupported")
            {
                var custom=IotInput();custom.SensorType="CUSTOM";custom.Unit="BARS";
                using var created=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/iot/sensors",custom));Assert.Equal(HttpStatusCode.OK,created.StatusCode);
                using var json=JsonDocument.Parse(await created.Content.ReadAsStringAsync());var id=json.RootElement.GetProperty("data").GetProperty("sensorId").GetString();
                using var manual=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/iot/sensors/"+id+"/readings",new{Value=123.4567m,ReadAt=IotAt()}));Assert.Equal(HttpStatusCode.OK,manual.StatusCode);
                Assert.Equal(123.4567m,await h.Db.IotSensorReadings.Where(r=>r.SensorId==id).Select(r=>r.Value).SingleAsync());
            }
        }
        Assert.Equal(stock,await PalletStockRows(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Iot_existing_manual_cycle_preserves_thresholds_history_precision_and_errors()
    {
        await using var h=await IotSeed("create");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var stock=await PalletStockRows(h);
        var input=IotInput();input.MinThreshold=2m;input.MaxThreshold=8m;input.Unit=null;
        using var created=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/iot/sensors",input));Assert.Equal(HttpStatusCode.OK,created.StatusCode);
        using var json=JsonDocument.Parse(await created.Content.ReadAsStringAsync());var id=json.RootElement.GetProperty("data").GetProperty("sensorId").GetString()!;
        var sensor=await h.Db.IotSensors.AsNoTracking().SingleAsync();Assert.Equal("℃",sensor.Unit);Assert.Equal("price-owner",sensor.Creator);
        input.SensorName="Updated simulated sensor";
        using var updated=await host.Send(OperationsRequest(actor,HttpMethod.Put,"api/wms/iot/sensors/"+id,input));Assert.Equal(HttpStatusCode.OK,updated.StatusCode);
        var at=IotAt();
        foreach(var item in new[]{(Offset:0,Value:2m),(Offset:1,Value:8m),(Offset:2,Value:8.0001m)})
        {using var sent=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/iot/sensors/"+id+"/readings",new{item.Value,ReadAt=at.AddSeconds(item.Offset)}));Assert.Equal(HttpStatusCode.OK,sent.StatusCode);}
        var readings=await h.Db.IotSensorReadings.AsNoTracking().OrderBy(r=>r.ReadAt).ToArrayAsync();Assert.False(readings[0].IsAlert);Assert.False(readings[1].IsAlert);Assert.True(readings[2].IsAlert);Assert.Equal(8.0001m,readings[2].Value);
        using var alerts=await host.Send(OperationsRequest(actor,HttpMethod.Get,"api/wms/iot/alerts"));Assert.Single(await IotHttpData<List<IotAlertDto>>(alerts));
        using var history=await host.Send(OperationsRequest(actor,HttpMethod.Get,"api/wms/iot/sensors/"+id+"/readings?limit=1"));Assert.Single(await IotHttpData<List<IotSensorReading>>(history));
        using var normal=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/iot/sensors/"+id+"/readings",new{Value=5m,ReadAt=at.AddSeconds(3)}));Assert.Equal(HttpStatusCode.OK,normal.StatusCode);
        using var clear=await host.Send(OperationsRequest(actor,HttpMethod.Get,"api/wms/iot/alerts"));Assert.Empty(await IotHttpData<List<IotAlertDto>>(clear));
        using var bad=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/iot/sensors",new IotSensorDto{WarehouseCd=""}));Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        using var deleted=await host.Send(OperationsRequest(actor,HttpMethod.Delete,"api/wms/iot/sensors/"+id));Assert.Equal(HttpStatusCode.OK,deleted.StatusCode);
        using var missing=await host.Send(OperationsRequest(actor,HttpMethod.Get,"api/wms/iot/sensors/"+id));Assert.Equal(HttpStatusCode.NotFound,missing.StatusCode);
        using var retained=await host.Send(OperationsRequest(actor,HttpMethod.Get,"api/wms/iot/sensors/"+id+"/readings"));Assert.Equal(4,(await IotHttpData<List<IotSensorReading>>(retained)).Count);
        Assert.Equal(stock,await PalletStockRows(h));Assert.Empty(await h.Db.StockTransactions.ToListAsync());
    }

    private static async Task<T> IotHttpData<T>(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode==HttpStatusCode.OK,await OperationsFailure(response));
        using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0,document.RootElement.GetProperty("code").GetInt32());
        return document.RootElement.GetProperty("data").Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static Task<string> IotWriteSet(Harness h)=>h.Db.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object('Sensors',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_IotSensor" s WHERE s."TenantId"={h.Tenant}),
          'Readings',(SELECT coalesce(jsonb_agg(to_jsonb(r) ORDER BY r."Id"),'[]') FROM public."T_IotSensorReading" r WHERE r."TenantId"={h.Tenant}),
          'Numbers',(SELECT coalesce(jsonb_agg(to_jsonb(n) ORDER BY n."Id"),'[]') FROM public."T_WmsSequence" n WHERE n."TenantId"={h.Tenant}),
          'Stock',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_Stock" s WHERE s."TenantId"={h.Tenant}),
          'StockTransactions',(SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY t."Id"),'[]') FROM public."T_StockTransaction" t WHERE t."TenantId"={h.Tenant}),
          'Outbound',(SELECT coalesce(jsonb_agg(to_jsonb(o) ORDER BY o."Id"),'[]') FROM public."T_OutboundOrder" o WHERE o."TenantId"={h.Tenant}))::text AS "Value"
        """).SingleAsync();
}
