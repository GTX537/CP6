using System.Text.Json;
using CP6.WebApi.Localization;
using Microsoft.Extensions.Localization;

namespace CP6.WebApi.Middleware;

/// <summary>
/// 全局捕获 <see cref="BizException"/> → 用 <see cref="IStringLocalizer"/> 把错误码解析为
/// 当前请求 culture 的本地化消息，返回项目统一信封 <c>{ code, message, data }</c>。i18n 优化 P1。
///
/// 注册位置必须在 <c>UseRequestLocalization</c> 之内（其后），否则异常上抛时
/// RequestLocalization 已还原 culture，解析会落到默认语言。
/// </summary>
public class BizExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public BizExceptionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IStringLocalizer localizer)
    {
        try
        {
            await _next(context);
        }
        catch (BizException ex)
        {
            var localized = ex.Args.Length > 0 ? localizer[ex.Code, ex.Args] : localizer[ex.Code];
            context.Response.StatusCode = ex.HttpStatus;
            context.Response.ContentType = "application/json; charset=utf-8";
            var payload = JsonSerializer.Serialize(new
            {
                code = ex.HttpStatus,
                message = localized.Value,   // 已是当前 culture 译文；未命中则回退码本身
                data = (object?)null
            });
            await context.Response.WriteAsync(payload);
        }
    }
}
