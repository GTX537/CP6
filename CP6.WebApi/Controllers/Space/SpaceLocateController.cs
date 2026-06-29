using CP6.Core.Services.Space;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

/// <summary>
/// Space 定位查询 Web API（ch06 §8）。按编码定位/前缀搜索/详情，全只读。
/// 路由前缀 /api/space；与 SpaceMasterController 的 /location 端点（literal 段优先）不冲突。
/// </summary>
[ApiController]
[Route("api/space")]
[Authorize]
public class SpaceLocateController : ControllerBase
{
    private readonly ISpaceLocateService _svc;
    public SpaceLocateController(ISpaceLocateService svc) => _svc = svc;

    private IActionResult Ok2(object? data = null, string msg = "OK") =>
        Ok(new { code = 0, message = msg, data });

    /// <summary>按编码精确定位（未命中→E-SPACE-601）。命中 Placed=false 时前端提示 W-SPACE-601。</summary>
    [HttpGet("location/locate")]
    public async Task<IActionResult> Locate([FromQuery] string code)
    {
        var r = await _svc.LocateAsync(code);
        if (r == null) return BadRequest(new { code = 400, message = "E-SPACE-601" });
        return Ok2(r);
    }

    /// <summary>按编码前缀模糊搜索（可选限本楼层）。</summary>
    [HttpGet("location/search")]
    public async Task<IActionResult> Search([FromQuery] string prefix, [FromQuery] Guid? floorId) =>
        Ok2(await _svc.SearchAsync(prefix, floorId));

    /// <summary>库位详情（含变长层级路径）。</summary>
    [HttpGet("location/{id:guid}/detail")]
    public async Task<IActionResult> Detail(Guid id)
    {
        var r = await _svc.DetailAsync(id);
        if (r == null) return BadRequest(new { code = 400, message = "E-SPACE-004" });
        return Ok2(r);
    }
}
