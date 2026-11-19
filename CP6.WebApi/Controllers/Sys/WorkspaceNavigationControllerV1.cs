using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace CP6.WebApi.Controllers.Sys;

[ApiController, Authorize, Route("api/dashboard/scoped-v1/navigation")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkspaceNavigationControllerV1(WorkspaceNavigationV1 navigation, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Current(CancellationToken ct)
    {
        try { return Ok(await navigation.ReadAsync(MenuRouteRegistryV1.Paths, ct)); }
        catch (CurrentPermissionUnavailableException) { return AuthorityUnavailable(); }
        catch (OrderPriceCorrectionException error)
        {
            return error.Status is 401 or 403 ? StatusCode(error.Status, new { code = error.Message }) : AuthorityUnavailable();
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            return AuthorityUnavailable();
        }
    }

    [HttpGet("crm")]
    public IActionResult Crm()
    {
        const string featureRef = "CRM.Workspace";
        var value = configuration["WorkspaceNavigation:CrmOrigin"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) || origin.Scheme != Uri.UriSchemeHttps ||
            origin.HostNameType != UriHostNameType.Dns || origin.AbsolutePath != "/" ||
            origin.UserInfo.Length != 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0)
            return Ok(new { featureRef, availability = "NOT_REGISTERED" });

        // 目标路径取自 CRM 已登记的工作区入口。会话与组织由 CRM 自己重新校验。
        return Ok(new { featureRef, availability = "READY", target = new Uri(origin, "/crm").AbsoluteUri, host = origin.Authority });
    }

    private IActionResult AuthorityUnavailable() => StatusCode(503, new { code = "WORKSPACE_AUTHORITY_UNAVAILABLE" });
}
