using CP6.Core.Services;
using CP6.Entity.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Erp;

/// <summary>
/// MSBBPA050 / MSBBPA060 - Web 製品マスタ Web API
/// </summary>
[ApiController]
[Route("api/products")]
[Authorize]
public class ProductController : ControllerBase
{
    private readonly IProductService _service;
    private readonly IWipCheckService _wipCheckService;

    public ProductController(IProductService service, IWipCheckService wipCheckService)
    {
        _service = service;
        _wipCheckService = wipCheckService;
    }

    private string? CurrentUser => User?.Identity?.Name;

    /// <summary>分页一覧（MSBBPA060 调用）— GET /api/products</summary>
    [HttpGet]
    public async Task<IActionResult> GetList([FromQuery] ProductQuery query)
    {
        var (rows, total) = await _service.GetPageListAsync(query);
        return Ok(new { code = 0, message = "OK", data = new { rows, total } });
    }

    /// <summary>採番（次の連番）— GET /api/products/next-seq</summary>
    /// <remarks>順序的に GetByCd の前に置く（{cd} ルートと衝突しないため）</remarks>
    [HttpGet("next-seq")]
    public async Task<IActionResult> GetNextSeq()
    {
        var seq = await _service.NextSequenceAsync();
        return Ok(new { code = 0, message = "OK", data = new { sequence = seq } });
    }

    /// <summary>御見積書 NO による部材一覧引入 — GET /api/products/by-quotation/{no}</summary>
    [HttpGet("by-quotation/{no}")]
    public async Task<IActionResult> GetByQuotation(string no)
    {
        var members = await _service.GetMembersByQuotationAsync(no);
        return Ok(new { code = 0, message = "OK", data = members });
    }

    /// <summary>見積計算書 NO による基本情報引入 — GET /api/products/by-estimate-calc/{no}</summary>
    [HttpGet("by-estimate-calc/{no}")]
    public async Task<IActionResult> GetByEstimateCalc(string no)
    {
        var info = await _service.GetBasicInfoByEstimateCalcAsync(no);
        if (info == null)
            return NotFound(new { code = 404, message = "見積計算書が見つかりません" });
        return Ok(new { code = 0, message = "OK", data = info });
    }

    /// <summary>CSV 出力（一覧の検索条件すべてを出力）— GET /api/products/export.csv</summary>
    [HttpGet("export.csv")]
    public async Task<IActionResult> ExportCsv([FromQuery] ProductQuery query)
    {
        var bytes = await _service.ExportCsvAsync(query);
        var filename = $"products_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        return File(bytes, "text/csv; charset=utf-8", filename);
    }

    /// <summary>仕掛チェック — GET /api/products/check-wip/{cd}</summary>
    /// <remarks>mcframe7 連携無し時は常に Level=0 を返す（NoOpWipCheckService）</remarks>
    [HttpGet("check-wip/{cd}")]
    public async Task<IActionResult> CheckWip(string cd)
    {
        var result = await _wipCheckService.CheckAsync(cd);
        return Ok(new { code = 0, message = "OK", data = result });
    }

    /// <summary>按 CD 查询单条（含 5 表关联）— GET /api/products/{cd}</summary>
    [HttpGet("{cd}")]
    public async Task<IActionResult> Get(string cd, [FromQuery] bool includeDeleted = false)
    {
        var dto = await _service.GetByCdAsync(cd, includeDeleted);
        if (dto == null)
            return NotFound(new { code = 404, message = "製品マスタが未登録です。" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    /// <summary>新建（登録）— POST /api/products</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ProductDto dto)
    {
        var cd = await _service.CreateAsync(dto, CurrentUser);
        var fresh = await _service.GetByCdAsync(cd);
        return Ok(new { code = 0, message = "OK", data = fresh });
    }

    /// <summary>修改（訂正）— PUT /api/products/{cd}</summary>
    [HttpPut("{cd}")]
    public async Task<IActionResult> Update(string cd, [FromBody] ProductDto dto)
    {
        try
        {
            await _service.UpdateAsync(cd, dto, CurrentUser);
            var fresh = await _service.GetByCdAsync(cd);
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
                message = "更新対象が、他の処理によって更新されています。最新情報を取得してください。",
                msgId = "MSG-W10002"
            });
        }
    }

    /// <summary>逻辑删除 — DELETE /api/products/{cd}</summary>
    [HttpDelete("{cd}")]
    public async Task<IActionResult> Delete(string cd, [FromBody] DeleteRequest? req)
    {
        try
        {
            await _service.DeleteAsync(cd, req?.RowVersion, CurrentUser);
            return Ok(new { code = 0, message = "OK" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = 404, message = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { code = 409, message = "排他锁冲突", msgId = "MSG-W10002" });
        }
    }

    /// <summary>复制新建 — POST /api/products/{cd}/copy</summary>
    [HttpPost("{cd}/copy")]
    public async Task<IActionResult> Copy(string cd)
    {
        try
        {
            var newCd = await _service.CopyAsync(cd, CurrentUser);
            var fresh = await _service.GetByCdAsync(newCd);
            return Ok(new { code = 0, message = "OK", data = fresh });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { code = 404, message = ex.Message });
        }
    }

    public class DeleteRequest
    {
        public byte[]? RowVersion { get; set; }
    }
}
