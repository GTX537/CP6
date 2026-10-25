using CP6.Core.Auth;
using CP6.Core.Services.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Mes;

/// <summary>MES Phase 4 — 設備管理 Web API</summary>
[ApiController]
[Route("api/mes/machines")]
[Authorize]
[CP6.WebApi.Controllers.Erp.OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MachineController : ControllerBase
{
    private readonly IMachineService _service;
    public MachineController(IMachineService service) => _service = service;

    private string? CurrentUser => User?.Identity?.Name;

    private IMachineCreationOperationsV1 Creations => _service as IMachineCreationOperationsV1
        ?? throw new CP6.Core.Services.Erp.OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);

    [HttpGet("creation-context")]
    [RequirePermission("mes-machine","add")]
    public async Task<IActionResult> CreationContext(CancellationToken ct)
        =>Ok(new{code=0,message="OK",data=await Creations.CreationContextAsync(ct)});

    [HttpGet("operations/scope")]
    [RequirePermission("mes-machine","query")]
    public async Task<IActionResult> CreationScope(CancellationToken ct)
        =>Ok(new{code=0,message="OK",data=await Creations.CreationScopeAsync(ct)});

    // The actual Owner chooses add for a new command, query for an immutable original.
    [HttpPost("operations")]
    public async Task<IActionResult> CreateOperation(MachineCreationCommandV1 request,CancellationToken ct)
    {
        try {return Ok(new{code=0,message="OK",data=await Creations.CreateCommandAsync(request,ct)});}
        catch(InvalidOperationException ex) when(ex is not CP6.Core.Services.Erp.OrderPriceCorrectionException)
        {return BadRequest(new{code=400,message=ex.Message});}
    }

    [HttpGet("operations/{operation:guid}")]
    [RequirePermission("mes-machine","query")]
    public async Task<IActionResult> ReadCreation(Guid operation,CancellationToken ct)
        =>Ok(new{code=0,message="OK",data=await Creations.ReadCreationAsync(operation,ct)});

    [HttpPost("operations/{operation:guid}/close")]
    [RequirePermission("mes-machine","query")]
    public async Task<IActionResult> CloseCreation(Guid operation,CancellationToken ct)
        =>Ok(new{code=0,message="OK",data=await Creations.CloseCreationAsync(operation,ct)});

    [HttpGet]
    [RequirePermission("mes-machine", "query")]
    public async Task<IActionResult> Search([FromQuery] MachineSearchQuery query)
    {
        var r = await _service.SearchAsync(query);
        return Ok(new { code = 0, message = "OK", data = r });
    }

    [HttpGet("{cd}")]
    [RequirePermission("mes-machine", "query")]
    public async Task<IActionResult> Get(string cd)
    {
        var dto = await _service.GetByCdAsync(cd);
        if (dto == null) return NotFound(new { code = 404, message = "ME-MSG-040" });
        return Ok(new { code = 0, message = "OK", data = dto });
    }

    [HttpPost]
    [RequirePermission("mes-machine", "add")]
    public async Task<IActionResult> Create([FromBody] MachineDto dto)
    {
        try
        {
            var cd = await _service.CreateAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041", data = new { machineCd = cd } });
        }
        catch (InvalidOperationException ex) when (ex is not CP6.Core.Services.Erp.OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPut("{cd}")]
    [RequirePermission("mes-machine", "edit")]
    public async Task<IActionResult> Update(string cd, [FromBody] MachineDto dto)
    {
        try
        {
            await _service.UpdateAsync(cd, dto, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041" });
        }
        catch (InvalidOperationException ex) when (ex is not CP6.Core.Services.Erp.OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpDelete("{cd}")]
    [RequirePermission("mes-machine", "del")]
    public async Task<IActionResult> Delete(string cd)
    {
        try
        {
            await _service.DeleteAsync(cd, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041" });
        }
        catch (InvalidOperationException ex) when (ex is not CP6.Core.Services.Erp.OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    /// <summary>設備状態変更</summary>
    [HttpPost("{cd}/status")]
    [RequirePermission("mes-machine", "status")]
    public async Task<IActionResult> ChangeStatus(string cd, [FromBody] ChangeStatusRequest req)
    {
        try
        {
            await _service.ChangeStatusAsync(cd, req.Status, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041" });
        }
        catch (InvalidOperationException ex) when (ex is not CP6.Core.Services.Erp.OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    // ─────── 停止記録 ───────

    [HttpGet("downtimes")]
    [RequirePermission("mes-machine", "query")]
    public async Task<IActionResult> SearchDowntimes([FromQuery] DowntimeSearchQuery query)
    {
        var r = await _service.SearchDowntimesAsync(query);
        return Ok(new { code = 0, message = "OK", data = r });
    }

    [HttpPost("downtimes")]
    [RequirePermission("mes-machine", "downtime")]
    public async Task<IActionResult> RegisterDowntime([FromBody] MachineDowntimeDto dto)
    {
        try
        {
            var no = await _service.RegisterDowntimeAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041", data = new { downtimeNo = no } });
        }
        catch (InvalidOperationException ex) when (ex is not CP6.Core.Services.Erp.OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPost("downtimes/{no}/close")]
    [RequirePermission("mes-machine", "downtime")]
    public async Task<IActionResult> CloseDowntime(string no, [FromBody] CloseDowntimeRequest req)
    {
        try
        {
            await _service.CloseDowntimeAsync(no, req.EndTime, req.RecoveryOperatorCd, CurrentUser);
            return Ok(new { code = 0, message = "ME-MSG-041" });
        }
        catch (InvalidOperationException ex) when (ex is not CP6.Core.Services.Erp.OrderPriceCorrectionException) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    public class ChangeStatusRequest { public int Status { get; set; } }
    public class CloseDowntimeRequest
    {
        public DateTime EndTime { get; set; } = DateTime.Now;
        public string? RecoveryOperatorCd { get; set; }
    }
}
