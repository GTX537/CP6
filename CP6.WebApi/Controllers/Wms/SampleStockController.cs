using CP6.Core.Auth;
using CP6.Core.Services.Erp;
using CP6.Entity.DTOs.Wms;
using CP6.WebApi.Controllers.Erp;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM260 — サンプル品 Web API</summary>
[ApiController]
[Route("api/wms/sample-stock")]
[Authorize]
[OrderOwnerResult]
public class SampleStockController : ControllerBase
{
    private readonly ISampleStockService _svc;
    private readonly SampleOwnerV1 _owner;
    public SampleStockController(ISampleStockService svc, SampleOwnerV1 owner) { _svc=svc; _owner=owner; }

    [HttpGet("owner/v1/records")]
    public async Task<IActionResult> Records([FromQuery] SampleQueryV1 query,CancellationToken ct)
        => Ok(new {code=0,message="OK",data=await _owner.SearchAsync(query,ct)});
    [HttpGet("owner/v1/records/{id:guid}")]
    public async Task<IActionResult> Record(Guid id,CancellationToken ct)
        => Ok(new {code=0,message="OK",data=await _owner.GetAsync(id,ct)});
    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] SampleSearchQuery q)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchAsync(q) });

    [HttpGet("{no}")]
    public async Task<IActionResult> Get(string no)
    {
        var r = await _svc.GetAsync(no);
        if (r == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = r });
    }

    [HttpPost]
    [RequirePermission("wms-sample-stock", "add")]
    public async Task<IActionResult> Create([FromBody] SampleDto dto)
    {
        try
        {
            var no = await _svc.CreateAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { sampleNo = no } });
        }
        catch (InvalidOperationException ex) when(ex is not OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPut("{no}")]
    [RequirePermission("wms-sample-stock", "edit")]
    public async Task<IActionResult> Update(string no, [FromBody] SampleDto dto)
    {
        try { await _svc.UpdateAsync(no, dto, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) when(ex is not OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/lend")]
    [RequirePermission("wms-sample-stock", "lend")]
    public async Task<IActionResult> Lend(string no, [FromBody] LendRequest req)
    {
        try { await _svc.LendAsync(no, req.LentTo, req.ExpectedReturnDate, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) when(ex is not OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/return")]
    [RequirePermission("wms-sample-stock", "return")]
    public async Task<IActionResult> Return(string no)
    {
        try { await _svc.ReturnAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) when(ex is not OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("{no}/expire")]
    [RequirePermission("wms-sample-stock", "expire")]
    public async Task<IActionResult> Expire(string no)
    {
        try { await _svc.ExpireAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) when(ex is not OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpGet("overdue")]
    public async Task<IActionResult> Overdue()
        => Ok(new { code = 0, message = "OK", data = await _svc.OverdueAsync() });

    [HttpDelete("{no}")]
    [RequirePermission("wms-sample-stock", "del")]
    public async Task<IActionResult> Delete(string no)
    {
        try { await _svc.DeleteAsync(no, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) when(ex is not OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    public class LendRequest
    {
        public string LentTo { get; set; } = string.Empty;
        public DateTime? ExpectedReturnDate { get; set; }
    }
}
