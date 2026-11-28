using System.Globalization;
using System.Net;
using System.Security.Claims;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using static CP6.Core.Services.Plm.EngineeringIntakeWireV1;

namespace CP6.WebApi.Controllers.Internal;

[ApiController, AllowAnonymous, ApiExplorerSettings(IgnoreApi = true)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(MaxBody)]
public sealed class EngineeringIntakeControllerV1(CrmOidcOptions oidc, IAuthenticationSchemeProvider schemes, ITenantContext tenant)
    : ControllerBase
{
    [HttpPost("/internal/plm/v1/engineering-handoff-next-context/query")]
    public Task<IActionResult> Next(CancellationToken ct) => ExecuteAsync(
        (owner, caller, bytes) => owner.QueryNextAsync(caller, bytes, ct), StatusCodes.Status200OK, ct);

    [HttpPost("/internal/plm/v1/engineering-handoffs")]
    public Task<IActionResult> Receive(CancellationToken ct) => ExecuteAsync(
        (owner, caller, bytes) => owner.ReceiveAsync(caller, bytes, ct), StatusCodes.Status202Accepted, ct);

    [HttpPost("/internal/plm/v1/engineering-handoff-originals/query")]
    public Task<IActionResult> Original(CancellationToken ct) => ExecuteAsync(
        (owner, caller, bytes) => owner.QueryOriginalAsync(caller, bytes, ct), StatusCodes.Status200OK, ct);

    private async Task<IActionResult> ExecuteAsync<T>(Func<EngineeringIntakeContextService, EngineeringIntakeCallerV1, byte[], Task<T>> action,
        int successStatus, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var caller = await AuthenticateAsync();
            if (Request.Query.Count != 0 || !Request.HasJsonContentType()) throw Failure("ENGINEERING_INTAKE_CONTRACT_INVALID", 400);
            var bytes = await OriginalBytesAsync(ct);
            // 主文 TenantId 只能核对，数据库上下文来自当前服务凭据。
            tenant.CurrentTenantId = caller.TenantId;
            var owner = HttpContext.RequestServices.GetRequiredService<EngineeringIntakeContextService>();
            return new JsonResult(await action(owner, caller, bytes), WireJson) { StatusCode = successStatus };
        }
        catch (OrderPriceCorrectionException error)
        {
            return StatusCode(error.Status, new { code = error.Message });
        }
    }

    private async Task<EngineeringIntakeCallerV1> AuthenticateAsync()
    {
        var scheme = CrmIdentityController.Scheme;
        if (!oidc.Enabled || await schemes.GetSchemeAsync(scheme) is null)
            throw Failure("AUTHORITY_FENCE_UNAVAILABLE", 503);
        if (!Request.IsHttps && !(oidc.AllowInsecureLoopback && HttpContext.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip)))
            throw Failure("ENGINEERING_INTAKE_TLS_REQUIRED", 403);
        var result = await HttpContext.AuthenticateAsync(scheme);
        if (!result.Succeeded || result.Principal is not { } principal)
            throw Failure("AUTHENTICATION_REQUIRED", 401);
        var client = Single(principal, "client_id");
        var issuer = Single(principal, "iss");
        var tenantClaim = Single(principal, "tenant_id");
        var tokenClaim = Single(principal, "jti");
        if (client is null || issuer is null || issuer != oidc.Issuer || Single(principal, "sub") != "service:" + client
            || Single(principal, "aud") != "CP6.Services" || Single(principal, "scope") != "cp6.services"
            || !Guid.TryParseExact(tenantClaim, "D", out var tenantId) || tenantId == Guid.Empty || tenantClaim != tenantId.ToString("D")
            || !Guid.TryParseExact(tokenClaim, "D", out var token) || token == Guid.Empty || tokenClaim != token.ToString("D")
            || !long.TryParse(Single(principal, "exp"), NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
            || expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() || expires > DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            || !oidc.ServiceClients.Any(registration => registration.ClientId == client && registration.TenantId == tenantId
                && registration.Enabled && registration.AllowedScopes.Contains("cp6.services", StringComparer.Ordinal))
            || Request.Headers.TryGetValue("tenantid", out var hint) && (hint.Count != 1 || hint[0] != tenantClaim))
            throw Failure("ENGINEERING_INTAKE_CREDENTIAL_FORBIDDEN", 403);
        return new(tenantId, issuer, client, token, DateTimeOffset.FromUnixTimeSeconds(expires));
    }

    private async Task<byte[]> OriginalBytesAsync(CancellationToken ct)
    {
        if (Request.ContentLength is > MaxBody) throw Failure("ENGINEERING_INTAKE_BODY_LIMIT", 413);
        using var bytes = new MemoryStream();
        var buffer = new byte[16_384];
        while (true)
        {
            var length = await Request.Body.ReadAsync(buffer, ct);
            if (length == 0) return bytes.ToArray();
            if (bytes.Length + length > MaxBody) throw Failure("ENGINEERING_INTAKE_BODY_LIMIT", 413);
            bytes.Write(buffer, 0, length);
        }
    }

    private static string? Single(ClaimsPrincipal principal, string type) =>
        principal.FindAll(type).Take(2).ToArray() is [var claim] ? claim.Value : null;
}
