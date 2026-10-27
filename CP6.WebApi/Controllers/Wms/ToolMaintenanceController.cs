using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Wms;
[ApiController, Authorize, OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ToolMaintenanceController(ToolMaintenanceOwnerV1 owner, ToolMaintenanceMasterOwnerV1 master) : ControllerBase
{
    private IActionResult Result<T>(T value) { Response.Headers.CacheControl = "no-store"; return Ok(new { code = 0, data = value }); }
    private string Match => Request.Headers.IfMatch.ToString();
    [HttpGet("api/wms/specials/v1/tools/{id:guid}/maintenance-facts/{fact:guid}")]
    public async Task<IActionResult> Fact(Guid id, Guid fact, CancellationToken ct) => Result(await owner.FactAsync(id, fact, ct));
    [HttpGet("api/wms/specials/v1/tools/{id:guid}/maintenance-context")]
    public async Task<IActionResult> Context(Guid id, [FromQuery] int page = 1, CancellationToken ct = default)
    { var value = await owner.ContextAsync(id, page, ct); Response.Headers.ETag = value.Etag; return Result(value); }
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/maintenance-policies"), RequestSizeLimit(16384)]
    public async Task<IActionResult> Policy(Guid id, [FromBody] ToolMaintenancePolicyCommandV1 command, CancellationToken ct)
        => Result(await owner.PolicyAsync(id, Match, command, ct));
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/maintenance"), RequestSizeLimit(16384)]
    public async Task<IActionResult> Start(Guid id, [FromBody] ToolMaintenanceStartV1 command, CancellationToken ct)
        => Result(await owner.StartAsync(id, Match, command, ct));
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/maintenance/{maintenance:guid}/reports"), RequestSizeLimit(49152)]
    public async Task<IActionResult> Report(Guid id, Guid maintenance, [FromBody] ToolMaintenanceReportCommandV1 command, CancellationToken ct)
        => Result(await owner.ReportAsync(id, maintenance, Match, command, ct));
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/maintenance/{maintenance:guid}/complete"), RequestSizeLimit(16384)]
    public async Task<IActionResult> Complete(Guid id, Guid maintenance, [FromBody] ToolMaintenanceCompleteV1 command, CancellationToken ct)
        => Result(await owner.CompleteAsync(id, maintenance, Match, command, ct));
    [HttpPost("api/master/tool-maintenance/v1/{id:guid}/{maintenance:guid}/assessments"), RequestSizeLimit(16384)]
    public async Task<IActionResult> Assess(Guid id, Guid maintenance, [FromBody] ToolMaintenanceAssessmentCommandV1 command, CancellationToken ct)
        => Result(await master.AssessAsync(id, maintenance, Match, command, ct));
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/maintenance/{maintenance:guid}/reset-applications"), RequestSizeLimit(16384)]
    public async Task<IActionResult> Reset(Guid id, Guid maintenance, [FromBody] ToolMaintenanceResetV1 command, CancellationToken ct)
        => Result(await owner.ResetAsync(id, maintenance, Match, command, ct));
    [HttpGet("api/wms/tool-maintenance/v1/operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, false, ct));
    [HttpPost("api/wms/tool-maintenance/v1/operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) => Result(await owner.OperationAsync(id, true, ct));
}
