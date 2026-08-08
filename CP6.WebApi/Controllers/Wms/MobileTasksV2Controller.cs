using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/v2/wms/tasks")]
[Authorize]
public sealed class MobileTasksV2Controller : ControllerBase
{
    private readonly IMobileTaskV2Service _service;

    public MobileTasksV2Controller(IMobileTaskV2Service service) => _service = service;

    private string? CurrentUser => User.Identity?.Name;

    [HttpGet]
    [RequirePermission("wms-mobile", "view")]
    public Task<PagedResult<MobileTaskV2Dto>> Get(
        [FromQuery] MobileTaskV2Query query,
        CancellationToken ct)
        => _service.GetTasksAsync(query, ct);

    [HttpGet("{taskNo}")]
    [RequirePermission("wms-mobile", "view")]
    public async Task<ActionResult<MobileTaskV2Dto>> GetOne(
        string taskNo,
        CancellationToken ct)
    {
        var task = await _service.GetAsync(taskNo, ct);
        return task is null
            ? NotFound(Error("WM-MSG-070"))
            : Ok(task);
    }

    [HttpGet("{taskNo}/events")]
    [RequirePermission("wms-mobile", "view")]
    public async Task<ActionResult<IReadOnlyList<MobileTaskEventDto>>> GetEvents(
        string taskNo,
        CancellationToken ct)
        => await ExecuteRead(() => _service.GetEventsAsync(taskNo, ct));

    [HttpGet("{taskNo}/scan-profile")]
    [RequirePermission("wms-mobile", "view")]
    public async Task<ActionResult<TaskScanProfileDto>> GetScanProfile(
        string taskNo,
        CancellationToken ct)
        => await ExecuteRead(() => _service.GetScanProfileAsync(taskNo, ct));

    [HttpPost]
    [RequirePermission("wms-mobile", "add")]
    public async Task<ActionResult<MobileTaskV2Dto>> Create(
        CreateMoveTaskV2Request request,
        CancellationToken ct)
    {
        try
        {
            var task = await _service.CreateAsync(request, CurrentUser, ct);
            return CreatedAtAction(nameof(GetOne), new { taskNo = task.TaskNo }, task);
        }
        catch (Exception ex) when (IsHandled(ex))
        {
            return await ErrorResult(ex, null, ct);
        }
    }

    [HttpPost("{taskNo}/assign")]
    [RequirePermission("wms-mobile", "assign")]
    public Task<ActionResult<MobileTaskV2Dto>> Assign(
        string taskNo, AssignTaskV2Request request, CancellationToken ct)
        => Execute(taskNo, () => _service.AssignAsync(taskNo, request, CurrentUser, ct), ct);

    [HttpPost("{taskNo}/claim")]
    [RequirePermission("wms-mobile", "claim")]
    public Task<ActionResult<MobileTaskV2Dto>> Claim(
        string taskNo, ClaimTaskV2Request request, CancellationToken ct)
        => Execute(taskNo, () => _service.ClaimAsync(taskNo, request, CurrentUser, ct), ct);

    [HttpPost("{taskNo}/start")]
    [RequirePermission("wms-mobile", "start")]
    public Task<ActionResult<MobileTaskV2Dto>> Start(
        string taskNo, StartTaskV2Request request, CancellationToken ct)
        => Execute(taskNo, () => _service.StartAsync(taskNo, request, CurrentUser, ct), ct);

    [HttpPost("{taskNo}/pause")]
    [RequirePermission("wms-mobile", "pause")]
    public Task<ActionResult<MobileTaskV2Dto>> Pause(
        string taskNo, PauseTaskRequest request, CancellationToken ct)
        => Execute(taskNo, () => _service.PauseAsync(taskNo, request, CurrentUser, ct), ct);

    [HttpPost("{taskNo}/release")]
    [RequirePermission("wms-mobile", "release")]
    public Task<ActionResult<MobileTaskV2Dto>> Release(
        string taskNo, ReleaseTaskRequest request, CancellationToken ct)
        => Execute(taskNo, () => _service.ReleaseAsync(taskNo, request, CurrentUser, ct), ct);

