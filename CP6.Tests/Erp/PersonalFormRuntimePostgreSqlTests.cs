using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_delegation_expiring_at_commit_rolls_back_vote_and_same_intent_keeps_principal_and_actor()
    {
        await using var h=await Seed();var(owner,_,flow)=await PreparePersonal(h);
        var schema=JsonSerializer.Serialize(new{start="s",nodes=new object[]{new{id="s",type="start"},new{id="a",type="approval",approverStrategy="Specified",approverUserId=h.User},new{id="e",type="end"}},edges=new[]{new{from="s",to="a"},new{from="a",to="e"}}});
        var f=await flow.SaveAsync("personal-delegate",DependentCommand("personal",schema:schema));
        var p=(await flow.PublishAsync("personal-delegate",PublicationCommand(await Preview(flow,"personal-delegate",f)))).Result!.Publication!;
        await flow.SetActivationAsync("personal-delegate",ActivationCommand((await flow.ActivationPreviewAsync("personal-delegate")).Current,p.VersionId));
        await owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(await owner.ContextAsync("personal")),"start"));
        var task=await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync();var actor=Guid.NewGuid();
        h.Db.Sys_Users.Add(new(){Id=actor,UserName="personal-delegate",Password="unused",RoleId=72});
        var grant=new Wf_FlowDelegate{GrantorId=h.User,DelegateId=actor,Scope="personal-delegate",ValidFrom=DateTime.Now.AddMinutes(-1),ValidTo=DateTime.Now.AddHours(1)};
        h.Db.Wf_FlowDelegates.Add(grant);await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();TaskActor(h,actor);
        var peer=h with{User=actor};var delegated=PersonalForm(peer);var view=await delegated.TaskContextAsync(task.Id,h.User);
        var command=new ApprovalTaskDecisionInputV2(Guid.NewGuid(),"approve","delegated",view.Task.TaskRowVersion,view.Task.InstanceRowVersion);
        var at=DateTimeOffset.UtcNow;var calls=0;var expiring=PersonalForm(peer,_=>{if(++calls==2)at=at.AddHours(2);return Task.FromResult(actor);});expiring.Clock=()=>at;
        Assert.Equal("DELEGATION_EXPIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>expiring.DecideTaskAsync(task.Id,command,h.User,"\""+command.TaskRowVersion+"\""))).Message);
        Assert.Equal(0,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);Assert.Null(await delegated.TaskOriginalAsync(task.Id,command.CommandId));
        Assert.False(await h.Db.Database.SqlQuery<bool>($"SELECT EXISTS(SELECT FROM cp6_personal_form_v2.votes v JOIN cp6_personal_form_v2.commands c ON c.id=v.command_id WHERE c.tenant_id={h.Tenant}) AS \"Value\"").SingleAsync());
        var accepted=await delegated.DecideTaskAsync(task.Id,command,h.User,"\""+command.TaskRowVersion+"\"");
        var vote=await h.Db.Database.SqlQuery<string>($"SELECT body AS \"Value\" FROM cp6_personal_form_v2.votes WHERE id={accepted.VoteId}").SingleAsync();
        using var doc=JsonDocument.Parse(vote);Assert.Equal(actor,doc.RootElement.GetProperty("actual").GetGuid());Assert.Equal(h.User,doc.RootElement.GetProperty("principal").GetGuid());
        await h.Db.Wf_FlowDelegates.Where(x=>x.Id==grant.Id).ExecuteUpdateAsync(x=>x.SetProperty(g=>g.Enable,false));
        Assert.Equal(accepted,await delegated.TaskOriginalAsync(task.Id,command.CommandId));
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_deferred_commit_rejection_is_definite_rollback_not_unknown_transport_success()
    {
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");
        var command=PersonalCommand(PersonalDirect(context),"deferred-reject");
        await h.Db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.personal_test_reject() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'personal_test_commit_reject' USING ERRCODE='23514';END $$;
            CREATE CONSTRAINT TRIGGER personal_test_reject AFTER INSERT ON public."Wf_FormData" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.personal_test_reject();
            """);
        try
        {
            var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>owner.SubmitDirectAsync("personal",command));Assert.Equal("23514",error.SqlState);
        }
        finally
        {
            await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER personal_test_reject ON public.\"Wf_FormData\";DROP FUNCTION public.personal_test_reject()");
        }
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.Direct,"personal","deferred-reject")).Outcome);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Empty(await h.Db.Wf_Notifications.ToArrayAsync());
        Assert.Equal("SUCCEEDED",(await owner.SubmitDirectAsync("personal",command)).Outcome);
    }
    [TenantManagementPostgresTheory]
    [InlineData("all")][InlineData("any")]
    public async Task PersonalForm_subflow_frozen_children_real_tasks_same_transaction_parent_completion(string completion)
    {
        const string formSchema="""{"fields":[{"name":"rows","type":"table","columns":[{"name":"item","type":"input"}]}]}""";
        await using var h=await Seed();var(owner,forms,flow)=await PreparePersonal(h,formSchema);
        var schema=JsonSerializer.Serialize(new{start="s",nodes=new object[]{new{id="s",type="start"},new{id="a",type="approval",approverStrategy="Specified",approverUserId=h.User},new{id="e",type="end"}},edges=new[]{new{from="s",to="a"},new{from="a",to="e"}}});
        var child=await flow.SaveAsync("personal-child",DependentCommand(schema:schema));
        var childPin=(await flow.PublishAsync("personal-child",PublicationCommand(await Preview(flow,"personal-child",child)))).Result!.Publication!;
        await flow.SetActivationAsync("personal-child",ActivationCommand((await flow.ActivationPreviewAsync("personal-child")).Current,childPin.VersionId));
        var rootSchema=JsonSerializer.Serialize(new{start="s",nodes=new object[]{new{id="s",type="start"},new{id="call",type="subFlow",subFlowKey="personal-child",subCollectionVar="rows",subCompletionPolicy=completion},new{id="e",type="end"}},edges=new[]{new{from="s",to="call"},new{from="call",to="e"}}});
        var root=await flow.SaveAsync("personal-root",DependentCommand("personal",schema:rootSchema));
        var rootPin=(await flow.PublishAsync("personal-root",PublicationCommand(await Preview(flow,"personal-root",root)))).Result!.Publication!;
        await flow.SetActivationAsync("personal-root",ActivationCommand((await flow.ActivationPreviewAsync("personal-root")).Current,rootPin.VersionId));
        var context=await owner.ContextAsync("personal");
        var submitted=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,"{\"rows\":[{\"item\":\"first\"},{\"item\":\"second\"}]}"),"tree")));
        Assert.Equal(3,await h.Db.Wf_FlowInstances.CountAsync());Assert.Equal(2,await h.Db.Wf_FlowTasks.CountAsync());
        Assert.All(await h.Db.Wf_FlowInstances.AsNoTracking().Where(i=>i.ParentInstanceId!=null).ToArrayAsync(),i=>{Assert.Equal(childPin.VersionId,i.FlowDefVersionId);Assert.Equal(submitted.FormDataId,i.FormDataId);});
        await flow.SetActivationAsync("personal-child",ActivationCommand((await flow.ActivationPreviewAsync("personal-child")).Current,childPin.VersionId,false,"disable-new"));
        await forms.SetActivationAsync("personal",FormActivationCommand((await forms.ActivationPreviewAsync("personal")).Current,context.FormVersionId,false,"disable-form"));
        var tasks=await h.Db.Wf_FlowTasks.AsNoTracking().OrderBy(t=>t.Id).ToArrayAsync();
        foreach(var task in tasks.Take(completion=="any"?1:2))
        {
            var view=await owner.TaskContextAsync(task.Id);var command=new ApprovalTaskDecisionInputV2(Guid.NewGuid(),"approve","pinned child",view.Task.TaskRowVersion,view.Task.InstanceRowVersion);
            await owner.DecideTaskAsync(task.Id,command,ifMatch:"\""+command.TaskRowVersion+"\"");
        }
        Assert.Equal(FlowInstanceStatus.Approved,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==submitted.FlowInstanceId)).Status);
        Assert.False(await h.Db.Wf_FlowTasks.AnyAsync(t=>t.Status==0||t.Status==4));Assert.False(await h.Db.Wf_FlowTokens.AnyAsync(t=>t.Status==0));
        Assert.Equal(1,await h.Db.Wf_Notifications.CountAsync(n=>n.Type==WfNotificationType.FlowApproved));
        Assert.Empty(await h.Db.Wf_ServiceJobs.ToArrayAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task PersonalForm_missing_writer_guard_and_direct_native_or_receipt_mutations_fail_without_effect()
    {
        await using var h=await Seed();var(owner,_,_)=await PreparePersonal(h);var context=await owner.ContextAsync("personal");
        var command=PersonalCommand(PersonalDirect(context),"missing");
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_personal_form_v2.commands DISABLE TRIGGER personal_command_complete");
        try{Assert.Equal("FORM_WRITER_COVERAGE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDirectAsync("personal",command))).Message);}
        finally{await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_personal_form_v2.commands ENABLE TRIGGER personal_command_complete");}
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Empty(await h.Db.Wf_Notifications.ToArrayAsync());
        var submitted=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal",command));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_personal_form_v2.commands SET result_json='{{}}' WHERE tenant_id={h.Tenant}"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Wf_FormDatas.Where(f=>f.Id==submitted.FormDataId).ExecuteDeleteAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>h.Db.Wf_FormDatas.Where(f=>f.Id==submitted.FormDataId).ExecuteUpdateAsync(x=>x.SetProperty(f=>f.DataJson,"{}")));
        Assert.Equal(submitted,PersonalResult<PersonalFormSubmissionV2>(await owner.OriginalAsync(PersonalFormOwnerV2.Direct,"personal","missing")));
    }
}
