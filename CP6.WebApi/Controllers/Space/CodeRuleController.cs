using CP6.Core.Auth;
using CP6.Core.Services.Space;
using CP6.Entity.DTOs.Space;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

/// <summary>
/// 编码规则与编码引擎 Web API（ch03）。
/// 路由前缀 /api/space；租户隔离由 TenantMiddleware + CP6Context 全局查询过滤自动施加。
/// 权限约定（波4）：变更端点（POST/PUT/DELETE）贴 [RequirePermission]，GET 只 [Authorize]。
/// 例外：POST code-rule/preview 为只读样例合成（不写库），按只读语义仅 [Authorize] 不贴。
/// </summary>
[ApiController]
[Route("api/space")]
[Authorize]
public class CodeRuleController : ControllerBase
{
    private readonly ICodeEngineService _svc;
    public CodeRuleController(ICodeEngineService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;

    private IActionResult Ok2(object? data = null, string msg = "OK") =>
        Ok(new { code = 0, message = msg, data });

    // ── CodeRule CRUD ──────────────────────────────────────────────────────

    /// <summary>列出全部编码规则（按作用域 + 名称排序）。</summary>
    [HttpGet("code-rule")]
    public async Task<IActionResult> ListRules() =>
        Ok2(await _svc.ListRulesAsync());

    /// <summary>新建编码规则。IsDefault=true 时同作用域其他规则 IsDefault 自动置 false。</summary>
    [HttpPost("code-rule")]
    [RequirePermission("space-code-rule", "add")]
    public async Task<IActionResult> CreateRule([FromBody] CodeRuleDto d)
    {
        return Ok2(new { id = await _svc.CreateRuleAsync(d, CurrentUser) });
    }

    /// <summary>修改编码规则。</summary>
    [HttpPut("code-rule/{id:guid}")]
    [RequirePermission("space-code-rule", "edit")]
    public async Task<IActionResult> UpdateRule(Guid id, [FromBody] CodeRuleDto d)
    {
        await _svc.UpdateRuleAsync(id, d, CurrentUser); return Ok2();
    }

    /// <summary>删除编码规则。</summary>
    [HttpDelete("code-rule/{id:guid}")]
    [RequirePermission("space-code-rule", "delete")]
    public async Task<IActionResult> DeleteRule(Guid id)
    {
        await _svc.DeleteRuleAsync(id); return Ok2();
    }

    /// <summary>实时预览编码样例（ch03 §8）。合成两路（有/无巷道），不写库。</summary>
    [HttpPost("code-rule/preview")]
    public async Task<IActionResult> Preview([FromBody] CodePreviewReq req)
    {
        return Ok2(await _svc.PreviewAsync(req));
    }

    // ── 批量生成 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 批量生成库位编码（ch03 §4.2）。
    /// body: { mode: "fill-empty"|"rebuild", scopeZoneId?: guid }
    /// 返回本次生成的编码列表。
    /// </summary>
    [HttpPost("floor/{id:guid}/generate-codes")]
    [RequirePermission("space-code-rule", "generate")]
    public async Task<IActionResult> GenerateCodes(Guid id, [FromBody] GenerateCodesReq req)
    {
        return Ok2(await _svc.GenerateAsync(id, req.Mode, req.ScopeZoneId));
    }

    // ── 发布前编码预检 ─────────────────────────────────────────────────────

    /// <summary>
    /// 发布前编码预检（ch03 §9.2，04 发布闸门）。
    /// 返回：空码数 / 重复码组 / 规则静态错误 / 未落位数。
    /// </summary>
    [HttpGet("floor/{id:guid}/code-precheck")]
    public async Task<IActionResult> CodePrecheck(Guid id, [FromQuery] Guid? zoneId = null)
    {
        return Ok2(await _svc.PrecheckAsync(id, zoneId));
    }

    // ── 单格生成 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 单格生成（ch03 §10）。对单个库位走同样的 Assemble 路径写回 LocationCode。
    /// </summary>
    [HttpPost("location/{id:guid}/gen-code")]
    [RequirePermission("space-code-rule", "generate")]
    public async Task<IActionResult> GenCode(Guid id)
    {
        return Ok2(new { code = await _svc.GenSingleAsync(id) });
    }
}

/// <summary>批量生成请求体。</summary>
public class GenerateCodesReq
{
    /// <summary>生成模式：fill-empty（仅填空码）/ rebuild（两阶段全量重排）。</summary>
    public string Mode { get; set; } = "fill-empty";

    /// <summary>限定库区（null = 楼层全部库区）。</summary>
    public Guid? ScopeZoneId { get; set; }
}
