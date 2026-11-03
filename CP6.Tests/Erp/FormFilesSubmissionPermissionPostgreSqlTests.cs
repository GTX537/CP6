using System.Text;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task RemoveFormActions(Harness h,params string[] actions)
    {
        var menu=await h.Db.Sys_Menus.SingleAsync(m=>m.MenuKey=="oa-form-catalog");
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==menu.MenuId&&actions.Contains(a.ActionCode)).ExecuteDeleteAsync();
    }

    [CurrentPermissionPostgresFact]
    public async Task FormFiles_submit_only_uploads_and_submits_without_creating_draft_or_granting_add()
    {
        await using var h=await Seed();await PreparePersonal(h,FileFormSchema);await RemoveFormActions(h,"add","edit","del");
        var store=new ContentStore();var scanner=new ContentInspector();var owner=PersonalForm(h,files:new(h.Db,store,scanner));
        var context=await owner.ContextAsync("personal");Assert.Contains("SUBMIT",context.Capabilities);Assert.DoesNotContain("CREATE_DRAFT",context.Capabilities);
        var bytes=Encoding.UTF8.GetBytes("direct submit attachment without draft privilege");var input=FormUpload(context,bytes);
        var file=(await owner.UploadFileAsync(input,new MemoryStream(bytes))).Result!;
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData(FileData(file.Reference)),null))))).Message);
        var submission=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,FileData(file.Reference)),"submit-only")));
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_personal_form_v2.drafts WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Single(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Empty(await h.Db.Wf_FlowInstances.ToArrayAsync());
        Assert.Equal(bytes,(await owner.DownloadFileAsync(new(new("HISTORY",null,submission.FormDataId,context.FormVersionId),"files",file.Reference))).Bytes);
        await RemoveFormActions(h,"submit");
        Assert.Equal(file.Reference,(await owner.FileOperationAsync(input.OperationId,false)).Result!.Reference);
        Assert.Equal(file.Reference,(await owner.UploadFileAsync(input,Stream.Null)).Result!.Reference);Assert.Equal(1,scanner.Calls);
        using var denied=new MemoryStream(bytes);
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.UploadFileAsync(FormUpload(context,bytes),denied))).Message);Assert.Equal(0,denied.Position);
    }

    [CurrentPermissionPostgresFact]
    public async Task FormFiles_add_only_can_prepare_draft_but_upload_never_grants_submit_or_existing_draft_edit()
    {
        await using var h=await Seed();await PreparePersonal(h,FileFormSchema);await RemoveFormActions(h,"submit","edit");
        var owner=PersonalForm(h,files:new(h.Db,new ContentStore(),new ContentInspector()));var context=await owner.ContextAsync("personal");
        var policy=(await owner.PolicyAsync("personal"))!;
        await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,false,[new(72,[])],policy.RetentionPolicyRef,30,true,"drafts allowed without a submit binding"));
        var bytes=Encoding.UTF8.GetBytes("draft-only attachment");var file=(await owner.UploadFileAsync(FormUpload(context,bytes),new MemoryStream(bytes))).Result!;
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,FileData(file.Reference)))))).Message);
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData(FileData(file.Reference)),null))));
        var next=FormUpload(context,bytes) with{Scope=new("DRAFT",null,draft.DraftId,draft.FormVersionId,draft.RowVersion)};
        using var denied=new MemoryStream(bytes);
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.UploadFileAsync(next,denied))).Message);Assert.Equal(0,denied.Position);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task FormFiles_submit_only_checks_current_admission_and_field_before_reading_bytes()
    {
        await using var h=await Seed();await PreparePersonal(h,FileFormSchema);await RemoveFormActions(h,"add");
        var store=new ContentStore();var owner=PersonalForm(h,files:new(h.Db,store,new ContentInspector()));var context=await owner.ContextAsync("personal");
        var bytes=Encoding.UTF8.GetBytes("blocked direct input");var policy=(await owner.PolicyAsync("personal"))!;
        policy=await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,false,[new(72,[])],policy.RetentionPolicyRef,30,true,"no binding and unbound prohibited"));
        using var denied=new MemoryStream(bytes);
        Assert.Equal("FORM_BINDING_REQUIRED",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.UploadFileAsync(FormUpload(context,bytes),denied))).Message);Assert.Equal(0,denied.Position);
        await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){{"files","hidden"}})],policy.RetentionPolicyRef,30,true,"hide attachment field"));
        Assert.Equal("FIELD_FILE_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.UploadFileAsync(FormUpload(context,bytes),denied))).Message);Assert.Equal(0,denied.Position);Assert.Empty(store.Bytes);
    }

    [CurrentPermissionPostgresFact]
    public async Task FormFiles_submit_only_revoked_during_scan_cannot_commit_new_content()
    {
        await using var h=await Seed();await PreparePersonal(h,FileFormSchema);await RemoveFormActions(h,"add");
        var store=new ContentStore();var scanner=new FormFileInspectionHook(()=>RemoveFormActions(h,"submit"));
        var owner=PersonalForm(h,files:new(h.Db,store,scanner));var context=await owner.ContextAsync("personal");var bytes=Encoding.UTF8.GetBytes("revoke while scanning");var input=FormUpload(context,bytes);
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.UploadFileAsync(input,new MemoryStream(bytes)))).Message);
        Assert.Equal("UNKNOWN",(await owner.FileOperationAsync(input.OperationId,false)).State);
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_files_v2.contents WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Single(store.Bytes);
    }

    [CurrentPermissionPostgresFact]
    public async Task FormFiles_preparation_requires_current_registered_action_and_current_read()
    {
        await using var h=await Seed();await PreparePersonal(h,FileFormSchema);
        var owner=PersonalForm(h,files:new(h.Db,new ContentStore(),new ContentInspector()));var context=await owner.ContextAsync("personal");
        var menu=await h.Db.Sys_Menus.SingleAsync(m=>m.MenuKey=="oa-form-catalog");
        await h.Db.Sys_MenuActions.Where(a=>a.MenuId==menu.MenuId&&a.ActionCode=="add").ExecuteDeleteAsync();
        var bytes=Encoding.UTF8.GetBytes("registered submit remains legitimate");
        Assert.Equal("COMMITTED",(await owner.UploadFileAsync(FormUpload(context,bytes),new MemoryStream(bytes))).State);
        await h.Db.Sys_MenuActions.Where(a=>a.MenuId==menu.MenuId&&a.ActionCode=="submit").ExecuteDeleteAsync();
        using var denied=new MemoryStream(bytes);
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.UploadFileAsync(FormUpload(context,bytes),denied))).Message);Assert.Equal(0,denied.Position);
        await EngineeringAction(h,"oa-form-catalog","submit");await RemoveFormActions(h,"query");
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.UploadFileAsync(FormUpload(context,bytes),denied))).Message);Assert.Equal(0,denied.Position);
    }
}
