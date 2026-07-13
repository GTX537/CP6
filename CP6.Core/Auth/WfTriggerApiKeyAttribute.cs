using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Core.Auth;

/// <summary>message 触发器外呼闸（spec §3.4）：X-Api-Key SHA-256 常量时间校验 + Idempotency-Key 必填
/// + 404 不区分「不存在/停用」。验过 key 后按触发器租户切 ITenantContext（AllowAnonymous 无 JWT 租户）。
/// 特性不能构造注入，用 RequestServices 服务定位（仿 RequirePlatformAdminAttribute）。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WfTriggerApiKeyAttribute : Attribute, IAsyncAuthorizationFilter
{
    public const string ItemKey = "WfTrigger.Fire.Trigger";
    public const int MaxIdempotencyKeyLength = 200;   // 进唯一索引键列（映射表④）

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetService<CP6Context>();
        if (db == null)
        {
            context.Result = new ObjectResult(new { code = 500, message = "服务未注册" }) { StatusCode = 500 };
            return;
        }

        static IActionResult NotFound404() => new NotFoundObjectResult(new { code = 404, message = "trigger not found" });

        if (!Guid.TryParse(context.RouteData.Values["id"]?.ToString(), out var id))
        {
            context.Result = NotFound404();
            return;
        }

        // 跨租户按 Id 定位（key 绑定单触发器单租户，IgnoreQueryFilters 仿 RefreshTokenService 先例）
        var trigger = await db.Wf_FlowTriggers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id && t.TriggerType == WfTriggerType.Message);
        if (trigger == null || !trigger.Enabled)
        {
            context.Result = NotFound404();   // 停用与不存在不区分（spec §3.4）
            return;
        }

        var rawKey = http.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(rawKey) || !WfApiKeyHelper.Verify(rawKey, trigger.ApiKeyHash))
        {
            context.Result = new ObjectResult(new { code = 401, message = "invalid api key" }) { StatusCode = 401 };
            return;
        }

        var idemKey = http.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idemKey) || idemKey.Length > MaxIdempotencyKeyLength)
        {
            context.Result = new BadRequestObjectResult(
                new { code = 400, message = $"Idempotency-Key header required (<= {MaxIdempotencyKeyLength} chars)" });
            return;
        }

        // 租户切换：同 scope 的 ITenantContext setter（对齐 TenantScopeRunner 现状口径，spec §6）
        http.RequestServices.GetRequiredService<ITenantContext>().CurrentTenantId = trigger.TenantId;
        http.Items[ItemKey] = trigger;
    }
}
