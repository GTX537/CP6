using CP6.Core.Auth;
using CP6.Core.Services.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

/// <summary>
/// 场景差量保存 + 导入导出 + D7 绑码 Web API（ch01 §G-1/G-3/I-1）。
/// 注意：GET /floor/{id}/scene 已由 SpaceMasterController 实现；
/// 本控制器只加 POST 差量保存（不冲突）+ export/import/bind-codes。
/// 权限约定（波4）：变更端点（POST/PUT/DELETE）贴 [RequirePermission]，GET（export）只 [Authorize]。
/// 场景全部变更归楼层编辑单一动作 space-floor:edit（映射表裁决）。
/// </summary>
[ApiController]
[Route("api/space")]
[Authorize]
public class SceneController : ControllerBase
{
    private readonly ISceneService _scene;
    private readonly ISceneIoService _io;

    public SceneController(ISceneService scene, ISceneIoService io)
    {
        _scene = scene;
        _io    = io;
    }

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null, string msg = "OK") =>
        Ok(new { code = 0, message = msg, data });

    // ── G-1 差量保存 ─────────────────────────────────────────────────────

    /// <summary>整层差量保存（upsert zones/aisles/racks/markers/locations + deletes）</summary>
    [HttpPost("floor/{id:guid}/scene")]
    [RequirePermission("space-floor", "edit")]
    public async Task<IActionResult> SaveScene(Guid id, [FromBody] SceneSaveDto dto)
    {
        return Ok2(new { idMap = await _scene.SaveSceneAsync(id, dto, CurrentUser) });
    }

    // ── G-3 导入导出 ──────────────────────────────────────────────────────

    /// <summary>导出楼层几何（不含敏感字段）</summary>
    [HttpGet("floor/{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id)
    {
        return Ok2(await _io.ExportAsync(id));
    }

    /// <summary>导入场景到指定站点（新 GUID 映射，库位按货架参数全枚举重建）</summary>
    [HttpPost("site/{id:guid}/import")]
    [RequirePermission("space-floor", "edit")]
    public async Task<IActionResult> Import(Guid id, [FromBody] SceneExportDto dto)
    {
        return Ok2(new { floorId = await _io.ImportAsync(id, dto, CurrentUser) });
    }

    // ── I-1 D7 绑码 ──────────────────────────────────────────────────────

    /// <summary>D7 反向建模绑码：校验 Status==1 &amp;&amp; !Placed &amp;&amp; CodeOrigin==2，回填几何坐标，不动 Code/Id/Status/Version</summary>
    [HttpPost("rack/{id:guid}/bind-codes")]
    [RequirePermission("space-floor", "edit")]
    public async Task<IActionResult> BindCodes(Guid id, [FromBody] BindCodesDto dto)
    {
        var pairs = dto.Pairs.Select(p => (p.LocationId, p.Col, p.Level, p.Depth));
        await _scene.BindCodesAsync(id, pairs, CurrentUser);
        return Ok2();
    }
}
