using System.Net;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string DictionaryPath="api/Dict";
    private static string DictionaryPrefix(Harness h)=>"SIM"+h.Tenant.ToString("N")[..12];
    private static string DictionaryCode(Harness h,string suffix)=>DictionaryPrefix(h)+suffix;
    private async Task<Harness> DictionarySeed()
    {
        var h=await PalletSeed("create");var db=h.Db;var menu=await db.Sys_Menus.SingleOrDefaultAsync(m=>m.MenuKey=="dict"||m.RoutePath=="/dict");
        if(menu==null){menu=new(){MenuId=9077,MenuKey="dict",MenuName="SIM dictionary"};db.Sys_Menus.Add(menu);await db.SaveChangesAsync();}
        foreach(var action in new[]{"add","edit","delete"})
        {
            if(!await db.Sys_MenuActions.AnyAsync(a=>a.MenuId==menu.MenuId&&a.ActionCode==action))db.Sys_MenuActions.Add(new(){MenuId=menu.MenuId,ActionCode=action,ActionName=action});
            db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu.MenuId,ActionCode=action});
        }
        db.Sys_DictTypes.AddRange(new(){TypeCode=DictionaryCode(h,"A"),TypeName="SIM A",OrderNo=1},new(){TypeCode=DictionaryCode(h,"B"),TypeName="SIM B",OrderNo=2});
        db.Sys_DictDatas.AddRange(new(){TypeCode=DictionaryCode(h,"A"),Value="A",Label="Original",OrderNo=1,Enable=true},new(){TypeCode=DictionaryCode(h,"A"),Value="OFF",Label="Disabled",OrderNo=2,Enable=false},new(){TypeCode=DictionaryCode(h,"B"),Value="B",Label="Other",OrderNo=1,Enable=true});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();return h;
    }
    private sealed record DictionaryHttpInput(HttpMethod Method,string Path,object Body);
    private static async Task<DictionaryHttpInput> DictionaryInput(Harness h,string operation)
    {
        var code=DictionaryCode(h,"A");var type=await h.Db.Sys_DictTypes.AsNoTracking().Where(x=>x.TypeCode==code).SingleAsync();var data=await h.Db.Sys_DictDatas.AsNoTracking().Where(x=>x.TypeCode==code&&x.Value=="A").SingleAsync();
        return operation switch
        {
            "create-type"=>new(HttpMethod.Post,DictionaryPath+"/types",new{typeCode=DictionaryCode(h,"C"),typeName="New",enable=true,orderNo=3}),
            "edit-type"=>new(HttpMethod.Put,DictionaryPath+"/types",new{id=type.Id,typeCode=code,typeName="Changed",enable=true,orderNo=3}),
            "delete-type"=>new(HttpMethod.Delete,DictionaryPath+"/types",new[]{type.Id}),
            "create-data"=>new(HttpMethod.Post,DictionaryPath+"/data",new{typeCode=code,value="NEW",label="New",enable=true,orderNo=3}),
            "edit-data"=>new(HttpMethod.Put,DictionaryPath+"/data",new{id=data.Id,typeCode=code,value="A",label="Changed",enable=true,orderNo=4}),
            "delete-data"=>new(HttpMethod.Delete,DictionaryPath+"/data",new[]{data.Id}),
            _=>throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }
    private static async Task<string> DictionaryRows(Harness h)
    {
        var prefix=DictionaryPrefix(h);return JsonSerializer.Serialize(new{Types=await h.Db.Sys_DictTypes.AsNoTracking().Where(x=>x.TypeCode.StartsWith(prefix)).OrderBy(x=>x.Id).ToArrayAsync(),Data=await h.Db.Sys_DictDatas.AsNoTracking().Where(x=>x.TypeCode.StartsWith(prefix)).OrderBy(x=>x.Id).ToArrayAsync()});
    }

    [TenantManagementPostgresTheory]
    [InlineData("create-type")][InlineData("edit-type")][InlineData("delete-type")]
    [InlineData("create-data")][InlineData("edit-data")][InlineData("delete-data")]
    public async Task Dictionary_existing_six_writes_decide_current_tenant_and_session(string operation)
    {
        await using var h=await DictionarySeed();await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);var input=await DictionaryInput(h,operation);
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);var before=await DictionaryRows(h);var stock=await PalletStockRows(h);
        await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var target=new NpgsqlConnection(connection);await target.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> pending;FenceWait pauseWait,commandWait;TenantManagementReceiptV1 suspended;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"Sys_DictTypes\", public.\"Sys_DictDatas\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var held=await target.BeginTransactionAsync();await using var tenantLock=new NpgsqlCommand("SELECT 1 FROM public.\"Sys_Tenants\" WHERE \"Id\"=@tenant FOR UPDATE",target,held);tenantLock.Parameters.AddWithValue("tenant",h.Tenant);Assert.Equal(1,await tenantLock.ExecuteScalarAsync());
                var pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("dictionary-"+operation,active.Revision,"suspend",null)));
                try{pauseWait=await WaitForFence(observer,target.ProcessID,"%UPDATE%Sys_Tenants%",null,pause);pending=Send();commandWait=await SerialNativeWait(observer,pauseWait.Backend,business.ProcessID,"dictionary-"+operation,pending);}
                finally{await held.RollbackAsync();}
                using var paused=await pause;suspended=await NumberHttpData<TenantManagementReceiptV1>(paused);Assert.False(suspended.Snapshot.Enable);
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("dictionary-"+operation+"-pause.json",new{Operation=operation,Status=(int)denied.StatusCode,AuthorityWait=commandWait.Blocker==pauseWait.Backend,pauseWait,commandWait,Before=before,After=await DictionaryRows(h),StockBefore=stock,StockAfter=await PalletStockRows(h)});
        Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);Assert.Equal(pauseWait.Backend,commandWait.Blocker);Assert.Equal(before,await DictionaryRows(h));Assert.Equal(stock,await PalletStockRows(h));
        using var restored=await host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("dictionary-restore-"+operation,suspended.Snapshot.Revision,"reactivate",null)));Assert.True((await NumberHttpData<TenantManagementReceiptV1>(restored)).Snapshot.Enable);actor=await Login(host,h);using var recovered=await Send();Assert.True(recovered.StatusCode==HttpStatusCode.OK,await OperationsFailure(recovered));
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));
    }

    [TenantManagementPostgresTheory]
    [InlineData("create-type","add")][InlineData("edit-type","edit")][InlineData("delete-type","delete")]
    public async Task Dictionary_current_action_revocation_wins_before_the_write(string operation,string action)
    {
        await using var h=await DictionarySeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var input=await DictionaryInput(h,operation);var before=await DictionaryRows(h);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="dict"||m.RoutePath=="/dict").Select(m=>m.MenuId).SingleAsync();await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"Sys_DictTypes\", public.\"Sys_DictDatas\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var changed=await control.BeginTransactionAsync();await using var change=new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"MenuId\"=@menu AND \"ActionCode\"=@action",control,changed);change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("menu",menu);change.Parameters.AddWithValue("action",action);await change.ExecuteNonQueryAsync();
                pending=host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"dictionary-revoke-"+action,pending);await changed.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("dictionary-revoke-"+action+".json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,wait,Before=before,After=await DictionaryRows(h)});Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(control.ProcessID,wait.Blocker);Assert.Equal(before,await DictionaryRows(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Dictionary_business_first_commits_before_the_later_tenant_pause()
    {
        await using var h=await DictionarySeed();await using var administrator=await TenantManagementSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var manager=await Login(host,administrator);var input=await DictionaryInput(h,"create-type");
        using var current=await host.Send(OperationsRequest(manager,HttpMethod.Get,ManagementPath(h.Tenant)));var active=await NumberHttpData<TenantManagementSnapshotV1>(current);await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();Task<HttpResponseMessage> writing,pause;FenceWait businessWait;FenceWait? pauseWait=null;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"Sys_DictTypes\", public.\"Sys_DictDatas\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                writing=host.Send(OperationsRequest(actor,input.Method,input.Path,input.Body));businessWait=await SerialNativeWait(observer,business.ProcessID,business.ProcessID,"dictionary-business-first",writing);
                pause=host.Send(OperationsRequest(manager,HttpMethod.Post,ManagementPath(h.Tenant,"/commands"),new TenantManagementCommandV1("dictionary-business-first",active.Revision,"suspend",null)));
                try{pauseWait=await SerialNativeWait(observer,businessWait.Backend,business.ProcessID,"dictionary-pause-after",pause);}catch(Xunit.Sdk.XunitException) when(pause.IsCompletedSuccessfully){ }
            }
            finally{await locked.RollbackAsync();}
        }
        using var committed=await writing;using var paused=await pause;var receipt=await NumberHttpData<TenantManagementReceiptV1>(paused);await DeviceEvidence("dictionary-business-first.json",new{Status=(int)committed.StatusCode,AuthorityWait=pauseWait?.Blocker==businessWait.Backend,businessWait,pauseWait});
        Assert.Equal(HttpStatusCode.OK,committed.StatusCode);Assert.False(receipt.Snapshot.Enable);Assert.NotNull(pauseWait);Assert.Equal(businessWait.Backend,pauseWait!.Blocker);var code=DictionaryCode(h,"C");Assert.Single(await h.Db.Sys_DictTypes.Where(x=>x.TypeCode==code).ToArrayAsync());
    }
}
