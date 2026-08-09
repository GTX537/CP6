using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

[ApiController]
[Route("api/v2/admin/wms-role-scopes")]
[Authorize]
public sealed class WmsRoleScopesController(IWmsRoleScopeService service)
    : ControllerBase
{
    [HttpGet("{roleId:int}")]
    [RequirePermission("pub-data-scope", "query")]
    public async Task<ActionResult<IReadOnlyList<WmsRoleScopeDto>>> Get(
        int roleId,
        CancellationToken ct)
    {
        try { return Ok(await service.GetAsync(roleId, ct)); }
        catch (KeyNotFoundException ex)
        {
            return NotFound(Error(ex.Message));
        }
    }

    [HttpPut("{roleId:int}")]
    [RequirePermission("pub-data-scope", "edit")]
    public async Task<ActionResult<IReadOnlyList<WmsRoleScopeDto>>> Replace(
        int roleId,
        ReplaceWmsRoleScopesRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await service.ReplaceAsync(
                roleId,
                request,
                User.Identity?.Name,
                ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(Error(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Error(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(Error(ex.Message));
        }
    }

    private static object Error(string code) => new { code, message = code };
}
