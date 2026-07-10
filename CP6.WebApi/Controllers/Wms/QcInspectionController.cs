using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM100 — 入荷検品 Web API</summary>
[ApiController]
[Route("api/wms/qc-inspection")]
[Authorize]
public class QcInspectionController : ControllerBase
{
    private readonly IQcInspectionService _svc;
    public QcInspectionController(IQcInspectionService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] QcInspectionSearchQuery q)
    {
        var list = await _svc.SearchAsync(q);
        return Ok(new { code = 0, message = "OK", data = list });
    }

    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var dto = await _svc.GetAsync(no);
        if (dto == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    /// <summary>入庫予定参照で検品作成</summary>
    [HttpPost("from-inbound/{inboundNo}")]
    [RequirePermission("wms-qc-inspection", "add")]
    public async Task<IActionResult> CreateFromInbound(string inboundNo)
    {
        try
        {
            var no = await _svc.CreateFromInboundAsync(inboundNo, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { inspectionNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    /// <summary>直入検品作成</summary>
    [HttpPost]
    [RequirePermission("wms-qc-inspection", "add")]
    public async Task<IActionResult> CreateDirect([FromBody] QcInspectionDto dto)
    {
        try
        {
            var no = await _svc.CreateDirectAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { inspectionNo = no } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    /// <summary>検査結果保存（status 0→1 自動）</summary>
    [HttpPut("{no}/items")]
    [RequirePermission("wms-qc-inspection", "edit")]
    public async Task<IActionResult> SaveItems(string no, [FromBody] List<QcInspectionItemDto> items)
    {
        try
        {
            await _svc.SaveItemsAsync(no, items, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    /// <summary>最終判定（PASS の場合は InboundReceipt 自動生成）</summary>
    [HttpPost("{no}/judge")]
    [RequirePermission("wms-qc-inspection", "judge")]
    public async Task<IActionResult> Judge(string no, [FromBody] JudgeRequest req)
    {
        try
        {
            var r = await _svc.JudgeAsync(no, req, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = r });
        }
        catch (InsufficientStockException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/cancel")]
    [RequirePermission("wms-qc-inspection", "cancel")]
    public async Task<IActionResult> Cancel(string no)
    {
        try
        {
            await _svc.CancelAsync(no, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071" });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }
}
