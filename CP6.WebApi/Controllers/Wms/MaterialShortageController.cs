using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/wms/material-shortage")]
[Authorize]
[CP6.WebApi.Controllers.Erp.OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MaterialShortageController : ControllerBase
{
    private readonly IMaterialShortageService _svc;

    public MaterialShortageController(IMaterialShortageService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;

    [HttpGet]
    [RequirePermission("wms-material-shortage", "query")]
    public async Task<IActionResult> Search([FromQuery] string? status, [FromQuery] string? workOrderNo, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(new
        {
            code = 0,
            message = "OK",
            data = await _svc.SearchAsync(new MaterialShortageQuery
            {
                Status = status,
                WorkOrderNo = workOrderNo,
                Page = page,
                PageSize = pageSize,
            }),
        });

    [HttpPost("{id:guid}/resolve")]
    [RequirePermission("wms-material-shortage", "resolve")]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ShortageActionRequest? req)
    {
        try
        {
            var data = await _svc.ResolveAsync(id, req?.Remark, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("WM-MSG-SHORTAGE-409"))
        {
            return Conflict(new { code = 409, message = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex is not CP6.Core.Services.Erp.OrderPriceCorrectionException)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/dismiss")]
    [RequirePermission("wms-material-shortage", "dismiss")]
    public async Task<IActionResult> Dismiss(Guid id, [FromBody] ShortageActionRequest? req)
    {
        try
        {
            var data = await _svc.DismissAsync(id, req?.Remark, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("WM-MSG-SHORTAGE-409"))
        {
            return Conflict(new { code = 409, message = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex is not CP6.Core.Services.Erp.OrderPriceCorrectionException)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    [HttpGet("owner-v1")]
    [RequirePermission("wms-material-shortage", "query")]
    public async Task<IActionResult> SearchOwner([FromQuery] MaterialShortageQuery query) =>
        Ok(new { code = 0, message = "OK", data = await _svc.SearchOwnerAsync(query) });

    [HttpPost("owner-v1/{id:guid}/resolve")]
    [RequirePermission("wms-material-shortage", "resolve")]
    public async Task<IActionResult> ResolveOwner(Guid id, [FromBody] CP6.Entity.DTOs.Wms.MaterialShortageActionV1 command) =>
        Ok(new { code = 0, message = "OK", data = await _svc.ResolveOwnerAsync(id, command) });

    [HttpPost("owner-v1/{id:guid}/dismiss")]
    [RequirePermission("wms-material-shortage", "dismiss")]
    public async Task<IActionResult> DismissOwner(Guid id, [FromBody] CP6.Entity.DTOs.Wms.MaterialShortageActionV1 command) =>
        Ok(new { code = 0, message = "OK", data = await _svc.DismissOwnerAsync(id, command) });

    public class ShortageActionRequest
    {
        public string? Remark { get; set; }
    }
}
