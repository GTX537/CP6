using System.Data.Common;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Tests.Platform;

public sealed partial class IamOrganizationPostgreSqlTests
{
    private sealed record OrganizationHarness(CP6Context Db,IamAdministrationTestSession Session,CrmIdentityRuntime Runtime,
        Guid Tenant,Guid Actor,Guid Root,Guid Child,Guid Grand,Guid Destination):IAsyncDisposable
    {public ValueTask DisposeAsync()=>Db.DisposeAsync();}
    private async Task<OrganizationHarness> SeedOrganization(params IInterceptor[] interceptors)
    {
        var tenant=Guid.NewGuid();const string issuer="https://organization.example.test";
        var options=new CrmIdentityOptions{Enabled=true,Issuer=issuer,ProjectionReaderClientIds=["fixture-reader"]};options.Tenants.Add(tenant,"us");
        var runtime=new CrmIdentityRuntime(options,new IdentityEventValidator(Cp6ContractBundle.Load(Path.Combine(AppContext.BaseDirectory,"contracts/events/platform")),issuer));
        var db=server.Context(new TenantContext{CurrentTenantId=tenant},runtime,interceptors);var root=Guid.NewGuid();var child=Guid.NewGuid();var grand=Guid.NewGuid();var target=Guid.NewGuid();
        var actor=new Sys_User{UserName="organization-admin",Password="owned",DeptId=root,RoleId=71};
        db.Sys_Tenants.Add(new(){Id=tenant,TenantCode=tenant.ToString("N"),TenantName="Organization"});db.Sys_Users.Add(actor);
        db.Sys_Roles.Add(new(){RoleId=71,RoleName="organization"});
        db.Sys_Depts.AddRange(new Sys_Dept{Id=root,DeptCode="root",DeptName="root",Path=$"/{root}/"},
            new Sys_Dept{Id=child,ParentId=root,DeptCode="child",DeptName="child",Path=$"/{root}/{child}/"},
            new Sys_Dept{Id=grand,ParentId=child,DeptCode="grand",DeptName="grand",Path=$"/{root}/{child}/{grand}/"},
            new Sys_Dept{Id=target,DeptCode="destination",DeptName="destination",Path=$"/{target}/"});
        db.Sys_RoleDataScopes.Add(new(){RoleId=71,ResourceKey="order",ScopeType=4,CustomDeptIds=grand.ToString("D")});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        return new(db,new(db,actor.Id),runtime,tenant,actor.Id,root,child,grand,target);
    }
    private static async Task<IamDepartmentCommandV1> MoveCommand(OrganizationHarness h,Guid? parent=null)
    {
        var preview=await h.Session.Departments.PreviewAsync(h.Child,parent??h.Destination,h.Session.Actor,default);
        return (await h.Session.DepartmentCommand(h.Child)) with{ParentId=preview.NewParentId,PreviewVersion=preview.PreviewVersion};
    }
    [CurrentPermissionPostgresFact]
    public async Task Organization_move_commits_subtree_receipt_identity_and_current_scope_without_using_old_path()
    {
        await using var h=await SeedOrganization();
        h.Db.PurchaseRequests.AddRange(new(){PrNo="root-doc",DeptId=h.Root,RequestDate=DateTime.Today},new(){PrNo="child-doc",DeptId=h.Grand,RequestDate=DateTime.Today});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var old=new UserPermissionContext{UserId=h.Actor,DeptId=h.Root,DeptPath=$"/{h.Root}/",DataScopes=new(){["order"]=3}};
        Assert.Equal(2,await new DataScopeFilter(h.Db).Apply(h.Db.PurchaseRequests,"order",old).CountAsync());
        var preview=await h.Session.Departments.PreviewAsync(h.Child,h.Destination,h.Session.Actor,default);
        Assert.Equal(1,preview.AffectedDescendantCount);Assert.Single(preview.ScopeRefs);
        var command=await MoveCommand(h);var result=await h.Session.Departments.ChangeAsync("dept-move",command,h.Session.Actor,default);
        Assert.Equal(h.Child,result.EntityId);Assert.NotEqual(preview.TreeVersion,result.TreeVersion);
        Assert.Equal($"/{h.Destination}/{h.Child}/{h.Grand}/",await h.Db.Sys_Depts.Where(x=>x.Id==h.Grand).Select(x=>x.Path).SingleAsync());
        var data=await h.Db.CrmIdentitySnapshots.AsNoTracking().SingleAsync(x=>x.AggregateId=="department:"+h.Grand);
        Assert.Contains($"/{h.Destination}/{h.Child}/{h.Grand}/",data.PayloadJson);Assert.Equal(2,data.Version);
        Assert.Equal("root-doc",(await new DataScopeFilter(h.Db).Apply(h.Db.PurchaseRequests,"order",old).SingleAsync()).PrNo);
        Assert.Equal(result,await h.Session.Authority.Resolve(command.OperationId,"dept-move",false,h.Session.Actor,default));
        Assert.Equal(result,await h.Session.Departments.ChangeAsync("dept-move",command,h.Session.Actor,default));
        Assert.Equal(1,await h.Db.Sys_SecurityLogs.CountAsync(x=>x.EventType==(int)SecurityEventType.IamAdministrationChanged));
        await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Session.Departments.ChangeAsync("dept-move",command with{ParentId=null},h.Session.Actor,default));
    }
    [CurrentPermissionPostgresFact]
    public async Task Organization_receipt_failure_rolls_back_paths_identity_audit_and_revisions()
    {
        var fault=new OrganizationReceiptFault();await using var h=await SeedOrganization(fault);var command=await MoveCommand(h);fault.Armed=true;
        await Assert.ThrowsAsync<IOException>(()=>h.Session.Departments.ChangeAsync("dept-move",command,h.Session.Actor,default));
        Assert.True(fault.Fired);Assert.Equal($"/{h.Root}/{h.Child}/{h.Grand}/",await h.Db.Sys_Depts.Where(x=>x.Id==h.Grand).Select(x=>x.Path).SingleAsync());
        Assert.Equal(command.ExpectedTreeVersion,await h.Session.Authority.Version("tree",default));
        Assert.Equal(1,await h.Db.CrmIdentitySnapshots.Where(x=>x.AggregateId=="department:"+h.Grand).Select(x=>x.Version).SingleAsync());
        Assert.False(await h.Db.Sys_SecurityLogs.AnyAsync(x=>x.EventType==(int)SecurityEventType.IamAdministrationChanged));
        Assert.Equal("UNKNOWN",(await h.Session.Authority.Resolve(command.OperationId,"dept-move",false,h.Session.Actor,default)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task Organization_lost_commit_ack_is_recovered_exactly_and_closed_original_blocks_late_move()
    {
        var fault=new OrganizationCommitFault();await using var h=await SeedOrganization(fault);var command=await MoveCommand(h);fault.Armed=true;
        var error=await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Session.Departments.ChangeAsync("dept-move",command,h.Session.Actor,default));Assert.Equal(503,error.Status);
        var recovered=await h.Session.Authority.Resolve(command.OperationId,"dept-move",true,h.Session.Actor,default);Assert.Equal("COMMITTED",recovered.State);
        var next=await MoveCommand(h,h.Root);await h.Session.Authority.Resolve(next.OperationId,"dept-move",true,h.Session.Actor,default);
        Assert.Equal("IAM_OPERATION_CLOSED",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Session.Departments.ChangeAsync("dept-move",next,h.Session.Actor,default))).Message);
        Assert.Equal(h.Destination,await h.Db.Sys_Depts.Where(x=>x.Id==h.Child).Select(x=>x.ParentId).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Organization_preview_is_bound_to_target_and_rejects_ABA_then_requires_new_confirmation()
    {
        await using var h=await SeedOrganization();var command=await MoveCommand(h);
        Assert.Equal(412,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Session.Departments.ChangeAsync("dept-move",command with{ParentId=null},h.Session.Actor,default))).Status);
        await h.Db.Sys_Depts.Where(x=>x.Id==h.Destination).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.Enable,false));
        await h.Db.Sys_Depts.Where(x=>x.Id==h.Destination).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.Enable,true));
        Assert.Equal(412,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Session.Departments.ChangeAsync("dept-move",command,h.Session.Actor,default))).Status);
        Assert.Equal(h.Root,await h.Db.Sys_Depts.Where(x=>x.Id==h.Child).Select(x=>x.ParentId).SingleAsync());
        await h.Session.MoveDepartment(h.Child,h.Destination);
    }
    [TenantManagementPostgresTheory]
    [InlineData("self")][InlineData("descendant")][InlineData("missing-parent")][InlineData("disabled-parent")][InlineData("bad-path")]
    public async Task Organization_move_rejects_cycles_and_unhealthy_dependencies(string scenario)
    {
        await using var h=await SeedOrganization();var parent=h.Destination;
        if(scenario=="self")parent=h.Child;if(scenario=="descendant")parent=h.Grand;if(scenario=="missing-parent")parent=Guid.NewGuid();
        if(scenario=="disabled-parent")await h.Db.Sys_Depts.Where(x=>x.Id==parent).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.Enable,false));
        if(scenario=="bad-path")await h.Db.Sys_Depts.Where(x=>x.Id==h.Grand).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.Path,"/"));
        await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Session.Departments.PreviewAsync(h.Child,parent,h.Session.Actor,default));
        Assert.Equal(h.Root,await h.Db.Sys_Depts.Where(x=>x.Id==h.Child).Select(x=>x.ParentId).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Organization_leader_null_and_disabled_history_are_explicit_and_foreign_identity_is_rejected()
    {
        await using var h=await SeedOrganization();
        await h.Session.SetLeader(h.Child,h.Actor);
        await h.Session.SetLeader(h.Child,null);Assert.Null(await h.Db.Sys_Depts.Where(x=>x.Id==h.Child).Select(x=>x.LeaderId).SingleAsync());
        var other=new Sys_User{UserName="disabled-leader",Password="owned",Enable=false};h.Db.Sys_Users.Add(other);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Session.SetLeader(h.Child,other.Id));
        await using var foreign=server.Context(new TenantContext{CurrentTenantId=Guid.NewGuid()});var outsider=new Sys_User{UserName="foreign",Password="owned"};foreign.Sys_Users.Add(outsider);await foreign.SaveChangesAsync();
        await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Session.SetLeader(h.Child,outsider.Id));
    }
    [CurrentPermissionPostgresFact]
    public async Task Organization_delete_reports_children_users_scopes_and_unverified_retention_without_cascading()
    {
        await using var h=await SeedOrganization();
        async Task<string> Delete(Guid id)=>(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>DeleteCommand(id))).Message;
        async Task DeleteCommand(Guid id)=>await h.Session.Departments.ChangeAsync("dept-delete",await h.Session.DepartmentCommand(id),h.Session.Actor,default);
        Assert.Equal("E-PUB-002",await Delete(h.Root));
        Assert.Equal("IAM_DEPARTMENT_SCOPE_REFERENCED",await Delete(h.Grand));
        Assert.Equal("IAM_DEPARTMENT_RETENTION_REVIEW_REQUIRED",await Delete(h.Destination));
        var refs=await h.Session.Departments.ReferencesAsync(h.Grand,h.Session.Actor,default);Assert.False(refs.DeletionAllowed);Assert.Single(refs.ScopeRefs);Assert.Contains("approval-history",refs.UnverifiedReferenceCategories);
        Assert.Equal(4,await h.Db.Sys_Depts.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Organization_disabled_ancestor_closes_actual_data_scope_permission_and_current_approval_routing()
    {
        await using var h=await SeedOrganization();await h.Session.SetLeader(h.Child,h.Actor);
        var subject=new Sys_User{UserName="subject",Password="owned",RoleId=71,DeptId=h.Grand};h.Db.Sys_Users.Add(subject);
        h.Db.PurchaseRequests.Add(new(){PrNo="grand-doc",DeptId=h.Grand,RequestDate=DateTime.Today});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var before=await new PermissionAggregator(h.Db).BuildAsync(subject.Id);Assert.Equal(h.Grand,before.DeptId);Assert.Single(before.CustomDeptIds["order"]);
        await h.Db.Sys_Depts.Where(x=>x.Id==h.Root).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.Enable,false));
        var current=await new PermissionAggregator(h.Db).BuildAsync(subject.Id);Assert.Null(current.DeptId);Assert.Empty(current.DeptPath);Assert.Empty(current.CustomDeptIds["order"]);
        foreach(var scope in new[]{2,3,4}){before.DataScopes["order"]=scope;Assert.Empty(await new DataScopeFilter(h.Db).Apply(h.Db.PurchaseRequests,"order",before).ToListAsync());}
        var route=await new ApproverResolver(h.Db).ResolveAsync(new(ApproverStrategy.DeptLeader,null,null,null),new(){StarterUserId=subject.Id});Assert.False(route.Resolved);
        var editor=await h.Session.Departments.ReadAsync(h.Session.Actor,default);Assert.False(editor.Departments.Single(x=>x.Id==h.Grand).Available);
    }
    [CurrentPermissionPostgresFact]
    public async Task Organization_actual_target_writer_wins_before_waiting_move_and_old_preview_cannot_commit()
    {
        await using var h=await SeedOrganization();var command=await MoveCommand(h);
        await using var writer=server.Context(new TenantContext{CurrentTenantId=h.Tenant},h.Runtime);await using var tx=await writer.Database.BeginTransactionAsync();
        await writer.Sys_Depts.Where(x=>x.Id==h.Destination).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.Enable,false));
        await h.Db.Database.OpenConnectionAsync();var pid=((NpgsqlConnection)h.Db.Database.GetDbConnection()).ProcessID;
        var pending=h.Session.Departments.ChangeAsync("dept-move",command,h.Session.Actor,default);
        try
        {
            await using var observer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while(!await observer.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE pid={pid} AND wait_event_type='Lock') AS \"Value\"").SingleAsync(timeout.Token))await Task.Delay(20,timeout.Token);
            Assert.False(pending.IsCompleted);
        }
        finally{await tx.CommitAsync();}
        Assert.Equal(412,(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>pending)).Status);
        Assert.Equal(h.Root,await h.Db.Sys_Depts.Where(x=>x.Id==h.Child).Select(x=>x.ParentId).SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Organization_create_update_preserve_immutable_identity_and_reject_path_overflow_without_partial_nodes()
    {
        await using var h=await SeedOrganization();var added=await h.Session.CreateDepartment("new-child","new child",h.Destination);
        var command=await h.Session.DepartmentCommand(added);
        await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Session.Departments.ChangeAsync("dept-update",command with{DeptCode="rename-code"},h.Session.Actor,default));
        await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Session.Departments.ChangeAsync("dept-update",command with{ParentId=null},h.Session.Actor,default));
        await h.Session.Departments.ChangeAsync("dept-update",command with{DeptName="updated",Enable=false},h.Session.Actor,default);
        Assert.False(await h.Db.Sys_Depts.Where(x=>x.Id==added).Select(x=>x.Enable).SingleAsync());
        var parent=h.Destination;var path=$"/{parent}/";
        for(var i=0;i<23;i++){var id=Guid.NewGuid();path+=id+"/";h.Db.Sys_Depts.Add(new(){Id=id,ParentId=parent,DeptCode="deep-"+i,DeptName="deep",Path=path});parent=id;}
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();var count=await h.Db.Sys_Depts.CountAsync();
        Assert.Equal("IAM_DEPARTMENT_PATH_LIMIT",(await Assert.ThrowsAsync<IamAdministrationExceptionV1>(()=>h.Session.CreateDepartment("too-deep","too deep",parent))).Message);
        Assert.Equal(count,await h.Db.Sys_Depts.CountAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Organization_trailing_space_path_is_not_normalized_into_a_valid_permission_root()
    {
        await using var h=await SeedOrganization();
        await h.Db.Sys_Depts.Where(x=>x.Id==h.Root).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.Path,$"/{h.Root}/ "));
        Assert.EndsWith(" ",await h.Db.Sys_Depts.Where(x=>x.Id==h.Root).Select(x=>x.Path).SingleAsync());
        var permission=await new PermissionAggregator(h.Db).BuildAsync(h.Actor);Assert.Null(permission.DeptId);
        h.Db.PurchaseRequests.Add(new(){PrNo="padded-root",DeptId=h.Root,RequestDate=DateTime.Today});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        permission.DataScopes["order"]=2;
        Assert.Empty(await new DataScopeFilter(h.Db).Apply(h.Db.PurchaseRequests,"order",permission).ToListAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task Organization_current_CRM_projection_and_approval_follow_move_leader_and_ancestor_state()
    {
        await using var h=await SeedOrganization();
        var subject=new Sys_User{UserName="crm-member",Password="owned",DeptId=h.Grand,RoleId=71};
        h.Db.Sys_Users.Add(subject);
        h.Db.Sys_Menus.Add(new(){MenuId=850,MenuName="CRM",MenuKey="crm-lead"});
        foreach(var action in new[]{"query","edit","assign"})h.Db.Sys_RoleActions.Add(new(){RoleId=71,MenuId=850,ActionCode=action});
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=71,ResourceKey="crm-lead",ScopeType=3});
        await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var directory=new CP6.WebApi.Services.CrmOidcDirectory(h.Db,new TenantContext{CurrentTenantId=h.Tenant},
            Moq.Mock.Of<ITokenBlacklistService>(),Moq.Mock.Of<IPasswordPolicyService>(),new(){Enabled=true,
            Organizations=[new(){TenantId=h.Tenant,Slug="owned",Region="us",CrmEnabled=true}]});
        var actor=await h.Db.Sys_Users.AsNoTracking().SingleAsync(x=>x.Id==h.Actor);
        var before=await directory.ProjectAsync(actor);Assert.Contains(before.EligibleOwners,x=>x.SubjectId==subject.Id);
        await h.Session.SetLeader(h.Root,h.Actor);
        var resolver=new ApproverResolver(h.Db);var rule=new ApproverRule(ApproverStrategy.DeptLeader,null,null,null);
        var context=new ApproverResolveContext{StarterUserId=subject.Id};
        Assert.Equal(h.Actor,Assert.Single((await resolver.ResolveAsync(rule,context)).ApproverIds));
        await h.Session.SetLeader(h.Destination,subject.Id);
        await h.Session.Departments.ChangeAsync("dept-move",await MoveCommand(h),h.Session.Actor,default);
        var moved=await directory.ProjectAsync(actor);Assert.DoesNotContain(moved.EligibleOwners,x=>x.SubjectId==subject.Id);
        Assert.DoesNotContain(moved.Departments,x=>x.DepartmentId==h.Grand);
        Assert.Equal(subject.Id,Assert.Single((await resolver.ResolveAsync(rule,context)).ApproverIds));
        await h.Db.Sys_Depts.Where(x=>x.Id==h.Destination).ExecuteUpdateAsync(x=>x.SetProperty(d=>d.Enable,false));
        var unavailable=await directory.ProjectAsync(subject);Assert.Null(unavailable.DepartmentId);
        Assert.Empty(unavailable.DepartmentIds);Assert.Empty(unavailable.EligibleOwners);Assert.Empty(unavailable.Departments);
        Assert.Contains("crm-lead:edit",unavailable.AllowedActions);
        Assert.False((await resolver.ResolveAsync(rule,context)).Resolved);
    }
    private sealed class OrganizationReceiptFault:DbCommandInterceptor
    {
        public bool Armed,Fired;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand cmd,CommandEventData data,InterceptionResult<int> result,CancellationToken ct=default)
        {if(Armed&&cmd.CommandText.Contains("INSERT INTO cp6_authentication_v1.iam_operations")){Fired=true;Armed=false;throw new IOException("owned organization receipt failure");}return base.NonQueryExecutingAsync(cmd,data,result,ct);}
    }
    private sealed class OrganizationCommitFault:DbTransactionInterceptor
    {
        public bool Armed;
        public override Task TransactionCommittedAsync(DbTransaction tx,TransactionEndEventData data,CancellationToken ct=default)
        {if(Armed){Armed=false;throw new IOException("owned organization acknowledgement lost");}return Task.CompletedTask;}
    }
}
