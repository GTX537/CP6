using CP6.Core.Services;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers;

/// <summary>
/// MSBBPA130 Web シート単価マスタ
/// </summary>
[ApiController]
[Authorize]
[Route("api/sheet-unit-prices")]
public class SheetUnitPriceController : ControllerBase
{
    private readonly ISheetUnitPriceService _service;
    public SheetUnitPriceController(ISheetUnitPriceService service) { _service = service; }

    /// <summary>参照検索 — GET /api/sheet-unit-prices/list</summary>
    [HttpGet("list")]
    public async Task<IActionResult> List(
        [FromQuery] DateTime baseDate,
        [FromQuery] string baseCd,
        [FromQuery] SheetPriceImportDiv importDiv = SheetPriceImportDiv.Standard,
        [FromQuery] string? customerCd = null,
        [FromQuery] string? salesStaffCd = null)
    {
        var rows = await _service.SearchAsync(new SheetPriceQueryDto
        {
            BaseDate = baseDate, BaseCd = baseCd, ImportDiv = importDiv,
            CustomerCd = customerCd, SalesStaffCd = salesStaffCd,
        });
        return Ok(new { code = 0, message = "OK", data = rows });
    }

    /// <summary>Excel 取込（multipart upload）— POST /api/sheet-unit-prices/import</summary>
    [HttpPost("import")]
    [RequestSizeLimit(50_000_000)] // 50 MB
    public async Task<IActionResult> Import(
        IFormFile file,
        [FromForm] DateTime baseDate,
        [FromForm] string baseCd,
        [FromForm] SheetPriceImportDiv importDiv = SheetPriceImportDiv.Standard)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { code = 400, message = "E10097: ファイルが選択されていません" });
        using var stream = file.OpenReadStream();
        var result = await _service.ImportExcelAsync(stream, baseDate, baseCd, importDiv);
        return Ok(new { code = 0, message = "OK", data = result });
    }

    /// <summary>選択行 一括更新（UPSERT） — PUT /api/sheet-unit-prices/batch-update</summary>
    [HttpPut("batch-update")]
    public async Task<IActionResult> BatchUpdate([FromBody] SheetPriceBatchUpdateDto request)
    {
        var userName = User?.Identity?.Name;
        var count = await _service.BatchUpdateAsync(request, userName);
        return Ok(new { code = 0, message = "OK", data = new { updated = count } });
    }
}
