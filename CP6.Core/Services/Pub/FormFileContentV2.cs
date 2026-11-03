using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pub;

// Internal participant in the actual FORM Owner transaction. The caller owns business/field authority.
// Storage and scanning happen outside that transaction; no cleanup guesses an uncommitted outcome.
public sealed class FormFileContentV2(CP6Context db,IFileStore files,IFileContentInspectorV1 inspector)
{
    internal sealed record Staged(string Path,string MediaType,FileInspectionV1 Inspection);
    internal sealed record Row(Guid Id,Guid Actor,Guid Head,Guid FormVersion,string Field,string ScopeKind,Guid? ScopeId,
        string Name,string Digest,long Size,string MediaType,string Path);
    internal sealed record State(long Revision,string Inspection,bool Held);
    internal sealed record Claim(string Kind,Guid Id);
    private static ScopeAuthorizationExceptionV1 Error(string code,int status=503)=>new(code,status);
    internal async Task Ready(CancellationToken ct)
    {
        if(db.Database.CurrentTransaction==null)throw new InvalidOperationException("FORM_FILE_TRANSACTION_REQUIRED");
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_form_files_v2.ready() AS \"Value\"").SingleAsync(ct))throw Error("FORM_FILE_WRITER_UNAVAILABLE");
    }
    internal async Task<Staged> Stage(FormFileUploadV2 input,Stream stream,CancellationToken ct)
    {
        var bytes=await FileContentBytesV1.Read(stream,input.ByteLength,ct);
        if(FileContentBytesV1.Digest(bytes)!=input.Digest)throw Error("FILE_CONTENT_MISMATCH",400);
        var path=await files.SaveAsync(new MemoryStream(bytes,false),"form-content-v2/"+db.CurrentTenantId.ToString("N")+"/"+Guid.NewGuid().ToString("N"));
        await Bytes(path,input.ByteLength,input.Digest,ct);
        return new(path,FileContentBytesV1.MediaType(bytes),await Inspect(bytes,ct));
    }
    private async Task<FileInspectionV1> Inspect(byte[] bytes,CancellationToken ct)
    {
        var result=await inspector.InspectAsync(bytes,ct);
        if(result==null||result.Status is not("CLEAN" or "PENDING" or "NOT_CLEAN")||string.IsNullOrWhiteSpace(result.Provider)
            ||result.Status=="CLEAN"&&(string.IsNullOrWhiteSpace(result.EngineVersion)||string.IsNullOrWhiteSpace(result.SignatureVersion)||result.EvidenceDigest?.Length!=64))
            throw Error("FILE_INSPECTION_UNAVAILABLE");
        return result;
    }
    internal async Task<byte[]> Bytes(string path,long size,string digest,CancellationToken ct)
    {
        try
        {
            await using var stream=await files.OpenReadBoundedAsync(path,size,ct);var bytes=await FileContentBytesV1.Read(stream,size,ct);
            if(FileContentBytesV1.Digest(bytes)!=digest)throw Error("FILE_CONTENT_INTEGRITY_UNAVAILABLE");return bytes;
        }
        catch(Exception e) when(e is IOException or NotSupportedException || e is CP6.Core.Services.Erp.OrderPriceCorrectionException {Message:"FILE_LENGTH_MISMATCH" or "FILE_SIZE_LIMIT"})
        {throw new ScopeAuthorizationExceptionV1("FILE_CONTENT_INTEGRITY_UNAVAILABLE",503,e);}
    }
    internal Task<Row?> Find(Guid environment,Guid id,CancellationToken ct)=>db.Database.SqlQuery<Row>($"""
        SELECT file_id AS "Id",creator_id AS "Actor",head_id AS "Head",form_version AS "FormVersion",field_name AS "Field",
          scope_kind AS "ScopeKind",scope_id AS "ScopeId",file_name AS "Name",digest AS "Digest",byte_length AS "Size",media_type AS "MediaType",store_path AS "Path"
        FROM cp6_form_files_v2.contents WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND file_id={id}
        """).SingleOrDefaultAsync(ct);
    internal Task<Claim?> Claimed(Guid environment,Guid id,CancellationToken ct)=>db.Database.SqlQuery<Claim>($"""
        SELECT target_kind AS "Kind",target_id AS "Id" FROM cp6_form_files_v2.claims
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND file_id={id}
        """).SingleOrDefaultAsync(ct);
    internal async Task<FormFileViewV2> View(Guid environment,Row row,CancellationToken ct)
    {
        var state=await db.Database.SqlQuery<State>($"""
            SELECT revision AS "Revision",inspection AS "Inspection",held AS "Held" FROM cp6_form_files_v2.events
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND file_id={row.Id} ORDER BY revision DESC LIMIT 1
            """).SingleAsync(ct);
        var inspection=JsonSerializer.Deserialize<FileInspectionV1>(state.Inspection)!;
        return new(FormFileReferenceV2.Encode(row.Id,row.Digest),row.Name,new(FormFileReferenceV2.Owner,row.Id,"1","SHA256",row.Digest,row.Size,row.MediaType),
            state.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),inspection,state.Held,state.Held?"HELD":inspection.Status=="CLEAN"?"AVAILABLE":inspection.Status=="PENDING"?"PENDING":"QUARANTINED");
    }
    internal async Task<FormFileViewV2> Create(Guid environment,Guid actor,Guid head,string scopeKind,Guid? scopeId,FormFileUploadV2 input,Staged staged,CancellationToken ct)
    {
        await Bytes(staged.Path,input.ByteLength,input.Digest,ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_form_files_v2.contents(tenant_id,environment_id,file_id,creator_id,head_id,form_version,field_name,scope_kind,scope_id,file_name,digest,byte_length,media_type,store_path,operation_id)
            VALUES({db.CurrentTenantId},{environment},{input.FileId},{actor},{head},{input.Scope.FormVersionId},{input.Field},{scopeKind},{scopeId},{input.FileName},{input.Digest},{input.ByteLength},{staged.MediaType},{staged.Path},{input.OperationId})
            """,ct);
        await Event(environment,input.FileId,input.OperationId,actor,1,staged.Inspection,false,"UPLOAD",ct);
        return await View(environment,(await Find(environment,input.FileId,ct))!,ct);
    }
    private Task Event(Guid environment,Guid file,Guid operation,Guid actor,long revision,FileInspectionV1 inspection,bool held,string action,CancellationToken ct)
        =>db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_form_files_v2.events(tenant_id,environment_id,file_id,revision,operation_id,actor_id,inspection,held,action)
            VALUES({db.CurrentTenantId},{environment},{file},{revision},{operation},{actor},{JsonSerializer.Serialize(inspection)},{held},{action})
            """,ct);
    internal async Task<FormFileViewV2> Change(Guid environment,Guid actor,Row row,FormFileViewV2 before,Guid operation,string action,CancellationToken ct)
    {
        var inspection=before.Inspection;
        if(action=="INSPECT")
        {
            try{inspection=await Inspect(await Bytes(row.Path,row.Size,row.Digest,ct),ct);}
            catch(ScopeAuthorizationExceptionV1 e) when(e.Message=="FILE_CONTENT_INTEGRITY_UNAVAILABLE")
            {inspection=new("NOT_CLEAN","ContentIntegrity",null,null,DateTimeOffset.UtcNow,null,e.Message);}
        }
        else if(action=="INTEGRITY_FAILURE")inspection=new("NOT_CLEAN","ContentIntegrity",null,null,DateTimeOffset.UtcNow,null,"FILE_CONTENT_INTEGRITY_UNAVAILABLE");
        var held=action=="HOLD"||action!="RELEASE"&&before.Held;
        await Event(environment,row.Id,operation,actor,long.Parse(before.StateVersion)+1,inspection,held,action,ct);
        return await View(environment,row,ct);
    }
    internal async Task Bind(Guid environment,Guid file,string field,string targetKind,Guid target,Guid formVersion,string targetVersion,Guid command,CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_form_files_v2.claims(tenant_id,environment_id,file_id,target_kind,target_id,command_id)
            SELECT {db.CurrentTenantId},{environment},{file},{targetKind},{target},{command}
            WHERE NOT EXISTS(SELECT FROM cp6_form_files_v2.claims WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND file_id={file});
            INSERT INTO cp6_form_files_v2.bindings(tenant_id,environment_id,file_id,field_name,target_kind,target_id,form_version,target_version,command_id)
            VALUES({db.CurrentTenantId},{environment},{file},{field},{targetKind},{target},{formVersion},{targetVersion},{command})
            """,ct);
    }
    internal Task<bool> Bound(Guid environment,Guid file,string field,string kind,Guid id,CancellationToken ct)=>db.Database.SqlQuery<bool>($"""
        SELECT EXISTS(SELECT FROM cp6_form_files_v2.bindings WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment}
          AND file_id={file} AND field_name={field} AND target_kind={kind} AND target_id={id}) AS "Value"
        """).SingleAsync(ct);
}
