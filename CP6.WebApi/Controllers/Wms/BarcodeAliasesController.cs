using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/v2/wms/barcodes")]
[Authorize]
public sealed class BarcodeAliasesController : ControllerBase
{
    private readonly IBarcodeAliasService _service;
    public BarcodeAliasesController(IBarcodeAliasService service) => _service = service;

    [HttpGet]
    [RequirePermission("wms-mobile", "barcode-manage")]
    public Task<PagedResult<BarcodeAliasDto>> Get(
        string? search,
        string? barcodeType,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
        => _service.GetAsync(search, barcodeType, page, pageSize, ct);

    [HttpPost]
    [RequirePermission("wms-mobile", "barcode-manage")]
    public async Task<ActionResult<BarcodeAliasDto>> Upsert(
        UpsertBarcodeAliasRequest request,
        CancellationToken ct)
    {
        try { return Ok(await _service.UpsertAsync(request, User.Identity?.Name, ct)); }
        catch (MobileTaskConflictException ex)
        {
            return Conflict(new { code = ex.Code, message = ex.Code });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = ex.Message, message = ex.Message });
        }
    }

    [HttpPost("import")]
    [RequirePermission("wms-mobile", "barcode-manage")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<BarcodeImportResult>> Import(
        IFormFile file,
        [FromQuery] bool commit,
        CancellationToken ct)
    {
        if (file.Length == 0)
            return BadRequest(new { code = "WM-BARCODE-IMPORT-EMPTY" });
        try
        {
            await using var stream = file.OpenReadStream();
            return Ok(await _service.ImportAsync(
                stream, commit, User.Identity?.Name, ct));
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or InvalidOperationException
                                   or FormatException)
        {
            return BadRequest(new { code = ex.Message, message = ex.Message });
        }
    }
}
