using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/v2/wms/label-jobs")]
[Authorize]
public sealed class LabelJobsController : ControllerBase
{
    private readonly ILabelJobService _service;
    public LabelJobsController(ILabelJobService service) => _service = service;

    [HttpGet]
    [RequirePermission("wms-mobile", "label-print")]
    public Task<PagedResult<LabelJobDto>> Get(
        string? status,
        string? warehouseCd,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
        => _service.GetJobsAsync(status, warehouseCd, page, pageSize, ct);

    [HttpPost]
    [RequirePermission("wms-mobile", "label-print")]
    public Task<ActionResult<LabelJobDto>> Create(
        CreateLabelJobRequest request, CancellationToken ct)
        => Execute(() => _service.CreateJobAsync(request, User.Identity?.Name, ct));

    [HttpPost("{jobNo}/claim")]
    [RequirePermission("wms-mobile", "label-print")]
    public Task<ActionResult<LabelJobDto>> Claim(
        string jobNo, LabelJobCommand request, CancellationToken ct)
        => Execute(() => _service.ClaimAsync(jobNo, request, User.Identity?.Name, ct));

    [HttpPost("{jobNo}/complete")]
    [RequirePermission("wms-mobile", "label-print")]
    public Task<ActionResult<LabelJobDto>> Complete(
        string jobNo, LabelJobCommand request, CancellationToken ct)
        => Execute(() => _service.CompleteAsync(
            jobNo, request, true, User.Identity?.Name, ct));

    [HttpPost("{jobNo}/fail")]
    [RequirePermission("wms-mobile", "label-print")]
    public Task<ActionResult<LabelJobDto>> Fail(
        string jobNo, LabelJobCommand request, CancellationToken ct)
        => Execute(() => _service.CompleteAsync(
            jobNo, request, false, User.Identity?.Name, ct));

    [HttpGet("templates")]
    [RequirePermission("wms-mobile", "label-manage")]
    public Task<IReadOnlyList<LabelTemplateDto>> GetTemplates(CancellationToken ct)
        => _service.GetTemplatesAsync(ct);

    [HttpPost("templates")]
    [RequirePermission("wms-mobile", "label-manage")]
    public async Task<ActionResult<LabelTemplateDto>> UpsertTemplate(
        UpsertLabelTemplateRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _service.UpsertTemplateAsync(
                request, User.Identity?.Name, ct));
        }
        catch (MobileTaskConflictException ex)
        {
            return Conflict(new { code = ex.Code, message = ex.Code });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = ex.Message, message = ex.Message });
        }
    }

    private static async Task<ActionResult<LabelJobDto>> Execute(
        Func<Task<LabelJobDto>> action)
    {
        try { return new OkObjectResult(await action()); }
        catch (MobileTaskNotFoundException ex)
        {
            return new NotFoundObjectResult(new { code = ex.Message, message = ex.Message });
        }
        catch (MobileTaskConflictException ex)
        {
            return new ConflictObjectResult(new { code = ex.Code, message = ex.Code });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new BadRequestObjectResult(new { code = ex.Message, message = ex.Message });
        }
    }
}
