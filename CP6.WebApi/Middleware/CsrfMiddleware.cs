using CP6.Core.Services.Sys;
using CP6.WebApi.Localization;
using Microsoft.Extensions.Options;

namespace CP6.WebApi.Middleware;

/// <summary>
/// CSRF 双提交校验（S 类认证加固 T6）。Cookie 化后浏览器自动带 cp6_at，需防跨站伪造写请求：
/// 非安全方法须同时带 cp6_csrf cookie 与 X-CSRF-Token 头且二者相等（攻击者跨站无法读非 httpOnly
/// cookie 也无法设自定义头）。开关 Security:Csrf:Enabled（默认 true；开发/QA 在 T9 前端注入头前置 false）。
/// 登录端点豁免（登录时尚无 csrf cookie）；refresh 不豁免（轮换是高价值写操作）。
/// </summary>
public class CsrfMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _enabled;
    private static readonly string[] UnsafeMethods = { "POST", "PUT", "PATCH", "DELETE" };

    public CsrfMiddleware(RequestDelegate next, IOptions<SecurityOptions> opt)
    {
        _next = next;
        _enabled = opt.Value.Csrf.Enabled;
    }

    public async Task Invoke(HttpContext ctx)
    {
        if (_enabled)
        {
            var path = ctx.Request.Path.Value ?? "";
            if (!IsExempt(path) && UnsafeMethods.Contains(ctx.Request.Method.ToUpperInvariant()))
            {
                var cookie = ctx.Request.Cookies[AuthCookieWriter.CsrfCookie];
                var header = ctx.Request.Headers["X-CSRF-Token"].ToString();
                if (string.IsNullOrEmpty(cookie) || cookie != header)
                    throw new BizException("E-SEC-010", 403);   // 403 Forbidden：CSRF 校验失败（spec §5.3）
            }
        }
        await _next(ctx);
    }

    /// <summary>CSRF 豁免路径（段边界匹配，杜绝同前缀误豁免）：
    /// ① 登录端点（登录时尚无 csrf cookie，杜绝 /api/auth/login-xxx 这类同前缀端点被静默豁免）；
    /// ② SignalR hub 路径（negotiate 是 POST 但不改业务状态）——豁免安全论据：
    ///    (a) 现有 4 个 hub(notify/mes/wms/space)均无状态变更方法，仅 Subscribe/Unsubscribe 组操作
    ///        (Groups.Add/RemoveToGroupAsync)，即便被跨站触发也无业务副作用；
    ///    (b) CORS 显式 allowlist（Program.cs WithOrigins + AllowCredentials，无通配源）挡跨站 negotiate。
    ///    ⚠ 前瞻警示：未来若给任一 hub 加可 invoke 的状态变更方法，须重新评估此豁免（届时应移除 /hubs 整段
    ///      豁免或改为仅豁免 negotiate 端点），否则跨站可经 hub 方法绕过 CSRF。
    /// 票11：否则 /hubs/*/negotiate 被 403 拦，实时通知连不上。/hubs 前缀覆盖 notify/mes/wms/space 全部 hub。
    /// ③ 消息触发器 fire 端点（波③终审 C-1，**形状精确**匹配 /api/oa/flow-triggers/{guid}/fire）——豁免安全论据：
    ///    该端点以自定义头 X-Api-Key 认证（跨站 HTML 表单/img 无法设置自定义头），且调用方不带任何环境
    ///    cookie 凭据（[AllowAnonymous] + 外部系统进程调用），CSRF 攻击模型不成立；反之若不豁免，
    ///    生产 Csrf.Enabled=true 下外部系统（无 cookie）被 403 E-SEC-010 拦死，消息触发功能整体失效。
    ///    ⚠ 严禁放宽为 /api/oa/flow-triggers 前缀豁免：同级管理端点（create/update/enable/reset-key/
    ///    manual-fire）走 cookie 认证，必须留在 CSRF 保护面。</summary>
    internal static bool IsExempt(string path)
        => PathMatches(path, "/api/auth/login")
           || PathMatches(path, "/hubs")
           || IsFlowTriggerFirePath(path);

    /// <summary>形状精确匹配 /api/oa/flow-triggers/{guid}/fire：前缀+字面 /fire 尾+中段须为单一 GUID 段
    /// （Guid.TryParse 与路由约束 {id:guid} 同判据；含 '/' 必然解析失败，杜绝多段穿透）。大小写不敏感与
    /// PathMatches 风格一致。非 GUID id、manual-fire/enable/reset-key 等兄弟端点、fire 后多余段均不命中。</summary>
    internal static bool IsFlowTriggerFirePath(string path)
    {
        const string prefix = "/api/oa/flow-triggers/";
        const string suffix = "/fire";
        if (path.Length <= prefix.Length + suffix.Length) return false;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
        var idSegment = path.Substring(prefix.Length, path.Length - prefix.Length - suffix.Length);
        return Guid.TryParse(idSegment, out _);
    }

    /// <summary>路径段边界匹配：path == prefix 或 path 以 "prefix/" 起头（避免同前缀误匹配）。</summary>
    internal static bool PathMatches(string path, string prefix)
        => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
}
