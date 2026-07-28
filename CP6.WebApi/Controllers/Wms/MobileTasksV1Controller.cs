using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/v1/wms/mobile/tasks")]
[Authorize]
public sealed class MobileTasksV1Controller : ControllerBase
{
    private readonly IMobileTaskV1Service _service;

    public MobileTasksV1Controller(IMobileTaskV1Service service) => _service = service;

    private string? CurrentUser => User.Identity?.Name;

    [HttpGet]
    [RequirePermission("wms-mobile", "view")]
    [ProducesResponseType<PagedResult<MobileTaskV1Dto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<MobileTaskV1Dto>> GetTasks(
        [FromQuery] MobileTaskV1Query query,
        CancellationToken ct)
        => _service.GetTasksAsync(query, ct);

    [HttpGet("{taskNo}")]
    [RequirePermission("wms-mobile", "view")]
    [ProducesResponseType<MobileTaskV1Dto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MobileTaskV1Dto>> Get(string taskNo, CancellationToken ct)
    {
        var task = await _service.GetAsync(taskNo, ct);
        return task == null
            ? NotFound(new { code = "WM-MSG-070", message = "Task not found." })
            : Ok(task);
    }

    [HttpPost]
    [RequirePermission("wms-mobile", "add")]
    [ProducesResponseType<MobileTaskV1Dto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<MobileTaskV1Dto>> Create(
        [FromBody] CreateMoveTaskRequest request,
        CancellationToken ct)
    {
        try
        {
            var task = await _service.CreateAsync(request, CurrentUser, ct);
            return CreatedAtAction(nameof(Get), new { taskNo = task.TaskNo }, task);
        }
        catch (ArgumentException ex) { return Validation(ex.Message); }
    }

    [HttpPost("{taskNo}/assign")]
    [RequirePermission("wms-mobile", "assign")]
    public Task<ActionResult<MobileTaskV1Dto>> Assign(
        string taskNo,
        [FromBody] AssignTaskRequest request,
        CancellationToken ct)
        => Execute(() => _service.AssignAsync(taskNo, request, CurrentUser, ct));

    [HttpPost("{taskNo}/claim")]
    [RequirePermission("wms-mobile", "claim")]
    public Task<ActionResult<MobileTaskV1Dto>> Claim(
        string taskNo,
        [FromBody] ClaimTaskRequest request,
        CancellationToken ct)
        => Execute(() => _service.ClaimAsync(taskNo, request, CurrentUser, ct));

    [HttpPost("{taskNo}/start")]
    [RequirePermission("wms-mobile", "start")]
    public Task<ActionResult<MobileTaskV1Dto>> Start(
        string taskNo,
        [FromBody] StartTaskRequest request,
        CancellationToken ct)
        => Execute(() => _service.StartAsync(taskNo, request, CurrentUser, ct));

    [HttpPost("{taskNo}/scan")]
    [RequirePermission("wms-mobile", "scan")]
    public async Task<ActionResult<MobileScanResult>> Scan(
        string taskNo,
        [FromBody] MobileScanRequest request,
        CancellationToken ct)
    {
        try { return Ok(await _service.ScanAsync(taskNo, request, ct)); }
        catch (MobileTaskNotFoundException ex) { return NotFound(Error(ex.Message)); }
        catch (MobileTaskConflictException ex) { return Conflict(Error(ex.Code)); }
        catch (ArgumentException ex) { return Validation(ex.Message); }
    }

    [HttpPost("{taskNo}/complete")]
    [RequirePermission("wms-mobile", "complete")]
    public Task<ActionResult<MobileTaskV1Dto>> Complete(
        string taskNo,
        [FromBody] CompleteMoveRequest request,
        CancellationToken ct)
        => Execute(() => _service.CompleteAsync(taskNo, request, CurrentUser, ct));

    [HttpPost("{taskNo}/cancel")]
    [RequirePermission("wms-mobile", "cancel")]
    public Task<ActionResult<MobileTaskV1Dto>> Cancel(
        string taskNo,
        [FromBody] CancelTaskRequest request,
        CancellationToken ct)
        => Execute(() => _service.CancelAsync(taskNo, request, CurrentUser, ct));

    private async Task<ActionResult<MobileTaskV1Dto>> Execute(Func<Task<MobileTaskV1Dto>> action)
    {
        try { return Ok(await action()); }
        catch (MobileTaskNotFoundException ex) { return NotFound(Error(ex.Message)); }
        catch (MobileTaskConflictException ex) { return Conflict(Error(ex.Code)); }
        catch (InsufficientStockException ex)
        {
            return Conflict(new
            {
                code = "WM-CONFLICT-INSUFFICIENT-STOCK",
                message = ex.Message,
            });
        }
        catch (ArgumentException ex) { return Validation(ex.Message); }
        catch (InvalidOperationException ex) { return Validation(ex.Message); }
    }

    private ActionResult Validation(string message)
        => BadRequest(Error(message));

    private static object Error(string code) => new { code, message = code };
}
