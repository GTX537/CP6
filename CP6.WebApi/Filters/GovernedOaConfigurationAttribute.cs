using CP6.Core.Services.Oa;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CP6.WebApi.Filters;

/// <summary>接管后的配置由版本控制入口修改。历史查询沿用原读路径。</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GovernedOaConfigurationAttribute(string kind) : Attribute,IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context,ActionExecutionDelegate next)
    {
        if(!HttpMethods.IsGet(context.HttpContext.Request.Method))
        {
            Guid? id=context.ActionArguments.TryGetValue("id",out var value) && value is Guid key ? key : null;
            string? name=context.ActionArguments.Values.OfType<WfConnectorSaveReq>().SingleOrDefault()?.Name;
            if((id.HasValue || name!=null) && await context.HttpContext.RequestServices.GetRequiredService<ConnectorControlRuntimeV1>()
                .GovernsLegacy(kind,id,name,context.HttpContext.RequestAborted))
            {
                context.Result=new ObjectResult(new {code="TRUSTED_CONN_V1_REQUIRED",message="该配置已采用版本控制。"}) {StatusCode=410};
                return;
            }
        }
        await next();
    }
}
