using CP6.Core.Services;
using CP6.Entity.DomainModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Erp;

/// <summary>
/// 為替レートマスタ（多通貨 Gap 4.3）。
/// </summary>
[ApiController]
[Route("api/erp/fx-rate")]
[Authorize]
public class FxRateController : ControllerBase
{
    private readonly IFxRateService _svc;

    public FxRateController(IFxRateService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? currencyCd)
        => Ok(new { code = 0, message = "OK", data = await _svc.ListAsync(currencyCd) });

    /// <summary>指定通貨・基準日の解決レートをプレビュー。</summary>
    [HttpGet("resolve")]
    public async Task<IActionResult> Resolve([FromQuery] string currencyCd, [FromQuery] DateTime? asOf)
        => Ok(new { code = 0, message = "OK", data = await _svc.ResolveRateAsync(currencyCd ?? "", asOf ?? DateTime.Today) });

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FxRate rate)
    {
        try
        {
            var data = await _svc.CreateAsync(rate, CurrentUser);
            return Ok(new { code = 0, message = "PA-MSG-FX-CREATED", data });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] FxRate rate)
    {
        try
        {
            await _svc.UpdateAsync(id, rate, CurrentUser);
            return Ok(new { code = 0, message = "PA-MSG-FX-UPDATED" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            await _svc.DeleteAsync(id, CurrentUser);
            return Ok(new { code = 0, message = "PA-MSG-FX-DELETED" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }
}
