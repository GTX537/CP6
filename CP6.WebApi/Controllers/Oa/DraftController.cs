using CP6.Core.Services.Oa;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController,Authorize,Route("api/oa")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class DraftController(PersonalFormOwnerV2 owner):LegacyPersonalFormHttpV1(owner)
{
    [HttpPost("forms/{formKey}/drafts"),RequestSizeLimit(PersonalFormOwnerV2.DataRequestMaxBytes)]
    public Task<IActionResult> Create(string formKey,CancellationToken ct)=>Run(()=>Write("FORM_DRAFT_CREATE",formKey,ct));
    [HttpGet("forms/{formKey}/initiation-context")]
    public Task<IActionResult> Context(string formKey,CancellationToken ct)=>Run(async()=>await Owner.ContextAsync(formKey,ct));
    [HttpGet("drafts")]
    public Task<IActionResult> List([FromQuery]int page=1,[FromQuery]int pageSize=20,CancellationToken ct=default)=>Run(async()=>
    {
        var value=await Owner.DraftsAsync(page,pageSize,ct);
        return new{items=value.Items.Select(x=>new{id=x.DraftId,x.FormKey,formName=x.Name,x.FormVersion,x.LatestPublishedVersion,x.Title,
            updatedAtUtc=x.UpdatedAtUtc.UtcDateTime,x.Stale,rowVersion=Guid.Parse(x.RowVersion).ToByteArray()}),value.Total,value.Page,value.PageSize};
    });
    [HttpGet("drafts/{draftId:guid}")]
    public Task<IActionResult> Get(Guid draftId,CancellationToken ct)=>Run(async()=>Detail(await Owner.DraftAsync(draftId,ct)));
    [HttpPut("drafts/{draftId:guid}"),RequestSizeLimit(PersonalFormOwnerV2.DataRequestMaxBytes)]
    public Task<IActionResult> Update(Guid draftId,CancellationToken ct)=>Run(()=>Write("FORM_DRAFT_SAVE",draftId.ToString("D"),ct));
    [HttpPost("drafts/{draftId:guid}/rebase"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> Rebase(Guid draftId,CancellationToken ct)=>Run(()=>Write("FORM_DRAFT_REBASE",draftId.ToString("D"),ct));
    [HttpPost("drafts/{draftId:guid}/submit"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> Submit(Guid draftId,CancellationToken ct)=>Run(()=>Write("FORM_DRAFT_SUBMIT",draftId.ToString("D"),ct));
    [HttpDelete("drafts/{draftId:guid}"),RequestSizeLimit(1024*1024)]
    public Task<IActionResult> Delete(Guid draftId,CancellationToken ct)=>Run(()=>Write("FORM_DRAFT_DELETE",draftId.ToString("D"),ct));
    [HttpGet("legacy-form-commands/{operation}/{resource}/{key}")]
    public Task<IActionResult> Original(string operation,string resource,string key,CancellationToken ct)=>Run(async()=>await Owner.LegacyOriginalAsync(operation,resource,key,ct));
    [HttpGet("legacy-drafts")]
    public Task<IActionResult> LegacyHistory([FromQuery]int page=1,[FromQuery]int pageSize=20,CancellationToken ct=default)
        =>Run(async()=>await Owner.LegacyDraftHistoryAsync(page,pageSize,ct));
}
