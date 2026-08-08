using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/v2/wms/task-analytics")]
[Authorize]
public sealed class TaskAnalyticsController : ControllerBase
{
    private readonly IMobileTaskV2Service _service;
    public TaskAnalyticsController(IMobileTaskV2Service service) => _service = service;

    [HttpGet]
    [RequirePermission("wms-mobile", "analytics")]
    public Task<TaskAnalyticsDto> Get(
        [FromQuery] TaskAnalyticsQuery query,
        CancellationToken ct)
        => _service.GetAnalyticsAsync(query, ct);
}
