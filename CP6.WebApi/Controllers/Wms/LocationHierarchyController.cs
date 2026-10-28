using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using CP6.WebApi.Controllers.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Wms;
[ApiController,Authorize,Route("api/wms/warehouses/{warehouse:guid}/hierarchy-v1"),OrderOwnerResult,DocumentReviewAvailability]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class LocationHierarchyController(LocationHierarchyOwnerV1 owner):ControllerBase
{
    private IActionResult Result<T>(T value)=>Ok(new{code=0,data=value});
    [HttpGet]public async Task<IActionResult> Context(Guid warehouse,CancellationToken ct)=>Result(await owner.ContextAsync(warehouse,ct));
    [HttpGet("versions/{version}")]public async Task<IActionResult> History(Guid warehouse,string version,CancellationToken ct)=>Result(await owner.HistoryAsync(warehouse,version,ct));
    [HttpPost("commands"),RequestSizeLimit(16384)]public async Task<IActionResult> Save(Guid warehouse,LocationHierarchyCommandV1 c,CancellationToken ct)=>Result(await owner.SaveAsync(warehouse,c,ct));
    [HttpPost("precheck"),RequestSizeLimit(4096)]public async Task<IActionResult> Precheck(Guid warehouse,LocationHierarchyPrecheckV1 c,CancellationToken ct)=>Result(await owner.PrecheckAsync(warehouse,c,ct));
    [HttpGet("operations/{id:guid}")]public async Task<IActionResult> Operation(Guid warehouse,Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(warehouse,id,false,ct));
    [HttpPost("operations/{id:guid}/close")]public async Task<IActionResult> Close(Guid warehouse,Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(warehouse,id,true,ct));
}
