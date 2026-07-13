using CP6.WebApi.Middleware;
using Xunit;

namespace CP6.Tests.Wf;

/// <summary>
/// 票11：SignalR hub negotiate 是 POST，默认不带 X-CSRF-Token 头，被 CsrfMiddleware 双提交校验 403 拦截。
/// B1：CsrfMiddleware 豁免 /hubs 前缀（段边界匹配，覆盖 notify/mes/wms/space 全部 hub 的 negotiate），
/// 不误豁免 /hubsxxx，业务写请求仍受 CSRF 约束。
/// </summary>
public class CsrfHubExemptionTests
{
    [Theory]
    [InlineData("/hubs/notify", true)]
    [InlineData("/hubs/notify/negotiate", true)]
    [InlineData("/hubs/mes/negotiate", true)]
    [InlineData("/hubs/wms", true)]
    [InlineData("/hubs/space/negotiate", true)]   // 波4 SpaceHub：/hubs 前缀一并覆盖
    [InlineData("/api/auth/login", true)]          // 既有豁免仍在
    [InlineData("/api/oa/designer/save", false)]   // 业务写请求仍受 CSRF 约束
    [InlineData("/hubsxxx/notify", false)]         // 段边界：非 /hubs/ 前缀不豁免
    public void HubPaths_AreExempt(string path, bool expectExempt)
        => Assert.Equal(expectExempt, CsrfMiddleware.IsExempt(path));

    /// <summary>
    /// 波③终审 C-1：外部系统调 POST /api/oa/flow-triggers/{guid}/fire 仅带 X-Api-Key +
    /// Idempotency-Key 头、无任何 cookie —— 生产 Csrf.Enabled=true 下被 403 E-SEC-010 拦死。
    /// 豁免须**形状精确**（GUID 段 + 字面 /fire 尾），杜绝把 /api/oa/flow-triggers 前缀整段豁免
    /// ——create/update/enable/reset-key/manual-fire 等管理端点走 cookie 认证，必须留在 CSRF 保护面。
    /// </summary>
    [Theory]
    [InlineData("/api/oa/flow-triggers/3fa85f64-5717-4562-b3fc-2c963f66afa6/fire", true)]   // 消息触发 fire：形状精确豁免
    [InlineData("/API/OA/FLOW-TRIGGERS/3FA85F64-5717-4562-B3FC-2C963F66AFA6/FIRE", true)]   // 大小写不敏感（与中间件既有风格一致）
    [InlineData("/api/oa/flow-triggers", false)]                                             // 裸集合端点（create）不豁免
    [InlineData("/api/oa/flow-triggers/3fa85f64-5717-4562-b3fc-2c963f66afa6", false)]        // update 不豁免
    [InlineData("/api/oa/flow-triggers/3fa85f64-5717-4562-b3fc-2c963f66afa6/manual-fire", false)] // 管理侧手动触发不豁免
    [InlineData("/api/oa/flow-triggers/3fa85f64-5717-4562-b3fc-2c963f66afa6/enable", false)] // enable 不豁免
    [InlineData("/api/oa/flow-triggers/3fa85f64-5717-4562-b3fc-2c963f66afa6/reset-key", false)] // reset-key 不豁免
    [InlineData("/api/oa/flow-triggers/abc/fire", false)]                                    // 非 GUID id 不豁免
    [InlineData("/api/oa/flow-triggers/3fa85f64-5717-4562-b3fc-2c963f66afa6/fire/x", false)] // fire 后多余段不豁免
    public void FlowTriggerFirePath_ShapeExactExemption(string path, bool expectExempt)
        => Assert.Equal(expectExempt, CsrfMiddleware.IsExempt(path));
}
