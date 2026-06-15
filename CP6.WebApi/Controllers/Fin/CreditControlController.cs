using CP6.Core.Services.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>
/// 信用控制 REST —— 财务章04 §3。/api/fin/ar/credit。出货/受注前查客户应收余额+本单是否超额度。
/// 返回明细供前端硬拦截或仅警告（Exceeded 标记），是财务反向约束业务的查询入口。
/// </summary>
[ApiController]
[Route("api/fin/ar/credit")]
[Authorize]
public class CreditControlController : ControllerBase
{
    private readonly ICreditControlService _svc;

    public CreditControlController(ICreditControlService svc) => _svc = svc;

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });

    /// <summary>信用校验：客户当前应收余额 + 本单金额 vs 信用额度。</summary>
    [HttpGet("check")]
    public async Task<IActionResult> Check([FromQuery] string customerId, [FromQuery] decimal orderAmount)
        => Ok2(await _svc.CheckCreditAsync(customerId, orderAmount));
}
