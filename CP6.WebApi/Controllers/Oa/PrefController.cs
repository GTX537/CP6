using CP6.Core.Auth;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>
/// 用户偏好设置 REST（Phase C）。/api/oa/pref — 读取 / 保存 JSON 格式偏好（主题、列宽、排序等）。
/// </summary>
[ApiController]
[Route("api/oa/pref")]
[Authorize]
public class PrefController : LocalizedControllerBase
{
    private readonly IPrefService _pref;
    private readonly ICurrentPermissionContext _ctx;

    public PrefController(IPrefService pref, ICurrentPermissionContext ctx)
    {
        _pref = pref;
        _ctx = ctx;
    }

    private async Task<Guid> CurrentUserIdAsync() => (await _ctx.GetAsync()).UserId;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    // ── 读取偏好 ──

    [HttpGet("get")]
    public async Task<IActionResult> Get()
    {
        var me = await CurrentUserIdAsync();
        var prefsJson = await _pref.GetAsync(me);
        return Ok2(new { prefsJson });
    }

    // ── 保存偏好 ──

    public record SavePrefReq(string PrefsJson);

    [HttpPost("save")]
    [RequirePermission("oa-settings", "edit")]
    public async Task<IActionResult> Save([FromBody] SavePrefReq r)
    {
        try
        {
            var me = await CurrentUserIdAsync();
            await _pref.SaveAsync(me, r.PrefsJson);
            return Ok2();
        }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
