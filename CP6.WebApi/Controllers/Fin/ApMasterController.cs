using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>
/// 应付主数据 REST —— 财务章03。/api/fin/ap/master。银行账户 + 税码 列表/新建（界面下拉用）。
/// </summary>
[ApiController]
[Route("api/fin/ap/master")]
[Authorize]
public class ApMasterController : ControllerBase
{
    private readonly IApMasterService _svc;
    public ApMasterController(IApMasterService svc) => _svc = svc;

    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });

    [HttpGet("bank-account")]
    public async Task<IActionResult> ListBanks([FromQuery] bool includeInactive = false)
        => Ok2(await _svc.ListBankAccountsAsync(includeInactive));

    [HttpPost("bank-account")]
    public async Task<IActionResult> CreateBank([FromBody] BankAccount bank)
    {
        try { return Ok2(new { id = await _svc.CreateBankAccountAsync(bank, CurrentUser) }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpGet("tax-code")]
    public async Task<IActionResult> ListTaxes([FromQuery] bool includeInactive = false)
        => Ok2(await _svc.ListTaxCodesAsync(includeInactive));

    [HttpPost("tax-code")]
    public async Task<IActionResult> CreateTax([FromBody] TaxCode tax)
    {
        try { return Ok2(new { id = await _svc.CreateTaxCodeAsync(tax, CurrentUser) }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }
}
