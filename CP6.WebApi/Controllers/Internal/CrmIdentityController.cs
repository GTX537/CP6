using System.Data.Common;
using System.Security.Claims;
using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Internal;

[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("/internal/crm/identity")]
public sealed class CrmIdentityController(CP6Context db, CrmOidcOptions oidc,
    ICrmOidcServiceDirectory directory, CrmIdentityRuntime? runtime = null, IdentitySnapshotReader? reader = null) : ControllerBase
{
    public const string Scheme = "C02.Services";

    [HttpGet("versions")]
    public async Task<IActionResult> Versions([FromQuery] string? cursor = null, [FromQuery] int pageSize = 200)
        => await ReadAsync(async tenant =>
        {
            if (Request.Query.Any(p => p.Value.Count != 1 || p.Key is not ("cursor" or "pageSize")))
                return BadRequest(new { code = "C02_INVALID_REQUEST" });
            return Ok(await reader!.ReadVersionsAsync(tenant, cursor, pageSize, HttpContext.RequestAborted));
        });

    [HttpGet("snapshots/{aggregateId}")]
    public async Task<IActionResult> Snapshot(string aggregateId) => await ReadAsync(async tenant =>
    {
        if (Request.Query.Count != 0) return BadRequest(new { code = "C02_INVALID_REQUEST" });
        var snapshot = await reader!.ReadSnapshotAsync(tenant, aggregateId, HttpContext.RequestAborted);
        return snapshot is null ? NotFound() : Ok(snapshot);
    });

    private async Task<IActionResult> ReadAsync(Func<Guid, Task<IActionResult>> read)
    {
        Response.Headers.CacheControl = "no-store";
        if (runtime is null || reader is null) return NotFound();
        var authentication = await HttpContext.AuthenticateAsync(Scheme);
        if (!authentication.Succeeded || authentication.Principal is null) return Challenge(Scheme);
        var principal = authentication.Principal;
        var clientId = Single(principal, "client_id");
        if (Single(principal, "iss") != oidc.Issuer || Single(principal, "sub") != "service:" + clientId ||
            Single(principal, "scope") != "cp6.services" ||
            !Guid.TryParseExact(Single(principal, "tenant_id"), "D", out var tenant) || tenant == Guid.Empty ||
            !Guid.TryParseExact(Single(principal, "jti"), "D", out var jti) || jti == Guid.Empty ||
            Single(principal, "tenant_id") != tenant.ToString("D") || Single(principal, "jti") != jti.ToString("D") ||
            clientId is null || !runtime.Options.ProjectionReaderClientIds.Contains(clientId, StringComparer.Ordinal) ||
            !runtime.Options.Tenants.ContainsKey(tenant) || !oidc.ServiceClients.Any(c =>
                c.ClientId == clientId && c.TenantId == tenant && c.Enabled && c.AllowedScopes.Contains("cp6.services", StringComparer.Ordinal)))
            return Forbid(Scheme);
        try
        {
            var now = runtime.Clock.GetUtcNow();
            if (!await directory.IsServiceTenantActiveAsync(tenant, now.UtcDateTime, HttpContext.RequestAborted)) return Forbid(Scheme);
            // Old unrecorded service tokens cannot silently gain the newly enabled reader privilege.
            var tokenId = jti.ToString("D");
            var skewBoundary = now.AddSeconds(-60);
            if (!await db.CrmServiceTokenRecords.AsNoTracking().AnyAsync(x => x.Issuer == oidc.Issuer && x.Jti == tokenId &&
                x.TenantId == tenant && x.ClientId == clientId && x.RevokedAtUtc == null && x.ExpiresAtUtc > skewBoundary, HttpContext.RequestAborted))
                return Forbid(Scheme);
            return await read(tenant);
        }
        catch (IdentityReadException ex)
        {
            return StatusCode(ex.Message switch
            {
                "C02_BOOTSTRAP_NOT_READY" => 503,
                "C02_SNAPSHOT_BOUNDARY_CHANGED" => 409,
                _ => 400
            }, new { code = ex.Message });
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException or TimeoutException)
        { return StatusCode(503, new { code = "C02_IDENTITY_STORE_UNAVAILABLE" }); }
    }

    private static string? Single(ClaimsPrincipal principal, string type)
    {
        var claims = principal.FindAll(type).ToArray();
        return claims.Length == 1 ? claims[0].Value : null;
    }
}
