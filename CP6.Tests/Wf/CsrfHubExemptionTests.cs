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
}
