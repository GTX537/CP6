using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
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

    [HttpPost("/internal/main/v1/bp/mapping-snapshots")]
    public Task<IActionResult> MappingSnapshot([FromBody] JsonElement body, CancellationToken ct)
        => ReadMappingOwner((owner, client) => owner.QueryMappingSnapshot(client, body, ct));

    [HttpPost("/internal/main/v2/account-mapping-decisions/query")]
    public Task<IActionResult> MappingDecision([FromBody] JsonElement body, CancellationToken ct)
        => ReadMappingOwner((owner, client) => owner.QueryMappingDecision(client, body, ct));

    [HttpGet("/internal/main/v2/account-mapping-decisions/evidence/{evidenceId}")]
    public Task<IActionResult> MappingDecisionEvidence(string evidenceId, [FromQuery] string? legalEntityKey,
        [FromQuery] string? accountId, CancellationToken ct) =>
        ReadAuthorizedAsync(Request.Query.Count == 2 && Request.Query["legalEntityKey"].Count == 1 && Request.Query["accountId"].Count == 1,
            async (tenant, client, _) =>
            {
                var workspace = MappingOwner(tenant);
                try
                {
                    var bytes = await workspace.ReadMappingDecisionEvidence(client, legalEntityKey, accountId, evidenceId, ct);
                    return bytes is null ? NotFound() : File(bytes, "application/json");
                }
                catch (OrderPriceCorrectionException error) { return StatusCode(error.Status, new { code = error.Message }); }
            }, allowQuery: true);

    private BusinessPartnerWorkspaceV1 MappingOwner(Guid tenant)
    {
        var services = HttpContext.RequestServices;
        services.GetRequiredService<ITenantContext>().CurrentTenantId = tenant;
        return services.GetRequiredService<BusinessPartnerWorkspaceV1>();
    }

    private Task<IActionResult> ReadMappingOwner(Func<BusinessPartnerWorkspaceV1, string, Task<BusinessPartnerWorkspaceV1.HttpResult?>> read)
        => ReadAuthorizedAsync(true, async (tenant, client, _) =>
        {
            try
            {
                var result = await read(MappingOwner(tenant), client);
                return result is null ? NotFound() : new JsonResult(result.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch (OrderPriceCorrectionException error) { return StatusCode(error.Status, new { code = error.Message }); }
        });

    [HttpGet("business-partner-request-proofs/{account:guid}/{requestId:guid}/{requestVersion:int}")]
    public Task<IActionResult> BusinessPartnerOriginal(Guid account, Guid requestId, int requestVersion)
        => ReadAuthorizedAsync(true, async (tenant, client, _) =>
        {
            var original = Header("X-C03-Original-Message");
            if (original is not { Length: > 0 and <= 128 } || original.Any(char.IsControl) ||
                account == Guid.Empty || requestId == Guid.Empty || requestVersion < 1)
                return BadRequest(new { code = "C03_REQUEST_METADATA_INVALID" });
            var proofs = HttpContext.RequestServices.GetRequiredService<ErpBusinessPartnerOriginalReader>();
            var proof = await proofs.ReadAsync(tenant, client, account, requestId, requestVersion, original,
                HttpContext.RequestAborted);
            return proof is null ? NotFound() : Ok(proof);
        });

    [HttpGet("business-partner-result-proofs/{account:guid}/{requestId:guid}/{requestVersion:int}/{eventId}/{resultVersion:int}")]
    public Task<IActionResult> BusinessPartnerResult(Guid account, Guid requestId, int requestVersion, string eventId, int resultVersion)
        => ReadAuthorizedAsync(true, async (tenant, client, principal) =>
        {
            var original = Header("X-C03-Original-Message");
            var digest = Header("X-C03-Result-Sha256");
            if (original is not { Length: > 0 and <= 128 } || original.Any(char.IsControl) ||
                eventId.Length is < 1 or > 128 || !char.IsAsciiLetterOrDigit(eventId[0]) ||
                !eventId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-') ||
                account == Guid.Empty || requestId == Guid.Empty || requestVersion < 1 || resultVersion < 1 ||
                digest is not { Length: 64 } || !digest.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                return BadRequest(new { code = "C03_REQUEST_METADATA_INVALID" });
            if (!long.TryParse(Single(principal, "iat"), System.Globalization.CultureInfo.InvariantCulture, out var issued) ||
                !long.TryParse(Single(principal, "exp"), System.Globalization.CultureInfo.InvariantCulture, out var expires))
                return Forbid(Scheme);
            var resolver = HttpContext.RequestServices.GetService<ErpBusinessPartnerResultReader>();
            if (resolver is null) return NotFound();
            var proof = await resolver.ReadAsync(tenant, client, Single(principal, "jti")!, issued, expires,
                account, requestId, requestVersion, original, eventId, resultVersion, digest, HttpContext.RequestAborted);
            return proof is null ? NotFound() : Ok(proof);
        });

    private Task<IActionResult> ReadAsync(string key, Func<Guid, Task<IActionResult>> read)
        => ReadAuthorizedAsync(key.Length is > 0 and <= 20 && !key.Any(char.IsControl), (tenant, _, _) => read(tenant));

    private async Task<IActionResult> ReadAuthorizedAsync(bool validKey, Func<Guid, string, ClaimsPrincipal, Task<IActionResult>> read, bool allowQuery = false)
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
            !allowQuery && Request.Query.Count != 0 || !validKey)
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
            return await read(tenant, clientId, principal);
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
