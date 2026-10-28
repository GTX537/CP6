using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task EngineeringIteration_department_binding_and_IAM_directory_preserve_current_membership(int type)
    {
        await using var h=await Seed();await RegisterScope(h);await GrantIteration(h);var admin=await MappingAdmin(h);
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey="plm-engineering-iteration",ScopeType=type,CustomDeptIds=admin.Child.ToString()});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await h.Db.Sys_Users.Where(u=>u.Id==h.User).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.DeptId,type==3?admin.Root:admin.Child));
        var o=Iterations(h);var c=await NewProject(o,ProjectDraft(h));Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(c))).Status);
        c=c with{DepartmentId=admin.Child};var value=await IterationWrite(o,c);Assert.Single(await o.ListAsync());
        var choice=Assert.Single(await StandaloneAdmin(h,admin).FindObjectsAsync("plm-engineering-iteration",value.Body.Number,_=>Task.FromResult(h.User),default));Assert.Equal(c.ObjectId,choice.Id);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='plm-engineering-iteration' AND object_id={c.ObjectId}");
        Assert.Empty(await o.ListAsync());Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.OperationAsync(c.OperationId,false))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringIteration_input_replacement_is_explicit_and_unapproved_successor_or_unknown_original_cannot_resume()
    {
        await using var h=await Seed();var f=await IterationSetup(h);var o=Iterations(h);var first=f.Iteration;
        var next=await IterationWrite(o,IterationChange(first,"REPLACE_INPUTS") with{RequirementSet=f.Set.Set.Source.Set.Reference});
        Assert.Equal("2",next.Body.Iteration!.InputVersion);Assert.Equal("1",next.Body.Iteration.PlanVersion);
        await Sets(h).CommandAsync(SetRevise(f.Set.Set.Source.Set,f.Set.Set.Source.Set.Body with{Name="Unapproved draft"}));var draft=await Sets(h).ReadAsync(f.Set.Set.Source.Set.Reference.Id);
        Assert.Equal(422,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(IterationChange(next,"REPLACE_INPUTS") with{RequirementSet=draft.Reference}))).Status);
        next=await IterationWrite(o,IterationChange(next,"HOLD"));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_requirement_set_v1.versions DISABLE TRIGGER requirement_set_provenance");
        try
        {
            Assert.Equal("UNKNOWN",(await o.ReadAsync(next.Reference.Id)).SourceDrift);
            Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(IterationChange(next,"RESUME")))).Status);
        }
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_requirement_set_v1.versions ENABLE TRIGGER requirement_set_provenance");}
        Assert.Equal("HELD",(await o.ReadAsync(next.Reference.Id)).Body.Iteration!.HoldState);
        Assert.Equal(first.OriginalJson,(await o.ReadAsync(next.Reference.Id,"1")).OriginalJson);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringIteration_changed_project_site_requires_explicit_input_readoption_and_change_path_requires_real_ECO()
    {
        await using var h=await Seed();var f=await IterationSetup(h);var o=Iterations(h);
        var second=await PhysicalSite(h,"SECOND");var reference=(await o.SitesAsync("SECOND")).Single().Reference;
        await IterationWrite(o,IterationChange(f.Project,"EDIT_PROJECT") with{Project=f.Project.Body.Project!.Definition with{Site=reference}});
        var v=await IterationWrite(o,IterationChange(f.Iteration,"SAVE_TASK") with{Task=IterationTask(h,f.Reviewer)});
        Assert.Equal("CHANGED",v.SourceDrift);Assert.Equal("ITERATION_PROJECT_INPUT_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(IterationChange(v,"START")))).Message);
        v=await IterationWrite(o,IterationChange(v,"REPLACE_INPUTS") with{RequirementSet=f.Set.Set.Source.Set.Reference});v=await IterationWrite(o,IterationChange(v,"START"));Assert.Equal("UNCHANGED",v.SourceDrift);
        var change=new EngineeringIterationCommandV1{OperationId=Guid.NewGuid(),ObjectId=Guid.NewGuid(),Action="CREATE_ITERATION",ProjectId=f.Project.Reference.Id,Plan=v.Body.Iteration!.Plan,Origin=new("CHANGE"),RequirementSet=f.Set.Set.Source.Set.Reference,Reason="Must use approved ECO and parent baseline"};
        Assert.Equal("ENGINEERING_PARENT_CHANGE_OWNER_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(change))).Message);
        Assert.Equal("UNKNOWN",(await o.OperationAsync(change.OperationId,false)).State);Assert.Equal(1,(await h.Db.Pub_DocSequences.AsNoTracking().SingleAsync(x=>x.BizKey=="PLM_ITERATION")).CurrentValue);
        await h.Db.Space_Sites.Where(x=>x.Id==second).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SiteName,"Renamed native site"));
        Assert.Equal("CHANGED",(await o.ReadAsync(v.Reference.Id)).SourceDrift);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringIteration_concurrent_project_edit_and_unknown_commit_preserve_one_version_and_number()
    {
        var fault=new InterruptCurrencyCommit();await using var h=await Seed(fault);await RegisterScope(h);await GrantIteration(h);var o=Iterations(h);var create=await NewProject(o,ProjectDraft(h));fault.Interrupt=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(create))).Message);fault.Interrupt=false;
        Assert.Equal("UNKNOWN",(await o.OperationAsync(create.OperationId,false)).State);var first=await IterationWrite(o,create);Assert.Equal("PRJ0001",first.Body.Number);
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var peer=Iterations(h with{Db=db});
        var command=IterationChange(first,"EDIT_PROJECT") with{Project=first.Body.Project!.Definition with{Name="Concurrent plan"}};
        async Task<string> Write(EngineeringIterationService service,EngineeringIterationCommandV1 c){try{return(await service.CommandAsync(c)).Version;}catch(OrderPriceCorrectionException e){return e.Message;}}
        var results=await Task.WhenAll(Write(o,command),Write(peer,command with{OperationId=Guid.NewGuid()}));Assert.Single(results,x=>x=="2");Assert.Single(results,x=>x=="ITERATION_VERSION_CHANGED");
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringIteration_final_actor_revocation_rolls_back_original_number_and_membership_projection()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);await RegisterScope(h);await GrantIteration(h);var o=Iterations(h);var create=await NewProject(o,ProjectDraft(h));
        hook.Before=async c=>
        {
            if(!c.CommandText.Contains("INSERT INTO cp6_engineering_iteration_v1.versions"))return;
            using var revoke=c.Connection!.CreateCommand();revoke.Transaction=c.Transaction;revoke.CommandText="UPDATE public.\"Sys_Users\" SET \"Enable\"=false WHERE \"Id\"=@who";var p=revoke.CreateParameter();p.ParameterName="who";p.Value=h.User;revoke.Parameters.Add(p);await revoke.ExecuteNonQueryAsync();
        };
        Assert.NotNull(await Record.ExceptionAsync(()=>o.CommandAsync(create)));hook.Before=null;
        Assert.True(await h.Db.Sys_Users.AnyAsync(x=>x.Id==h.User&&x.Enable));Assert.Empty(await o.ListAsync());Assert.Equal("UNKNOWN",(await o.OperationAsync(create.OperationId,false)).State);
        Assert.Equal("PRJ0001",(await IterationWrite(o,create)).Body.Number);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringIteration_rejects_foreign_context_dependencies_before_disclosure_or_write()
    {
        await using var h=await Seed();await RegisterScope(h);await GrantIteration(h);
        await using var db=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var other=h with{Db=db};
        var invalid=new EngineeringIterationService(h.Db,IterationAuthority(h),Sets(other),Documents(h,new ContentStore()),ScopeFence(h));
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>invalid.ListAsync())).Message);
        invalid=new(h.Db,IterationAuthority(h),Sets(h),Documents(other,new ContentStore()),ScopeFence(h));
        Assert.Equal("AUTHORITY_FENCE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>invalid.ListAsync())).Message);
        Assert.Empty(await Iterations(h).ListAsync());
    }
}
