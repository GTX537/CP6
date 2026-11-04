using System.Text;
using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

public abstract class LegacyPersonalFormHttpV1(PersonalFormOwnerV2 owner):ControllerBase
{
    protected PersonalFormOwnerV2 Owner=>owner;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private string? Header(string name)
    {
        var values=Request.Headers[name];
        if(values.Count>1)throw new ScopeAuthorizationExceptionV1("LEGACY_HEADER_INVALID",400);
        return values.Count==0?null:values[0];
    }
    private LegacyPersonalFormHeadersV1 Headers()=>new(Header("Idempotency-Key")??"",Header("X-Form-Version-Id"),
        Header("X-Form-Activation-Revision"),Header("X-Form-Binding-Revision"),Header("X-Flow-Version-Id"),
        Header("X-Flow-Activation-Revision"),Header("X-Form-Rebase-Preview"),Header("X-Form-Rebase-Digest"),Header("If-Match"),
        Header("X-Form-Disposition-Request"),Header("X-Form-Disposition-Receipt"));
    private async Task<byte[]> Raw(bool emptyDelete,CancellationToken ct)
    {
        using var bytes=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await Request.Body.ReadAsync(buffer,ct))!=0)
        {
            if(bytes.Length+count>PersonalFormOwnerV2.DataRequestMaxBytes)throw new ScopeAuthorizationExceptionV1("JSON_SIZE_LIMIT",400);
            await bytes.WriteAsync(buffer.AsMemory(0,count),ct);
        }
        return emptyDelete&&bytes.Length==0?Encoding.UTF8.GetBytes("{}"):bytes.ToArray();
    }
    protected async Task<object> Write(string operation,string resource,CancellationToken ct)
    {
        var headers=Headers();var result=await owner.LegacyExecuteAsync(operation,resource,await Raw(operation=="FORM_DRAFT_DELETE",ct),headers,ct);
        Response.Headers.Location="/api/oa/legacy-form-commands/"+operation+"/"+Uri.EscapeDataString(resource)+"/"+Uri.EscapeDataString(headers.Key);
        if(operation=="FORM_DRAFT_DELETE")return true;
        if(operation=="FORM_DRAFT_REBASE")return await owner.LegacyRebaseResultAsync(Guid.Parse(resource),headers.Key,ct);
        if(operation is "FORM_DIRECT_SUBMIT" or "FORM_DRAFT_SUBMIT")
        {
            var value=result.Result!.Value.Deserialize<PersonalFormSubmissionV2>(Json)!;
            return new SubmitFormResult(value.FormDataId,value.FormDefVersionId,value.FormVersion,value.FlowInstanceId,value.FlowDefVersionId,value.FlowVersion);
        }
        return Detail(result.Result!.Value.Deserialize<PersonalFormDraftV2>(Json)!);
    }
    protected static DraftDetail Detail(PersonalFormDraftV2 value)=>new(value.DraftId,value.FormKey,value.Name,value.FormVersionId,
        value.FormVersion,value.LatestPublishedVersion,value.SchemaJson,value.DataJson,value.Title,value.Stale,Guid.Parse(value.RowVersion).ToByteArray());
    protected async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try{return Ok(new{code=0,message="OK",data=await action()});}
        catch(PersonalFormDispositionRequiredV2 e)
        {return StatusCode(403,new{code="E-WF-043",message=e.Message,dispositionRequestId=e.RequestId,retryMode="QUALIFIED_DISPOSITION_REQUIRED"});}
        catch(ScopeAuthorizationExceptionV1 e)
        {
            var code=e.Message switch
            {
                "FORM_CONTEXT_REQUIRED" or "FORM_CONTEXT_CHANGED" or "DRAFT_REBASE_REQUIRED" or "REBASE_TARGET_CHANGED" or "REBASE_CONTEXT_REQUIRED" or "REBASE_PREVIEW_CHANGED"=>"E-WF-040",
                "DRAFT_VERSION_REQUIRED" or "DRAFT_VERSION_CONFLICT"=>"E-WF-041",
                "LEGACY_COMMAND_KEY_INVALID" or "COMMAND_BODY_CONFLICT" or "COMMAND_CLOSED" or "LEGACY_COMMAND_MAPPING_REQUIRED" or "LEGACY_SUBMISSION_MAPPING_REQUIRED"=>"E-WF-044",
                "FORM_FIELD_UNKNOWN"=>"E-WF-039",
                "REBASE_CONFIRMATION_REQUIRED"=>"E-WF-048",
                _ when e.Status is 403 or 404=>"E-WF-043",
                _ when e.Status is 400 or 422=>"E-WF-047",
                _=>e.Message
            };
            return StatusCode(e.Status,new{code,message=e.Message,contextEndpoint="/api/oa/v2/forms/{formKey}/initiation-context",
                retryMode=e.Status is 503 or 504?"READ_ORIGINAL":e.Status==409?"REFRESH_AND_NEW_INTENT":"NONE"});
        }
        catch(Exception e)when(e is Npgsql.NpgsqlException or Microsoft.EntityFrameworkCore.DbUpdateException or CurrentPermissionUnavailableException)
        {return StatusCode(503,new{code="FORM_STORAGE_UNAVAILABLE",retryMode="READ_ORIGINAL"});}
    }
}
