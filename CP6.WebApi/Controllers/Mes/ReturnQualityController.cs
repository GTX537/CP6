using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.WebApi.Controllers.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

[ApiController,Authorize,Route("api/wms/rma/v1"),RmaFormalResult]
[ResponseCache(NoStore = true,Location = ResponseCacheLocation.None)]
public sealed class ReturnQualityController(ReturnQualityOwnerV1 owner) : ControllerBase
{
    private Guid Command() => Guid.TryParse(Request.Headers["Idempotency-Key"],out var id) && id !=Guid.Empty
        ? id : throw new OrderPriceCorrectionException("IDEMPOTENCY_KEY_REQUIRED",400);
    [HttpGet("applications/{applicationId:guid}/quality/context")]
    public async Task<IActionResult> Context(Guid applicationId,CancellationToken ct) => Ok(await owner.ContextAsync(applicationId,ct));
    [HttpGet("applications/{applicationId:guid}/quality/inspections/{inspectionId:guid}")]
    public async Task<IActionResult> Read(Guid applicationId,Guid inspectionId,int? revision,CancellationToken ct) => Ok(await owner.ReadAsync(applicationId,inspectionId,revision,ct));
    [HttpPost("applications/{applicationId:guid}/quality/inspections"),RequestSizeLimit(65536)]
    public async Task<IActionResult> Start(Guid applicationId,ReturnQualityStartV1 input,CancellationToken ct) => Ok(await owner.StartAsync(applicationId,Command(),input,ct));
    [HttpPost("applications/{applicationId:guid}/quality/inspections/{inspectionId:guid}/decisions"),RequestSizeLimit(1048576)]
    public async Task<IActionResult> Decide(Guid applicationId,Guid inspectionId,ReturnQualityDecideV1 input,CancellationToken ct) => Ok(await owner.DecideAsync(applicationId,inspectionId,Command(),input,ct));
    [HttpPost("applications/{applicationId:guid}/quality/inspections/{inspectionId:guid}/withdrawals"),RequestSizeLimit(65536)]
    public async Task<IActionResult> Withdraw(Guid applicationId,Guid inspectionId,ReturnQualityWithdrawV1 input,CancellationToken ct) => Ok(await owner.WithdrawAsync(applicationId,inspectionId,Command(),input,ct));
    [HttpPost("applications/{applicationId:guid}/quality/inspections/{inspectionId:guid}/observations"),RequestSizeLimit(65536)]
    public async Task<IActionResult> Observe(Guid applicationId,Guid inspectionId,RmaObserveQualityV1 input,CancellationToken ct) => Ok(await owner.ObserveAsync(applicationId,inspectionId,Command(),input,ct));
    [HttpPost("quality/commands/query"),RequestSizeLimit(65536)]
    public async Task<IActionResult> Query(ReturnQualityQueryV1 input,CancellationToken ct) => Ok(await owner.QueryAsync(input,ct));
}
