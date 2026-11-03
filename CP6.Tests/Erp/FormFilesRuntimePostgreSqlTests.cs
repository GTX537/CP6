using System.Text;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task FormFiles_returned_current_cycle_replaces_reference_atomically_and_inbox_reads_current_field_authority()
    {
        await using var h=await Seed();var(_,_,flow)=await PreparePersonal(h,FileFormSchema);await NativeOperationPermissions(h);
        var store=new ContentStore();var owner=PersonalForm(h,files:new(h.Db,store,new ContentInspector()));
        var saved=await flow.SaveAsync("file-flow",DependentCommand("personal",schema:JsonSerializer.Serialize(LinearNative(h.User),PersonalFormOwnerV2.Json)));
        var pub=(await flow.PublishAsync("file-flow",PublicationCommand(await Preview(flow,"file-flow",saved)))).Result!.Publication!;
        await flow.SetActivationAsync("file-flow",ActivationCommand((await flow.ActivationPreviewAsync("file-flow")).Current,pub.VersionId));
        var context=await owner.ContextAsync("personal");var bytes=Encoding.UTF8.GetBytes("first immutable version");var file=(await owner.UploadFileAsync(FormUpload(context,bytes),new MemoryStream(bytes))).Result!;
        var submit=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,FileData(file.Reference)))));
        var root=submit.FlowInstanceId!.Value;var inbox=NativeInbox(h,owner);Assert.Equal(bytes,(await inbox.DownloadFileAsync(root,new("files",file.Reference))).Bytes);
        var task=await h.Db.Wf_FlowTasks.SingleAsync();await owner.SendBackAsync(task.Id,SendBackCommand(await owner.SendBackPreviewAsync(task.Id,new("STARTER",null))));
        var returned=await owner.ReturnedFormAsync(root);var secondBytes=Encoding.UTF8.GetBytes("replacement immutable version");
        var scope=new FormFileScopeV2("RETURNED",null,root,returned.FormDefVersionId,returned.InstanceVersion,returned.FormDataVersion);
        var next=FormUpload(context,secondBytes) with{Scope=scope};var second=(await owner.UploadFileAsync(next,new MemoryStream(secondBytes))).Result!;
        var patched=PersonalResult<ReturnedFormResultV2>(await owner.EditReturnedAsync(root,PersonalCommand(new ReturnedFormEditV2(returned.ReturnRevision,returned.FormDataVersion,PersonalData(FileData(second.Reference)),"replace returned attachment"),"replace",returned.InstanceVersion)));
        Assert.Equal(secondBytes,(await inbox.DownloadFileAsync(root,new("files",second.Reference))).Bytes);
        Assert.Equal("FIELD_FILE_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>inbox.DownloadFileAsync(root,new("files",file.Reference)))).Message);
        var snapshot=await h.Db.Database.SqlQuery<string>($"SELECT snapshot_json AS \"Value\" FROM cp6_personal_form_v2.returns WHERE instance_id={root}").SingleAsync();Assert.Contains(file.Reference,snapshot);Assert.DoesNotContain(second.Reference,snapshot);
        var resumed=await owner.ResubmitReturnedAsync(root,PersonalCommand(new ReturnedFormResubmitV2(returned.ReturnRevision,patched.FormDataVersion,null),"resubmit",patched.InstanceVersion));Assert.Equal("SUCCEEDED",resumed.Outcome);
        var policy=(await owner.PolicyAsync("personal"))!;await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){{"files","hidden"}})],policy.RetentionPolicyRef,30,true,"hide current file field"));
        Assert.DoesNotContain(second.Reference,(await inbox.DetailAsync(root)).Form!.DataJson);
        Assert.Equal("FIELD_FILE_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>inbox.ReadFileAsync(root,new("files",second.Reference)))).Message);
        Assert.Equal(2,store.Bytes.Count);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormFiles_same_operation_converges_and_concurrent_drafts_cannot_claim_one_staged_file_twice()
    {
        await using var h=await Seed();await PreparePersonal(h,FileFormSchema);var store=new ContentStore();var scan=new ContentInspector();
        var first=PersonalForm(h,files:new(h.Db,store,scan));var context=await first.ContextAsync("personal");var bytes=Encoding.UTF8.GetBytes("concurrent owned staging");var input=FormUpload(context,bytes);
        await using var peer=server.Context(new TenantContext{CurrentTenantId=h.Tenant});var second=PersonalForm(h with{Db=peer},files:new(peer,store,scan));
        var results=await Task.WhenAll(first.UploadFileAsync(input,new MemoryStream(bytes)),second.UploadFileAsync(input,new MemoryStream(bytes)));
        Assert.Equal(results[0].Result!.Reference,results[1].Result!.Reference);
        async Task<bool> Claim(PersonalFormOwnerV2 owner,string command)
        {
            try { await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData(FileData(results[0].Result!.Reference)),null),command));return true; }
            catch(ScopeAuthorizationExceptionV1 e){Assert.Equal("FORM_FILE_REFERENCE_SCOPE",e.Message);return false;}
        }
        Assert.Single(await Task.WhenAll(Claim(first,"draft-one"),Claim(second,"draft-two")),x=>x);
        Assert.Single((await first.DraftsAsync()).Items);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_files_v2.contents WHERE tenant_id={h.Tenant}").SingleAsync());
        await using var foreign=await Seed();await PreparePersonal(foreign,FileFormSchema);var foreignOwner=PersonalForm(foreign,files:new(foreign.Db,store,scan));var foreignContext=await foreignOwner.ContextAsync("personal");
        Assert.Equal("FORM_FILE_NOT_FOUND",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>foreignOwner.ReadFileAsync(new(new("FORM","personal",null,foreignContext.FormVersionId),"files",results[0].Result!.Reference)))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormFiles_hidden_retained_value_survives_partial_save_and_rebase_preserves_original_content_identity()
    {
        await using var h=await Seed();var(_,form,_)=await PreparePersonal(h,FileFormSchema);var store=new ContentStore();var owner=PersonalForm(h,files:new(h.Db,store,new ContentInspector()));
        var context=await owner.ContextAsync("personal");var bytes=Encoding.UTF8.GetBytes("version-independent immutable content");var file=(await owner.UploadFileAsync(FormUpload(context,bytes),new MemoryStream(bytes))).Result!;
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData(FileData(file.Reference)),null))));
        var policy=(await owner.PolicyAsync("personal"))!;await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){{"files","hidden"}})],policy.RetentionPolicyRef,30,true,"retain hidden original"));
        var hidden=PersonalResult<PersonalFormDraftV2>(await owner.SaveDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormSaveV2(PersonalData("{\"a\":\"changed\"}"),null),"save-hidden",draft.RowVersion)));
        Assert.DoesNotContain(file.Reference,hidden.DataJson);
        Assert.Contains(file.Reference,await h.Db.Database.SqlQuery<string>($"SELECT data_json AS \"Value\" FROM cp6_personal_form_v2.drafts WHERE id={draft.DraftId}").SingleAsync());
        policy=(await owner.PolicyAsync("personal"))!;await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,[])],policy.RetentionPolicyRef,30,true,"restore edit"));
        var head=await form.ReadHeadAsync("personal");var next=(await form.CreateNextDraftAsync("personal",PersonalCommand(new FormNextDraftBodyV2(context.FormVersionId),"next",head.Version))).Result!.Draft!;
        var publication=(await form.PublishAsync("personal",FormPublicationCommand(await form.PreviewPublicationAsync("personal",new(next.DraftId,next.Version)),"publish-next"))).Result!.Publication!;
        var preview=await owner.RebasePreviewAsync(draft.DraftId,new(publication.VersionId,hidden.RowVersion));
        var rebased=PersonalResult<PersonalFormDraftV2>(await owner.RebaseDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormRebaseInputV2(preview.PreviewId,preview.Digest,publication.VersionId,false),"rebase",hidden.RowVersion)));
        Assert.Contains(file.Reference,rebased.DataJson);Assert.Equal(bytes,(await owner.DownloadFileAsync(new(new("DRAFT",null,draft.DraftId,publication.VersionId),"files",file.Reference))).Bytes);
        Assert.Single(store.Bytes);
    }
}
