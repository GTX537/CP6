using CP6.Core.Auth;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

[ApiController]
[Route("api/fin/bank-import-profile")]
[Authorize]
public class BankImportProfileController : ControllerBase
{
    private readonly IBankStatementService _svc;
    public BankImportProfileController(IBankStatementService svc) => _svc = svc;
    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    [HttpGet]
    [RequirePermission("fin-bank-reconciliation", "view")]
    public async Task<IActionResult> List([FromQuery] Guid? bankAccountId) => Ok2(await _svc.ListProfilesAsync(bankAccountId));

    [HttpPost("upsert")]
    [RequirePermission("fin-bank-reconciliation", "profile-manage")]
    public async Task<IActionResult> Upsert([FromBody] BankImportProfile dto)
        => Fin(await _svc.UpsertProfileAsync(dto, CurrentUser));

    [HttpDelete("{id}")]
    [RequirePermission("fin-bank-reconciliation", "profile-manage")]
    public async Task<IActionResult> Delete(Guid id)
        => Fin(await _svc.DeleteProfileAsync(id, CurrentUser));
}
