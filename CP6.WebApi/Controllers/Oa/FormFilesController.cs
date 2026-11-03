using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController,Authorize,Route("api/oa/v2/personal-forms/files")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class FormFilesController(PersonalFormOwnerV2 forms,NativeInboxOwnerV2 inbox):ControllerBase
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){PropertyNameCaseInsensitive=false};
    private void Actual(){if(Request.Headers["X-Acting-As"].ToString().Length!=0)throw new ScopeAuthorizationExceptionV1("FORM_FILE_ACTUAL_ONLY",403);}
    private Guid? ActingAs()
    {
        var text=Request.Headers["X-Acting-As"].ToString();if(text.Length==0)return null;
        return Guid.TryParseExact(text,"D",out var id)&&id!=Guid.Empty?id:throw new ScopeAuthorizationExceptionV1("ACT_AS_INVALID",422);
    }
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try{Response.Headers.CacheControl="no-store";return Ok(new{code=0,data=await action()});}
        catch(ScopeAuthorizationExceptionV1 e){return StatusCode(e.Status,new{code=e.Message,message=e.Message});}
        catch(CP6.Core.Services.Erp.OrderPriceCorrectionException e){return StatusCode(e.Status,new{code=e.Message,message=e.Message});}
    }
    [HttpPost("uploads"),RequestSizeLimit(20971520)]
    public Task<IActionResult> Upload([FromHeader(Name="X-File-Command")]string encoded,CancellationToken ct)=>Run(async()=>
    {
        FormFileUploadV2 input;
        try
        {
            if(encoded.Length>32768)throw new FormatException();input=JsonSerializer.Deserialize<FormFileUploadV2>(Convert.FromBase64String(encoded),Json)??throw new JsonException();
        }
        catch(Exception e)when(e is FormatException or JsonException){throw new ScopeAuthorizationExceptionV1("FILE_UPLOAD_INVALID",400);}
        if(input.Scope?.Kind!="TASK")Actual();return await forms.UploadFileAsync(input,Request.Body,ct,ActingAs());
    });
    [HttpPost("read")]
    public Task<IActionResult> Read(FormFileReadV2 input,CancellationToken ct)=>Run(async()=>{if(input.Scope?.Kind!="TASK")Actual();return await forms.ReadFileAsync(input,ct,ActingAs());});
    [HttpPost("commands")]
    public Task<IActionResult> Command(FormFileStateCommandV2 input,CancellationToken ct)=>Run(async()=>{if(input.Scope?.Kind!="TASK")Actual();return await forms.ChangeFileAsync(input,ct,ActingAs());});
    [HttpGet("operations/{id:guid}")]
    public Task<IActionResult> Operation(Guid id,CancellationToken ct)=>Run(async()=>await forms.FileOperationAsync(id,false,ct));
    [HttpPost("operations/{id:guid}/close")]
    public Task<IActionResult> Close(Guid id,CancellationToken ct)=>Run(async()=>await forms.FileOperationAsync(id,true,ct));
    [HttpPost("download")]
    public Task<IActionResult> Download(FormFileReadV2 input,CancellationToken ct)=>Download(async()=>{if(input.Scope?.Kind!="TASK")Actual();return await forms.DownloadFileAsync(input,ct,ActingAs());});
    [HttpPost("/api/oa/v2/inbox/instances/{instance:guid}/files/read")]
    public Task<IActionResult> InboxRead(Guid instance,FormBoundFileReadV2 input,CancellationToken ct)=>Run(async()=>await inbox.ReadFileAsync(instance,input,ActingAs(),ct));
    [HttpPost("/api/oa/v2/inbox/instances/{instance:guid}/files/download")]
    public Task<IActionResult> InboxDownload(Guid instance,FormBoundFileReadV2 input,CancellationToken ct)=>Download(()=>inbox.DownloadFileAsync(instance,input,ActingAs(),ct));
    [HttpPost("/api/oa/v2/form-data/{data:guid}/files/read")]
    public Task<IActionResult> QueryRead(Guid data,FormBoundFileReadV2 input,CancellationToken ct)=>Run(async()=>await inbox.ReadQueryFileAsync(data,input,ActingAs(),ct));
    [HttpPost("/api/oa/v2/form-data/{data:guid}/files/download")]
    public Task<IActionResult> QueryDownload(Guid data,FormBoundFileReadV2 input,CancellationToken ct)=>Download(()=>inbox.DownloadQueryFileAsync(data,input,ActingAs(),ct));
    private async Task<IActionResult> Download(Func<Task<FileContentDownloadV1>> action)
    {
        try
        {
            var value=await action();Response.Headers.CacheControl="no-store";Response.Headers["X-Content-Type-Options"]="nosniff";Response.Headers["X-Content-SHA256"]=value.ContentRef.Digest;
            return File(value.Bytes,"application/octet-stream",value.FileName,enableRangeProcessing:false);
        }
        catch(ScopeAuthorizationExceptionV1 e){return StatusCode(e.Status,new{code=e.Message,message=e.Message});}
    }
}
