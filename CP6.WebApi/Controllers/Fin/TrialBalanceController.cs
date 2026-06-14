using CP6.Core.Services.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>试算平衡表 REST —— 财务章02 §2。/api/fin/trial-balance/{periodId}。</summary>
[ApiController]
[Route("api/fin/trial-balance")]
[Authorize]
public class TrialBalanceController : ControllerBase
{
    private readonly ITrialBalanceService _svc;
    public TrialBalanceController(ITrialBalanceService svc) => _svc = svc;

    [HttpGet("{periodId}")]
    public async Task<IActionResult> Get(Guid periodId)
    {
        try { return Ok(new { code = 0, message = "OK", data = await _svc.BuildAsync(periodId) }); }
        catch (InvalidOperationException e) { return BadRequest(new { code = 400, message = e.Message }); }
    }
}
