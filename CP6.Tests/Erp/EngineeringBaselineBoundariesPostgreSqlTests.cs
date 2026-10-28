using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task EngineeringBaseline_department_freeze_and_original_recovery_require_current_parent_mapping(int type)
    {
        await using var h=await Seed();
        var f=await BaselineSetup(h,beforeCheck:async()=>{
            var admin=await MappingAdmin(h);
            await h.Db.Sys_Users.Where(x=>x.Id==h.User).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.DeptId,type==3?admin.Root:admin.Child));
            await h.Db.Sys_RoleDataScopes.Where(x=>x.RoleId==72 && x.ResourceKey=="plm-engineering-baseline").ExecuteUpdateAsync(x=>x.SetProperty(v=>v.ScopeType,type).SetProperty(v=>v.CustomDeptIds,admin.Child.ToString()));
            var revision=await EngineeringRevisionStoreV1.Heads(h.Db).Select(x=>x.Id).SingleAsync();
            await BindStandalone(h,admin,"plm-engineering-revision",revision);
        });
        var owner=Baselines(h,f.Review.Source.Files);var result=await owner.FreezeAsync(f.Command);
        Assert.Equal(result.Baseline,Assert.Single(await owner.ListAsync()).Reference);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='plm-engineering-revision' AND object_id={f.Command.Revision.Id}");
        Assert.Empty(await owner.ListAsync());
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ReadAsync(result.Baseline.Id))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.OperationAsync(f.Command.OperationId))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.FreezeAsync(f.Command with{OperationId=Guid.NewGuid()}))).Status);
        Assert.Equal(1,await BaselineCount(h,"baselines"));
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBaseline_missing_member_intent_and_final_professional_revocation_roll_back_every_effect()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await BaselineSetup(h);var o=Baselines(h,f.Review.Source.Files);
        foreach(var table in new[]{"members","intents"})
        {
            hook.Before=command=>{if(command.CommandText.Contains("INSERT INTO cp6_engineering_baseline_v1."+table))command.CommandText="SELECT 1";return Task.CompletedTask;};
            Assert.NotNull(await Record.ExceptionAsync(()=>o.FreezeAsync(f.Command)));hook.Before=null;
            foreach(var name in new[]{"baselines","members","intents"})Assert.Equal(0,await BaselineCount(h,name));
            Assert.Equal("UNKNOWN",(await o.OperationAsync(f.Command.OperationId)).State);
        }
        var reached=false;
        hook.Before=async command=>{
            if(!command.CommandText.Contains("INSERT INTO cp6_engineering_baseline_v1.intents"))return;
            reached=true;await using var mutate=command.Connection!.CreateCommand();mutate.Transaction=command.Transaction;
            mutate.CommandText="DELETE FROM public.\"Sys_RoleAction\" WHERE \"ActionCode\"='review-decide'";Assert.True(await mutate.ExecuteNonQueryAsync()>0);
        };
        Assert.NotNull(await Record.ExceptionAsync(()=>o.FreezeAsync(f.Command)));hook.Before=null;Assert.True(reached);
        Assert.Equal(0,await BaselineCount(h,"baselines"));Assert.Equal(0,await BaselineCount(h,"intents"));Assert.Equal("UNKNOWN",(await o.OperationAsync(f.Command.OperationId)).State);
        Assert.Equal("ACCEPTED",(await o.FreezeAsync(f.Command)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBaseline_two_connections_unique_candidate_and_cancellation_vs_commit_have_one_durable_outcome()
    {
        await using var h=await Seed();var f=await BaselineSetup(h);var o=Baselines(h,f.Review.Source.Files);
        var closed=f.Command with{OperationId=Guid.NewGuid()};var cancel=new EngineeringFreezeCancelV1(Guid.NewGuid(),closed.OperationId,"UNKNOWN","Cancel before delayed sender");
        var cancelled=await o.CancelAsync(cancel);Assert.Equal("CLOSED",cancelled.Outcome.State);Assert.Equal(cancelled,await o.CancelAsync(cancel));
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.FreezeAsync(closed))).Message);
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=Baselines(h with{Db=db},f.Review.Source.Files);
        EngineeringFreezeResultV1? frozen=null;EngineeringFreezeCancellationV1? race=null;
        var errors=await Task.WhenAll(Record.ExceptionAsync(async()=>{frozen=await o.FreezeAsync(f.Command);}),Record.ExceptionAsync(async()=>{race=await peer.CancelAsync(new(Guid.NewGuid(),f.Command.OperationId,"UNKNOWN","Compete with commit"));}));
        Assert.NotNull(race);if(race.Outcome.State=="CLOSED"){Assert.Null(frozen);Assert.IsType<OrderPriceCorrectionException>(errors[0]);Assert.Equal(0,await BaselineCount(h,"baselines"));}
        else{Assert.NotNull(frozen);Assert.Equal(frozen,race.Outcome.Result);Assert.Equal(1,await BaselineCount(h,"baselines"));}
        var c1=f.Command with{OperationId=Guid.NewGuid()};var c2=f.Command with{OperationId=Guid.NewGuid()};
        var results=await Task.WhenAll(o.FreezeAsync(c1),peer.FreezeAsync(c2));Assert.Equal(results[0].Baseline,results[1].Baseline);
        Assert.Equal(1,await BaselineCount(h,"baselines"));Assert.Equal(1,await BaselineCount(h,"intents"));
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBaseline_withdrawn_technical_source_rejects_new_freeze_and_leaves_no_original_effect()
    {
        await using var h=await Seed();var f=await BaselineSetup(h);var o=Baselines(h,f.Review.Source.Files);
        var rule=ConfigurationRules(h);var policy=await rule.ReadAsync(f.Review.Source.Check.Original.Command.Policy.Id,null);
        await Step(rule,policy,"WITHDRAW");
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.FreezeAsync(f.Command));Assert.Equal(0,await BaselineCount(h,"baselines"));
        Assert.Equal("UNKNOWN",(await o.OperationAsync(f.Command.OperationId)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringBaseline_original_disclosure_enforces_actual_actor_tenant_scope_and_every_native_source_read()
    {
        await using var h=await Seed();var f=await BaselineSetup(h);var o=Baselines(h,f.Review.Source.Files);var result=await o.FreezeAsync(f.Command);var before=await o.ReadAsync(result.Baseline.Id);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.ResourceKey=="plm-engineering-baseline").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ScopeType,1));
        var other=Baselines(DecisionActor(h,f.Review.Source.Iteration.Reviewer),f.Review.Source.Files);
        Assert.Empty(await other.ListAsync());Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>other.ReadAsync(result.Baseline.Id))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>other.OperationAsync(f.Command.OperationId))).Status);
        var rule=ConfigurationRules(h);var policy=await rule.ReadAsync(f.Review.Source.Check.Original.Command.Policy.Id,null);await Step(rule,policy,"WITHDRAW");
        var after=await o.ReadAsync(result.Baseline.Id);Assert.Equal(before.OriginalJson,after.OriginalJson);Assert.Equal("FROZEN",after.State);Assert.Equal("CHANGED",after.SourceStatus);
        await h.Db.Database.ExecuteSqlRawAsync("DELETE FROM public.\"Sys_RoleAction\" WHERE \"ActionCode\"='query' AND \"MenuId\" IN(SELECT \"MenuId\" FROM public.\"Sys_Menus\" WHERE \"MenuKey\"='file-content')");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ReadAsync(result.Baseline.Id))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.OperationAsync(f.Command.OperationId))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.DiffAsync(result.Baseline.Id,result.Baseline.Id))).Status);
        await using var db=server.Context(new TenantContext{CurrentTenantId=Guid.NewGuid()});
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Baselines(h with{Db=db},f.Review.Source.Files).ReadAsync(result.Baseline.Id));
    }
}
