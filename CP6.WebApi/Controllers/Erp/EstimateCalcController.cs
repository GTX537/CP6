using CP6.Core.Services;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Erp;

/// <summary>
/// MSBBPA010 - 見積計算書 Web API
/// </summary>
[ApiController]
[Route("api/estimate-calcs")]
[Authorize]
public class EstimateCalcController : LocalizedControllerBase
{
    private readonly IEstimateCalcService _service;

    public EstimateCalcController(IEstimateCalcService service)
    {
        _service = service;
    }

    private string? CurrentUser => User?.Identity?.Name;

    /// <summary>
    /// 分页列表（MSBBPA020 调用）
    /// GET /api/estimate-calcs?page=1&amp;pageSize=10&amp;customerCd=C0001
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] EstimateCalcQuery query)
    {
        var (rows, total) = await _service.GetPageListAsync(query);
        return Ok(new { code = 0, message = "OK", data = new { rows, total } });
    }

    /// <summary>
    /// 按 NO 查询单条（含工程明细）
    /// GET /api/estimate-calcs/{no}
    /// </summary>
    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no, [FromQuery] bool includeDeleted = false)
    {
        var dto = await _service.GetByNoAsync(no, includeDeleted);
        if (dto == null) return NotFound(new { code = 404, message = Localizer["見積計算書NOが未登録です。"], msgId = "MSG-102" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    /// <summary>
    /// 新建（登録）
    /// POST /api/estimate-calcs
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EstimateCalcDto dto)
    {
        var no = await _service.CreateAsync(dto, CurrentUser);
        var fresh = await _service.GetByNoAsync(no);
        return Ok(new { code = 0, message = "OK", data = fresh });
    }

    /// <summary>
    /// 修改（訂正）
    /// PUT /api/estimate-calcs/{no}
    /// </summary>
    [HttpPut("{no}")]
    public async Task<IActionResult> Update(string no, [FromBody] EstimateCalcDto dto)
    {
        try
        {
            await _service.UpdateAsync(no, dto, CurrentUser);
            var fresh = await _service.GetByNoAsync(no);
            return Ok(new { code = 0, message = "OK", data = fresh });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = 404, message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new
            {
                code = 409,
                message = Localizer["更新対象が、他の処理によって更新されています。最新情報を取得してください。"],
                msgId = "MSG-W10002"
            });
        }
    }

    /// <summary>
    /// 逻辑删除（削除）
    /// DELETE /api/estimate-calcs/{no}
    /// Body: { "rowVersion": "base64..." }
    /// </summary>
    [HttpDelete("{no}")]
    public async Task<IActionResult> Delete(string no, [FromBody] DeleteRequest? req)
    {
        try
        {
            await _service.DeleteAsync(no, req?.RowVersion, CurrentUser);
            return Ok(new { code = 0, message = "OK" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = 404, message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { code = 409, message = Localizer["排他锁冲突"], msgId = "MSG-W10002" });
        }
    }

    /// <summary>
    /// 复制新建（コピー）
    /// POST /api/estimate-calcs/{no}/copy
    /// </summary>
    [HttpPost("{no}/copy")]
    public async Task<IActionResult> Copy(string no)
    {
        try
        {
            var newNo = await _service.CopyAsync(no, CurrentUser);
            var fresh = await _service.GetByNoAsync(newNo);
            return Ok(new { code = 0, message = "OK", data = fresh });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = 404, message = ex.Message });
        }
    }

    /// <summary>
    /// 計算（引擎：面積 × 原紙単価（M_GenericCode Paper）× 段成率（M067））
    /// POST /api/estimate-calcs/calculate
    /// </summary>
    [HttpPost("calculate")]
    [AllowAnonymous]
    public async Task<IActionResult> Calculate([FromBody] EstimateCalcDto dto)
    {
        var result = await _service.CalculateAsync(dto);
        return Ok(new { code = 0, message = "OK", data = result });
    }

    public class DeleteRequest
    {
        public byte[]? RowVersion { get; set; }
    }
}
