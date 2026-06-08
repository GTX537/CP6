using CP6.Core.Services;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers;

[ApiController]
[Route("api/orders/unshipped")]
[Authorize]
public class UnshippedOrderController : ControllerBase
{
    private readonly IUnshippedOrderService _service;

    public UnshippedOrderController(IUnshippedOrderService service)
    {
        _service = service;
    }

    /// <summary>POST /api/orders/unshipped/search</summary>
    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] UnshippedOrderQuery query)
    {
        if (query.PageSize <= 0 || query.PageSize > 200) query.PageSize = 50;
        if (query.Page <= 0) query.Page = 1;

        var paged = await _service.SearchAsync(query);
        return Ok(new { code = 0, message = "OK", data = paged });
    }

    /// <summary>POST /api/orders/unshipped/export-csv</summary>
    [HttpPost("export-csv")]
    public async Task<IActionResult> ExportCsv([FromBody] UnshippedOrderQuery query)
    {
        var bytes = await _service.ExportCsvAsync(query);
        var fileName = $"unshipped-orders-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
        return File(bytes, "text/csv", fileName);
    }
}
