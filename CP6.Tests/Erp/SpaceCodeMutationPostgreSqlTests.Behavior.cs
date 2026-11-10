using System.Net;
using System.Text.Json;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DTOs.Space;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<T> SpaceCodeHttpData<T>(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode==HttpStatusCode.OK,await OperationsFailure(response));
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static Task<string?[]> SpaceCodes(Harness h)=>h.Db.Space_Locations.AsNoTracking().OrderBy(x=>x.Col).Select(x=>x.LocationCode).ToArrayAsync();

    [CurrentPermissionPostgresFact]
    public async Task SpaceCode_second_rebuild_write_failure_preserves_old_codes_and_recovers()
    {
        await using var h=await SpaceCodeSeed();var second=await h.Db.Space_Locations.SingleAsync(x=>x.Col==2);second.LocationCode="OLD-2";await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var input=await SpaceCodeInput(h,"rebuild");var before=await SpaceCodeRows(h);var stock=await PalletStockRows(h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var install=new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_space_rebuild_failure() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN RAISE EXCEPTION 'simulated second space rebuild write failure'; END $fault$;
            CREATE TRIGGER cp6_test_space_rebuild_failure BEFORE UPDATE ON public."Space_Location" FOR EACH ROW
            WHEN (NEW."Id"='{{second.Id}}' AND OLD."LocationCode" IS NULL AND NEW."LocationCode" IS NOT NULL)
            EXECUTE FUNCTION public.cp6_test_space_rebuild_failure();
            """,control);await install.ExecuteNonQueryAsync();
        try
        {
            using var failed=await Send();var after=await SpaceCodeRows(h);var codes=await SpaceCodes(h);
            await DeviceEvidence("space-code-rebuild-fault.json",new{Status=(int)failed.StatusCode,Before=before,After=after,CodesAfter=codes,StockBefore=stock,StockAfter=await PalletStockRows(h)});
            Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.Equal(before,after);Assert.Equal(new[]{"OLD-1","OLD-2","FROZEN"},codes);Assert.Equal(stock,await PalletStockRows(h));
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_space_rebuild_failure ON public.\"Space_Location\"; DROP FUNCTION public.cp6_test_space_rebuild_failure()",control);await remove.ExecuteNonQueryAsync();}
        using var recovered=await Send();Assert.Equal(new[]{"A-01-01","A-01-02"},(await SpaceCodeHttpData<string[]>(recovered)).OrderBy(x=>x));Assert.Equal(new[]{"A-01-01","A-01-02","FROZEN"},await SpaceCodes(h));
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));
    }

    [CurrentPermissionPostgresFact]
    public async Task SpaceCode_concurrent_new_defaults_preserve_one_per_scope()
    {
        await using var h=await SpaceCodeSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var zone=await h.Db.Space_Zones.Select(x=>x.Id).SingleAsync();
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();await using var install=new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_space_default_gate() RETURNS trigger LANGUAGE plpgsql AS $gate$
            BEGIN PERFORM pg_advisory_xact_lock(726456001); RETURN NEW; END $gate$;
            CREATE TRIGGER cp6_test_space_default_gate BEFORE INSERT ON public."Space_CodeRule" FOR EACH ROW
            WHEN (NEW."ScopeId"='{{zone}}' AND NEW."IsDefault") EXECUTE FUNCTION public.cp6_test_space_default_gate();
            """,control);await install.ExecuteNonQueryAsync();
        try
        {
            Task<HttpResponseMessage> first,second;FenceWait firstWait,secondWait;
            await using(var held=await control.BeginTransactionAsync())
            {
                await using var gate=new NpgsqlCommand("SELECT pg_advisory_xact_lock(726456001)",control,held);await gate.ExecuteNonQueryAsync();
                try{first=Send("SIM Concurrent First");firstWait=await WaitForFence(observer,control.ProcessID,"%INSERT%Space_CodeRule%",null,first);second=Send("SIM Concurrent Second");secondWait=await SerialNativeWait(observer,firstWait.Backend,control.ProcessID,"space-code-second-default",second);}
                finally{await held.RollbackAsync();}
            }
            using var a=await first;using var b=await second;var rows=await h.Db.Space_CodeRules.AsNoTracking().Where(x=>x.ScopeType==2&&x.ScopeId==zone).OrderBy(x=>x.RuleName).ToArrayAsync();
            await DeviceEvidence("space-code-competing-defaults.json",new{FirstStatus=(int)a.StatusCode,SecondStatus=(int)b.StatusCode,AuthorityWait=secondWait.Blocker==firstWait.Backend,firstWait,secondWait,Count=rows.Length,Defaults=rows.Where(x=>x.IsDefault).Select(x=>x.RuleName).ToArray()});
            Assert.Equal(HttpStatusCode.OK,a.StatusCode);Assert.Equal(HttpStatusCode.OK,b.StatusCode);Assert.Equal(2,rows.Length);Assert.Equal("SIM Concurrent Second",Assert.Single(rows,x=>x.IsDefault).RuleName);Assert.Equal(firstWait.Backend,secondWait.Blocker);
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_space_default_gate ON public.\"Space_CodeRule\"; DROP FUNCTION public.cp6_test_space_default_gate()",control);await remove.ExecuteNonQueryAsync();}
        Task<HttpResponseMessage> Send(string name)=>host.Send(OperationsRequest(actor,HttpMethod.Post,SpaceCodePath+"/code-rule",SpaceRuleDto(name,2,zone,true)));
    }

    [CurrentPermissionPostgresFact]
    public async Task SpaceCode_moving_a_current_default_clears_the_target_scope_default()
    {
        await using var h=await SpaceCodeSeed();var tenant=await h.Db.Space_CodeRules.AsNoTracking().SingleAsync(x=>x.ScopeType==0);var floor=await h.Db.Space_CodeRules.AsNoTracking().SingleAsync(x=>x.ScopeType==1);var zone=await h.Db.Space_Zones.Select(x=>x.Id).SingleAsync();
        var independent=new Space_CodeRule{Id=Guid.NewGuid(),RuleName="SIM Independent",ScopeType=2,ScopeId=zone,IsDefault=true,Segments=tenant.Segments};h.Db.Space_CodeRules.Add(independent);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);
        using var result=await host.Send(OperationsRequest(actor,HttpMethod.Put,SpaceCodePath+"/code-rule/"+tenant.Id,SpaceRuleDto("SIM Moved",1,floor.ScopeId,true)));
        var rows=await h.Db.Space_CodeRules.AsNoTracking().ToArrayAsync();var defaults=rows.Where(x=>x.ScopeType==1&&x.ScopeId==floor.ScopeId&&x.IsDefault).Select(x=>x.Id).ToArray();
        await DeviceEvidence("space-code-moved-default.json",new{Status=(int)result.StatusCode,Moved=tenant.Id,Defaults=defaults,OtherScopeDefault=rows.Single(x=>x.Id==independent.Id).IsDefault,RemainingOldScope=rows.Count(x=>x.ScopeType==0)});
        Assert.Equal(HttpStatusCode.OK,result.StatusCode);Assert.Equal(tenant.Id,Assert.Single(defaults));Assert.True(rows.Single(x=>x.Id==independent.Id).IsDefault);Assert.DoesNotContain(rows,x=>x.ScopeType==0);
    }

    [CurrentPermissionPostgresFact]
    public async Task SpaceCode_normal_rule_crud_fill_rebuild_single_and_read_semantics_hold()
    {
        await using var h=await SpaceCodeSeed();await using var other=await SpaceCodeSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var otherActor=await Login(host,other);var otherBefore=await SpaceCodeRows(other);var stock=await PalletStockRows(h);var floor=await h.Db.Space_Floors.Select(x=>x.Id).SingleAsync();
        using var create=await Send(HttpMethod.Post,"/code-rule",SpaceRuleDto("SIM New"));var id=(await SpaceCodeHttpData<JsonElement>(create)).GetProperty("id").GetGuid();
        using var edit=await Send(HttpMethod.Put,"/code-rule/"+id,SpaceRuleDto("SIM Edited"));Assert.Equal(HttpStatusCode.OK,edit.StatusCode);Assert.Equal("SIM Edited",await h.Db.Space_CodeRules.Where(x=>x.Id==id).Select(x=>x.RuleName).SingleAsync());
        using var delete=await Send(HttpMethod.Delete,"/code-rule/"+id,new{});Assert.Equal(HttpStatusCode.OK,delete.StatusCode);Assert.False(await h.Db.Space_CodeRules.AnyAsync(x=>x.Id==id));
        using var list=await Send(HttpMethod.Get,"/code-rule",null);var rules=await SpaceCodeHttpData<Space_CodeRule[]>(list);Assert.Equal(2,rules.Length);
        using var otherList=await host.Send(OperationsRequest(otherActor,HttpMethod.Get,SpaceCodePath+"/code-rule"));var otherRules=await SpaceCodeHttpData<Space_CodeRule[]>(otherList);Assert.Equal(2,otherRules.Length);Assert.Empty(rules.Select(x=>x.Id).Intersect(otherRules.Select(x=>x.Id)));
        using var preview=await Send(HttpMethod.Post,"/code-rule/preview",new CodePreviewReq{Segments=SpaceCodeSegments()});var previewData=await SpaceCodeHttpData<CodePreviewResp>(preview);Assert.True(previewData.Precheck.Ok);Assert.Contains("A-03-05",previewData.Samples);
        using var precheck=await Send(HttpMethod.Get,"/floor/"+floor+"/code-precheck",null);var before=await SpaceCodeHttpData<CodePrecheckResp>(precheck);Assert.Equal(1,before.EmptyCodeCount);Assert.Empty(before.DuplicateGroups);Assert.Empty(before.PrecheckErrors);
        using var fill=await Send(HttpMethod.Post,"/floor/"+floor+"/generate-codes",new{mode="fill-empty"});Assert.Equal("A-01-02",Assert.Single(await SpaceCodeHttpData<string[]>(fill)));Assert.Equal(new[]{"OLD-1","A-01-02","FROZEN"},await SpaceCodes(h));
        var loc=await h.Db.Space_Locations.Where(x=>x.Col==1).Select(x=>x.Id).SingleAsync();using var single=await Send(HttpMethod.Post,"/location/"+loc+"/gen-code",new{});Assert.Equal("A-01-01",(await SpaceCodeHttpData<JsonElement>(single)).GetProperty("code").GetString());Assert.Equal(new[]{"A-01-01","A-01-02","FROZEN"},await SpaceCodes(h));
        using var rebuild=await Send(HttpMethod.Post,"/floor/"+floor+"/generate-codes",new{mode="rebuild"});Assert.Equal(new[]{"A-01-01","A-01-02"},(await SpaceCodeHttpData<string[]>(rebuild)).OrderBy(x=>x));var codes=await SpaceCodes(h);Assert.Equal(new[]{"A-01-01","A-01-02","FROZEN"},codes);
        var otherAfter=await SpaceCodeRows(other);var stockAfter=await PalletStockRows(h);Assert.Equal(otherBefore,otherAfter);Assert.Equal(stock,stockAfter);
        await DeviceEvidence("space-code-normal.json",new{Status=200,RuleCount=rules.Length,OtherRuleCount=otherRules.Length,Preview=previewData.Samples,Fill="A-01-02",Single="A-01-01",Codes=codes,OtherBefore=otherBefore,OtherAfter=otherAfter,StockBefore=stock,StockAfter=stockAfter});
        Task<HttpResponseMessage> Send(HttpMethod method,string path,object? body)=>host.Send(OperationsRequest(actor,method,SpaceCodePath+path,body));
    }
}