    [HttpPost("{taskNo}/takeover")]
    [RequirePermission("wms-mobile", "takeover")]
    public Task<ActionResult<MobileTaskV2Dto>> Takeover(
        string taskNo, TakeoverTaskRequest request, CancellationToken ct)
        => Execute(taskNo, () => _service.TakeoverAsync(taskNo, request, CurrentUser, ct), ct);

    [HttpPost("{taskNo}/exception")]
    [RequirePermission("wms-mobile", "exception")]
    public Task<ActionResult<MobileTaskV2Dto>> RaiseException(
        string taskNo, RaiseTaskExceptionRequest request, CancellationToken ct)
        => Execute(taskNo, () => _service.RaiseExceptionAsync(taskNo, request, CurrentUser, ct), ct);

    [HttpPost("{taskNo}/resolve-exception")]
    [RequirePermission("wms-mobile", "exception")]
    public Task<ActionResult<MobileTaskV2Dto>> ResolveException(
        string taskNo, ResolveTaskExceptionRequest request, CancellationToken ct)
        => Execute(taskNo, () => _service.ResolveExceptionAsync(taskNo, request, CurrentUser, ct), ct);

    [HttpPost("{taskNo}/scan")]
    [RequirePermission("wms-mobile", "scan")]
    public async Task<ActionResult<ScanResult>> Scan(
        string taskNo, ScanCommand request, CancellationToken ct)
    {
        try { return Ok(await _service.ScanAsync(taskNo, request, CurrentUser, ct)); }
        catch (Exception ex) when (IsHandled(ex))
        {
            return await ErrorResult(ex, taskNo, ct);
        }
    }

    [HttpPost("{taskNo}/complete")]
    [RequirePermission("wms-mobile", "complete")]
    public Task<ActionResult<MobileTaskV2Dto>> Complete(
        string taskNo, CompleteMoveV2Request request, CancellationToken ct)
        => Execute(taskNo, () => _service.CompleteAsync(taskNo, request, CurrentUser, ct), ct);

    [HttpPost("{taskNo}/cancel")]
    [RequirePermission("wms-mobile", "cancel")]
    public Task<ActionResult<MobileTaskV2Dto>> Cancel(
        string taskNo, CancelTaskV2Request request, CancellationToken ct)
        => Execute(taskNo, () => _service.CancelAsync(taskNo, request, CurrentUser, ct), ct);

    private async Task<ActionResult<MobileTaskV2Dto>> Execute(
        string taskNo,
        Func<Task<MobileTaskV2Dto>> action,
        CancellationToken ct)
    {
        try { return Ok(await action()); }
        catch (Exception ex) when (IsHandled(ex))
        {
            return await ErrorResult(ex, taskNo, ct);
        }
    }

    private async Task<ActionResult<T>> ExecuteRead<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (MobileTaskNotFoundException ex) { return NotFound(Error(ex.Message)); }
    }

    private async Task<ActionResult> ErrorResult(
        Exception ex,
        string? taskNo,
        CancellationToken ct)
    {
        var code = ex switch
        {
            MobileTaskConflictException conflict => conflict.Code,
            InsufficientStockException => "WM-CONFLICT-INSUFFICIENT-STOCK",
            _ => ex.Message
        };
        if (ex is WmsAccessDeniedException)
            return StatusCode(StatusCodes.Status403Forbidden, Error(code));
        if (ex is MobileTaskNotFoundException)
            return NotFound(Error(code));
        if (ex is MobileTaskConflictException or InsufficientStockException)
        {
            var latest = taskNo is null ? null : await _service.GetAsync(taskNo, ct);
            return Conflict(new
            {
                code,
                message = code,
                latestRowVersion = latest?.RowVersion,
                latest
            });
        }
        return BadRequest(Error(code));
    }

    private static bool IsHandled(Exception ex)
        => ex is MobileTaskNotFoundException
            or MobileTaskConflictException
            or InsufficientStockException
            or WmsAccessDeniedException
            or ArgumentException
            or InvalidOperationException;

    private static object Error(string code) => new { code, message = code };
}
