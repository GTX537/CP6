using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController,Authorize,Route("api/wms/rma/v1/corrections"),RmaFormalResult]
[ResponseCache(NoStore =true,Location =ResponseCacheLocation.None)]
public sealed class ReturnCorrectionController(ReturnCorrectionOwnerV1 owner) : ControllerBase
{
    private Guid Command() => Guid.TryParse(Request.Headers["Idempotency-Key"],out var value) && value !=Guid.Empty ? value : throw new OrderPriceCorrectionException("IDEMPOTENCY_KEY_REQUIRED",400);
    [HttpGet("applications/{application:guid}/context")]
    public async Task<IActionResult> Context(Guid application,CancellationToken ct) => Ok(await owner.ContextAsync(application,ct));
    [HttpPost("applications/{application:guid}/proposals"),RequestSizeLimit(1048576)]
    public async Task<IActionResult> Draft(Guid application,ReturnCorrectionDraftV1 input,CancellationToken ct) => Ok(await owner.DraftAsync(application,Command(),input,ct));
    [HttpGet("applications/{application:guid}/proposals/{proposal:guid}")]
    public async Task<IActionResult> Read(Guid application,Guid proposal,CancellationToken ct) => Ok(await owner.ReadAsync(application,proposal,ct));
    [HttpPost("applications/{application:guid}/proposals/{proposal:guid}/edit"),RequestSizeLimit(1048576)]
    public async Task<IActionResult> Edit(Guid application,Guid proposal,ReturnCorrectionEditV1 input,CancellationToken ct) => Ok(await owner.EditAsync(application,proposal,Command(),input,ct));
    [HttpPost("applications/{application:guid}/proposals/{proposal:guid}/{operation}"),RequestSizeLimit(65536)]
    public async Task<IActionResult> Act(Guid application,Guid proposal,string operation,ReturnCorrectionActionV1 input,CancellationToken ct) => Ok(await owner.ActAsync(application,proposal,Command(),operation,input,ct));
    [HttpPost("commands/query"),RequestSizeLimit(65536)]
    public async Task<IActionResult> Query(ReturnCorrectionQueryV1 input,CancellationToken ct) => Ok(await owner.QueryAsync(input,ct));
}
