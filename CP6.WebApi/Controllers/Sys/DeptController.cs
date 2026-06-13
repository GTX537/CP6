using CP6.Core.Services.Sys;
using CP6.Entity.DTOs.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

/// <summary>部门（组织树）REST —— PUB 章00。/api/pub/dept</summary>
[ApiController]
[Route("api/pub/dept")]
[Authorize]
public class DeptController : ControllerBase
{
    private readonly IDeptService _svc;
    public DeptController(IDeptService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    [HttpGet("tree")]
    public async Task<IActionResult> Tree() => Ok2(await _svc.TreeAsync());

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] DeptDto d)
    {
        try { return Ok2(new { id = await _svc.CreateAsync(d, d.ParentId, CurrentUser) }); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] DeptDto d)
    {
        try { await _svc.UpdateAsync(id, d, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try { await _svc.DeleteAsync(id); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPost("{id}/move")]
    public async Task<IActionResult> Move(Guid id, [FromBody] MoveReq r)
    {
        try { await _svc.MoveAsync(id, r.NewParentId, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpPut("{id}/leader")]
    public async Task<IActionResult> Leader(Guid id, [FromBody] LeaderReq r)
    {
        try { await _svc.SetLeaderAsync(id, r.LeaderId, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    public record MoveReq(Guid? NewParentId);
    public record LeaderReq(Guid? LeaderId);
}
