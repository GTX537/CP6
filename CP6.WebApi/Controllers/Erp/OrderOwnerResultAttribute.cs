using CP6.Core.Services.Erp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CP6.WebApi.Controllers.Erp;

/// <summary>Safe Owner errors, preserving denial/unavailability through the actual MVC pipeline.</summary>
public sealed class OrderOwnerResultAttribute : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        var result = await next();
        if (result.Exception is OrderPriceCorrectionException error)
        {
            result.Result = new ObjectResult(new { code = error.Status, message = error.Message }) { StatusCode = error.Status };
            result.ExceptionHandled = true;
        }
    }
}
