using System.Text;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string FileFormSchema="""{"fields":[{"name":"a","type":"input"},{"name":"files","type":"upload"},{"name":"other","type":"upload"}]}""";
    private static FormFileUploadV2 FormUpload(PersonalFormContextV2 context,byte[] bytes,string field="files")
        =>new(Guid.NewGuid(),Guid.NewGuid(),new("FORM",context.FormKey,null,context.FormVersionId),field,"普通表单原件.txt",bytes.Length,FileContentBytesV1.Digest(bytes));
    private static string FileData(string reference,string field="files")=>JsonSerializer.Serialize(new Dictionary<string,object>{{"a","original"},{field,new[]{reference}}});
    private sealed class FormFileInspectionHook(Func<Task> callback):IFileContentInspectorV1
    {
        public async Task<FileInspectionV1> InspectAsync(byte[] bytes,CancellationToken ct){await callback();return await new ContentInspector().InspectAsync(bytes,ct);}
    }
    private sealed class SkipFormFileBinding:DbCommandInterceptor
    {
        internal bool Armed;
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<int> result,CancellationToken ct=default)
        {
            if(Armed&&command.CommandText.Contains("INSERT INTO cp6_form_files_v2.claims")){Armed=false;return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));}
            return ValueTask.FromResult(result);
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task FormFiles_final_revoke_unknown_commit_and_current_read_recovery_never_reupload_or_claim_no_effect()
    {
        var fault=new CommitFault();await using var h=await Seed(fault);await PreparePersonal(h,FileFormSchema);var store=new ContentStore();var scanner=new ContentInspector();
        var owner=PersonalForm(h,files:new(h.Db,store,scanner));var context=await owner.ContextAsync("personal");var bytes=Encoding.UTF8.GetBytes("unknown commit original");var input=FormUpload(context,bytes);
        fault.Armed=true;Assert.Equal("COMMAND_OUTCOME_UNKNOWN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.UploadFileAsync(input,new MemoryStream(bytes)))).Message);
        var menu=await h.Db.Sys_Menus.SingleAsync(m=>m.MenuKey=="oa-form-catalog");
        await h.Db.Sys_RoleActions.Where(a=>a.MenuId==menu.MenuId&&(a.ActionCode=="add"||a.ActionCode=="submit")).ExecuteDeleteAsync();
        Assert.Equal("COMMITTED",(await owner.FileOperationAsync(input.OperationId,true)).State);
        Assert.Equal("COMMITTED",(await owner.UploadFileAsync(input,Stream.Null)).State);Assert.Single(store.Bytes);Assert.Equal(1,scanner.Calls);
        using var unread=new MemoryStream(bytes);Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.UploadFileAsync(input with{OperationId=Guid.NewGuid(),FileId=Guid.NewGuid()},unread))).Message);Assert.Equal(0,unread.Position);
        await EngineeringAction(h,"oa-form-catalog","add");var next=FormUpload(context,bytes);
        var revoke=PersonalForm(h,files:new(h.Db,store,new FormFileInspectionHook(async()=>{await h.Db.Sys_RoleActions.Where(a=>a.MenuId==menu.MenuId&&a.ActionCode=="add").ExecuteDeleteAsync();})));
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>revoke.UploadFileAsync(next,new MemoryStream(bytes)))).Message);
        Assert.Equal("UNKNOWN",(await owner.FileOperationAsync(next.OperationId,false)).State);Assert.Equal("CLOSED",(await owner.FileOperationAsync(next.OperationId,true)).State);
        Assert.Equal(2,store.Bytes.Count);Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_files_v2.contents WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task FormFiles_missing_binding_at_commit_rolls_back_form_command_data_and_claim_then_same_command_succeeds()
    {
        var skip=new SkipFormFileBinding();await using var h=await Seed(skip);await PreparePersonal(h,FileFormSchema);var store=new ContentStore();
        var owner=PersonalForm(h,files:new(h.Db,store,new ContentInspector()));var context=await owner.ContextAsync("personal");var bytes=Encoding.UTF8.GetBytes("atomic binding");
        var file=(await owner.UploadFileAsync(FormUpload(context,bytes),new MemoryStream(bytes))).Result!;var raw=PersonalCommand(PersonalDirect(context,FileData(file.Reference)),"atomic");
        skip.Armed=true;var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>owner.SubmitDirectAsync("personal",raw));Assert.Contains("FORM_FILE_BINDING_REQUIRED",error.MessageText);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());Assert.Empty(await h.Db.Wf_Notifications.ToArrayAsync());
        Assert.Equal(0,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_files_v2.claims WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal("NOT_OBSERVED",(await owner.OriginalAsync(PersonalFormOwnerV2.Direct,"personal","atomic")).Outcome);
        var accepted=await owner.SubmitDirectAsync("personal",raw);Assert.Equal("SUCCEEDED",accepted.Outcome);Assert.Single(await h.Db.Wf_FormDatas.ToArrayAsync());
        Assert.Equal(accepted.ResourceId,(await owner.SubmitDirectAsync("personal",raw)).ResourceId);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormFiles_exact_staging_draft_binding_submit_and_original_receipts_share_real_transaction()
    {
        await using var h=await Seed();await PreparePersonal(h,FileFormSchema);var store=new ContentStore();var scanner=new ContentInspector();
        var owner=PersonalForm(h,files:new(h.Db,store,scanner));var context=await owner.ContextAsync("personal");var bytes=Encoding.UTF8.GetBytes("FORM immutable original\r\n原字节");
        var input=FormUpload(context,bytes);var uploaded=await owner.UploadFileAsync(input,new MemoryStream(bytes));var reference=uploaded.Result!.Reference;
        Assert.Equal("AVAILABLE",uploaded.Result.Availability);Assert.Single(store.Bytes);
        Assert.Equal(reference,(await owner.UploadFileAsync(input,Stream.Null)).Result!.Reference);Assert.Equal(1,scanner.Calls);
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData(FileData(reference)),null))));
        var read=new FormFileReadV2(new("DRAFT",null,draft.DraftId,draft.FormVersionId,draft.RowVersion),"files",reference);
        Assert.Equal(bytes,(await owner.DownloadFileAsync(read)).Bytes);
        Assert.Equal("FORM_FILE_REFERENCE_SCOPE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,FileData(reference)),"reuse-elsewhere")))).Message);
        var submitted=PersonalResult<PersonalFormSubmissionV2>(await owner.SubmitDraftAsync(draft.DraftId,PersonalCommand(PersonalSubmit(context),"submit-files",draft.RowVersion)));
        var history=read with{Scope=new("HISTORY",null,submitted.FormDataId,context.FormVersionId)};
        Assert.Equal(bytes,(await owner.DownloadFileAsync(history)).Bytes);
        Assert.Equal(2,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_form_files_v2.bindings WHERE tenant_id={h.Tenant}").SingleAsync());
        Assert.Equal(reference,(await owner.FileOperationAsync(input.OperationId,false)).Result!.Reference);
        Assert.Equal("FORM_FILE_REFERENCE_SCOPE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.ReadFileAsync(history with{Field="other"}))).Message);
        var policy=(await owner.PolicyAsync("personal"))!;await owner.ConfigureAsync("personal",new(Guid.NewGuid(),policy.Version.ToString("D"),true,true,[new(72,new(){{"files","hidden"}})],policy.RetentionPolicyRef,30,true,"hide file field"));
        Assert.Equal("FIELD_FILE_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.DownloadFileAsync(history))).Message);
        Assert.Equal("FIELD_FILE_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.FileOperationAsync(input.OperationId,false))).Message);
    }
    [CurrentPermissionPostgresFact]
    public async Task FormFiles_pending_hold_integrity_and_deleted_draft_do_not_pretend_available_or_delete_bytes()
    {
        await using var h=await Seed();await PreparePersonal(h,FileFormSchema);var store=new ContentStore();var scanner=new ContentInspector{Status="PENDING"};
        var owner=PersonalForm(h,files:new(h.Db,store,scanner));var context=await owner.ContextAsync("personal");var bytes=Encoding.UTF8.GetBytes("original file");
        var input=FormUpload(context,bytes);var file=(await owner.UploadFileAsync(input,new MemoryStream(bytes))).Result!;var read=new FormFileReadV2(input.Scope,"files",file.Reference);
        Assert.Equal("PENDING",file.Availability);
        Assert.Equal("FILE_CONTENT_NOT_AVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.SubmitDirectAsync("personal",PersonalCommand(PersonalDirect(context,FileData(file.Reference)))))).Message);
        Assert.Empty(await h.Db.Wf_FormDatas.ToArrayAsync());scanner.Status="CLEAN";
        file=(await owner.ChangeFileAsync(new(Guid.NewGuid(),input.Scope,"files",file.Reference,file.StateVersion,"INSPECT","explicit test scan"))).Result!;
        Assert.Equal("ACTION_FORBIDDEN",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.ChangeFileAsync(new(Guid.NewGuid(),input.Scope,"files",file.Reference,file.StateVersion,"HOLD","ordinary form edit is insufficient")))).Message);
        await EngineeringAction(h,"file-content","edit");var fileMenu=await h.Db.Sys_Menus.SingleAsync(m=>m.MenuKey=="file-content");
        h.Db.Sys_RoleMenus.Add(new(){RoleId=72,MenuId=fileMenu.MenuId});await h.Db.SaveChangesAsync();h.Db.ChangeTracker.Clear();
        var held=(await owner.ChangeFileAsync(new(Guid.NewGuid(),input.Scope,"files",file.Reference,file.StateVersion,"HOLD","retain original"))).Result!;
        Assert.Equal("FILE_CONTENT_NOT_AVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.DownloadFileAsync(read))).Message);
        file=(await owner.ChangeFileAsync(new(Guid.NewGuid(),input.Scope,"files",file.Reference,held.StateVersion,"RELEASE","release hold"))).Result!;
        var draft=PersonalResult<PersonalFormDraftV2>(await owner.CreateDraftAsync("personal",PersonalCommand(new PersonalFormCreateV2(context.FormVersionId,PersonalData(FileData(file.Reference)),null))));
        var deleted=PersonalResult<PersonalFormDraftV2>(await owner.DeleteDraftAsync(draft.DraftId,PersonalCommand(new PersonalFormReasonV2("keep immutable bytes"),"delete",draft.RowVersion)));
        Assert.Single(store.Bytes);Assert.Equal(2,deleted.Status);
        read=read with{Scope=new("DRAFT",null,draft.DraftId,draft.FormVersionId)};
        var key=store.Bytes.Keys.Single();store.Bytes[key]=Encoding.UTF8.GetBytes("corrupt bytes");
        Assert.Equal("FILE_CONTENT_INTEGRITY_UNAVAILABLE",(await Assert.ThrowsAsync<ScopeAuthorizationExceptionV1>(()=>owner.DownloadFileAsync(read))).Message);
        Assert.Equal("QUARANTINED",(await owner.ReadFileAsync(read)).Availability);
    }
}
