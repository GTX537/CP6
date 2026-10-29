using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController,Authorize,Route("api/wms/rma/v2/dispositions/{kind}"),RmaFormalResult]
[ResponseCache(NoStore =true,Location =ResponseCacheLocation.None)]
public sealed class ReturnDispositionController(ReturnResaleOwnerV2 resale,ReturnRepairTransferOwnerV2 repair,ReturnScrapOwnerV2 scrap,
    SupplierReturnShipmentOwnerV2 supplier,CustomerReturnShipmentOwnerV2 customer) : ControllerBase
{
    private ReturnDispositionOwnerV2 Owner(string kind) => kind switch { "RESALE_STOCK" =>resale,"REPAIR" =>repair,"SCRAP" =>scrap,
        "SUPPLIER_RETURN" =>supplier,"RETURN_TO_CUSTOMER" =>customer,_ =>throw new OrderPriceCorrectionException("DISPOSITION_INVALID",400) };
    private Guid Command() => Guid.TryParse(Request.Headers["Idempotency-Key"],out var value) && value !=Guid.Empty ? value : throw new OrderPriceCorrectionException("IDEMPOTENCY_KEY_REQUIRED",400);
    [HttpGet("applications/{application:guid}/context")]
    public async Task<IActionResult> Context(string kind,Guid application,CancellationToken ct) => Ok(await Owner(kind).ContextAsync(application,ct));
    [HttpPost("applications/{application:guid}/works"),RequestSizeLimit(1048576)]
    public async Task<IActionResult> Prepare(string kind,Guid application,ReturnDispositionPrepareV2 input,CancellationToken ct) => Ok(await Owner(kind).PrepareAsync(application,Command(),input,ct));
    [HttpGet("applications/{application:guid}/works/{work:guid}")]
    public async Task<IActionResult> Read(string kind,Guid application,Guid work,CancellationToken ct) => Ok(await Owner(kind).ReadAsync(application,work,ct));
    [HttpPost("applications/{application:guid}/works/{work:guid}/commit"),RequestSizeLimit(65536)]
    public async Task<IActionResult> Commit(string kind,Guid application,Guid work,ReturnDispositionActionV2 input,CancellationToken ct) => Ok(await Owner(kind).CommitAsync(application,work,Command(),input,ct));
    [HttpPost("applications/{application:guid}/works/{work:guid}/seal"),RequestSizeLimit(65536)]
    public async Task<IActionResult> Seal(string kind,Guid application,Guid work,ReturnDispositionActionV2 input,CancellationToken ct) => Ok(await Owner(kind).SealAsync(application,work,Command(),input,ct));
    [HttpGet("applications/{application:guid}/works/{work:guid}/outcome")]
    public async Task<IActionResult> Outcome(string kind,Guid application,Guid work,CancellationToken ct) => Ok(await Owner(kind).OutcomeAsync(application,work,ct));
    [HttpPost("commands/query"),RequestSizeLimit(65536)]
    public async Task<IActionResult> Query(string kind,ReturnDispositionQueryV2 input,CancellationToken ct) => Ok(await Owner(kind).QueryAsync(input,ct));
}
