using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>
/// 表单分类目录 REST（Phase C）。/api/oa/catalog — 分类树（含收藏标注）/ 收藏切换。
/// </summary>
[ApiController]
[Route("api/oa/catalog")]
[Authorize]
public class CatalogController : LocalizedControllerBase
{
    private readonly ICatalogService _catalog;
    private readonly IFavoriteService _favorite;
    private readonly ICurrentPermissionContext _ctx;

    public CatalogController(ICatalogService catalog, IFavoriteService favorite, ICurrentPermissionContext ctx)
    {
        _catalog = catalog;
        _favorite = favorite;
        _ctx = ctx;
    }

    private async Task<Guid> CurrentUserIdAsync() => (await _ctx.GetAsync()).UserId;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    // ── 分类树 + 收藏标注 ──

    [HttpGet("tree")]
    public async Task<IActionResult> Tree()
    {
        var me = await CurrentUserIdAsync();
        return Ok2(await _catalog.CatalogAsync(me));
    }

    // ── 收藏 / 取消收藏 ──

    public record FavoriteReq(string FormKey, bool On);

    [HttpPost("favorite")]
    public async Task<IActionResult> Favorite([FromBody] FavoriteReq r)
    {
        try
        {
            var me = await CurrentUserIdAsync();
            if (r.On)
                await _favorite.AddAsync(me, r.FormKey);
            else
                await _favorite.RemoveAsync(me, r.FormKey);
            return Ok2();
        }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
