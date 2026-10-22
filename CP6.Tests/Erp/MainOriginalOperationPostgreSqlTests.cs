using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using CP6.Entity.DTOs;
using CP6.Tests.Platform;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OrderPriceCorrectionCommandV1 Command(Harness h,Guid? id=null,decimal price=4m)
        => new(){OperationId=id??Guid.NewGuid(),Items=h.Request(price).Items};
    private static Task<int> Operations(Harness h) => h.Db.Database.SqlQuery<int>(
        $"SELECT count(*)::int AS \"Value\" FROM cp6_main_owner_v1.operations WHERE tenant_id={h.Tenant}").SingleAsync();

    [CurrentPermissionPostgresFact]
    public async Task Original_command_commits_business_audit_intent_and_result_once_with_exact_input_conflict()
    {
        await using var h=await Seed();await GrantQuery(h);
        var command=Command(h);command.Items[0].SetUnitPriceAfter=12.25m;
        command.Items[0].RowVersion=await h.Db.OrderDetails.Where(d=>d.WebOrderDetailNo==1).Select(d=>d.RowVersion).SingleAsync();
        var result=await h.Owner.ExecuteCommandAsync(command);h.Db.ChangeTracker.Clear();
        Assert.Equal(command.OperationId,result.OperationId);Assert.Equal(2,result.WfRequestedCount);
        var replay=await h.Owner.ExecuteCommandAsync(command);Assert.Equal(JsonSerializer.Serialize(result),JsonSerializer.Serialize(replay));
        Assert.Equal(1,await Operations(h));Assert.Equal(2,await Intents(h));
        var audit=await h.Db.Database.SqlQuery<string>($"SELECT audit_json::text AS \"Value\" FROM cp6_main_owner_v1.operations WHERE tenant_id={h.Tenant}").SingleAsync();
        Assert.Contains("12.25",audit);Assert.Contains("before",audit);Assert.Contains("after",audit);
        command.Items[0].IndividualUnitPriceAfter=9m;
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Owner.ExecuteCommandAsync(command))).Message);
        Assert.Equal(1,await Operations(h));Assert.Equal(2,await Intents(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Original_command_lost_commit_ack_recovers_after_write_revocation_without_any_business_or_root_write()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);await GrantQuery(h);var command=Command(h);
        fault.Armed=true;
        Assert.Equal("PRICE_CORRECTION_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Owner.ExecuteCommandAsync(command))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="correct").ExecuteDeleteAsync();
        var revision=await h.Db.Database.SqlQueryRaw<long>("SELECT revision AS \"Value\" FROM cp6_platform_admin_guard_v1.root").SingleAsync();
        var recovered=await h.Owner.ReadOperationAsync(command.OperationId);
        Assert.Equal("COMMITTED",recovered.State);Assert.Equal(1,recovered.Result!.UpdatedCount);
        Assert.Equal(revision,await h.Db.Database.SqlQueryRaw<long>("SELECT revision AS \"Value\" FROM cp6_platform_admin_guard_v1.root").SingleAsync());
        Assert.Equal(1,(await h.Owner.ExecuteCommandAsync(command)).UpdatedCount);
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Owner.ExecuteCommandAsync(Command(h)))).Message);
        Assert.Equal(1,await Operations(h));Assert.Equal(1,await Intents(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Original_command_final_authority_failure_rolls_back_journal_audit_business_and_intent()
    {
        var hook=new CommandHook();await using var h=await Seed(hook);await GrantQuery(h);var count=0;var command=Command(h);
        hook.Before=c=>{if(c.CommandText.Contains("WITH field_subject") && ++count==2)throw new IOException("owned final authority fault");return Task.CompletedTask;};
        await Assert.ThrowsAnyAsync<Exception>(()=>h.Owner.ExecuteCommandAsync(command));hook.Before=null;
        Assert.Equal(0,await Operations(h));Assert.Equal(0,await Intents(h));
        Assert.Equal("UNKNOWN",(await h.Owner.ReadOperationAsync(command.OperationId)).State);
        Assert.All(await h.Db.OrderDetails.AsNoTracking().ToArrayAsync(),x=>Assert.Equal(3m,x.IndividualUnitPrice));
    }

    [CurrentPermissionPostgresFact]
    public async Task Original_command_unknown_is_not_rollback_and_explicit_close_fences_late_commands()
    {
        await using var h=await Seed();await GrantQuery(h);var command=Command(h);
        Assert.Equal("UNKNOWN",(await h.Owner.ReadOperationAsync(command.OperationId)).State);Assert.Equal(0,await Operations(h));
        Assert.Equal("CLOSED",(await h.Owner.CloseOperationAsync(command.OperationId)).State);
        Assert.Equal("CLOSED",(await h.Owner.CloseOperationAsync(command.OperationId)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Owner.ExecuteCommandAsync(command))).Message);
        Assert.Equal(1,await Operations(h));Assert.Equal(0,await Intents(h));
        var next=Command(h);await h.Owner.ExecuteCommandAsync(next);h.Db.ChangeTracker.Clear();
        Assert.Equal("COMMITTED",(await h.Owner.CloseOperationAsync(next.OperationId)).State);Assert.Equal(2,await Operations(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Original_command_recovery_rechecks_current_fields_objects_and_query_without_leaking_other_tenant()
    {
        await using var h=await Seed();await GrantQuery(h);var command=Command(h);await h.Owner.ExecuteCommandAsync(command);h.Db.ChangeTracker.Clear();
        await using var other=await Seed();await GrantQuery(other);
        Assert.Equal("UNKNOWN",(await other.Owner.ReadOperationAsync(command.OperationId)).State);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="order",FieldName="Amount",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal("FIELD_READ_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Owner.ReadOperationAsync(command.OperationId))).Message);
        await h.Db.Sys_RoleFieldPerms.ExecuteDeleteAsync();
        await h.Db.Sys_RoleDataScopes.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        await h.Db.OrderDetails.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Creator,"someone-else"));
        Assert.Equal("OBJECT_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Owner.ReadOperationAsync(command.OperationId))).Message);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="query").ExecuteDeleteAsync();
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>h.Owner.ReadOperationAsync(command.OperationId))).Message);
        Assert.Equal(1,await Intents(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Original_command_concurrent_same_key_applies_once_and_different_input_conflicts()
    {
        await using var h=await Seed();await GrantQuery(h);var command=Command(h);
        async Task<OrderPriceCorrectionBatchResultDto> Execute(decimal price)
        {
            await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
            var owner=new OrderPriceCorrectionOwnerV1(db,new HttpContextAccessor{HttpContext=h.Http},_=>Task.FromResult(h.User),h.Catalog.Capture);
            return await owner.ExecuteCommandAsync(Command(h,command.OperationId,price));
        }
        var results=await Task.WhenAll(Execute(4m),Execute(4m));Assert.All(results,r=>Assert.Equal(command.OperationId,r.OperationId));
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Execute(5m))).Message);
        Assert.Equal(1,await Operations(h));Assert.Equal(1,await Intents(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Original_command_receipts_reject_mutation_delete_and_truncate()
    {
        await using var h=await Seed();await GrantQuery(h);await h.Owner.ExecuteCommandAsync(Command(h));h.Db.ChangeTracker.Clear();
        foreach(var sql in new[]{"UPDATE cp6_main_owner_v1.operations SET state='CLOSED' WHERE false",
            "DELETE FROM cp6_main_owner_v1.operations WHERE false","TRUNCATE cp6_main_owner_v1.operations"})
            Assert.Equal("23514",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync(sql))).SqlState);
        Assert.Equal(1,await Operations(h));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task Original_command_close_and_execute_share_one_actual_fence(bool closeFirst)
    {
        var hook=new CommandHook();await using var h=await Seed(hook);await GrantQuery(h);var command=Command(h);
        Task? competing=null;var blocked=false;
        hook.After=async sql=>
        {
            if(!sql.CommandText.Contains("actor_id AS"))return;hook.After=null;
            // Independent requests must not share HttpContextAccessor's flowed mutable holder.
            using(ExecutionContext.SuppressFlow()) competing=Task.Run(async()=>
            {
                await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
                var owner=new OrderPriceCorrectionOwnerV1(db,new HttpContextAccessor{HttpContext=h.Http},_=>Task.FromResult(h.User),h.Catalog.Capture);
                if(closeFirst)Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ExecuteCommandAsync(command))).Message);
                else Assert.Equal("COMMITTED",(await owner.CloseOperationAsync(command.OperationId)).State);
            });
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while(!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS(SELECT FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'
                AND query LIKE '%cp6_authentication_v1.acquire()%') AS "Value"
                """).SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            Assert.False(competing.IsCompleted);blocked=true;
        };
        Exception? firstFailure=null;
        try
        {
            if(closeFirst)Assert.Equal("CLOSED",(await h.Owner.CloseOperationAsync(command.OperationId)).State);
            else Assert.Equal(1,(await h.Owner.ExecuteCommandAsync(command)).UpdatedCount);
        }
        catch(Exception error){firstFailure=error;throw;}
        finally
        {
            if(competing!=null)try{await competing;}catch(Exception second)
            {if(firstFailure!=null)throw new AggregateException(firstFailure,second);throw;}
        }
        Assert.True(blocked);Assert.Equal(1,await Operations(h));Assert.Equal(closeFirst?0:1,await Intents(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Original_command_other_actor_cannot_disclose_or_close_existing_original()
    {
        await using var h=await Seed();await GrantQuery(h);var command=Command(h);await h.Owner.ExecuteCommandAsync(command);h.Db.ChangeTracker.Clear();
        var actor=new CP6.Entity.DomainModels.Sys.Sys_User {UserName="other-price-reader",Password="unused",RoleId=72};
        h.Db.Sys_Users.Add(actor);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var owner=new OrderPriceCorrectionOwnerV1(h.Db,new HttpContextAccessor{HttpContext=new DefaultHttpContext
            {User=CurrentPermissionContextTests.Principal(actor.Id,h.Tenant)}},_=>Task.FromResult(actor.Id),h.Catalog.Capture);
        Assert.Equal("ORIGINAL_OPERATION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ReadOperationAsync(command.OperationId))).Message);
        Assert.Equal("ORIGINAL_OPERATION_FORBIDDEN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CloseOperationAsync(command.OperationId))).Message);
        Assert.Equal(1,await Operations(h));Assert.Equal(1,await Intents(h));
    }

    [CurrentPermissionPostgresFact]
    public async Task Original_command_actual_http_requires_csrf_and_preserves_read_only_recovery_after_write_revocation()
    {
        await using var h=await Seed();await GrantQuery(h);await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies=await Login(host,h);var command=Command(h);
        HttpRequestMessage Message(HttpMethod method,string path,object? body=null,bool csrf=true)
        {
            var request=new HttpRequestMessage(method,"api/orders/price-correction/"+path);
            if(body!=null)request.Content=JsonContent.Create(body);
            request.Headers.Add("Cookie",string.Join("; ",cookies.Select(x=>x.Key+"="+x.Value)));
            if(csrf)request.Headers.Add("X-CSRF-Token",cookies["cp6_csrf"]);return request;
        }
        using(var denied=await host.Send(Message(HttpMethod.Put,"commands/v1",command,false)))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var scope=await host.Send(Message(HttpMethod.Get,"operations/v1/scope")))Assert.Equal(HttpStatusCode.OK,scope.StatusCode);
        using(var written=await host.Send(Message(HttpMethod.Put,"commands/v1",command)))Assert.True(written.IsSuccessStatusCode,await written.Content.ReadAsStringAsync());
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="correct").ExecuteDeleteAsync();
        using(var read=await host.Send(Message(HttpMethod.Get,$"operations/v1/{command.OperationId}")))
        {Assert.True(read.IsSuccessStatusCode,await read.Content.ReadAsStringAsync());Assert.Contains("COMMITTED",await read.Content.ReadAsStringAsync());Assert.True(read.Headers.CacheControl?.NoStore);}
        using(var replay=await host.Send(Message(HttpMethod.Put,"commands/v1",command)))Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        using(var newWrite=await host.Send(Message(HttpMethod.Put,"commands/v1",Command(h))))Assert.Equal(HttpStatusCode.Forbidden,newWrite.StatusCode);
        using(var noCsrfClose=await host.Send(Message(HttpMethod.Post,$"operations/v1/{Guid.NewGuid()}/close",csrf:false)))Assert.Equal(HttpStatusCode.Forbidden,noCsrfClose.StatusCode);
        await h.Db.Sys_RoleActions.Where(x=>x.ActionCode=="query").ExecuteDeleteAsync();
        using(var read=await host.Send(Message(HttpMethod.Get,$"operations/v1/{command.OperationId}")))Assert.Equal(HttpStatusCode.Forbidden,read.StatusCode);
        Assert.Equal(1,await Operations(h));Assert.Equal(1,await Intents(h));
    }
}
