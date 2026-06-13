using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Core.Auth;

/// <summary>
/// 字段权限序列化掩码 —— PUB 章04 §4。贴在返回 DTO/列表的动作上，
/// 序列化前把当前用户对该资源的隐藏字段(Access=3)反射置空。
/// <para>注意：直接返回 DTO/集合时生效；若返回 {code,message,data} 信封（匿名只读类型），
/// 掩码无法写入匿名属性——这类端点应直接返回 data 或在 service 内手动调用 MaskHiddenAsync。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class FieldMaskAttribute : Attribute, IAsyncResultFilter
{
    private readonly string _resource;
    public FieldMaskAttribute(string resource) => _resource = resource;

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult obj && obj.Value != null)
        {
            var svc = context.HttpContext.RequestServices.GetService<IFieldPermService>();
            if (svc != null) await svc.MaskHiddenAsync(obj.Value, _resource);
        }
        await next();
    }
}
