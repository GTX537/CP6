using CP6.Core.Auth;
using CP6.Core.Services.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>
/// 预算行 REST —— 财务 A5。/api/fin/budget/lines。
/// 预算明细行 Upsert/Delete + Excel 批量导入（预览→确认两步）。
/// </summary>
[ApiController]
[Route("api/fin/budget/lines")]
[Authorize]
public class BudgetLineController : ControllerBase
{
    private readonly IBudgetLineService _svc;

    public BudgetLineController(IBudgetLineService svc) => _svc = svc;

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });
    private static bool TryDecodeRowVersion(string? encoded, out byte[]? rowVersion)
    {
        rowVersion = null;
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        try
        {
            rowVersion = Convert.FromBase64String(encoded);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    [HttpGet]
    [RequirePermission("fin-budget", "view")]
    public async Task<IActionResult> List([FromQuery] Guid versionId)
        => Ok2(await _svc.ListLinesAsync(versionId));

    [HttpPost]
    [RequirePermission("fin-budget", "edit")]
    public async Task<IActionResult> Upsert([FromBody] BudgetLineDto dto)
    {
        if (dto.VersionRowVersion is not { Length: > 0 })
            return Fin(FinResult.Fail("E-A5-CONCURRENCY-001"));
        return Fin(await _svc.UpsertLineAsync(dto));
    }

    [HttpDelete("{lineId:guid}")]
    [RequirePermission("fin-budget", "edit")]
    public async Task<IActionResult> Delete(
        Guid lineId,
        [FromQuery] string? lineRowVersion,
        [FromQuery] string? versionRowVersion)
    {
        if (!TryDecodeRowVersion(lineRowVersion, out var lineToken) ||
            !TryDecodeRowVersion(versionRowVersion, out var versionToken))
            return Fin(FinResult.Fail("E-A5-CONCURRENCY-001"));
        return Fin(await _svc.DeleteLineAsync(lineId, lineToken, versionToken));
    }

    [HttpPost("import/preview")]
    [RequirePermission("fin-budget", "import")]
    public async Task<IActionResult> ImportPreview([FromQuery] Guid versionId, IFormFile file)
    {
        using var s = file.OpenReadStream();
        return Ok2(await _svc.PreviewImportAsync(versionId, s));
    }

    [HttpPost("import/confirm")]
    [RequirePermission("fin-budget", "import")]
    public async Task<IActionResult> ImportConfirm(
        [FromQuery] Guid versionId,
        [FromQuery] string? versionRowVersion,
        IFormFile file)
    {
        if (!TryDecodeRowVersion(versionRowVersion, out var versionToken))
            return Fin(FinResult.Fail("E-A5-CONCURRENCY-001"));
        using var s = file.OpenReadStream();
        return Fin(await _svc.ConfirmImportAsync(versionId, s, versionToken));
    }
}
