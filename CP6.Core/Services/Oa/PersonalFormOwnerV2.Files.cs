using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class PersonalFormOwnerV2
{
    private FormFileContentV2 Files=>fileContent??throw Error("FORM_FILE_OWNER_UNAVAILABLE");
    private string FileJournalOwner=>FormFileReferenceV2.Owner+"/"+fence.EnvironmentId.ToString("D");
    private sealed record FileContext(Guid Head,Guid Version,string Kind,Guid? Id,string Data,FormSchema Schema,Dictionary<string,string> Mask);
    private async Task<FileContext> FileAccess(FormFileScopeV2 scope,string field,UserPermissionContext actor,bool write,CancellationToken ct)
    {
        if(scope==null||scope.FormVersionId==Guid.Empty||string.IsNullOrEmpty(field)||field.Length>200)throw Error("FORM_FILE_SCOPE_INVALID",400);
        FileContext context;
        if(scope.Kind=="FORM"&&scope.Id==null&&!string.IsNullOrEmpty(scope.Key))
        {
            await Action(actor,write?"add":"query",ct);
            if(write)
            {
                var source=await Resolve(actor,scope.Key,false,ct);
                context=new(source.Head.Id,source.Form.VersionId,"FORM",null,"{}",PersonalFormValuesV2.Schema(source.Form.Fields["SchemaJson"]!),source.Mask);
            }
            else
            {
                var head=await Heads().SingleOrDefaultAsync(h=>h.Key==scope.Key,ct)??throw Error("FORM_NOT_FOUND",404);
                var form=await Publication(head.Id,scope.FormVersionId,ct);
                context=new(head.Id,form.VersionId,"FORM",null,"{}",PersonalFormValuesV2.Schema(form.Fields["SchemaJson"]!),await Mask(actor,head.Id,form,ct));
            }
        }
        else if(scope.Kind=="DRAFT"&&scope.Id is Guid draft&&scope.Key==null)
        {
            await Action(actor,write?"edit":"query",ct);var row=await Owned(draft,actor.UserId,ct);
            if(write){Active(row);Expected(row,scope.ExpectedVersion);}
            var form=await Publication(row.Head,row.Version,ct);
            context=new(row.Head,row.Version,"DRAFT",row.Id,row.Data,PersonalFormValuesV2.Schema(form.Fields["SchemaJson"]!),await Mask(actor,row.Head,form,ct));
        }
        else if(scope.Kind=="RETURNED"&&scope.Id is Guid instance&&scope.Key==null)
        {
            var source=await ReturnedSourceAsync(instance,ct);var mask=await ReturnedAccess(source,actor,ct,write?"returned-edit":null);
            var data=await db.Wf_FormDatas.AsNoTracking().SingleAsync(f=>f.Id==source.Data,ct);
            if(write)
            {
                var runtime=await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==instance,ct);
                if(runtime.Status!=FlowInstanceStatus.Draft)throw Error("ALREADY_RESUBMITTED",409);
                if(Convert.ToBase64String(runtime.RowVersion!)!=scope.ExpectedVersion||Convert.ToBase64String(data.RowVersion!)!=scope.ExpectedDataVersion)throw Error("RETURN_CONTEXT_CHANGED",409);
            }
            var form=await Publication(source.Head,source.FormVersion,ct);
            context=new(source.Head,source.FormVersion,"SUBMISSION",data.Id,data.DataJson,PersonalFormValuesV2.Schema(form.Fields["SchemaJson"]!),mask);
        }
        else if(scope.Kind=="HISTORY"&&scope.Id is Guid data&&scope.Key==null&&!write)
        {
            await Action(actor,"query",ct);var row=await Unbound(actor.UserId).SingleOrDefaultAsync(s=>s.Data==data,ct)??throw Error("RESOURCE_NOT_FOUND",404);
            var form=await Publication(row.Head,row.Version,ct);
            context=new(row.Head,row.Version,"SUBMISSION",row.Data,row.DataJson,PersonalFormValuesV2.Schema(form.Fields["SchemaJson"]!),await Mask(actor,row.Head,form,ct));
        }
        else throw Error("FORM_FILE_SCOPE_INVALID",400);
        if(context.Version!=scope.FormVersionId)throw Error("FORM_CONTEXT_CHANGED",409);
        if(!context.Mask.TryGetValue(field,out var permission)||permission=="hidden"||write&&permission!="edit"
            ||!context.Schema.Fields.Any(f=>f.Name==field&&f.Type.Equals("upload",StringComparison.OrdinalIgnoreCase)))throw Error("FIELD_FILE_FORBIDDEN",403);
        return context;
    }
    private static bool ContainsFile(string data,string field,string reference)
    {
        using var doc=JsonDocument.Parse(data);return doc.RootElement.TryGetProperty(field,out var value)&&FormFileReferenceV2.Values(value).Contains(reference,StringComparer.Ordinal);
    }
    private async Task<bool> FileLineage(FormFileContentV2.Claim claim,string kind,Guid? id,CancellationToken ct)
    {
        if(claim.Kind==kind&&claim.Id==id)return true;
        return claim.Kind=="DRAFT"&&kind=="SUBMISSION"&&await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_personal_form_v2.submissions WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}
              AND form_data_id={id} AND draft_id={claim.Id}) AS "Value"
            """).SingleAsync(ct);
    }
    private async Task<FormFileContentV2.Row> FileReference(FileContext context,string field,string reference,Guid actor,bool reading,bool available,CancellationToken ct)
    {
        var parsed=FormFileReferenceV2.Parse(reference);var row=await Files.Find(fence.EnvironmentId,parsed.Id,ct)??throw Error("FORM_FILE_NOT_FOUND",404);
        if(row.Head!=context.Head||row.Field!=field||row.Digest!=parsed.Digest)throw Error("FORM_FILE_REFERENCE_SCOPE",422);
        var claim=await Files.Claimed(fence.EnvironmentId,row.Id,ct);
        if(claim!=null)
        {
            if(!await FileLineage(claim,context.Kind,context.Id,ct))throw Error("FORM_FILE_REFERENCE_SCOPE",422);
            if(reading&&(!ContainsFile(context.Data,field,reference)||!await Files.Bound(fence.EnvironmentId,row.Id,field,context.Kind,context.Id!.Value,ct)))throw Error("FORM_FILE_NOT_FOUND",404);
        }
        else if(row.Actor!=actor||row.FormVersion!=context.Version||row.ScopeKind=="DRAFT"&&(context.Kind!="DRAFT"||row.ScopeId!=context.Id)
            ||row.ScopeKind=="RETURNED"&&(context.Kind!="SUBMISSION"||row.ScopeId!=context.Id))throw Error("FORM_FILE_REFERENCE_SCOPE",422);
        if(available)
        {
            if((await Files.View(fence.EnvironmentId,row,ct)).Availability!="AVAILABLE")throw Error("FILE_CONTENT_NOT_AVAILABLE",409);
            await Files.Bytes(row.Path,row.Size,row.Digest,ct);
        }
        return row;
    }
    private async Task<FormFileOperationV2?> FileOriginal(Guid operation,UserPermissionContext actor,byte[]? expected,CancellationToken ct)
    {
        var original=await new MainOwnerOperationJournalV1(db).Read(FileJournalOwner,operation,ct);if(original==null)return null;
        if(original.Actor!=actor.UserId||original.Contract!=FormFileReferenceV2.Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")return new(operation,"CLOSED");
        if(expected!=null&&!MainOwnerOperationJournalV1.Matches(original,expected))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var value=JsonSerializer.Deserialize<FormFileOperationV2>(original.Result)!;
        // The frozen input owns the original business scope. Re-read current field authority before disclosure.
        using var input=JsonDocument.Parse(original.Input!);
        if(!input.RootElement.TryGetProperty("Scope",out var frozenScope))throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        var scope=frozenScope.Deserialize<FormFileScopeV2>()!;
        var field=input.RootElement.GetProperty("Field").GetString()!;await FileAccess(scope,field,actor,false,ct);
        return value;
    }
    private Task FileReceipt(Guid operation,Guid actor,byte[] input,FormFileViewV2 result,object audit,CancellationToken ct)
        =>new MainOwnerOperationJournalV1(db).Store(FileJournalOwner,FormFileReferenceV2.Contract,operation,actor,input,
            new FormFileOperationV2(operation,"COMMITTED",result),[result.ContentRef.Id],audit,ct);
    public async Task<FormFileOperationV2> UploadFileAsync(FormFileUploadV2 input,Stream stream,CancellationToken ct=default)
    {
        FormFileReferenceV2.Upload(input);var raw=MainOwnerOperationJournalV1.Encode(input);Entry();
        Guid expectedActor;
        await using(var pre=await db.Database.BeginTransactionAsync(ct))
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);expectedActor=actor.UserId;await Files.Ready(ct);
            var old=await FileOriginal(input.OperationId,actor,raw,ct);
            if(old!=null){await Final(actor,lease,ct,false);return old;}
            await WriteReady(lease,ct);await FileAccess(input.Scope,input.Field,actor,true,ct);await Final(actor,lease,ct);
        }
        var staged=await Files.Stage(input,stream,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);if(actor.UserId!=expectedActor)throw Error("REAUTH_REQUIRED",401);await Files.Ready(ct);
            var old=await FileOriginal(input.OperationId,actor,raw,ct);
            if(old!=null){await Final(actor,lease,ct,false);return old;}
            await WriteReady(lease,ct);var context=await FileAccess(input.Scope,input.Field,actor,true,ct);
            if(await Files.Find(fence.EnvironmentId,input.FileId,ct)!=null)throw Error("FILE_ID_CONFLICT",409);
            var value=await Files.Create(fence.EnvironmentId,actor.UserId,context.Head,input.Scope.Kind,context.Id,input,staged,ct);
            await FileReceipt(input.OperationId,actor.UserId,raw,value,new{Scope=input.Scope,input.Field,lease.Snapshot},ct);
            await Final(actor,lease,ct);committing=true;await tx.CommitAsync(ct);return new(input.OperationId,"COMMITTED",value);
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
    public async Task<FormFileOperationV2> FileOperationAsync(Guid operation,bool close,CancellationToken ct=default)
    {
        if(operation==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await Files.Ready(ct);
            var old=await FileOriginal(operation,actor,null,ct);
            if(old!=null){await Final(actor,lease,ct,false);return old;}
            await Action(actor,"query",ct);var result=new FormFileOperationV2(operation,close?"CLOSED":"UNKNOWN");
            if(close)
            {
                await WriteReady(lease,ct);await new MainOwnerOperationJournalV1(db).Store(FileJournalOwner,FormFileReferenceV2.Contract,operation,actor.UserId,null,result,[],new{Action="CLOSE_UNOBSERVED_FORM_FILE"},ct);
                await Final(actor,lease,ct);committing=true;await tx.CommitAsync(ct);
            }
            else await Final(actor,lease,ct,false);return result;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
    public async Task<FormFileViewV2> ReadFileAsync(FormFileReadV2 input,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);var actor=await Actor(ct);
        var context=await FileAccess(input.Scope,input.Field,actor,false,ct);await Files.Ready(ct);
        var row=await FileReference(context,input.Field,input.Reference,actor.UserId,true,false,ct);var value=await Files.View(fence.EnvironmentId,row,ct);
        await Final(actor,lease,ct,false);return value;
    }
    public async Task<FormFileOperationV2> ChangeFileAsync(FormFileStateCommandV2 input,CancellationToken ct=default)
    {
        if(input.OperationId==Guid.Empty||input.Action is not("HOLD" or "RELEASE" or "INSPECT")||string.IsNullOrWhiteSpace(input.Reason)||input.Reason.Length>1000)throw Error("FILE_COMMAND_INVALID",400);
        var raw=MainOwnerOperationJournalV1.Encode(input);Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);await Files.Ready(ct);var old=await FileOriginal(input.OperationId,actor,raw,ct);
            if(old!=null){await Final(actor,lease,ct,false);return old;}
            await WriteReady(lease,ct);var context=await FileAccess(input.Scope,input.Field,actor,true,ct);
            var row=await FileReference(context,input.Field,input.Reference,actor.UserId,true,false,ct);var before=await Files.View(fence.EnvironmentId,row,ct);
            if(input.Action is "HOLD" or "RELEASE")
            {
                // Form editing does not grant retention-control authority. This contract manages owned files only;
                // broader department/other-Owner file administration remains with its explicit adapter.
                await Action(actor,"edit",ct,"file-content");
                var scope=actor.DataScopes.GetValueOrDefault("file-content",1);
                if(scope!=5&&(scope!=1||row.Actor!=actor.UserId))throw Error("FILE_CONTROL_SCOPE_FORBIDDEN",403);
                var fields=actor.FieldPerms.GetValueOrDefault("file-content");
                if(new[]{"Content","FileName"}.Any(f=>(fields?.GetValueOrDefault(f,1)??1)!=1))throw Error("FIELD_WRITE_FORBIDDEN",403);
            }
            if(before.StateVersion!=input.ExpectedStateVersion)throw Error("FILE_STATE_CHANGED",412);
            var value=await Files.Change(fence.EnvironmentId,actor.UserId,row,before,input.OperationId,input.Action,ct);
            await FileReceipt(input.OperationId,actor.UserId,raw,value,new{input.Action,input.Reason,lease.Snapshot},ct);await Final(actor,lease,ct);
            committing=true;await tx.CommitAsync(ct);return new(input.OperationId,"COMMITTED",value);
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
    private async Task<FileContentDownloadV1?> FileBytes(FormFileContentV2.Row row,Guid actor,CancellationToken ct)
    {
        var view=await Files.View(fence.EnvironmentId,row,ct);if(view.Availability!="AVAILABLE")throw Error("FILE_CONTENT_NOT_AVAILABLE",409);
        try{return new(await Files.Bytes(row.Path,row.Size,row.Digest,ct),view.ContentRef,row.Name);}
        catch(ScopeAuthorizationExceptionV1 e) when(e.Message=="FILE_CONTENT_INTEGRITY_UNAVAILABLE")
        {
            var id=Guid.NewGuid();var after=await Files.Change(fence.EnvironmentId,actor,row,view,id,"INTEGRITY_FAILURE",ct);
            await FileReceipt(id,actor,MainOwnerOperationJournalV1.Encode(new{Action="INTEGRITY_FAILURE",row.Id}),after,new{Action="INTEGRITY_FAILURE"},ct);return null;
        }
    }
    public async Task<FileContentDownloadV1> DownloadFileAsync(FormFileReadV2 input,CancellationToken ct=default)
    {
        Entry();FileContentDownloadV1? result;await using(var tx=await db.Database.BeginTransactionAsync(ct))
        {
            var lease=await ReadLease(ct);var actor=await Actor(ct);var context=await FileAccess(input.Scope,input.Field,actor,false,ct);await Files.Ready(ct);
            var row=await FileReference(context,input.Field,input.Reference,actor.UserId,true,false,ct);result=await FileBytes(row,actor.UserId,ct);
            await Final(actor,lease,ct,false);await tx.CommitAsync(ct);
        }
        return result??throw Error("FILE_CONTENT_INTEGRITY_UNAVAILABLE");
    }
    // Ordinary form mutation already owns the exact schema, identity and field merge. Only new references need admission;
    // unchanged hidden/readonly draft values remain retained. Submission rechecks all current availability.
    private async Task CheckFiles(Guid head,Guid version,string kind,Guid? target,Guid actor,FormSchema schema,string data,string? before,bool requireAll,IReadOnlyDictionary<string,string> mask,CancellationToken ct)
    {
        using var document=JsonDocument.Parse(data);var references=schema.Fields.Where(f=>f.Type.Equals("upload",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(references.Length==0)return;await Files.Ready(ct);
        var context=new FileContext(head,version,kind,target,data,schema,[]);
        foreach(var field in references)
            if(document.RootElement.TryGetProperty(field.Name,out var value))
                foreach(var reference in FormFileReferenceV2.Values(value))
                {
                    var changed=before==null||!ContainsFile(before,field.Name,reference);
                    if(changed&&mask[field.Name]!="edit")throw Error("FIELD_WRITE_FORBIDDEN",403);
                    await FileReference(context,field.Name,reference,actor,false,requireAll||changed,ct);
                }
    }
    private async Task BindFiles(Guid command,Guid version,string kind,Guid target,string token,FormSchema schema,string data,CancellationToken ct)
    {
        using var doc=JsonDocument.Parse(data);
        foreach(var field in schema.Fields.Where(f=>f.Type.Equals("upload",StringComparison.OrdinalIgnoreCase)))
            if(doc.RootElement.TryGetProperty(field.Name,out var value))
                foreach(var reference in FormFileReferenceV2.Values(value))await Files.Bind(fence.EnvironmentId,FormFileReferenceV2.Parse(reference).Id,field.Name,kind,target,version,token,command,ct);
    }
    private async Task<FormFileContentV2.Row> BoundFileRow(NativeFormContentV2 content,string field,string reference,CancellationToken ct)
    {
        if(!content.FieldMask.TryGetValue(field,out var access)||access=="hidden"||!ContainsFile(content.DataJson,field,reference))throw Error("FIELD_FILE_FORBIDDEN",403);
        await Files.Ready(ct);var parsed=FormFileReferenceV2.Parse(reference);var row=await Files.Find(fence.EnvironmentId,parsed.Id,ct)??throw Error("FORM_FILE_NOT_FOUND",404);
        if(row.Digest!=parsed.Digest||!await Files.Bound(fence.EnvironmentId,row.Id,field,"SUBMISSION",content.FormDataId,ct))throw Error("FORM_FILE_NOT_FOUND",404);
        return row;
    }
    internal async Task<FormFileViewV2> BoundFileView(NativeFormContentV2 content,string field,string reference,CancellationToken ct)
        =>await Files.View(fence.EnvironmentId,await BoundFileRow(content,field,reference,ct),ct);
    internal async Task<FileContentDownloadV1?> BoundFileBytes(NativeFormContentV2 content,string field,string reference,Guid actor,CancellationToken ct)
        =>await FileBytes(await BoundFileRow(content,field,reference,ct),actor,ct);
}
