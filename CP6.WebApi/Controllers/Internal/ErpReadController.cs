using System.Data.Common;
using System.Security.Claims;
using CP6.Core.EFDbContext;
using CP6.Core.Services.ErpIntegration;
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
[Route("/internal/erp/v1")]
public sealed class ErpReadController(CP6Context db, CrmOidcOptions oidc, ICrmOidcServiceDirectory directory,
    ErpReadService reader, ErpIntegrationRuntime? runtime = null) : ControllerBase
{
    public const string Scheme = "C03.Services";
    public const string TenantHeader = "tenantid";
    public const string CorrelationHeader = "correlationid";

    [HttpGet("business-partners/{key}")]
    public Task<IActionResult> BusinessPartner(string key) => ReadAsync(key, async tenant =>
    {
        var value = await reader.BusinessPartnerAsync(tenant, key, HttpContext.RequestAborted);
        return value is null ? NotFound() : Ok(value);
    });

    [HttpGet("quotations/{key}")]
    public Task<IActionResult> Quotation(string key) => ReadAsync(key, async tenant =>
    {
        var value = await reader.QuotationAsync(tenant, key, HttpContext.RequestAborted);
        return value is null ? NotFound() : Ok(value);
    });

    [HttpGet("orders/{key}")]
    public Task<IActionResult> Order(string key) => ReadAsync(key, async tenant =>
    {
        var value = await reader.OrderAsync(tenant, key, HttpContext.RequestAborted);
        return value is null ? NotFound() : Ok(value);
    });

    private async Task<IActionResult> ReadAsync(string key, Func<Guid, Task<IActionResult>> read)
    {
        Response.Headers.CacheControl = "no-store";
        if (runtime is null) return NotFound();
        var authentication = await HttpContext.AuthenticateAsync(Scheme);
        if (!authentication.Succeeded || authentication.Principal is null) return Challenge(Scheme);
        var principal = authentication.Principal;
        var clientId = Single(principal, "client_id");
        if (Single(principal, "iss") != oidc.Issuer || Single(principal, "sub") != "service:" + clientId ||
            Single(principal, "scope") != "cp6.services" ||
            !Guid.TryParseExact(Single(principal, "tenant_id"), "D", out var tenant) || tenant == Guid.Empty ||
            !Guid.TryParseExact(Single(principal, "jti"), "D", out var jti) || jti == Guid.Empty ||
            Single(principal, "tenant_id") != tenant.ToString("D") || Single(principal, "jti") != jti.ToString("D") ||
            clientId is null || !runtime.Options.ReaderClientIds.Contains(clientId, StringComparer.Ordinal) ||
            !runtime.Options.Tenants.ContainsKey(tenant) || !oidc.ServiceClients.Any(c => c.ClientId == clientId &&
                c.TenantId == tenant && c.Enabled && c.AllowedScopes.Contains("cp6.services", StringComparer.Ordinal)))
            return Forbid(Scheme);
        var correlation = Header(CorrelationHeader);
        if (Header(TenantHeader) != tenant.ToString("D") || correlation is not { Length: > 0 and <= 128 } ||
            !correlation.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':') ||
            Request.Query.Count != 0 || key.Length is < 1 or > 20 || key.Any(char.IsControl))
            return BadRequest(new { code = "C03_REQUEST_METADATA_INVALID" });
        Response.Headers[CorrelationHeader] = correlation;
        try
        {
            var now = runtime.Clock.GetUtcNow();
            if (!await directory.IsServiceTenantActiveAsync(tenant, now.UtcDateTime, HttpContext.RequestAborted)) return Forbid(Scheme);
            var tokenId = jti.ToString("D");
            if (!await db.CrmServiceTokenRecords.AsNoTracking().AnyAsync(x => x.Issuer == oidc.Issuer && x.Jti == tokenId &&
                    x.TenantId == tenant && x.ClientId == clientId && x.RevokedAtUtc == null && x.ExpiresAtUtc > now,
                    HttpContext.RequestAborted)) return Forbid(Scheme);
            return await read(tenant);
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException or TimeoutException)
        { return StatusCode(503, new { code = "C03_ERP_UNAVAILABLE" }); }
    }

    private string? Header(string name) => Request.Headers.TryGetValue(name, out var values) && values.Count == 1 ? values[0] : null;
    private static string? Single(ClaimsPrincipal principal, string type)
    {
        var values = principal.FindAll(type).ToArray(); return values.Length == 1 ? values[0].Value : null;
    }
}
