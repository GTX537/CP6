using System.Security.Claims;
using CP6.Core.Services;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Erp;

/// <summary>
/// MSBBPA110 / 120 — Web 取引先マスタ
/// </summary>
[ApiController]
[Route("api/business-partners")]
[Authorize]
public class BusinessPartnerController : ControllerBase
{
    private readonly IBusinessPartnerService _service;

    public BusinessPartnerController(IBusinessPartnerService service)
    {
        _service = service;
    }

    private string? UserName => User.FindFirstValue(ClaimTypes.Name);

    /// <summary>取引先 1 件取得（全 Tab フィールド一括）— GET /api/business-partners/{cd}</summary>
    [HttpGet("{bpCd}")]
    public async Task<IActionResult> Get(string bpCd, [FromQuery] bool includeDeleted = false)
    {
        var dto = await _service.GetByCdAsync(bpCd, includeDeleted);
        if (dto == null)
            return Ok(new { code = 404, message = "E10008: 検索結果がありません", data = (object?)null });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    /// <summary>取引先 CD 重複チェック — GET /api/business-partners/check-exists/{cd}</summary>
    [HttpGet("check-exists/{bpCd}")]
    public async Task<IActionResult> CheckExists(string bpCd)
    {
        var exists = await _service.ExistsAsync(bpCd);
        return Ok(new { code = 0, message = "OK", data = new { exists } });
    }

    /// <summary>新規登録 — POST /api/business-partners?preRegister=true|false</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] BusinessPartnerDto dto, [FromQuery] bool preRegister = false)
    {
        try
        {
            var bpCd = await _service.CreateAsync(dto, UserName, preRegister);
            var saved = await _service.GetByCdAsync(bpCd);
            return Ok(new { code = 0, message = "OK", data = saved });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>訂正 — PUT /api/business-partners/{cd}</summary>
    [HttpPut("{bpCd}")]
    public async Task<IActionResult> Update(string bpCd, [FromBody] BusinessPartnerDto dto)
    {
        try
        {
            await _service.UpdateAsync(bpCd, dto, UserName);
            var saved = await _service.GetByCdAsync(bpCd);
            return Ok(new { code = 0, message = "OK", data = saved });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { code = 409, message = "E10034: すでに更新済みです。再検索してください", msgId = "E10034" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = 404, message = ex.Message });
        }
    }

    /// <summary>削除 — DELETE /api/business-partners/{cd}（管理者のみ）</summary>
    [HttpDelete("{bpCd}")]
    [Authorize(Roles = "1,Admin")]   // RoleId=1 = 管理者
    public async Task<IActionResult> Delete(string bpCd, [FromBody] DeleteRequest? req = null)
    {
        try
        {
            await _service.DeleteAsync(bpCd, req?.RowVersion, UserName);
            return Ok(new { code = 0, message = "OK", data = (object?)null });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { code = 409, message = "E10034: すでに更新済みです" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = 404, message = ex.Message });
        }
    }

    /// <summary>PA120 一覧検索 — GET /api/business-partners/list</summary>
    [HttpGet("list")]
    public async Task<IActionResult> Search([FromQuery] BpQueryDto query)
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

    /// <summary>PA120 CSV 出力 — GET /api/business-partners/export-csv</summary>
    [HttpGet("export-csv")]
    public async Task<IActionResult> ExportCsv([FromQuery] BpQueryDto query)
    {
        var bytes = await _service.ExportListCsvAsync(query);
        var filename = $"business-partners_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        return File(bytes, "text/csv; charset=utf-8", filename);
    }
}

public class DeleteRequest
{
    public byte[]? RowVersion { get; set; }
}
