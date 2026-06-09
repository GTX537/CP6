using System.Security.Claims;
using CP6.Core.Services;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Erp;

/// <summary>
/// MSBBPA100 — FSC 製品化チェックシート出力
/// </summary>
[ApiController]
[Route("api/fsc-checklists")]
[Authorize]
public class FscChecklistController : ControllerBase
{
    private readonly IFscChecklistService _service;

    public FscChecklistController(IFscChecklistService service)
    {
        _service = service;
    }

    private string? UserName => User.FindFirstValue(ClaimTypes.Name);

    /// <summary>多条件検索 — GET /api/fsc-checklists/list</summary>
    [HttpGet("list")]
    public async Task<IActionResult> Search([FromQuery] FscChecklistQueryDto query)
    {
        try
        {
            var (rows, total) = await _service.SearchAsync(query);
            return Ok(new { code = 0, message = "OK", data = new { rows, total } });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>出力フォーマット一覧 — GET /api/fsc-checklists/formats</summary>
    [HttpGet("formats")]
    public IActionResult Formats()
    {
        var data = _service.GetFormats().Select(f => new { name = f.Name, templatePath = f.TemplatePath });
        return Ok(new { code = 0, message = "OK", data });
    }

    /// <summary>チェックシート発行 — POST /api/fsc-checklists/issue</summary>
    [HttpPost("issue")]
    public async Task<IActionResult> Issue([FromBody] FscIssueRequestDto req)
    {
        try
        {
            var result = await _service.IssueAsync(req, UserName);
            return Ok(new { code = 0, message = "OK", data = result });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>発行済 Excel ダウンロード — GET /api/fsc-checklists/download/{fscNo}</summary>
    [HttpGet("download/{fscManagementNo}")]
    public async Task<IActionResult> Download(string fscManagementNo)
    {
        var r = await _service.DownloadAsync(fscManagementNo);
        if (r == null) return NotFound(new { code = 404, message = "発行履歴が見つかりません" });
        var (content, fileName) = r.Value;
        // 真正の Excel テンプレートが存在する場合はそのまま、無ければ plain-text fallback
        var contentType = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "text/plain; charset=utf-8";
        return File(content, contentType, fileName);
    }
}
