using System.Text;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task PersonalSnapshotRecovery_FILE_current_hold_blocks_copy_and_immutable_ancestry_allows_descendant_submission_only()
    {
        await using var h=await Seed();var(_,form,_)=await PreparePersonal(h,FileFormSchema);var store=new ContentStore();var scanner=new ContentInspector();
        var owner=PersonalForm(h,files:new(h.Db,store,scanner));var context=await owner.ContextAsync("personal");var bytes=Encoding.UTF8.GetBytes("Original snapshot file 原件");
        var file=(await owner.UploadFileAsync(FormUpload(context,bytes),new MemoryStream(bytes))).Result!;
        var original=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData(FileData(file.Reference)),"source"))));
        var next=await form.CreateNextDraftAsync("personal",FormNextDraftCommand((await form.ReadHeadAsync("personal")).Version,context.FormVersionId));
        var target=(await form.PublishAsync("personal",FormPublicationCommand(await form.PreviewPublicationAsync("personal",new(next.Result!.Draft!.DraftId,next.ResourceVersion)),"next-publish"))).Result!.Publication!;
        async Task<(PersonalFormDraftV2 Draft,Guid History)> Rebase(PersonalFormDraftV2 draft,string command)
        {
            var preview=await owner.RebasePreviewAsync(draft.DraftId,new(target.VersionId,draft.RowVersion));
            var after=PersonalResult<PersonalFormDraftV2>(await owner.RebaseDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormRebaseInputV2(preview.PreviewId,preview.Digest,target.VersionId,false),command,draft.RowVersion)));
            var history=await h.Db.Database.SqlQuery<Guid>($"SELECT h.id AS \"Value\" FROM cp6_personal_form_v2.history h JOIN cp6_personal_form_v2.commands c ON c.id=h.command_id WHERE h.draft_id={draft.DraftId} AND c.command_id={command}").SingleAsync();
            return(after,history);
        }
        var first=await Rebase(original,"first-rebase");
        var scope=new FormFileScopeV2("DRAFT",null,original.DraftId,target.VersionId,first.Draft.RowVersion);
        await EngineeringAction(h,"file-content","edit");var menu=await h.Db.Sys_Menus.SingleAsync(m=>m.MenuKey=="file-content");h.Db.Sys_RoleMenus.Add(new(){RoleId=72,MenuId=menu.MenuId});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var held=(await owner.ChangeFileAsync(new(Guid.NewGuid(),scope,"files",file.Reference,file.StateVersion,"HOLD","retain original"))).Result!;
        var input=PersonalCommand(new PersonalFormSnapshotRecoveryV2(first.History,"restore exact file"),"recover-file",first.Draft.RowVersion);
        Assert.Equal("FILE_CONTENT_NOT_AVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.RecoverSnapshotAsync(original.DraftId,input))).Message);
        Assert.Equal(1,(await owner.DraftsAsync()).Total);Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.RecoverSnapshot,original.DraftId.ToString("D"),"recover-file")).Outcome);
        await owner.ChangeFileAsync(new(Guid.NewGuid(),scope,"files",file.Reference,held.StateVersion,"RELEASE","allow original"));
        var recovered=PersonalResult<PersonalFormDraftV2>(await owner.RecoverSnapshotAsync(original.DraftId,input));
        Assert.Equal(bytes,(await owner.DownloadFileAsync(new(new("DRAFT",null,recovered.DraftId,recovered.FormVersionId),"files",file.Reference))).Bytes);
        var second=await Rebase(recovered,"second-rebase");
        var descendant=PersonalResult<PersonalFormDraftV2>(await owner.RecoverSnapshotAsync(recovered.DraftId,PersonalCommand(new PersonalFormSnapshotRecoveryV2(second.History,"copy descendant"),"recover-again",second.Draft.RowVersion)));
        Assert.Equal(original.DataJson,descendant.DataJson);
        Assert.Equal("FORM_FILE_REFERENCE_SCOPE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData(FileData(file.Reference)),"unrelated"),"unrelated")))).Message);
        var submission=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDraftAsync(descendant.DraftId,PersonalCommand(PersonalSubmit(context),"submit-descendant",descendant.RowVersion)));
        Assert.Equal(bytes,(await owner.DownloadFileAsync(new(new("HISTORY",null,submission.FormDataId,context.FormVersionId),"files",file.Reference))).Bytes);
        Assert.Single(store.Bytes);Assert.Equal(1,scanner.Calls);
        Assert.Equal(original.DraftId,await h.Db.Database.SqlQuery<Guid>($"SELECT target_id AS \"Value\" FROM cp6_form_files_v2.claims WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.draft_recoveries WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(1,await h.Db.Wf_FormDatas.CountAsync());
    }
}
