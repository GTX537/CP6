using System.Net;
using System.Text.Json;
using CP6.Entity.DomainModels.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string AuditListPath="api/sys/field-audit";
    private const string AuditListMarker="SIM-AUDIT-909";
    private async Task<Harness> AuditListSeed()
    {
        var h=await PalletSeed("create");await GrantFieldAudit(h);
        h.Db.Sys_FieldAuditLogs.AddRange(
            new(){EntityName=AuditListMarker,EntityKey="KEY-1",UserId=h.User,UserName="SIM actor",Operation=2,ChangedAt=new DateTime(2026,11,10,10,0,0),Changes="""[{"Field":"Secret","Old":"RAW-AUDIT-VALUE","New":"RAW-AUDIT-VALUE-NEW"}]"""},
            new(){EntityName=AuditListMarker,EntityKey="KEY-1",UserId=h.User,UserName="SIM actor",Operation=2,ChangedAt=new DateTime(2026,11,10,10,1,0),Changes="RAW-AUDIT-VALUE{"},
            new(){EntityName=AuditListMarker,EntityKey="KEY-2",UserId=h.User,UserName="SIM actor",Operation=1,ChangedAt=new DateTime(2026,11,9,10,0,0),Changes="[]"});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();return h;
    }
    private static async Task<string> AuditListRows(Harness h)=>JsonSerializer.Serialize(await h.Db.Sys_FieldAuditLogs.AsNoTracking().Where(x=>x.EntityName==AuditListMarker).OrderBy(x=>x.Id).ToArrayAsync());

    [TenantManagementPostgresTheory]
    [InlineData("disable")][InlineData("revoke")]
    public async Task AuditList_current_identity_and_action_decide_before_summary_read(string kind)
    {
        await using var h=await AuditListSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="sys-field-audit").Select(m=>m.MenuId).SingleAsync();var users=h.Db.Model.FindEntityType(typeof(Sys_User))!.GetTableName();var before=await AuditListRows(h);var stock=await PalletStockRows(h);
        await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> pending;FenceWait wait;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"Sys_FieldAuditLogs\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                await using var changed=await control.BeginTransactionAsync();var sql=kind=="disable"?"UPDATE public.\""+users+"\" SET \"Enable\"=false WHERE \"TenantId\"=@tenant AND \"Id\"=@user":
                    "DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"MenuId\"=@menu AND \"ActionCode\"='query'";
                await using var change=new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); "+sql,control,changed);change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("user",h.User);change.Parameters.AddWithValue("menu",menu);await change.ExecuteNonQueryAsync();
                pending=Send();wait=await SerialNativeWait(observer,control.ProcessID,business.ProcessID,"audit-list-"+kind,pending);await changed.CommitAsync();
            }
            finally{await locked.RollbackAsync();}
        }
        using var denied=await pending;await DeviceEvidence("audit-list-"+kind+".json",new{Status=(int)denied.StatusCode,AuthorityWait=wait.Blocker==control.ProcessID,wait,Before=before,After=await AuditListRows(h),StockBefore=stock,StockAfter=await PalletStockRows(h)});
        Assert.Equal(kind=="disable"?HttpStatusCode.Unauthorized:HttpStatusCode.Forbidden,denied.StatusCode);Assert.Equal(control.ProcessID,wait.Blocker);Assert.Equal(before,await AuditListRows(h));Assert.Equal(stock,await PalletStockRows(h));
        if(kind=="disable")await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Enable,true));else{h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=menu,ActionCode="query"});await h.Db.SaveChangesAsync();}
        actor=await Login(host,h);using var recovered=await Send();Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);Assert.Equal(before,await AuditListRows(h));
        Task<HttpResponseMessage> Send()=>host.Send(OperationsRequest(actor,HttpMethod.Get,AuditListPath+"?entityName="+AuditListMarker));
    }

    [CurrentPermissionPostgresFact]
    public async Task AuditList_summary_first_completes_before_the_permission_revocation()
    {
        await using var h=await AuditListSeed();var connection=h.Db.Database.GetConnectionString()!;await using var host=await AuthenticationApiProcess.Start(connection);var actor=await Login(host,h);var before=await AuditListRows(h);
        var menu=await h.Db.Sys_Menus.Where(m=>m.MenuKey=="sys-field-audit").Select(m=>m.MenuId).SingleAsync();await using var business=new NpgsqlConnection(connection);await business.OpenAsync();await using var control=new NpgsqlConnection(connection);await control.OpenAsync();await using var observer=new NpgsqlConnection(connection);await observer.OpenAsync();
        Task<HttpResponseMessage> reading;Task<int> revoke;FenceWait readWait;FenceWait? revokeWait=null;var released=false;
        await using(var locked=await business.BeginTransactionAsync())
        {
            await using var barrier=new NpgsqlCommand("LOCK TABLE public.\"Sys_FieldAuditLogs\" IN ACCESS EXCLUSIVE MODE",business,locked);await barrier.ExecuteNonQueryAsync();
            try
            {
                reading=host.Send(OperationsRequest(actor,HttpMethod.Get,AuditListPath+"?entityName="+AuditListMarker));readWait=await SerialNativeWait(observer,business.ProcessID,business.ProcessID,"audit-list-first",reading);
                await using var change=new NpgsqlCommand("SELECT cp6_authentication_v1.acquire(); DELETE FROM public.\"Sys_RoleAction\" WHERE \"TenantId\"=@tenant AND \"RoleId\"=72 AND \"MenuId\"=@menu AND \"ActionCode\"='query'",control);change.Parameters.AddWithValue("tenant",h.Tenant);change.Parameters.AddWithValue("menu",menu);revoke=change.ExecuteNonQueryAsync();
                revokeWait=await OperationLogControlWait(observer,readWait.Backend,revoke);await locked.RollbackAsync();released=true;using var result=await reading;await revoke;
                await DeviceEvidence("audit-list-first.json",new{Status=(int)result.StatusCode,AuthorityWait=revokeWait?.Blocker==readWait.Backend,readWait,revokeWait,Before=before,After=await AuditListRows(h)});
                Assert.Equal(HttpStatusCode.OK,result.StatusCode);Assert.NotNull(revokeWait);Assert.Equal(readWait.Backend,revokeWait!.Blocker);Assert.Equal(before,await AuditListRows(h));
            }
            finally{if(!released)await locked.RollbackAsync();}
        }
        using var denied=await host.Send(OperationsRequest(actor,HttpMethod.Get,AuditListPath));Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task AuditList_filters_summary_cap_and_tie_order_preserve_original_history()
    {
        await using var h=await AuditListSeed();await using var other=await AuditListSeed();await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var actor=await Login(host,h);var before=await AuditListRows(h);var otherBefore=await AuditListRows(other);
        using var response=await host.Send(OperationsRequest(actor,HttpMethod.Get,AuditListPath+"?entityName="+AuditListMarker+"&entityKey=KEY-1&userId="+h.User+"&from=2026-11-10&to=2026-11-10&pageSize=1"));Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var body=await response.Content.ReadAsStringAsync();Assert.DoesNotContain("RAW-AUDIT-VALUE",body);using var doc=JsonDocument.Parse(body);Assert.Equal(2,doc.RootElement.GetProperty("total").GetInt32());var first=Assert.Single(doc.RootElement.GetProperty("rows").EnumerateArray());Assert.Equal("UNREADABLE",first.GetProperty("evidenceStatus").GetString());Assert.Equal(JsonValueKind.Null,first.GetProperty("changeCount").ValueKind);Assert.False(first.TryGetProperty("changes",out _));
        using var second=await host.Send(OperationsRequest(actor,HttpMethod.Get,AuditListPath+"?entityName="+AuditListMarker+"&entityKey=KEY-1&page=2&pageSize=1"));Assert.Equal(HttpStatusCode.OK,second.StatusCode);using var next=JsonDocument.Parse(await second.Content.ReadAsStringAsync());var readable=Assert.Single(next.RootElement.GetProperty("rows").EnumerateArray());Assert.Equal("READABLE",readable.GetProperty("evidenceStatus").GetString());Assert.Equal(1,readable.GetProperty("changeCount").GetInt32());
        Assert.Equal(before,await AuditListRows(h));Assert.Equal(otherBefore,await AuditListRows(other));
        for(var i=1;i<=203;i++)h.Db.Sys_FieldAuditLogs.Add(new(){Id=AuditListCapId(i),EntityName=AuditListMarker,EntityKey="CAP",UserId=h.User,ChangedAt=new DateTime(2026,11,10,12,0,0),Changes="[]"});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var all=await AuditListRows(h);
        using var capped=await host.Send(OperationsRequest(actor,HttpMethod.Get,AuditListPath+"?entityName="+AuditListMarker+"&entityKey=CAP&page=0&pageSize=2147483647"));Assert.Equal(HttpStatusCode.OK,capped.StatusCode);using var cap=JsonDocument.Parse(await capped.Content.ReadAsStringAsync());var rows=cap.RootElement.GetProperty("rows");Assert.Equal(203,cap.RootElement.GetProperty("total").GetInt32());Assert.Equal(200,rows.GetArrayLength());Assert.Equal(AuditListCapId(203),rows[0].GetProperty("id").GetGuid());Assert.Equal(AuditListCapId(4),rows[199].GetProperty("id").GetGuid());
        using var tail=await host.Send(OperationsRequest(actor,HttpMethod.Get,AuditListPath+"?entityName="+AuditListMarker+"&entityKey=CAP&page=2&pageSize=200"));Assert.Equal(HttpStatusCode.OK,tail.StatusCode);using var end=JsonDocument.Parse(await tail.Content.ReadAsStringAsync());Assert.Equal(3,end.RootElement.GetProperty("rows").GetArrayLength());Assert.Equal(all,await AuditListRows(h));Assert.Equal(otherBefore,await AuditListRows(other));
        await DeviceEvidence("audit-list-normal.json",new{Status=(int)response.StatusCode,FilteredTotal=2,CapTotal=203,CapReturned=200,TailReturned=3,OtherBefore=otherBefore,OtherAfter=await AuditListRows(other),Before=all,After=await AuditListRows(h)});
    }
    private static Guid AuditListCapId(int i)=>Guid.Parse("00000000-0000-0000-0000-"+i.ToString("D12"));
}
