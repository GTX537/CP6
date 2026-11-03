using System.Net;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static SfsTaskDecisionInputV2 SfsInput(PersonalFormTaskContextV2 c,string patch="{}",string decision="APPROVE",string? comment="reviewed")
        =>new("SFS",c.Task.TaskId,decision,PersonalData(patch),c.Task.TaskRowVersion,c.Task.InstanceRowVersion,c.FormDataVersion,comment);
    private static byte[] SfsCommand(SfsTaskDecisionInputV2 input,string id="sfs-vote")=>PersonalCommand(input,id,input.ExpectedTaskVersion);
    [CurrentPermissionPostgresFact]
    public async Task SfsTask_context_uses_actual_form_version_and_pinned_node_field_mask()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h,configure:g=>g.Nodes.Single(n=>n.Id=="a").FieldPerms=new(){["a"]="readonly"});
        var context=await f.Owner.TaskContextAsync(f.Task);var json=JsonSerializer.SerializeToElement(context,PersonalFormOwnerV2.Json);
        Assert.Equal("readonly",context.FieldMask["a"]);
        Assert.Equal("SFS",json.GetProperty("kind").GetString());
        Assert.Equal(Convert.ToBase64String((await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).RowVersion!),json.GetProperty("formDataVersion").GetString());
    }

    [CurrentPermissionPostgresFact]
    public async Task SfsTask_HTTP_WFC_patch_updates_form_and_decision_together()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);
        var context=await f.Owner.TaskContextAsync(f.Task);var data=await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync();
        await using var host=await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!,new Dictionary<string,string>{["ScopeAuthorization__EnvironmentId"]=ScopeEnvironment.ToString("D")});
        var cookies=await Login(host,h);
        var body=new{kind="SFS",taskId=f.Task,decision="APPROVE",comment="edit at approval",dataPatch=new{a="approved edit"},expectedTaskVersion=context.Task.TaskRowVersion,
            expectedInstanceVersion=context.Task.InstanceRowVersion,expectedFormDataVersion=Convert.ToBase64String(data.RowVersion!)};
        using var response=await host.Send(PersonalHttp(cookies,"/api/oa/v2/personal-forms/tasks/"+f.Task+"/decisions",PersonalCommand(body,"edit-vote",body.expectedTaskVersion)));
        var result=await PersonalHttpResult<PersonalFormResultV2>(response);Assert.Equal("SUCCEEDED",result.Outcome);
        var stored=await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync();Assert.Contains("approved edit",stored.DataJson);
        var instance=await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync();Assert.Equal(FlowInstanceStatus.Approved,instance.Status);Assert.Contains("approved edit",instance.VarsJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task SfsTask_exact_numeric_patch_recomputes_and_preserves_pinned_readonly_hidden_and_handled_snapshot()
    {
        const string schema="""{"fields":[{"name":"amount","type":"number"},{"name":"sum","type":"number"},{"name":"locked","type":"input"},{"name":"secret","type":"input"},{"name":"rows","type":"table","columns":[{"name":"quantity","type":"number"}]}],"rules":[{"then":[{"action":"compute","target":"sum","expr":"amount + 0.0001"}]}]}""";
        await using var h=await Seed();var f=await NativeFormSetup(h,formSchema:schema,values:"""{"amount":1,"locked":"retained","secret":"private","rows":[{"quantity":1}]}""",
            configure:g=>g.Nodes.Single(n=>n.Id=="a").FieldPerms=new(){["locked"]="readonly",["secret"]="hidden",["sum"]="readonly"});
        var c=await f.Owner.TaskContextAsync(f.Task);Assert.DoesNotContain("private",c.DataJson);Assert.DoesNotContain("secret",c.SchemaJson);
        foreach(var patch in new[]{"{\"locked\":\"retained\"}","{\"secret\":\"private\"}","{\"unknown\":1}"})
        {
            await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.DecideSfsTaskAsync(f.Task,SfsCommand(SfsInput(c,patch),Guid.NewGuid().ToString("D"))));
            Assert.Equal(FlowTaskStatus.Pending,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);
        }
        var prior=await h.Db.Wf_FlowDatas.AsNoTracking().OrderBy(x=>x.CreateDate).Select(x=>x.DataJson).ToArrayAsync();
        var raw=SfsCommand(SfsInput(c,"""{"amount":12345678901234567890.0001,"rows":[{"quantity":9007199254740993.01}]}"""));
        var result=await f.Owner.DecideSfsTaskAsync(f.Task,raw);var after=await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync();
        Assert.Contains("12345678901234567890.0002",after.DataJson);Assert.Contains("9007199254740993.01",after.DataJson);Assert.Contains("retained",after.DataJson);Assert.Contains("private",after.DataJson);
        Assert.NotEqual(c.FormDataVersion,PersonalResult<SfsTaskDecisionResultV2>(result).FormDataVersion);
        var snapshots=await h.Db.Wf_FlowDatas.AsNoTracking().Select(x=>x.DataJson).ToArrayAsync();Assert.All(prior,p=>Assert.Contains(p,snapshots));Assert.Contains(snapshots,p=>p.Contains("12345678901234567890.0002"));
        Assert.Equal(ApprovalCanonicalV2.Bytes(result),ApprovalCanonicalV2.Bytes(await f.Owner.DecideSfsTaskAsync(f.Task,raw)));
        Assert.Equal(ApprovalCanonicalV2.Bytes(result),ApprovalCanonicalV2.Bytes(await f.Owner.OriginalAsync("SFS_TASK_DECISION",f.Task.ToString("D"),"sfs-vote")));
    }

    [TenantManagementPostgresTheory]
    [InlineData("task")][InlineData("instance")][InlineData("data")]
    public async Task SfsTask_each_stale_client_version_rejects_without_any_form_or_vote_effect(string version)
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);var c=await f.Owner.TaskContextAsync(f.Task);var input=SfsInput(c,"{\"a\":\"must not save\"}");
        var stale=Convert.ToBase64String(new byte[]{1,2,3,4,5,6,7,8});
        input=version switch{"task"=>input with{ExpectedTaskVersion=stale},"instance"=>input with{ExpectedInstanceVersion=stale},_=>input with{ExpectedFormDataVersion=stale}};
        var e=await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.DecideSfsTaskAsync(f.Task,SfsCommand(input)));Assert.Equal(409,e.Status);Assert.Equal("TASK_CONTEXT_CHANGED",e.Message);
        Assert.Equal(c.DataJson,(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson);Assert.Equal(c.Task,(await f.Owner.TaskContextAsync(f.Task)).Task);
        Assert.Equal("NOT_OBSERVED",(await f.Owner.OriginalAsync("SFS_TASK_DECISION",f.Task.ToString("D"),"sfs-vote")).Outcome);
    }

    [CurrentPermissionPostgresFact]
    public async Task SfsTask_parallel_approval_does_not_overwrite_another_reviewers_form_edit()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h,true);var tasks=await h.Db.Wf_FlowTasks.AsNoTracking().OrderBy(t=>t.Id).ToArrayAsync();
        var first=await f.Owner.TaskContextAsync(tasks[0].Id);var stale=await f.Owner.TaskContextAsync(tasks[1].Id);
        await f.Owner.DecideSfsTaskAsync(tasks[0].Id,SfsCommand(SfsInput(first,"{\"a\":\"first edit\"}"),"first"));
        Assert.Equal(409,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.DecideSfsTaskAsync(tasks[1].Id,SfsCommand(SfsInput(stale,"{\"a\":\"stale overwrite\"}"),"second")))).Status);
        var refreshed=await f.Owner.TaskContextAsync(tasks[1].Id);Assert.Contains("first edit",refreshed.DataJson);
        var onlyDataStale=SfsInput(refreshed) with{ExpectedFormDataVersion=stale.FormDataVersion};
        Assert.Equal(409,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.DecideSfsTaskAsync(tasks[1].Id,SfsCommand(onlyDataStale,"third")))).Status);
        await f.Owner.DecideSfsTaskAsync(tasks[1].Id,SfsCommand(SfsInput(refreshed),"confirmed"));
        Assert.Contains("first edit",(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson);
        Assert.Equal(FlowInstanceStatus.Approved,(await h.Db.Wf_FlowInstances.AsNoTracking().SingleAsync()).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task SfsTask_precommit_fault_rolls_back_edit_then_lost_commit_recovers_exact_original()
    {
        var hook=new StartWriteHook();var fault=new CommitFault();await using var h=await Seed(hook,fault);var f=await NativeFormSetup(h);var c=await f.Owner.TaskContextAsync(f.Task);
        var raw=SfsCommand(SfsInput(c,"{\"a\":\"atomic edit\"}"));var notifications=await h.Db.Wf_Notifications.CountAsync();
        hook.Before=sql=>{if(sql.CommandText.Contains("INSERT INTO cp6_personal_form_v2.votes")){hook.Before=null;throw new InvalidOperationException("injected vote failure");}return Task.CompletedTask;};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Owner.DecideSfsTaskAsync(f.Task,raw));
        Assert.Equal(c.DataJson,(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson);Assert.Equal(c.Task,(await f.Owner.TaskContextAsync(f.Task)).Task);Assert.Equal(notifications,await h.Db.Wf_Notifications.CountAsync());
        fault.Armed=true;Assert.Equal("COMMAND_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.DecideSfsTaskAsync(f.Task,raw))).Message);
        var original=await f.Owner.OriginalAsync("SFS_TASK_DECISION",f.Task.ToString("D"),"sfs-vote");Assert.Equal("SUCCEEDED",original.Outcome);
        Assert.Equal(ApprovalCanonicalV2.Bytes(original),ApprovalCanonicalV2.Bytes(await f.Owner.DecideSfsTaskAsync(f.Task,raw)));
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.votes WHERE task_id={f.Task}").SingleAsync());
        Assert.Contains("atomic edit",(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task SfsTask_task_file_upload_and_binding_are_scoped_and_missing_binding_rolls_back_decision()
    {
        var skip=new SkipFormFileBinding();await using var h=await Seed(skip);var store=new ContentStore();var f=await NativeFormSetup(h,formSchema:FileFormSchema,files:new(h.Db,store,new ContentInspector()));
        var owner=f.Owner;var c=await owner.TaskContextAsync(f.Task);var bytes=System.Text.Encoding.UTF8.GetBytes("approval attachment");
        var scope=new FormFileScopeV2("TASK",null,f.Task,c.FormVersionId,c.Task.TaskRowVersion,c.FormDataVersion);
        var upload=new FormFileUploadV2(Guid.NewGuid(),Guid.NewGuid(),scope,"files","approval.txt",bytes.Length,FileContentBytesV1.Digest(bytes));
        var file=(await owner.UploadFileAsync(upload,new MemoryStream(bytes))).Result!;
        Assert.Equal(bytes,(await owner.DownloadFileAsync(new(scope,"files",file.Reference))).Bytes);
        var raw=SfsCommand(SfsInput(c,FileData(file.Reference)));skip.Armed=true;
        Assert.Contains("FORM_FILE_BINDING_REQUIRED",(await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>owner.DecideSfsTaskAsync(f.Task,raw))).MessageText);
        Assert.Equal(c.DataJson,(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson);Assert.Equal(FlowTaskStatus.Pending,(await h.Db.Wf_FlowTasks.AsNoTracking().SingleAsync()).Status);
        var result=await owner.DecideSfsTaskAsync(f.Task,raw);Assert.Equal("SUCCEEDED",result.Outcome);
        Assert.Equal(bytes,(await owner.DownloadFileAsync(new(scope,"files",file.Reference))).Bytes);
        Assert.Equal("COMMITTED",(await owner.FileOperationAsync(upload.OperationId,false)).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task SfsTask_current_dictionary_and_principal_field_masks_are_enforced_at_the_final_decision()
    {
        await using var h=await Seed();var type=await OptionDictionary(h);var f=await NativeFormSetup(h,formSchema:OptionFormSchema(type),values:"{\"choice\":\"A\",\"a\":\"before\"}");
        var c=await f.Owner.TaskContextAsync(f.Task);var query=new FormOptionQueryV1(new("TASK",c.FormVersionId,Id:f.Task),["choice"]);
        Assert.Equal(4,(await f.Owner.OptionsAsync(query)).Items.Length);
        var code=(await h.Db.Sys_DictTypes.AsNoTracking().SingleAsync(d=>d.Id==type)).TypeCode;
        var item=(await h.Db.Sys_DictDatas.AsNoTracking().Where(d=>d.TypeCode==code).ToArrayAsync()).Single(d=>d.Value=="A");
        await h.Db.Sys_DictDatas.Where(d=>d.Id==item.Id).ExecuteUpdateAsync(s=>s.SetProperty(d=>d.Enable,false));
        Assert.Equal("FORM_OPTION_VALUE_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.DecideSfsTaskAsync(f.Task,SfsCommand(SfsInput(c,"{\"a\":\"cannot retain revoked choice\"}"))))).Message);
        var actual=await GrantDelegate(h);var grant=await CreateTaskGrant(h,h.User,actual,"native-inbox",new(["TASK_READ"],["native-inbox"]));
        TaskActor(h,actual);var delegated=PersonalForm(h with{User=actual});
        Assert.Equal("TASK_EDIT_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>delegated.OptionsAsync(query,behalf:h.User))).Message);
        TaskActor(h,h.User);var grants=TaskGrants(h);await grants.WriteAsync("REVISE",grant.GrantId,PersonalCommand(GrantInput(actual),"enable-task-edit",grant.Version.ToString("D")));
        TaskActor(h,actual);Assert.Equal(3,(await delegated.OptionsAsync(query,behalf:h.User)).Items.Length);
        var view=await delegated.TaskContextAsync(f.Task,h.User);var raw=SfsCommand(SfsInput(view,"{\"choice\":\"a\"}"));
        TaskActor(h,h.User);var policy=(await f.Owner.PolicyAsync("personal"))!;
        await f.Owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){["choice"]="readonly"})],policy.RetentionPolicyRef,30,true,"current field revoke"));
        TaskActor(h,actual);Assert.Equal("FIELD_WRITE_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>delegated.DecideSfsTaskAsync(f.Task,raw,h.User))).Message);
        Assert.Equal(c.DataJson,(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task SfsTask_new_writes_require_SFS_shape_and_old_native_slots_remain_closable()
    {
        await using var h=await Seed();var f=await NativeFormSetup(h);var c=await f.Owner.TaskContextAsync(f.Task);
        var old=new ApprovalTaskDecisionInputV2(Guid.NewGuid(),"approve","old two versions",c.Task.TaskRowVersion,c.Task.InstanceRowVersion);
        Assert.Equal(410,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.DecideTaskAsync(f.Task,old,ifMatch:"\""+old.TaskRowVersion+"\""))).Status);
        await f.Owner.CloseAsync("FORM_TASK_DECISION",f.Task.ToString("D"),old.CommandId.ToString("D").ToUpperInvariant());
        Assert.Equal("CLOSED",(await f.Owner.TaskOriginalAsync(f.Task,old.CommandId))!.State);
        Assert.Equal("COMMAND_CLOSED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.DecideTaskAsync(f.Task,old))).Message);
        var input=SfsInput(c);
        foreach(var bad in new[]{input with{Kind="APP"},input with{TaskId=Guid.NewGuid()},input with{ExpectedFormDataVersion=null!},input with{DataPatch=PersonalData("null")}})
            Assert.Equal(400,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.DecideSfsTaskAsync(f.Task,SfsCommand(bad)))).Status);
        var mismatch=PersonalCommand(input,"mismatch",Convert.ToBase64String(new byte[8]));
        Assert.Equal("PRECONDITION_MISMATCH",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>f.Owner.DecideSfsTaskAsync(f.Task,mismatch))).Message);
        Assert.Equal("SUCCEEDED",(await f.Owner.DecideSfsTaskAsync(f.Task,SfsCommand(input with{Comment=new string('意',1000)}))).Outcome);
    }

    [CurrentPermissionPostgresFact]
    public async Task SfsTask_two_connections_cannot_commit_competing_edits_and_votes_for_one_task()
    {
        var hook=new StartWriteHook();await using var h=await Seed(hook);var f=await NativeFormSetup(h);var c=await f.Owner.TaskContextAsync(f.Task);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hook.Before=async sql=>{if(sql.CommandText.Contains("INSERT INTO cp6_personal_form_v2.votes")){hook.Before=null;entered.SetResult();await release.Task;}};
        var winner=f.Owner.DecideSfsTaskAsync(f.Task,SfsCommand(SfsInput(c,"{\"a\":\"winner\"}"),"winner"));await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var other=PersonalForm(h with{Db=peer});
        var loser=Record.ExceptionAsync(()=>other.DecideSfsTaskAsync(f.Task,SfsCommand(SfsInput(c,"{\"a\":\"loser\"}"),"loser")));
        try{await Task.Delay(100);Assert.False(loser.IsCompleted);}finally{release.TrySetResult();}
        await winner;Assert.Equal(409,Assert.IsType<ScopeAuthorizationExceptionV1>(await loser).Status);
        Assert.Contains("winner",(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson);
        Assert.Equal("NOT_OBSERVED",(await other.OriginalAsync("SFS_TASK_DECISION",f.Task.ToString("D"),"loser")).Outcome);
    }

    [CurrentPermissionPostgresFact]
    public async Task SfsTask_delegate_file_upload_rechecks_write_grant_after_scanning()
    {
        await using var h=await Seed();var store=new ContentStore();var f=await NativeFormSetup(h,formSchema:FileFormSchema,files:new(h.Db,store,new ContentInspector()));
        var actual=await GrantDelegate(h);var grant=await CreateTaskGrant(h,h.User,actual,"native-inbox");var grants=TaskGrants(h);
        TaskActor(h,actual);
        var owner=PersonalForm(h with{User=actual},files:new(h.Db,store,new FormFileInspectionHook(async()=>
        {
            TaskActor(h,h.User);await grants.WriteAsync("REVOKE",grant.GrantId,PersonalCommand(new TaskDelegationRevokeV2("revoke while scanning"),"scan-revoke",grant.Version.ToString("D")));TaskActor(h,actual);
        })));
        var c=await owner.TaskContextAsync(f.Task,h.User);var bytes=System.Text.Encoding.UTF8.GetBytes("delegate scan boundary");
        var input=new FormFileUploadV2(Guid.NewGuid(),Guid.NewGuid(),new("TASK",null,f.Task,c.FormVersionId,c.Task.TaskRowVersion,c.FormDataVersion),"files","delegate.txt",bytes.Length,FileContentBytesV1.Digest(bytes));
        Assert.Equal(403,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.UploadFileAsync(input,new MemoryStream(bytes),behalf:h.User))).Status);
        Assert.Equal("UNKNOWN",(await owner.FileOperationAsync(input.OperationId,false)).State);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_files_v2.contents WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(c.DataJson,(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson);
    }

    [CurrentPermissionPostgresFact]
    public async Task SfsTask_shared_form_delta_reaches_live_children_without_losing_item_mappings()
    {
        const string schema="""{"fields":[{"name":"a","type":"input"},{"name":"item","type":"input"},{"name":"itemIndex","type":"number"},{"name":"rows","type":"table","columns":[{"name":"item","type":"input"}]}]}""";
        await using var h=await Seed();var(owner,_,flow)=await PreparePersonal(h,schema);
        var childGraph=new FlowSchema{Start="s",Nodes=[new(){Id="s",Type="start"},new(){Id="a",Type="approval",ApproverStrategy="Specified",ApproverUserId=h.User},new(){Id="e",Type="end"}],Edges=[new(){From="s",To="a"},new(){From="a",To="e"}]};
        var child=await flow.SaveAsync("edit-child",DependentCommand(schema:JsonSerializer.Serialize(childGraph,PersonalFormOwnerV2.Json)));
        var childPin=(await flow.PublishAsync("edit-child",PublicationCommand(await Preview(flow,"edit-child",child)))).Result!.Publication!;
        await flow.SetActivationAsync("edit-child",ActivationCommand((await flow.ActivationPreviewAsync("edit-child")).Current,childPin.VersionId));
        var root=await flow.SaveAsync("edit-root",DependentCommand("personal",schema:"""{"start":"s","nodes":[{"id":"s","type":"start"},{"id":"call","type":"subFlow","subFlowKey":"edit-child","subCollectionVar":"rows","subCompletionPolicy":"all"},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"call"},{"from":"call","to":"e"}]}"""));
        var pin=(await flow.PublishAsync("edit-root",PublicationCommand(await Preview(flow,"edit-root",root)))).Result!.Publication!;
        await flow.SetActivationAsync("edit-root",ActivationCommand((await flow.ActivationPreviewAsync("edit-root")).Current,pin.VersionId));
        await owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(await owner.ContextAsync("personal"),"{\"a\":\"before\",\"rows\":[{\"item\":\"one\"},{\"item\":\"two\"}]}"),"tree"));
        var tasks=await h.Db.Wf_FlowTasks.AsNoTracking().OrderBy(t=>t.Id).ToArrayAsync();var first=await owner.TaskContextAsync(tasks[0].Id);var stale=await owner.TaskContextAsync(tasks[1].Id);
        var items=await h.Db.Wf_FlowInstances.AsNoTracking().Where(i=>i.ParentInstanceId!=null).ToDictionaryAsync(i=>i.Id,i=>JsonDocument.Parse(i.VarsJson).RootElement.GetProperty("item").GetRawText());
        await owner.DecideSfsTaskAsync(tasks[0].Id,SfsCommand(SfsInput(first,"{\"a\":\"shared edit\",\"item\":\"legal root field\",\"itemIndex\":987}"),"child-one"));
        foreach(var instance in await h.Db.Wf_FlowInstances.AsNoTracking().ToArrayAsync())
        {
            Assert.Contains("shared edit",instance.VarsJson);
            if(items.TryGetValue(instance.Id,out var item))Assert.Equal(item,JsonDocument.Parse(instance.VarsJson).RootElement.GetProperty("item").GetRawText());
        }
        var data=(await h.Db.Wf_FormDatas.AsNoTracking().SingleAsync()).DataJson;Assert.Contains("legal root field",data);Assert.Contains("987",data);
        Assert.Equal(409,(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.DecideSfsTaskAsync(tasks[1].Id,SfsCommand(SfsInput(stale),"stale-child")))).Status);
        await owner.DecideSfsTaskAsync(tasks[1].Id,SfsCommand(SfsInput(await owner.TaskContextAsync(tasks[1].Id)),"child-two"));
        Assert.All(await h.Db.Wf_FlowInstances.AsNoTracking().ToArrayAsync(),i=>Assert.Equal(FlowInstanceStatus.Approved,i.Status));
    }
}
