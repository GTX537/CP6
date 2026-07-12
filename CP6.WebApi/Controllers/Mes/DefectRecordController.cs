using CP6.Core.Auth;
using CP6.Core.Services.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

/// <summary>
/// MSBBME080 — 不良品管理 Web API
/// </summary>
[ApiController]
[Route("api/mes/defects")]
[Authorize]
public class DefectRecordController : ControllerBase
{
    private readonly IDefectRecordService _service;

    public DefectRecordController(IDefectRecordService service) => _service = service;

    private string? CurrentUser => User?.Identity?.Name;

    /// <summary>ME080 — 一覧</summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] DefectRecordSearchQuery query)
    {
        var r = await _service.SearchAsync(query);
        return Ok(new { code = 0, message = "OK", data = r });
    }

    /// <summary>ME080 — 単条</summary>
    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var dto = await _service.GetByNoAsync(no);
        if (dto == null) return NotFound(new { code = 404, message = "ME-MSG-040" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    /// <summary>ME080 — 手動起票</summary>
    [HttpPost]
    [RequirePermission("mes-defect", "add")]
    public async Task<IActionResult> Create([FromBody] DefectRecordDto dto)
    {
        try
        {
            var no = await _service.CreateAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041", data = new { defectNo = no } });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>ME080 — 更新（原因分析 / 是正処置）</summary>
    [HttpPut("{no}")]
    [RequirePermission("mes-defect", "edit")]
    public async Task<IActionResult> Update(string no, [FromBody] DefectRecordDto dto)
    {
        try
        {
            await _service.UpdateAsync(no, dto, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>ME080 — 削除</summary>
    [HttpDelete("{no}")]
    [RequirePermission("mes-defect", "del")]
    public async Task<IActionResult> Delete(string no)
    {
        try
        {
            await _service.DeleteAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>ME080 — 不良分類マスタ 一覧</summary>
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        var rows = await _service.GetAllCategoriesAsync();
        return Ok(new { code = 0, message = "OK", data = rows });
    }
}
