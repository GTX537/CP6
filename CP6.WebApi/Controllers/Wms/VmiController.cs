using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM250 — VMI 客先預り在庫 Web API</summary>
[ApiController]
[Route("api/wms/vmi")]
[Authorize]
public class VmiController : ControllerBase
{
    private readonly IVmiService _svc;
    public VmiController(IVmiService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet("customers")]
    public async Task<IActionResult> Customers([FromQuery] string? customerCd)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchByCustomerAsync(customerCd) });

    [HttpGet("customers/{customerCd}/details")]
    public async Task<IActionResult> Details(string customerCd)
        => Ok(new { code = 0, message = "OK", data = await _svc.GetDetailsAsync(customerCd) });

    [HttpGet("billings")]
    public async Task<IActionResult> Billings([FromQuery] string? customerCd, [FromQuery] string? yearMonth, [FromQuery] bool? confirmed)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchBillingsAsync(customerCd, yearMonth, confirmed) });

    [HttpPost("billings/calculate")]
    public async Task<IActionResult> Calculate([FromBody] CalculateRequest req)
    {
        try
        {
            var n = await _svc.CalculateMonthlyBillingAsync(req.YearMonth, req.DailyStorageRate, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { upserted = n } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("billings/{no}/confirm")]
    public async Task<IActionResult> Confirm(string no)
    {
        try { await _svc.ConfirmBillingAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    public class CalculateRequest
    {
        public string YearMonth { get; set; } = string.Empty;
        public decimal DailyStorageRate { get; set; } = 1.0m;
    }
}
