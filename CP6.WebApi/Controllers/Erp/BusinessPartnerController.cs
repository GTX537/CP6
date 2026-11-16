using System.Security.Claims;
using CP6.Core.Auth;
using CP6.Core.Services;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Erp;

/// <summary>
/// MSBBPA110 / 120 业务伙伴档案
/// </summary>
[ApiController]
[Route("api/business-partners")]
[Authorize]
[OrderOwnerResult]
public class BusinessPartnerController : LocalizedControllerBase
{
    private readonly IBusinessPartnerService _service;

    public BusinessPartnerController(IBusinessPartnerService service)
    {
        _service = service;
    }

    private string? UserName => User.FindFirstValue(ClaimTypes.Name);

    /// <summary>按代码查询业务伙伴档案。</summary>
    [HttpGet("{bpCd}")]
    public async Task<IActionResult> Get(string bpCd, [FromQuery] bool includeDeleted = false)
    {
        var dto = await _service.GetByCdAsync(bpCd, includeDeleted);
        if (dto == null)
            return Ok(new { code = 404, message = "未找到业务伙伴。", data = (object?)null });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    /// <summary>查询当前范围内的业务伙伴代码是否存在。</summary>
    [HttpGet("check-exists/{bpCd}")]
    public async Task<IActionResult> CheckExists(string bpCd)
    {
        var exists = await _service.ExistsAsync(bpCd);
        return Ok(new { code = 0, message = "OK", data = new { exists } });
    }

    /// <summary>新建或预登记业务伙伴。</summary>
    [HttpPost]
    [RequirePermission("erp-business-partner", "add")]
    public async Task<IActionResult> Create([FromBody] BusinessPartnerDto dto, [FromQuery] bool preRegister = false)
    {
        try
        {
            var saved = await _service.CreateAsync(dto, UserName, preRegister);
            return Ok(new { code = 0, message = "OK", data = saved });
        }
        catch (InvalidOperationException ex) when (ex is not OrderPriceCorrectionException)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>修改业务伙伴。</summary>
    [HttpPut("{bpCd}")]
    [RequirePermission("erp-business-partner", "edit")]
    public async Task<IActionResult> Update(string bpCd, [FromBody] BusinessPartnerDto dto)
    {
        try
        {
            var saved = await _service.UpdateAsync(bpCd, dto, UserName);
            return Ok(new { code = 0, message = "OK", data = saved });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { code = 409, message = "业务伙伴已更新，请重新查询。", msgId = "E10034" });
        }
        catch (InvalidOperationException ex) when (ex is not OrderPriceCorrectionException)
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
    [RequirePermission("erp-business-partner", "del")]
    public async Task<IActionResult> Delete(string bpCd, [FromBody] DeleteRequest? req = null)
    {
        try
        {
            await _service.DeleteAsync(bpCd, req?.RowVersion, UserName);
            return Ok(new { code = 0, message = "OK", data = (object?)null });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { code = 409, message = Localizer["E10034: すでに更新済みです"] });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = 404, message = ex.Message });
        }
    }

    /// <summary>查询当前范围内的业务伙伴列表。</summary>
    [HttpGet("list")]
    public async Task<IActionResult> Search([FromQuery] BpQueryDto query)
    {
        try
        {
            var (rows, total) = await _service.SearchAsync(query);
            return Ok(new { code = 0, message = "OK", data = new { rows, total } });
        }
        catch (InvalidOperationException ex) when (ex is not OrderPriceCorrectionException)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>按当前导出权限生成业务伙伴 CSV。</summary>
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
