using System.Net;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task GrantRequirement(Harness h)
    {
        var m=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="plm-engineering-requirement");
        foreach(var action in new[]{"query","add","edit"})
        {
            if(!await h.Db.Sys_MenuActions.AnyAsync(x=>x.MenuId==m.MenuId&&x.ActionCode==action))h.Db.Sys_MenuActions.Add(new(){MenuId=m.MenuId,ActionCode=action,ActionName=action});
            h.Db.Sys_RoleActions.Add(new(){RoleId=72,MenuId=m.MenuId,ActionCode=action});
        }
        h.Db.Sys_RoleDataScopes.Add(new(){RoleId=72,ResourceKey="plm-engineering-requirement",ScopeType=5});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
    }
    private static EngineeringRequirementService Requirements(Harness h,CP6Context? db=null)
    {
        var catalog=new AuthorizationCatalogStoreV1();catalog.RegisterScope("plm-engineering-requirement","Requirements",[1,2,3,4,5],5);
        catalog.RegisterFields("plm-engineering-requirement",[new("Definition","Definition"),new("Source","Source")]);catalog.Seal();db??=h.Db;
        return new(db,new(db,new OperationsHttp{HttpContext=h.Http},_=>Task.FromResult(h.User),catalog.Capture));
    }
    private static EngineeringRequirementBodyV1 SourceBody(string kind="INTERNAL_PROPOSAL")=>new("SOURCE","Internal development proposal",
        new(kind,"Product manager's internal proposal","Proposal 01","Recorded directly by authenticated proposer",[new("strength","Strength at least 10 under specified conditions","Clause 1"),new("water","Resist moisture","Clause 2") ]));
    private static EngineeringRequirementBodyV1 DefinitionBody(EngineeringSourceRefV1 source)=>new("REQUIREMENT","Strength and moisture",Requirement:
        new(source,["strength","water"],"Engineering interpretation kept separate","","",[],[],[]));
    private static async Task<EngineeringRequirementCommandV1> NewRequirement(EngineeringRequirementService owner,EngineeringRequirementBodyV1 body,Guid? department=null)=>new()
    {OperationId=Guid.NewGuid(),ObjectId=Guid.NewGuid(),Action="CREATE",Reason="Explicit definition draft",ExpectedAuthorityVersion=(await owner.CreationContextAsync()).AuthorityVersion,DepartmentId=department,Body=body};
    private static EngineeringRequirementCommandV1 ReviseRequirement(EngineeringRequirementSnapshotV1 v,EngineeringRequirementBodyV1 body)=>new()
    {OperationId=Guid.NewGuid(),ObjectId=v.Reference.Id,Action="REVISE",ExpectedVersion=v.Reference.Version,ExpectedEtag=v.Etag,Reason="Explicit revision",Body=body};
    private static async Task<EngineeringRequirementSnapshotV1> CreateRequirement(EngineeringRequirementService owner,EngineeringRequirementBodyV1 body,Guid? department=null)
    {var c=await NewRequirement(owner,body,department);await owner.CommandAsync(c);return await owner.ReadAsync(c.ObjectId);}

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_original_source_revision_does_not_replace_exact_requirement_input()
    {
        await using var h=await Seed();await GrantRequirement(h);var o=Requirements(h);var s=await CreateRequirement(o,SourceBody());
        var c=await NewRequirement(o,DefinitionBody(s.Reference));var receipt=await o.CommandAsync(c);Assert.Equal(receipt,await o.CommandAsync(c));
        var r=await o.ReadAsync(c.ObjectId);Assert.NotEqual(r.Reference.Id,s.Reference.Id);Assert.Equal(h.User,s.AuthorId);Assert.Equal("DRAFT_ONLY",r.State);
        Assert.Contains("CRITERIA_REQUIRED",r.Missing);Assert.Contains("REQUIREMENT_APPROVAL_PENDING",r.Missing);
        await o.CommandAsync(ReviseRequirement(s,s.Body with{Source=s.Body.Source! with{Clauses=[new("strength","New customer-neutral internal text","Clause 1")]}}));
        var old=await o.ReadAsync(s.Reference.Id,"1");Assert.Equal(s.OriginalJson,old.OriginalJson);Assert.False(old.IsCurrent);
        var unchanged=await o.ReadAsync(r.Reference.Id);Assert.Equal(r.OriginalJson,unchanged.OriginalJson);Assert.Equal(s.Reference,unchanged.Body.Requirement!.Source);
        Assert.Contains("SOURCE_VERSION_CHANGED:"+r.Reference.Id,unchanged.Missing);Assert.Equal(receipt,(await o.OperationAsync(c.OperationId,false)).Result);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(c with{Reason="different"}))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_exact_criteria_keep_unknown_units_and_endpoints_without_guessing()
    {
        await using var h=await Seed();await GrantRequirement(h);var o=Requirements(h);var s=await CreateRequirement(o,SourceBody("UNRESOLVED"));
        var b=DefinitionBody(s.Reference);var exact=new string('9',1024)+"."+new string('0',1023)+"1";
        var criterion=new RequirementCriterionV1("K1","QUANTITATIVE","strength","ambient","load",Lower:exact);
        var r=await CreateRequirement(o,b with{Requirement=b.Requirement! with{Criteria=[criterion],ClarificationQuestions=["Unit and endpoint not yet provided"]}});
        Assert.Equal(exact,r.Body.Requirement!.Criteria[0].Lower);Assert.Null(r.Body.Requirement.Criteria[0].ProposedUnit);Assert.Null(r.Body.Requirement.Criteria[0].LowerInclusive);
        Assert.Contains("UNIT_BASIS_REQUIRED:K1",r.Missing);Assert.Contains("ENDPOINT_REQUIRED:K1",r.Missing);Assert.Contains("SOURCE_CLARIFICATION_PENDING",r.Missing);
        foreach(var c in new[]{criterion with{Lower="1e3"},criterion with{Lower="2",Upper="1"},criterion with{Upper=exact,LowerInclusive=false},criterion with{Kind="QUALITATIVE"}})
        {var bad=ReviseRequirement(r,r.Body with{Requirement=r.Body.Requirement with{Criteria=[c]}});Assert.Equal(400,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(bad))).Status);Assert.Equal("UNKNOWN",(await o.OperationAsync(bad.OperationId,false)).State);}
        await o.CommandAsync(ReviseRequirement(r,r.Body with{Requirement=r.Body.Requirement with{Criteria=[new("K2","QUALITATIVE","appearance","daylight","expert assessment",QualitativeBasis:"Reference color acceptance proposal")]}}));
        Assert.Equal(r.OriginalJson,(await o.ReadAsync(r.Reference.Id,"1")).OriginalJson);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_relations_reject_historical_self_proof_and_retain_uncovered_clauses()
    {
        await using var h=await Seed();await GrantRequirement(h);var o=Requirements(h);var s=await CreateRequirement(o,SourceBody());
        var parent=await CreateRequirement(o,DefinitionBody(s.Reference));var b=DefinitionBody(s.Reference);
        var child=await CreateRequirement(o,b with{Name="Strength child",Requirement=b.Requirement! with{ClauseIds=["strength"],Relations=[new("parent","PARENT",parent.Reference,"Strength only","Moisture is not covered","Required")]}});
        Assert.Contains("UNCOVERED:parent",child.Missing);Assert.Equal(new[]{"strength","water"},(await o.ReadAsync(parent.Reference.Id)).Body.Requirement!.ClauseIds);
        var cycle=ReviseRequirement(parent,parent.Body with{Requirement=parent.Body.Requirement! with{Relations=[new("child","DEPENDENCY",child.Reference,"","","")]}});
        Assert.Equal("REQUIREMENT_RELATION_CYCLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(cycle))).Message);
        Assert.Equal("1",(await o.ReadAsync(parent.Reference.Id)).Reference.Version);Assert.Equal("UNKNOWN",(await o.OperationAsync(cycle.OperationId,false)).State);
        var forged=await NewRequirement(o,b with{Requirement=b.Requirement! with{ClauseIds=["invented"]}});
        Assert.Equal("REQUIREMENT_CLAUSE_NOT_FOUND",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(forged))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_write_fault_and_missing_journal_rollback_then_same_key_recovers()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);await GrantRequirement(h);var o=Requirements(h);var c=await NewRequirement(o,SourceBody());
        foreach(var table in new[]{"cp6_engineering_requirement_v1.versions","cp6_main_owner_v1.operations"})
        {
            hook.Before=cmd=>{if(cmd.CommandText.Contains("INSERT INTO "+table))cmd.CommandText="SELECT 1";return Task.CompletedTask;};
            Assert.NotNull(await Record.ExceptionAsync(()=>o.CommandAsync(c)));hook.Before=null;
            Assert.Empty(await o.ListAsync());Assert.Equal("UNKNOWN",(await o.OperationAsync(c.OperationId,false)).State);
        }
        var result=await o.CommandAsync(c);Assert.Equal(result,(await o.OperationAsync(c.OperationId,false)).Result);
        foreach(var sql in new[]{"UPDATE cp6_engineering_requirement_v1.versions SET body_bytes=decode('7b7d','hex')","DELETE FROM cp6_engineering_requirement_v1.versions","TRUNCATE cp6_engineering_requirement_v1.versions CASCADE","DELETE FROM cp6_engineering_requirement_v1.heads"})
            Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>h.Db.Database.ExecuteSqlRawAsync(sql))).SqlState);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_competing_revisions_have_one_CAS_winner_and_close_prevents_late_commit()
    {
        await using var h=await Seed();await GrantRequirement(h);var o=Requirements(h);var s=await CreateRequirement(o,SourceBody());
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var c=ReviseRequirement(s,s.Body with{Name="Updated"});
        async Task<string> Save(EngineeringRequirementService owner,EngineeringRequirementCommandV1 command)
        {try{await owner.CommandAsync(command);return "COMMITTED";}catch(OrderPriceCorrectionException e){return e.Message;}}
        var outcomes=await Task.WhenAll(Save(o,c),Save(Requirements(h,peer),c with{OperationId=Guid.NewGuid()}));
        Assert.Single(outcomes,x=>x=="COMMITTED");Assert.Single(outcomes,x=>x=="REQUIREMENT_VERSION_CHANGED");
        var late=await NewRequirement(o,SourceBody());Assert.Equal("CLOSED",(await o.OperationAsync(late.OperationId,true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(late))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_current_field_and_tenant_authority_protect_originals()
    {
        await using var h=await Seed();await GrantRequirement(h);var o=Requirements(h);var c=await NewRequirement(o,SourceBody());await o.CommandAsync(c);
        await using var other=await Seed();await GrantRequirement(other);Assert.Empty(await Requirements(other).ListAsync());Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Requirements(other).ReadAsync(c.ObjectId))).Status);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="plm-engineering-requirement",FieldName="Source",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ReadAsync(c.ObjectId))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.OperationAsync(c.OperationId,false))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(c))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_actual_Program_HTTP_C02_strict_contract_original_and_CAS()
    {
        await using var h=await Seed();await GrantRequirement(h);var c=await NewRequirement(Requirements(h),SourceBody());
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);var cookies=await Login(host,h);const string url="/api/engineering-requirements/owner-v1";
        // Creation authority includes the actual Program catalog; retrieve it from the same host.
        using(var context=await host.Send(OperationsRequest(cookies,HttpMethod.Get,url+"/creation-context")))
            c=c with{ExpectedAuthorityVersion=(await DocumentHttpData<EngineeringRevisionCreationContextV1>(context)).AuthorityVersion};
        async Task<HttpResponseMessage> Save(object body,bool csrf=true){using var q=OperationsRequest(cookies,HttpMethod.Post,url+"/commands",body);if(!csrf)q.Headers.Remove("X-CSRF-Token");return await host.Send(q);}
        using(var denied=await Save(c,false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var created=await Save(c)){Assert.Equal(c.ObjectId,(await DocumentHttpData<EngineeringRequirementResultV1>(created)).ObjectId);Assert.True(created.Headers.CacheControl?.NoStore);}
        using(var replay=await Save(c))Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        EngineeringRequirementSnapshotV1 v;using(var read=await host.Send(OperationsRequest(cookies,HttpMethod.Get,url+"/"+c.ObjectId)))v=await DocumentHttpData<EngineeringRequirementSnapshotV1>(read);
        using(var stale=await Save(ReviseRequirement(v,v.Body) with{ExpectedEtag=Guid.NewGuid()}))Assert.Equal(HttpStatusCode.PreconditionFailed,stale.StatusCode);
        var strict=JsonSerializer.SerializeToNode(c,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;strict["approved"]=true;
        using(var invalid=await Save(strict))Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        using(var recovered=await host.Send(OperationsRequest(cookies,HttpMethod.Get,url+"/operations/"+c.OperationId)))Assert.Equal("COMMITTED",(await DocumentHttpData<EngineeringRequirementOperationV1>(recovered)).State);
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task EngineeringRequirement_department_sources_are_current_on_history_and_recovery(int type)
    {
        await using var h=await Seed();await GrantRequirement(h);var o=Requirements(h);var admin=await MappingAdmin(h);
        await StandaloneScope(h,admin,type);
        var missing=await NewRequirement(o,SourceBody());Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(missing))).Status);
        var s=await CreateRequirement(o,SourceBody(),admin.Child);var c=await NewRequirement(o,DefinitionBody(s.Reference),admin.Child);await o.CommandAsync(c);
        Assert.Equal(2,(await o.ListAsync()).Length);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false WHERE tenant_id={h.Tenant} AND object_kind='plm-engineering-requirement' AND object_id={s.Reference.Id}");
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.ReadAsync(c.ObjectId,"1"))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.OperationAsync(c.OperationId,false))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_unknown_commit_rolls_back_binding_and_admin_uses_identity_only()
    {
        var failure=new InterruptCurrencyCommit();await using var h=await Seed(failure);await GrantRequirement(h);var o=Requirements(h);var admin=await MappingAdmin(h);
        var c=await NewRequirement(o,SourceBody(),admin.Child);failure.Interrupt=true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(c))).Message);
        Assert.Equal("UNKNOWN",(await o.OperationAsync(c.OperationId,false)).State);Assert.Empty(await o.ListAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_operations_owner_v1.department_mappings WHERE tenant_id={h.Tenant} AND object_kind='plm-engineering-requirement' AND object_id={c.ObjectId}").SingleAsync());
        failure.Interrupt=false;await o.CommandAsync(c);
        var choice=Assert.Single(await StandaloneAdmin(h,admin).FindObjectsAsync("plm-engineering-requirement",c.ObjectId.ToString(),_=>Task.FromResult(h.User),default));
        Assert.Equal(c.ObjectId,choice.Id);Assert.Equal(c.ObjectId.ToString(),choice.Name);
    }
    [CurrentPermissionPostgresFact]
    public async Task EngineeringRequirement_original_guard_loss_fails_closed_without_saving_new_revision()
    {
        await using var h=await Seed();await GrantRequirement(h);var o=Requirements(h);var s=await CreateRequirement(o,SourceBody());var c=ReviseRequirement(s,s.Body);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_engineering_requirement_v1.versions DISABLE TRIGGER requirement_provenance");
        try {Assert.Equal("REQUIREMENT_STORE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>o.CommandAsync(c))).Message);}
        finally {await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_engineering_requirement_v1.versions ENABLE TRIGGER requirement_provenance");}
        Assert.Equal("UNKNOWN",(await o.OperationAsync(c.OperationId,false)).State);Assert.Equal(s.OriginalJson,(await o.ReadAsync(s.Reference.Id)).OriginalJson);
    }
}
