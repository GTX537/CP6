using System.Net;
using System.Text.Json;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<string> DictionaryOptionLabel(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode==HttpStatusCode.OK,await OperationsFailure(response));using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return string.Join("|",json.RootElement.EnumerateArray().Select(x=>x.GetProperty("label").GetString()));
    }
    private static async Task<int> DictionaryResponseId(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode==HttpStatusCode.OK,await OperationsFailure(response));using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());return json.RootElement.GetProperty("id").GetInt32();
    }

    [CurrentPermissionPostgresFact]
    public async Task Dictionary_normal_six_crud_routes_preserve_shared_options_and_known_commit_invalidation()
    {
        await using var h=await DictionarySeed();await using var other=await Seed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var otherActor=await Login(host,other);var stock=await PalletStockRows(h);var code=DictionaryCode(h,"C");
        using var shared=await host.Send(OperationsRequest(otherActor,HttpMethod.Get,DictionaryPath+"/options/"+DictionaryCode(h,"A")));Assert.Equal("Original",await DictionaryOptionLabel(shared));
        using var empty=await Options();Assert.Equal("",await DictionaryOptionLabel(empty));
        using var created=await host.Send(OperationsRequest(actor,HttpMethod.Post,DictionaryPath+"/types",new{typeCode=code,typeName="New",enable=true,orderNo=3}));var type=await DictionaryResponseId(created);
        using var edited=await host.Send(OperationsRequest(actor,HttpMethod.Put,DictionaryPath+"/types",new{id=type,typeCode=code,typeName="Edited",enable=true,orderNo=4}));Assert.Equal(type,await DictionaryResponseId(edited));
        using var added=await Add("Created");var data=await DictionaryResponseId(added);using var actual=await Options();Assert.Equal("Created",await DictionaryOptionLabel(actual));
        using var changed=await host.Send(OperationsRequest(actor,HttpMethod.Put,DictionaryPath+"/data",new{id=data,typeCode=code,value="C",label="Updated",enable=true,orderNo=1}));Assert.Equal(data,await DictionaryResponseId(changed));using var updated=await Options();Assert.Equal("Updated",await DictionaryOptionLabel(updated));
        using var deleted=await host.Send(OperationsRequest(actor,HttpMethod.Delete,DictionaryPath+"/data",new[]{data}));Assert.Equal(HttpStatusCode.OK,deleted.StatusCode);using var removed=await Options();Assert.Equal("",await DictionaryOptionLabel(removed));
        using var again=await Add("Reborn");await DictionaryResponseId(again);using var reborn=await Options();Assert.Equal("Reborn",await DictionaryOptionLabel(reborn));
        using var cascade=await host.Send(OperationsRequest(actor,HttpMethod.Delete,DictionaryPath+"/types",new[]{type}));Assert.Equal(HttpStatusCode.OK,cascade.StatusCode);using var gone=await Options();Assert.Equal("",await DictionaryOptionLabel(gone));Assert.Empty(await h.Db.Sys_DictTypes.Where(x=>x.TypeCode==code).ToArrayAsync());Assert.Empty(await h.Db.Sys_DictDatas.Where(x=>x.TypeCode==code).ToArrayAsync());
        using var duplicate=await host.Send(OperationsRequest(actor,HttpMethod.Post,DictionaryPath+"/types",new{typeCode=DictionaryCode(h,"A"),typeName="Duplicate",enable=true,orderNo=0}));Assert.Equal(HttpStatusCode.BadRequest,duplicate.StatusCode);
        using var missingType=await host.Send(OperationsRequest(actor,HttpMethod.Put,DictionaryPath+"/types",new{id=int.MaxValue,typeCode=code,typeName="Missing",enable=true,orderNo=0}));Assert.Equal(HttpStatusCode.NotFound,missingType.StatusCode);
        using var missingData=await host.Send(OperationsRequest(actor,HttpMethod.Put,DictionaryPath+"/data",new{id=int.MaxValue,typeCode=code,value="M",label="Missing",enable=true,orderNo=0}));Assert.Equal(HttpStatusCode.NotFound,missingData.StatusCode);
        Assert.Equal(stock,await PalletStockRows(h));await DeviceEvidence("dictionary-normal.json",new{Status=200,SharedLabel="Original",CreatedLabel="Created",UpdatedLabel="Updated",AfterDataDelete="",AfterCascade="",MissingTypeStatus=(int)missingType.StatusCode,MissingDataStatus=(int)missingData.StatusCode,DuplicateStatus=(int)duplicate.StatusCode,StockBefore=stock,StockAfter=await PalletStockRows(h)});
        Task<HttpResponseMessage> Options()=>host.Send(OperationsRequest(actor,HttpMethod.Get,DictionaryPath+"/options/"+code));
        Task<HttpResponseMessage> Add(string label)=>host.Send(OperationsRequest(actor,HttpMethod.Post,DictionaryPath+"/data",new{typeCode=code,value="C",label,enable=true,orderNo=1}));
    }

    [CurrentPermissionPostgresFact]
    public async Task Dictionary_options_are_invalidated_after_a_known_commit_not_inside_the_parent()
    {
        await using var h=await DictionarySeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var input=await DictionaryInput(h,"edit-data");var code=DictionaryCode(h,"A");var data=await h.Db.Sys_DictDatas.AsNoTracking().Where(x=>x.TypeCode==code&&x.Value=="A").Select(x=>x.Id).SingleAsync();
        using var warm=await Options();Assert.Equal("Original",await DictionaryOptionLabel(warm));await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();await using var install=new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_dictionary_commit_gate() RETURNS trigger LANGUAGE plpgsql AS $gate$
            BEGIN PERFORM pg_advisory_xact_lock(726455002); RETURN NEW; END $gate$;
            CREATE CONSTRAINT TRIGGER cp6_test_dictionary_commit_gate AFTER UPDATE ON public."Sys_DictDatas"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW."Id"={{data}}) EXECUTE FUNCTION public.cp6_test_dictionary_commit_gate();
            """,control);await install.ExecuteNonQueryAsync();
        try
        {
            Task<HttpResponseMessage> pending;FenceWait wait;string during;
            await using(var held=await control.BeginTransactionAsync())
            {
                await using var gate=new NpgsqlCommand("SELECT pg_advisory_xact_lock(726455002)",control,held);await gate.ExecuteNonQueryAsync();
                try
                {
                    pending=host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));wait=await WaitForFence(observer,control.ProcessID,"%",null,pending);
                    using var visible=await Options();during=await DictionaryOptionLabel(visible);Assert.Equal("Original",during);
                }
                finally{await held.RollbackAsync();}
            }
            using var committed=await pending;Assert.True(committed.StatusCode==HttpStatusCode.OK,await OperationsFailure(committed));using var fresh=await Options();var after=await DictionaryOptionLabel(fresh);
            await DeviceEvidence("dictionary-commit-cache.json",new{Status=(int)committed.StatusCode,BeforeCommit=during,AfterCommit=after,wait});Assert.Equal("Changed",after);
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_dictionary_commit_gate ON public.\"Sys_DictDatas\"; DROP FUNCTION public.cp6_test_dictionary_commit_gate()",control);await remove.ExecuteNonQueryAsync();}
        Task<HttpResponseMessage> Options()=>host.Send(OperationsRequest(actor,HttpMethod.Get,DictionaryPath+"/options/"+code));
    }
}
