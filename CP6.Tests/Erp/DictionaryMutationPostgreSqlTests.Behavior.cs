using System.Net;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Dictionary_native_late_cascade_delete_fault_preserves_both_tables_and_recovers()
    {
        await using var h=await DictionarySeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var input=await DictionaryInput(h,"delete-type");var before=await DictionaryRows(h);var stock=await PalletStockRows(h);var code=DictionaryCode(h,"A");
        using var warm=await Options();Assert.Equal("Original",await DictionaryOptionLabel(warm));await using var fault=new NpgsqlConnection(h.Db.Database.GetConnectionString()!);await fault.OpenAsync();await using var install=new NpgsqlCommand($$"""
            CREATE SEQUENCE public.cp6_test_dictionary_once;
            CREATE FUNCTION public.cp6_test_dictionary_failure() RETURNS trigger LANGUAGE plpgsql AS $fault$
            BEGIN IF nextval('public.cp6_test_dictionary_once')=1 THEN RAISE EXCEPTION 'simulated dictionary type delete failure'; END IF; RETURN OLD; END $fault$;
            CREATE TRIGGER cp6_test_dictionary_failure BEFORE DELETE ON public."Sys_DictTypes" FOR EACH ROW
            WHEN (OLD."TypeCode"='{{code}}') EXECUTE FUNCTION public.cp6_test_dictionary_failure();
            """,fault);await install.ExecuteNonQueryAsync();
        try
        {
            using var failed=await Send();using var cached=await Options();await DeviceEvidence("dictionary-cascade-fault.json",new{Status=(int)failed.StatusCode,Before=before,After=await DictionaryRows(h),CachedLabel=await DictionaryOptionLabel(cached),StockBefore=stock,StockAfter=await PalletStockRows(h)});
            Assert.Equal(HttpStatusCode.ServiceUnavailable,failed.StatusCode);Assert.Equal(before,await DictionaryRows(h));Assert.Equal(stock,await PalletStockRows(h));
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_dictionary_failure ON public.\"Sys_DictTypes\"; DROP FUNCTION public.cp6_test_dictionary_failure(); DROP SEQUENCE public.cp6_test_dictionary_once",fault);await remove.ExecuteNonQueryAsync();}
        using var recovered=await Send();Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);using var empty=await Options();Assert.Equal("",await DictionaryOptionLabel(empty));Assert.Empty(await h.Db.Sys_DictTypes.Where(x=>x.TypeCode==code).ToArrayAsync());Assert.Empty(await h.Db.Sys_DictDatas.Where(x=>x.TypeCode==code).ToArrayAsync());
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));
        Task<HttpResponseMessage> Options()=>host.Send(OperationsRequest(actor,HttpMethod.Get,DictionaryPath+"/options/"+code));
    }

    [CurrentPermissionPostgresFact]
    public async Task Dictionary_competing_same_type_adds_obey_the_existing_duplicate_rule()
    {
        await using var h=await DictionarySeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var input=await DictionaryInput(h,"create-type");var code=DictionaryCode(h,"C");
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();await using var install=new NpgsqlCommand($$"""
            CREATE FUNCTION public.cp6_test_dictionary_gate() RETURNS trigger LANGUAGE plpgsql AS $gate$
            BEGIN PERFORM pg_advisory_xact_lock(726455001); RETURN NEW; END $gate$;
            CREATE TRIGGER cp6_test_dictionary_gate BEFORE INSERT ON public."Sys_DictTypes" FOR EACH ROW
            WHEN (NEW."TypeCode"='{{code}}') EXECUTE FUNCTION public.cp6_test_dictionary_gate();
            """,control);await install.ExecuteNonQueryAsync();
        try
        {
            Task<HttpResponseMessage> first,second;FenceWait firstWait,secondWait;
            await using(var held=await control.BeginTransactionAsync())
            {
                await using var gate=new NpgsqlCommand("SELECT pg_advisory_xact_lock(726455001)",control,held);await gate.ExecuteNonQueryAsync();
                try{first=Send();firstWait=await WaitForFence(observer,control.ProcessID,"%INSERT%Sys_DictTypes%",null,first);second=Send();secondWait=await SerialNativeWait(observer,firstWait.Backend,control.ProcessID,"dictionary-second-add",second);}
                finally{await held.RollbackAsync();}
            }
            using var a=await first;using var b=await second;var rows=await h.Db.Sys_DictTypes.AsNoTracking().Where(x=>x.TypeCode==code).ToArrayAsync();await DeviceEvidence("dictionary-two-adds.json",new{FirstStatus=(int)a.StatusCode,SecondStatus=(int)b.StatusCode,AuthorityWait=secondWait.Blocker==firstWait.Backend,firstWait,secondWait,Count=rows.Length});
            Assert.Equal(HttpStatusCode.OK,a.StatusCode);Assert.Equal(HttpStatusCode.BadRequest,b.StatusCode);Assert.Single(rows);Assert.Equal(firstWait.Backend,secondWait.Blocker);
        }
        finally{await using var remove=new NpgsqlCommand("DROP TRIGGER cp6_test_dictionary_gate ON public.\"Sys_DictTypes\"; DROP FUNCTION public.cp6_test_dictionary_gate()",control);await remove.ExecuteNonQueryAsync();}
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));
    }

    [CurrentPermissionPostgresFact]
    public async Task Dictionary_edit_type_cannot_take_an_existing_type_code()
    {
        await using var h=await DictionarySeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var code=DictionaryCode(h,"A");var row=await h.Db.Sys_DictTypes.AsNoTracking().SingleAsync(x=>x.TypeCode==code);var before=await DictionaryRows(h);
        using var response=await host.Send(OperationsRequest(actor,HttpMethod.Put,DictionaryPath+"/types",new{id=row.Id,typeCode=DictionaryCode(h,"B"),typeName="Collision",enable=true,orderNo=3}));await DeviceEvidence("dictionary-edit-collision.json",new{Status=(int)response.StatusCode,Before=before,After=await DictionaryRows(h)});
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal(before,await DictionaryRows(h));
    }
}
