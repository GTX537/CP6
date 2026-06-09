using CP6.Core.Services;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Erp;

[ApiController]
[Route("api/backorder")]
[Authorize]
public class BackorderController : ControllerBase
{
    private readonly IBackorderService _service;

    public BackorderController(IBackorderService service)
    {
        _service = service;
    }

    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet("queue")]
    public async Task<IActionResult> Queue([FromQuery] BackorderQueueQuery query, CancellationToken ct)
    {
        var data = await _service.GetQueueAsync(query ?? new BackorderQueueQuery(), ct);
        return Ok(new { code = 0, message = "OK", data });
    }

    [HttpPost("{webOrderNo}/{detailNo:int}/close-remaining")]
    public async Task<IActionResult> CloseRemaining(
        string webOrderNo,
        int detailNo,
        [FromBody] BackorderActionRequest? request,
        CancellationToken ct)
    {
        try
        {
            var data = await _service.CloseRemainingAsync(webOrderNo, detailNo, request ?? new BackorderActionRequest(), CurrentUser, ct);
            return Ok(new { code = 0, message = "OK", data });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = 404, message = ex.Message, data = (object?)null });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message, data = (object?)null });
        }
    }

    [HttpPost("{webOrderNo}/{detailNo:int}/split-to-new-order")]
    public async Task<IActionResult> SplitToNewOrder(
        string webOrderNo,
        int detailNo,
        [FromBody] BackorderActionRequest? request,
        CancellationToken ct)
    {
        try
        {
            var data = await _service.SplitToNewOrderAsync(webOrderNo, detailNo, request ?? new BackorderActionRequest(), CurrentUser, ct);
            return Ok(new { code = 0, message = "OK", data });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = 404, message = ex.Message, data = (object?)null });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message, data = (object?)null });
        }
    }
}
