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
    [InlineData("create")][InlineData("consume")][InlineData("slit")]
    public async Task Roll_native_failures_keep_parent_children_numbers_and_unrelated_stock_atomic(string kind)
    {
        await using var h=await RollSeed(kind=="create"?"create":"consume");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);
        await using var fault=new NpgsqlConnection(h.Db.Database.GetConnectionString()!);await fault.OpenAsync();var hit=kind=="slit"?2:1;
        await using var install=new NpgsqlCommand($$"""
            CREATE SEQUENCE public.cp6_test_roll_once;
            CREATE FUNCTION public.cp6_test_roll_failure() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN IF nextval('public.cp6_test_roll_once')={{hit}} THEN RAISE EXCEPTION 'simulated roll native failure'; END IF; RETURN NEW; END $fault$;
            CREATE TRIGGER cp6_test_roll_failure BEFORE {{(kind=="consume"?"UPDATE":"INSERT")}} ON public."T_PaperRoll" FOR EACH ROW
            WHEN (NEW."TenantId"='{{h.Tenant}}'::uuid) EXECUTE FUNCTION public.cp6_test_roll_failure();
            """,fault);await install.ExecuteNonQueryAsync();var before=await RollWriteSet(h);
        try
        {
            using var failed=await Send();await DeviceEvidence("roll-"+kind+"-fault.json",new{Status=(int)failed.StatusCode,Before=before,After=await RollWriteSet(h)});
            Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.Equal(before,await RollWriteSet(h));
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_roll_failure ON public.\"T_PaperRoll\"; DROP FUNCTION public.cp6_test_roll_failure(); DROP SEQUENCE public.cp6_test_roll_once",fault);await remove.ExecuteNonQueryAsync();}
        using var restored=await Send();Assert.True(restored.StatusCode==HttpStatusCode.OK,await OperationsFailure(restored));
        if(kind=="create")
        {Assert.Single(await h.Db.PaperRolls.ToListAsync());Assert.Equal(1,await h.Db.WmsSequences.Where(s=>s.Prefix=="ROLL").Select(s=>s.NextNo).SingleAsync());}
        else if(kind=="consume")Assert.Equal(9.12345677m,await h.Db.PaperRolls.Where(s=>s.RollNo==RollNo(h,"A")).Select(s=>s.RemainingLengthM).SingleAsync());
        else
        {
            Assert.Equal(3,await h.Db.PaperRolls.CountAsync(s=>s.ParentRollNo==RollNo(h,"A")));
            Assert.Equal(3,await h.Db.WmsSequences.Where(s=>s.Prefix=="ROLL").Select(s=>s.NextNo).SingleAsync());
            Assert.Equal(0m,await h.Db.PaperRolls.Where(s=>s.RollNo==RollNo(h,"A")).Select(s=>s.RemainingLengthM).SingleAsync());
        }
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,HttpMethod.Post,kind=="create"?"api/wms/paper-roll":kind=="slit"?"api/wms/paper-roll/slit":"api/wms/paper-roll/"+RollNo(h,"A")+"/consume",
            kind=="create"?RollInput():kind=="slit"?new SlitRequest{ParentRollNo=RollNo(h,"A"),ChildWidths=new(){100,120},KeepRemnant=true}:new{LengthM=1.00000001m}));
    }

    [TenantManagementPostgresTheory]
    [InlineData("late-negative")][InlineData("overflow")]
    public async Task Roll_entire_child_width_set_is_rejected_before_any_allocated_effect(string kind)
    {
        await using var h=await RollSeed("slit");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var before=await RollWriteSet(h);
        using var failed=await host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/paper-roll/slit",new SlitRequest{ParentRollNo=RollNo(h,"A"),ChildWidths=kind=="overflow"?new(){int.MaxValue,int.MaxValue}:new(){100,-1},KeepRemnant=true}));
        await DeviceEvidence("roll-width-"+kind+".json",new{Status=(int)failed.StatusCode,Before=before,After=await RollWriteSet(h)});
        Assert.Equal(HttpStatusCode.BadRequest,failed.StatusCode);Assert.Equal(before,await RollWriteSet(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Roll_competing_consumes_decide_the_current_remaining_length()
    {
        await using var h=await RollSeed("consume");var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var stock=await PalletStockRows(h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        await using var install=new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_roll_gate() RETURNS trigger LANGUAGE plpgsql AS $gate$
            BEGIN PERFORM pg_advisory_xact_lock(726448001); RETURN NEW; END $gate$;
            CREATE TRIGGER cp6_test_roll_gate BEFORE UPDATE ON public."T_PaperRoll" FOR EACH ROW
            WHEN (NEW."TenantId"='{{h.Tenant}}'::uuid) EXECUTE FUNCTION public.cp6_test_roll_gate();
            """,control);await install.ExecuteNonQueryAsync();
        try
        {
            Task<HttpResponseMessage> first,second;FenceWait firstWait,secondWait;
            await using(var held=await control.BeginTransactionAsync())
            {
                await using var gate=new NpgsqlCommand("SELECT pg_advisory_xact_lock(726448001)",control,held);await gate.ExecuteNonQueryAsync();
                try
                {
                    first=Send();firstWait=await WaitForFence(observer,control.ProcessID,"%UPDATE%T_PaperRoll%",null,first);
                    second=Send();secondWait=await SerialNativeWait(observer,firstWait.Backend,control.ProcessID,"roll-second-consume",second);
                }
                finally{await held.RollbackAsync();}
            }
            using var a=await first;using var b=await second;var row=await h.Db.PaperRolls.AsNoTracking().SingleAsync(s=>s.RollNo==RollNo(h,"A"));
            await DeviceEvidence("roll-two-consumes.json",new{FirstStatus=(int)a.StatusCode,SecondStatus=(int)b.StatusCode,firstWait,secondWait,row.RemainingLengthM});
            Assert.Equal(HttpStatusCode.OK,a.StatusCode);Assert.Equal(HttpStatusCode.BadRequest,b.StatusCode);Assert.Equal(firstWait.Backend,secondWait.Blocker);
            Assert.Contains("WM-MSG-202",await b.Content.ReadAsStringAsync());Assert.Equal(3.12345677m,row.RemainingLengthM);Assert.Equal(stock,await PalletStockRows(h));
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_roll_gate ON public.\"T_PaperRoll\"; DROP FUNCTION public.cp6_test_roll_gate()",control);await remove.ExecuteNonQueryAsync();}
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,HttpMethod.Post,"api/wms/paper-roll/"+RollNo(h,"A")+"/consume",new{LengthM=7.00000001m}));
    }

    [CurrentPermissionPostgresFact]
    public async Task Roll_legacy_cycle_preserves_eight_decimal_lengths_widths_status_and_no_stock_effect()
    {
        await using var h=await RollSeed("create");await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var stock=await PalletStockRows(h);const string path="api/wms/paper-roll";
        using var bad=await host.Send(OperationsRequest(actor,HttpMethod.Post,path,new PaperRollDto()));Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        async Task<string> Create(PaperRollDto input)
        {using var result=await host.Send(OperationsRequest(actor,HttpMethod.Post,path,input));Assert.True(result.StatusCode==HttpStatusCode.OK,await OperationsFailure(result));using var j=JsonDocument.Parse(await result.Content.ReadAsStringAsync());return j.RootElement.GetProperty("data").GetProperty("rollNo").GetString()!;}
        var no=await Create(RollInput());var row=await h.Db.PaperRolls.AsNoTracking().SingleAsync();Assert.Equal(3m,row.CoreDiameterInch);Assert.Equal(10.12345678m,row.RemainingLengthM);Assert.Equal("price-owner",row.Creator);
        using var match=await host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/match?paperGrade=SIM-GRADE&widthMm=100&grainDirection=T&requiredLengthM=9"));Assert.Single(await RollHttpData<List<PaperRoll>>(match));
        using var zero=await host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/"+no+"/consume",new{LengthM=0m}));Assert.Equal(HttpStatusCode.BadRequest,zero.StatusCode);
        using var over=await host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/"+no+"/consume",new{LengthM=11m}));Assert.Equal(HttpStatusCode.BadRequest,over.StatusCode);
        using var threshold=await host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/"+no+"/consume",new{LengthM=8m}));Assert.Equal(HttpStatusCode.OK,threshold.StatusCode);
        row=await h.Db.PaperRolls.AsNoTracking().SingleAsync();Assert.Equal(2.12345678m,row.RemainingLengthM);Assert.Equal(PaperRollStatus.Remnant,row.Status);
        using var emptied=await host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/"+no+"/consume",new{LengthM=2.12345678m}));Assert.Equal(HttpStatusCode.OK,emptied.StatusCode);
        row=await h.Db.PaperRolls.AsNoTracking().SingleAsync();Assert.Equal(0m,row.RemainingLengthM);Assert.Equal(PaperRollStatus.Disposed,row.Status);
        using var ended=await host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/"+no+"/consume",new{LengthM=1m}));Assert.Equal(HttpStatusCode.BadRequest,ended.StatusCode);
        no=await Create(RollInput());
        using var split=await host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/slit",new SlitRequest{ParentRollNo=no,ChildWidths=new(){100,120},KeepRemnant=true}));Assert.Equal(HttpStatusCode.OK,split.StatusCode);
        var children=await h.Db.PaperRolls.AsNoTracking().Where(r=>r.ParentRollNo==no).OrderBy(r=>r.WidthMm).ToArrayAsync();Assert.Equal(new[]{80,100,120},children.Select(r=>r.WidthMm));
        Assert.All(children,c=>{Assert.Equal(10.12345678m,c.OriginalLengthM);Assert.Equal(10.12345678m,c.RemainingLengthM);Assert.Equal("price-owner",c.Creator);});Assert.Equal(PaperRollStatus.Remnant,children[0].Status);
        row=await h.Db.PaperRolls.AsNoTracking().SingleAsync(r=>r.RollNo==no);Assert.Equal(PaperRollStatus.Disposed,row.Status);Assert.Equal(0m,row.RemainingLengthM);
        var child=children[1];
        for(var i=0;i<2;i++){using var disposed=await host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/"+child.RollNo+"/dispose"));Assert.Equal(HttpStatusCode.OK,disposed.StatusCode);}
        row=await h.Db.PaperRolls.AsNoTracking().SingleAsync(r=>r.RollNo==child.RollNo);Assert.Equal(PaperRollStatus.Disposed,row.Status);Assert.Equal(10.12345678m,row.RemainingLengthM);
        var maximum=RollInput();maximum.WidthMm=int.MaxValue;no=await Create(maximum);
        using var full=await host.Send(OperationsRequest(actor,HttpMethod.Post,path+"/slit",new SlitRequest{ParentRollNo=no,ChildWidths=new(){int.MaxValue},KeepRemnant=false}));Assert.Equal(HttpStatusCode.OK,full.StatusCode);
        Assert.Equal(int.MaxValue,await h.Db.PaperRolls.Where(r=>r.ParentRollNo==no).Select(r=>r.WidthMm).SingleAsync());
        using var detail=await host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/"+no));Assert.Equal(no,(await RollHttpData<PaperRoll>(detail)).RollNo);
        using var list=await host.Send(OperationsRequest(actor,HttpMethod.Get,path+"?rollNo="+no+"&paperGrade=SIM-GRADE&widthMm="+int.MaxValue+"&grainDirection=T&status=3&warehouseCd=W1"));Assert.Single(await RollHttpData<List<PaperRoll>>(list));
        using var missing=await host.Send(OperationsRequest(actor,HttpMethod.Get,path+"/MISSING"));Assert.Equal(HttpStatusCode.NotFound,missing.StatusCode);
        Assert.Equal(stock,await PalletStockRows(h));Assert.Empty(await h.Db.StockTransactions.ToListAsync());
    }

    private static async Task<T> RollHttpData<T>(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode==HttpStatusCode.OK,await OperationsFailure(response));using var document=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0,document.RootElement.GetProperty("code").GetInt32());return document.RootElement.GetProperty("data").Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static Task<string> RollWriteSet(Harness h)=>h.Db.Database.SqlQuery<string>($"""
        SELECT jsonb_build_object('Rolls',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_PaperRoll" s WHERE s."TenantId"={h.Tenant}),
          'Numbers',(SELECT coalesce(jsonb_agg(to_jsonb(n) ORDER BY n."Id"),'[]') FROM public."T_WmsSequence" n WHERE n."TenantId"={h.Tenant}),
          'Stock',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_Stock" s WHERE s."TenantId"={h.Tenant}),
          'StockTransactions',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_StockTransaction" s WHERE s."TenantId"={h.Tenant}),
          'Outbound',(SELECT coalesce(jsonb_agg(to_jsonb(s) ORDER BY s."Id"),'[]') FROM public."T_OutboundOrder" s WHERE s."TenantId"={h.Tenant}))::text AS "Value"
        """).SingleAsync();
}
