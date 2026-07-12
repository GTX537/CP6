using CP6.Core.Auth;
using CP6.Core.Services.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

/// <summary>
/// MSBBME060 / 070 — 品質検査 Web API
/// </summary>
[ApiController]
[Route("api/mes/inspections")]
[Authorize]
public class QualityInspectionController : ControllerBase
{
    private readonly IQualityInspectionService _service;

    public QualityInspectionController(IQualityInspectionService service) => _service = service;

    private string? CurrentUser => User?.Identity?.Name;

    /// <summary>ME070 — 一覧</summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] QualityInspectionSearchQuery query)
    {
        var r = await _service.SearchAsync(query);
        return Ok(new { code = 0, message = "OK", data = r });
    }

    /// <summary>ME060 — 単条</summary>
    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var dto = await _service.GetByNoAsync(no);
        if (dto == null) return NotFound(new { code = 404, message = "ME-MSG-040" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    /// <summary>ME060 — 新規登録</summary>
    [HttpPost]
    [RequirePermission("mes-quality-inspection", "add")]
    public async Task<IActionResult> Create([FromBody] QualityInspectionDto dto)
    {
        try
        {
            var no = await _service.CreateAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041", data = new { inspectionNo = no } });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>ME060 — 訂正</summary>
    [HttpPut("{no}")]
    [RequirePermission("mes-quality-inspection", "edit")]
    public async Task<IActionResult> Update(string no, [FromBody] QualityInspectionDto dto)
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

    /// <summary>ME060 — 検査項目テンプレート取得</summary>
    [HttpGet("templates/{cd}")]
    public async Task<IActionResult> GetTemplate(string cd)
    {
        var rows = await _service.GetTemplateAsync(cd);
        return Ok(new { code = 0, message = "OK", data = rows });
    }

    /// <summary>ME060 — 全テンプレートCD一覧</summary>
    [HttpGet("templates")]
    public async Task<IActionResult> GetAllTemplates()
    {
        var rows = await _service.GetAllTemplateCodesAsync();
        return Ok(new { code = 0, message = "OK", data = rows });
    }
}
