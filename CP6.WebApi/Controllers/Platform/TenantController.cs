using CP6.Core.Auth;
using CP6.Core.Services.Platform;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Platform;

/// <summary>
/// 平台租户管理（多租户合规 #5 块①）。仅平台超管放行（<see cref="RequirePlatformAdminAttribute"/> 三道闸）。
/// 列/查/建（建即原子开通首个 admin + 一次性临时密码）/改/停用/重启用。
/// 服务层经 <see cref="System.InvalidOperationException"/> 携 E-SEC 码 → 此处转 <see cref="BizException"/> 本地化。
/// </summary>
[ApiController]
[Route("api/platform/tenant")]
[Authorize]
[RequirePlatformAdmin]
public class TenantController : ControllerBase
{
    private readonly ITenantAdminService _svc;
    public TenantController(ITenantAdminService svc) => _svc = svc;

    /// <summary>分页查询租户（keyword 命中编码/名称，enable 过滤启用态）。</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? keyword,
        [FromQuery] bool? enable,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _svc.ListAsync(keyword, enable, page, pageSize);
        return Ok(new { rows = result.Rows, total = result.Total });
    }

    /// <summary>租户详情。不存在 → 404。</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var detail = await _svc.GetAsync(id);
        return detail == null ? NotFound() : Ok(detail);
    }

    /// <summary>建租户（原子开通首个 admin）。返回一次性临时密码（TempPassword 仅本次可见）。</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest req)
    {
        try
        {
            var result = await _svc.CreateAsync(req.Code, req.Name, req.Expire, req.Remark, req.AdminUserName);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);   // E-SEC-033（编码冲突）
        }
    }

    /// <summary>改租户（名称/到期/备注）。不存在 → E-SEC-032。</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTenantRequest req)
    {
        try
        {
            await _svc.UpdateAsync(id, req.Name, req.Expire, req.Remark, req.TimeZoneId);
            return Ok(new { code = 0 });
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);   // E-SEC-032（不存在）/ E-WF-028（时区不可解析）
        }
    }

    /// <summary>停用租户（Enable=false → 该租户用户登录被拒）。</summary>
    [HttpPost("{id:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid id)
    {
        try
        {
            await _svc.SuspendAsync(id);
            return Ok(new { code = 0 });
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);   // E-SEC-032
        }
    }

    /// <summary>重启用租户（Enable=true）。</summary>
    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        try
        {
            await _svc.ReactivateAsync(id);
            return Ok(new { code = 0 });
        }
        catch (InvalidOperationException ex)
        {
            throw new BizException(ex.Message);   // E-SEC-032
        }
    }

    public record CreateTenantRequest(string Code, string Name, DateTime? Expire, string? Remark, string AdminUserName);
    public record UpdateTenantRequest(string Name, DateTime? Expire, string? Remark, string? TimeZoneId = null);
}
