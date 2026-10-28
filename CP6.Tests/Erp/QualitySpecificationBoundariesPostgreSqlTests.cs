using System.Net;
using System.Net.Http.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task QualitySpecification_current_professional_role_and_policy_issuer_revoke_future_votes()
    {
        await using var h=await Seed();var f=await QualitySetup(h);var owner=Specifications(h,f.Files);await QualityReady(owner,f.Create);
        var reviewer=Specifications(DecisionActor(h,f.First),f.Files);var command=QualityCommand(await reviewer.ReadAsync(f.Create.SpecificationId),"APPROVE");
        await h.Db.Sys_UserRoles.Where(x=>x.UserId==f.First&&x.RoleId==74).ExecuteDeleteAsync();
        Assert.Equal("QUALITY_REVIEW_PROFESSIONAL_ROLE_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>reviewer.CommandAsync(command))).Message);
        Assert.Empty((await owner.ReadAsync(f.Create.SpecificationId)).Votes);Assert.Equal("UNKNOWN",(await reviewer.OperationAsync(command.OperationId)).State);
        var menu=await h.Db.Sys_Menus.SingleAsync(x=>x.MenuKey=="quality-specification");
        await h.Db.Sys_RoleActions.Where(x=>x.MenuId==menu.MenuId&&x.ActionCode=="review-policy").ExecuteDeleteAsync();
        var second=Specifications(DecisionActor(h,f.Second),f.Files);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>second.CommandAsync(command with{OperationId=Guid.NewGuid()}))).Status);
        Assert.Empty((await owner.ReadAsync(f.Create.SpecificationId)).Votes);
    }
    [CurrentPermissionPostgresFact]
    public async Task QualitySpecification_current_field_object_tenant_and_environment_boundaries_hide_originals()
    {
        await using var h=await Seed();var f=await QualitySetup(h);var owner=Specifications(h,f.Files);await owner.CommandAsync(f.Create);
        await h.Db.Sys_RoleDataScopes.Where(x=>x.RoleId==72&&x.ResourceKey=="quality-specification").ExecuteUpdateAsync(x=>x.SetProperty(s=>s.ScopeType,1));
        var reviewer=Specifications(DecisionActor(h,f.First),f.Files);
        Assert.Empty(await reviewer.ListAsync());Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>reviewer.ReadAsync(f.Create.SpecificationId))).Status);
        await using var other=await Seed();Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Specifications(other,f.Files).ReadAsync(f.Create.SpecificationId))).Status);
        Assert.Equal(503,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Specifications(h,f.Files,"").ReadAsync(f.Create.SpecificationId))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>Specifications(h,f.Files,"REAL").ReadAsync(f.Create.SpecificationId))).Status);
        h.Db.Sys_RoleFieldPerms.Add(new(){RoleId=72,ResourceKey="quality-specification",FieldName="Requirements",Access=3});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.ReadAsync(f.Create.SpecificationId))).Status);
        Assert.Equal(403,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.OperationAsync(f.Create.OperationId))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task QualitySpecification_missing_approval_rolls_back_vote_control_and_original_then_recovers_same_key()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await QualitySetup(h);var owner=Specifications(h,f.Files);await QualityReady(owner,f.Create);
        var first=Specifications(DecisionActor(h,f.First),f.Files);await first.CommandAsync(QualityCommand(await first.ReadAsync(f.Create.SpecificationId),"APPROVE"));
        var second=Specifications(DecisionActor(h,f.Second),f.Files);var before=await second.ReadAsync(f.Create.SpecificationId);var command=QualityCommand(before,"APPROVE");
        hook.Before=c=>{if(c.CommandText.Contains("INSERT INTO cp6_quality_specification_v1.records")&&c.Parameters.Cast<System.Data.Common.DbParameter>().Any(p=>p.Value is string x&&x=="APPROVAL"))c.CommandText="SELECT 1";return Task.CompletedTask;};
        var failure=await Record.ExceptionAsync(()=>second.CommandAsync(command));Assert.NotNull(failure);Assert.Contains("QUALITY_SPEC_CONTROL_PROOF_REQUIRED",failure.ToString());hook.Before=null;
        var restored=await owner.ReadAsync(f.Create.SpecificationId);Assert.Equal(before.Etag,restored.Etag);Assert.Single(restored.Votes);Assert.Null(restored.CurrentApproval);
        Assert.Equal("UNKNOWN",(await second.OperationAsync(command.OperationId)).State);
        var result=await second.CommandAsync(command);Assert.Equal("APPROVED",result.State);Assert.Equal(result,(await second.OperationAsync(command.OperationId)).Result);
    }
    [CurrentPermissionPostgresFact]
    public async Task QualitySpecification_HoldExact_blocks_real_withdrawal_and_rejects_stale_control_after_commit()
    {
        await using var h=await Seed();var f=await QualitySetup(h);var approved=await QualityApprove(h,f);var owner=Specifications(h,f.Files);
        Task<QualitySpecificationResultV1>? writer=null;var started=new TaskCompletionSource();var application="w192-quality-withdraw-"+Guid.NewGuid().ToString("N");
        await QualityAuthority(h).ExecuteAsync("quality-specification","query",false,async(a,t)=>
        {
            var held=await owner.HoldExact(a,approved.Version.Reference,approved.ControlReference,approved.Version.Body.Product,t);
            Assert.Equal("HELD_IN_CURRENT_UOW",held.Observation);
            writer=Task.Run(async()=>{await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});await peer.Database.OpenConnectionAsync();await peer.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('application_name',{application},false)");started.SetResult();return await Specifications(h with{Db=peer},f.Files).CommandAsync(QualityCommand(approved,"WITHDRAW"));});
            await started.Task;
            var waiting=false;var deadline=DateTime.UtcNow.AddSeconds(8);
            while(!waiting&&DateTime.UtcNow<deadline)
            {
                waiting=await h.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name={application} AND wait_event_type='Lock') AS \"Value\"").SingleAsync(t);
                if(!waiting)await Task.Delay(25,t);
            }
            Assert.True(waiting,"Actual withdrawal session must be waiting on the held database gate");Assert.False(writer.IsCompleted);return true;
        });
        Assert.Equal("WITHDRAWN",(await writer!).State);
        Assert.Equal("QUALITY_SPEC_SOURCE_UNAVAILABLE",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>QualityAuthority(h).ExecuteAsync("quality-specification","query",false,
            async(a,t)=>await owner.HoldExact(a,approved.Version.Reference,approved.ControlReference,approved.Version.Body.Product,t)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task QualitySpecification_large_exact_limits_preserve_precision_and_invalid_order_has_no_effect()
    {
        await using var h=await Seed();var f=await QualitySetup(h);await GrantUomProfile(h);var unit=await MakeUnit(Units(h));var owner=Specifications(h,f.Files);
        var number=new string('9',200)+"."+new string('0',199)+"1";
        var requirement=f.Create.Body!.Requirements[0] with{Unit=unit.Reference,LowerLimit=number,UpperLimit=number,Target=number,Waivable=true,WaiverBasis=f.Document.Reference};
        var command=f.Create with{Body=f.Create.Body with{Requirements=[requirement]}};await owner.CommandAsync(command);
        var view=await owner.ReadAsync(command.SpecificationId);Assert.Equal(number,Assert.Single(view.Version.Body.Requirements).Target);Assert.Contains(number,view.Version.OriginalJson);
        var invalid=QualityCommand(view,"REVISE") with{Body=view.Version.Body with{Requirements=[requirement with{UpperLimit="1"}]}};
        Assert.Equal("QUALITY_REQUIREMENT_LIMIT_INVALID",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(invalid))).Message);
        Assert.Equal(view.Etag,(await owner.ReadAsync(command.SpecificationId)).Etag);
    }
    [CurrentPermissionPostgresFact]
    public async Task QualitySpecification_actual_Program_HTTP_CSRF_strict_contract_CAS_signatures_and_original_recovery()
    {
        await using var h=await Seed();var f=await QualitySetup(h);var settings=await DocumentHostSettings(f.Files);settings["QualitySpecification__EvidenceMode"]="DEMO";
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,settings);var author=await Login(host,h);var url="/api/quality-specification/owner-v1";
        async Task<QualitySpecificationViewV1> Read(Dictionary<string,string> cookies){using var response=await host.Send(OperationsRequest(cookies,HttpMethod.Get,url+"/"+f.Create.SpecificationId));return await DocumentHttpData<QualitySpecificationViewV1>(response);}
        async Task<HttpResponseMessage> Post(Dictionary<string,string> cookies,object input,bool csrf=true)
        {using var request=OperationsRequest(cookies,HttpMethod.Post,url+"/commands",input);if(!csrf)request.Headers.Remove("X-CSRF-Token");return await host.Send(request);}
        using(var denied=await Post(author,f.Create,false))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var created=await Post(author,f.Create)){Assert.Equal("CREATE",(await DocumentHttpData<QualitySpecificationResultV1>(created)).Action);Assert.True(created.Headers.CacheControl?.NoStore);}
        using(var recovered=await host.Send(OperationsRequest(author,HttpMethod.Get,url+"/operations/"+f.Create.OperationId)))Assert.Equal("COMMITTED",(await DocumentHttpData<QualitySpecificationOperationV1>(recovered)).State);
        var original=await Read(author);var policy=QualityCommand(original,"POLICY") with{Policy=new("ENABLED",[new("Independent quality",74,2)],DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddDays(1))};
        using(var saved=await Post(author,policy))await DocumentHttpData<QualitySpecificationResultV1>(saved);
        using(var stale=await Post(author,QualityCommand(original,"SUBMIT")))Assert.Equal(HttpStatusCode.PreconditionFailed,stale.StatusCode);
        using(var submitted=await Post(author,QualityCommand(await Read(author),"SUBMIT")))Assert.Equal("IN_REVIEW",(await DocumentHttpData<QualitySpecificationResultV1>(submitted)).State);
        using(var self=await Post(author,QualityCommand(await Read(author),"APPROVE")))Assert.Equal(HttpStatusCode.Forbidden,self.StatusCode);
        foreach(var id in new[]{f.First,f.Second})
        {var actor=await DocumentReviewerLogin(host,h,id);using var vote=await Post(actor,QualityCommand(await Read(actor),"APPROVE"));await DocumentHttpData<QualitySpecificationResultV1>(vote);}
        var approved=await Read(author);Assert.Equal("APPROVED",approved.Control.State);Assert.NotNull(approved.CurrentApproval);
        using(var withdrawn=await Post(author,QualityCommand(approved,"WITHDRAW")))Assert.Equal("WITHDRAWN",(await DocumentHttpData<QualitySpecificationResultV1>(withdrawn)).State);
        var clone=new{operationId=Guid.NewGuid(),specificationId=Guid.NewGuid(),action="CREATE",expectedEtag=(string?)null,reason="Cannot assign approval",body=f.Create.Body,approved=true};
        using(var strict=await Post(author,clone))Assert.Equal(HttpStatusCode.BadRequest,strict.StatusCode);
    }
    [CurrentPermissionPostgresFact]
    public async Task QualitySpecification_HoldExact_rechecks_source_mutated_in_same_UoW_and_rejects_stale_control()
    {
        await using var h=await Seed();var f=await QualitySetup(h);var approved=await QualityApprove(h,f);var owner=Specifications(h,f.Files);
        var originalName=await h.Db.Space_Sites.Where(x=>x.Id==approved.Version.Body.Site.Id).Select(x=>x.SiteName).SingleAsync();
        var error=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>QualityAuthority(h).ExecuteAsync("quality-specification","query",true,async(a,t)=>
        {
            await owner.HoldExact(a,approved.Version.Reference,approved.ControlReference,approved.Version.Body.Product,t);
            await h.Db.Space_Sites.Where(x=>x.Id==approved.Version.Body.Site.Id).ExecuteUpdateAsync(x=>x.SetProperty(s=>s.SiteName,"Changed inside consumer transaction"),t);
            return true;
        }));Assert.Equal("QUALITY_SPEC_SITE_REFERENCE_CHANGED",error.Message);
        Assert.Equal(originalName,await h.Db.Space_Sites.Where(x=>x.Id==approved.Version.Body.Site.Id).Select(x=>x.SiteName).SingleAsync());
        await owner.CommandAsync(QualityCommand(approved,"POLICY") with{Policy=approved.Policy!.Body.Input});
        Assert.Equal("AVAILABLE",(await owner.SourceAsync(approved.Version.Reference)).Availability);
        Assert.Equal("QUALITY_SPEC_CONTROL_CHANGED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>QualityAuthority(h).ExecuteAsync("quality-specification","query",false,
            async(a,t)=>await owner.HoldExact(a,approved.Version.Reference,approved.ControlReference,approved.Version.Body.Product,t)))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task QualitySpecification_missing_control_head_update_cannot_claim_withdrawal_or_commit_original()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await QualitySetup(h);var approved=await QualityApprove(h,f);var owner=Specifications(h,f.Files);
        var command=QualityCommand(approved,"WITHDRAW");
        hook.Before=c=>{if(c.CommandText.Contains("UPDATE cp6_quality_specification_v1.heads SET control_revision="))c.CommandText=c.CommandText.Replace("SET control_revision=","SET control_revision=control_revision+0* ");return Task.CompletedTask;};
        var failure=await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(command));Assert.Equal("QUALITY_SPEC_CONTROL_WRITE_INCOMPLETE",failure.Message);hook.Before=null;
        Assert.Equal(approved.Etag,(await owner.ReadAsync(f.Create.SpecificationId)).Etag);Assert.Equal("AVAILABLE",(await owner.SourceAsync(approved.Version.Reference)).Availability);
        Assert.Equal("UNKNOWN",(await owner.OperationAsync(command.OperationId)).State);
        Assert.Equal("WITHDRAWN",(await owner.CommandAsync(command)).State);Assert.Equal("UNAVAILABLE",(await owner.SourceAsync(approved.Version.Reference)).Availability);
    }

}
