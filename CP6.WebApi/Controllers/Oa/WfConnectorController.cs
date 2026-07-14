using CP6.Core.Auth;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>连接器管理页后端（WFS infra ④，D-T2；spec §5）。当前租户 <see cref="Wf_Connector"/> CRUD：
/// 列表（掩码，无明文凭证）/ 取单 / 新建 / 编辑 / 启停。凭证写路径即写即加密（DataProtection，
/// purpose="Wfs.Connector.Auth"），读路径恒掩码（<see cref="WfConnectorView.HasAuth"/> 指示，
/// <see cref="WfConnectorView.AuthJson"/> 恒 null）；编辑留空 AuthJson=保留原密文（D-T1 掩码读契约 §2）。
///
/// 权限点（menuKey <c>oa-flow-admin</c>，MenuAction Connector.View/Edit；沿 oa-flow-admin 家族，
/// 波③映射②口径 = 连接器 tab 挂在流程管理页）：Edit=新建/编辑/启停（写），View=列表/取单（只读 GET，
/// 循 OA 兄弟控制器约定不贴细粒度键，读授权=登录态+租户隔离，NoReadOnlyGetAction 守卫禁 GET 贴键）。
/// ★菜单/权限/i18n **种子落库归 F-T1 收口**（本任务仅贴 [RequirePermission] 贴点；键面清单交接 F-T1，
///   与 A-T4 年历页、波③ F-T2、波④ B-T2 既定分工先例一致）。种子落地前生产端写端点 fail-closed 403 = 既定中间态。
///
/// E-WF-028（TimeoutSec ≥ 租约 → 拒绝，D-T1 服务层校验）抛 InvalidOperationException → 本控制器转 400 + 码。
/// 计入 <see cref="OawfPermissionAttributeTests"/> fail-closed 守卫扫描面（计数 18→19，Edit 端点×3 贴键）。</summary>
[ApiController]
[Route("api/oa/wf-connector")]
[Authorize]
public class WfConnectorController : LocalizedControllerBase
{
    private readonly IWfConnectorService _svc;

    public WfConnectorController(IWfConnectorService svc) { _svc = svc; }

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    /// <summary>列当前租户全部连接器（掩码；无明文凭证）。</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok2(await _svc.ListAsync(ct));

    /// <summary>取单个连接器（掩码）。</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var item = await _svc.GetAsync(id, ct);
        return item is null ? NotFound(new { code = 404, message = "E-WF-018" }) : Ok2(item);
    }

    /// <summary>新建连接器。AuthJson 空→无认证；非空→即写即加密。E-WF-028→400。</summary>
    [HttpPost]
    [RequirePermission("oa-flow-admin", "Connector.Edit")]
    public async Task<IActionResult> Create([FromBody] WfConnectorSaveReq req, CancellationToken ct)
    {
        try { return Ok2(new { id = await _svc.CreateAsync(req, ct) }); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>编辑连接器。AuthJson 空/null→保留原密文（掩码读契约：编辑元数据不清空凭证）；非空→重加密覆盖。E-WF-028→400。</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("oa-flow-admin", "Connector.Edit")]
    public async Task<IActionResult> Update(Guid id, [FromBody] WfConnectorSaveReq req, CancellationToken ct)
    {
        try { await _svc.UpdateAsync(id, req, ct); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    /// <summary>启停切换。</summary>
    [HttpPost("{id:guid}/enabled")]
    [RequirePermission("oa-flow-admin", "Connector.Edit")]
    public async Task<IActionResult> SetEnabled(Guid id, [FromBody] EnableReq r, CancellationToken ct)
    {
        try { await _svc.SetEnabledAsync(id, r.Enabled, ct); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    public record EnableReq(bool Enabled);
}
