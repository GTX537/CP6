using CP6.Core.Auth;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

[ApiController]
[Route("api/fin/bank-statement")]
[Authorize]
public class BankStatementController : ControllerBase
{
    private readonly IBankStatementService _svc;
    public BankStatementController(IBankStatementService svc) => _svc = svc;
    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    [HttpGet]
    [RequirePermission("fin-bank-reconciliation", "view")]
    public async Task<IActionResult> List([FromQuery] Guid? bankAccountId, [FromQuery] Guid? fiscalPeriodId, [FromQuery] BankStatementStatus? status)
        => Ok2(await _svc.ListAsync(bankAccountId, fiscalPeriodId, status));

    [HttpGet("{id}")]
    [RequirePermission("fin-bank-reconciliation", "view")]
    public async Task<IActionResult> Get(Guid id)
        => Ok2(new { statement = await _svc.GetAsync(id), lines = await _svc.GetLinesAsync(id) });

    [HttpPost]
    [RequirePermission("fin-bank-reconciliation", "view")]
    public async Task<IActionResult> Create([FromBody] BankStatement dto)
        => Fin(await _svc.CreateAsync(dto, CurrentUser));

    [HttpPost("{id}/import")]
    [RequirePermission("fin-bank-reconciliation", "import")]
    public async Task<IActionResult> Import(Guid id, [FromQuery] Guid profileId, [FromQuery] bool dryRun, IFormFile file)
    {
        using var stream = file.OpenReadStream();
        if (dryRun) return Ok2(await _svc.PreviewAsync(id, profileId, stream, file.FileName));
        return Fin(await _svc.ConfirmImportAsync(id, profileId, stream, file.FileName, CurrentUser));
    }

    [HttpPost("{id}/line")]
    [RequirePermission("fin-bank-reconciliation", "import")]
    public async Task<IActionResult> AddLine(Guid id, [FromBody] BankStatementLine line)
        => Fin(await _svc.AddLineAsync(id, line, CurrentUser));

    [HttpPut("{id}/line/{lineId}")]
    [RequirePermission("fin-bank-reconciliation", "import")]
    public async Task<IActionResult> UpdateLine(Guid id, Guid lineId, [FromBody] BankStatementLine line)
        => Fin(await _svc.UpdateLineAsync(id, lineId, line, line.RowVersion, CurrentUser));

    [HttpDelete("{id}/line/{lineId}")]
    [RequirePermission("fin-bank-reconciliation", "import")]
    public async Task<IActionResult> DeleteLine(Guid id, Guid lineId)
        => Fin(await _svc.DeleteLineAsync(id, lineId, CurrentUser));
}
