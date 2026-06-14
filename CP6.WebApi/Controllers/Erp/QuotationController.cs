using CP6.Core.Services;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Erp;

/// <summary>
/// MSBBPA030 / MSBBPA040 - 御見積書 Web API
/// </summary>
[ApiController]
[Route("api/quotations")]
[Authorize]
public class QuotationController : LocalizedControllerBase
{
    private readonly IQuotationService _service;

    public QuotationController(IQuotationService service)
    {
        _service = service;
    }

    private string? CurrentUser => User?.Identity?.Name;

    /// <summary>
    /// 分页列表（MSBBPA040 调用）
    /// GET /api/quotations?page=1&amp;pageSize=20&amp;customerCd=...&amp;statuses=0&amp;statuses=9
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] QuotationQuery query)
    {
        var (rows, total) = await _service.GetPageListAsync(query);
        return Ok(new { code = 0, message = "OK", data = new { rows, total } });
    }

    /// <summary>
    /// 按 NO 查询单条（含関連計算書+明细）
    /// GET /api/quotations/{no}
    /// </summary>
    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no, [FromQuery] bool includeDeleted = false)
    {
        var dto = await _service.GetByNoAsync(no, includeDeleted);
        if (dto == null)
            return NotFound(new { code = 404, message = Localizer["御見積書NOが未登録です。"], msgId = "MSG-102" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    /// <summary>
    /// 新建（登録）
    /// POST /api/quotations
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] QuotationDto dto)
    {
        var no = await _service.CreateAsync(dto, CurrentUser);
        var fresh = await _service.GetByNoAsync(no);
        return Ok(new { code = 0, message = "OK", data = fresh });
    }

    /// <summary>
    /// 修改（訂正）
    /// PUT /api/quotations/{no}
    /// </summary>
    [HttpPut("{no}")]
    public async Task<IActionResult> Update(string no, [FromBody] QuotationDto dto)
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
        catch (InvalidOperationException ex)
        {
            // MSG-004 業務禁止（已確定等）
            return BadRequest(new { code = 400, message = ex.Message, msgId = "MSG-004" });
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
    /// DELETE /api/quotations/{no}
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
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message, msgId = "MSG-004" });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { code = 409, message = Localizer["排他锁冲突"], msgId = "MSG-W10002" });
        }
    }

    /// <summary>
    /// 复制新建（コピー）
    /// POST /api/quotations/{no}/copy
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
    /// 確定登録
    /// POST /api/quotations/{no}/confirm
    /// Body: { "qtnCalcNos": ["..."], "rowVersion": "base64" }
    /// </summary>
    [HttpPost("{no}/confirm")]
    public async Task<IActionResult> Confirm(string no, [FromBody] ConfirmRequest req)
    {
        try
        {
            await _service.ConfirmAsync(no, req, CurrentUser);
            var fresh = await _service.GetByNoAsync(no);
            return Ok(new { code = 0, message = "OK", data = fresh });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = 404, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            var msgId = ex.Message.Contains("決定") ? "MSG-003"
                      : ex.Message.Contains("存在しません") ? "MSG-008" : "MSG-002";
            return BadRequest(new { code = 400, message = ex.Message, msgId });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { code = 409, message = Localizer["排他锁冲突"], msgId = "MSG-W10002" });
        }
    }

    /// <summary>
    /// 確定取消
    /// POST /api/quotations/{no}/cancel-confirm
    /// </summary>
    [HttpPost("{no}/cancel-confirm")]
    public async Task<IActionResult> CancelConfirm(string no, [FromBody] DeleteRequest? req)
    {
        try
        {
            await _service.CancelConfirmAsync(no, req?.RowVersion, CurrentUser);
            var fresh = await _service.GetByNoAsync(no);
            return Ok(new { code = 0, message = "OK", data = fresh });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = 404, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message, msgId = "MSG-009" });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { code = 409, message = Localizer["排他锁冲突"], msgId = "MSG-W10002" });
        }
    }

    /// <summary>
    /// 発行帳票（当前阶段只更新日期 + 返回文件名；Phase 5 会返回真正的 PDF 流）
    /// POST /api/quotations/{no}/issue
    /// </summary>
    [HttpPost("{no}/issue")]
    public async Task<IActionResult> Issue(string no, [FromBody] IssueRequest req)
    {
        try
        {
            var files = await _service.IssueAsync(no, req, CurrentUser);
            return Ok(new { code = 0, message = "OK", data = new { files } });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = 404, message = ex.Message });
        }
    }

    /// <summary>
    /// 取得関連見積計算書候选（§2.6.1 案件NO設定後联动刷新）
    /// GET /api/quotations/calcs?customerCd=&amp;projectNoParent=&amp;...&amp;currentQuotationNo=
    /// </summary>
    [HttpGet("calcs")]
    public async Task<IActionResult> GetCalcCandidates(
        [FromQuery] string customerCd,
        [FromQuery] string? projectNoParent,
        [FromQuery] string? projectNoChild,
        [FromQuery] string? projectNoMaterial,
        [FromQuery] string? currentQuotationNo)
    {
        if (string.IsNullOrWhiteSpace(customerCd))
            return BadRequest(new { code = 400, message = "customerCd is required" });

        var list = await _service.GetCalcCandidatesAsync(
            customerCd, projectNoParent, projectNoChild, projectNoMaterial, currentQuotationNo);
        return Ok(new { code = 0, message = "OK", data = list });
    }

    public class DeleteRequest
    {
        public byte[]? RowVersion { get; set; }
    }
}
