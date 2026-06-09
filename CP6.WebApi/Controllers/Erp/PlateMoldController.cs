using CP6.Core.Services;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Erp;

/// <summary>
/// MSBBPA140/150 Web 版型/木型マスタ
/// </summary>
[ApiController]
[Authorize]
[Route("api/plate-molds")]
public class PlateMoldController : ControllerBase
{
    private readonly IPlateMoldService _service;
    public PlateMoldController(IPlateMoldService service) { _service = service; }

    // ─── PA140 ───

    [HttpGet("next-seq")]
    public async Task<IActionResult> NextSeq()
    {
        var no = await _service.NextSequenceAsync();
        return Ok(new { code = 0, message = "OK", data = new { sequence = no } });
    }

    [HttpGet("by-estimate-calc/{calcNo}")]
    public async Task<IActionResult> ByEstimate(string calcNo)
    {
        var dto = await _service.GetByEstimateNoAsync(calcNo);
        if (dto == null) return Ok(new { code = 0, message = "E10008: 検索結果がありません", data = (object?)null });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    [HttpGet("by-product/{productCd}")]
    public async Task<IActionResult> ByProduct(string productCd)
    {
        var dto = await _service.GetCompositionByProductAsync(productCd);
        if (dto == null) return Ok(new { code = 0, message = "E10008: 検索結果がありません", data = (object?)null });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    [HttpGet("{wdPtnNo}")]
    public async Task<IActionResult> GetByNo(string wdPtnNo, [FromQuery] int? rev = null)
    {
        var dto = await _service.GetByNoAsync(wdPtnNo, rev);
        if (dto == null) return Ok(new { code = 0, message = "E10008: 検索結果がありません", data = (object?)null });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    [HttpGet("{wdPtnNo}/history")]
    public async Task<IActionResult> History(string wdPtnNo)
    {
        var rows = await _service.GetHistoryAsync(wdPtnNo);
        return Ok(new { code = 0, message = "OK", data = rows });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PlateMoldDto dto)
    {
        var userName = User?.Identity?.Name;
        try
        {
            var (no, rev, link, pe) = await _service.CreateAsync(dto, userName);
            return Ok(new {
                code = 0, message = "OK",
                data = new { wdPtnNo = no, wdRev = rev, orderLink = link, peApi = pe }
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    [HttpPut("{wdPtnNo}/revise")]
    public async Task<IActionResult> Revise(string wdPtnNo, [FromBody] PlateMoldDto dto)
    {
        var userName = User?.Identity?.Name;
        try
        {
            var (rev, link, pe) = await _service.ReviseAsync(wdPtnNo, dto, userName);
            return Ok(new {
                code = 0, message = "OK",
                data = new { wdPtnNo, wdRev = rev, orderLink = link, peApi = pe }
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    [HttpPut("{wdPtnNo}/{wdRev:int}")]
    public async Task<IActionResult> Update(string wdPtnNo, int wdRev, [FromBody] PlateMoldDto dto)
    {
        var userName = User?.Identity?.Name;
        try
        {
            await _service.UpdateAsync(wdPtnNo, wdRev, dto, userName);
            return Ok(new { code = 0, message = "OK" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { code = 409, message = $"E10023: すでに更新済みです。再検索してください。版型・木型No:{wdPtnNo} Rev:{wdRev}" });
        }
    }

    [HttpDelete("{wdPtnNo}/{wdRev:int}")]
    public async Task<IActionResult> Delete(string wdPtnNo, int wdRev, [FromQuery] string? rowVersion = null)
    {
        var userName = User?.Identity?.Name;
        var rv = !string.IsNullOrEmpty(rowVersion) ? Convert.FromBase64String(rowVersion) : null;
        await _service.DeleteAsync(wdPtnNo, wdRev, rv, userName);
        return Ok(new { code = 0, message = "OK" });
    }

    // ─── PA150 ───

    [HttpGet("list")]
    public async Task<IActionResult> List([FromQuery] PlateMoldQueryDto query)
    {
        var (rows, total) = await _service.SearchAsync(query);
        return Ok(new { code = 0, message = "OK", data = new { rows, total } });
    }

    [HttpGet("list/export.csv")]
    public async Task<IActionResult> ExportCsv([FromQuery] PlateMoldQueryDto query)
    {
        var bytes = await _service.ExportListCsvAsync(query);
        var fn = $"plate-molds_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        return File(bytes, "text/csv; charset=utf-8", fn);
    }

    /// <summary>ラベル発行 CSV — POST /api/plate-molds/label</summary>
    [HttpPost("label")]
    public async Task<IActionResult> Label([FromBody] LabelRequest req)
    {
        if (req?.Targets == null || req.Targets.Count == 0)
            return BadRequest(new { code = 400, message = "E10010: 発行対象がありません" });
        var bytes = await _service.IssueLabelCsvAsync(
            req.Targets.Select(t => (t.WdPtnNo, t.WdRev)));
        var fn = $"plate-mold-labels_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        return File(bytes, "text/csv; charset=utf-8", fn);
    }
}

public class LabelRequest
{
    public List<LabelTarget> Targets { get; set; } = new();
}
public class LabelTarget
{
    public string WdPtnNo { get; set; } = string.Empty;
    public int WdRev { get; set; }
}
