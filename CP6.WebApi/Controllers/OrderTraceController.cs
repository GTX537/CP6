using CP6.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers;

[ApiController]
[Route("api/order-trace")]
[Authorize]
public class OrderTraceController : ControllerBase
{
    private readonly IOrderTraceService _service;

    public OrderTraceController(IOrderTraceService service)
    {
        _service = service;
    }

    /// <summary>GET /api/order-trace/{webOrderNo}</summary>
    [HttpGet("{webOrderNo}")]
    public async Task<IActionResult> Get(string webOrderNo, CancellationToken ct)
    {
        var trace = await _service.GetAsync(webOrderNo, ct);
        if (trace == null)
        {
            return NotFound(new { code = 1, message = "Order not found.", data = (object?)null });
        }

        return Ok(new { code = 0, message = "OK", data = trace });
    }
}
