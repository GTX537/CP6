using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Pub;
using CP6.WebApi.Configuration;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace CP6.WebApi.Controllers.Internal;

[ApiController, AllowAnonymous, ApiExplorerSettings(IgnoreApi = true)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("internal/main/v1/act-file-references")]
public sealed class ActFileReferenceControllerV1(ActFileReferenceRegistrationV1 registration, CrmOidcOptions oidc,
    CrmOidcCrypto crypto, ITenantContext tenant) : ControllerBase
{
    [HttpPost("proofs")]
    public async Task<IActionResult> Proof([FromBody] JsonElement body, CancellationToken ct)
    {
        try
        {
            var caller = RequireCaller();
            var input = ActFileReferenceV1.Read<ActFileProofRequestV1>(body);
            return Ok(await Owner.ProveAsync(caller, input, ct));
        }
        catch (JsonException) { return Error(400, "ACT_FILE_REFERENCE_INVALID"); }
        catch (OrderPriceCorrectionException error) { return Error(error.Status, error.Message); }
    }

    [HttpPost("downloads")]
    public async Task<IActionResult> Download([FromBody] JsonElement body, CancellationToken ct)
    {
        try
        {
            var caller = RequireCaller();
            if (Request.Headers.ContainsKey(HeaderNames.Range)) return Error(400, "ACT_FILE_REFERENCE_INVALID");
            var input = ActFileReferenceV1.Read<ActFileDownloadRequestV1>(body);
            await Owner.DownloadAsync(caller, input, WriteAsync, ct);
            return new EmptyResult();
        }
        catch (JsonException) { return Error(400, "ACT_FILE_REFERENCE_INVALID"); }
        catch (OrderPriceCorrectionException error) { return Error(error.Status, error.Message); }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
            return Error(503, "ACT_FILE_STREAM_INTERRUPTED");
        }
    }

    private ActFileReferenceOwnerV1 Owner => HttpContext.RequestServices.GetRequiredService<ActFileReferenceOwnerV1>();

    private async Task WriteAsync(ActFileDownloadSliceV1 slice, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (slice.Offset == 0)
        {
            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(slice.FileName);
            Response.Headers.ContentDisposition = disposition.ToString();
            Response.Headers["X-Content-SHA256"] = slice.Sha256;
            Response.ContentType = slice.MediaType;
            Response.ContentLength = slice.Length;
        }
        await Response.Body.WriteAsync(slice.Bytes, ct);
        await Response.Body.FlushAsync(ct);
    }

    private IActionResult Error(int status, string code)
    {
        if (Response.HasStarted || HttpContext.RequestAborted.IsCancellationRequested)
        {
            HttpContext.Abort();
            return new EmptyResult();
        }
        Response.ContentLength = null;
        Response.ContentType = null;
        Response.Headers.Remove(HeaderNames.ContentDisposition);
        Response.Headers.Remove("X-Content-SHA256");
        Response.Headers.CacheControl = "no-store";
        return StatusCode(status, new { code });
    }

    private ActFileCallerV1 RequireCaller()
    {
        if (!registration.Enabled) throw new OrderPriceCorrectionException("ACT_FILE_REFERENCE_UNAVAILABLE", 404);
        if (!Request.IsHttps) throw new OrderPriceCorrectionException("ACT_FILE_TLS_REQUIRED", 403);
        if (Request.Query.Count != 0) throw new OrderPriceCorrectionException("ACT_FILE_REFERENCE_INVALID", 400);
        var header = Request.Headers.Authorization;
        if (header.Count != 1 || header[0] is not { Length: > 7 and <= 8192 } bearer ||
            !bearer.StartsWith("Bearer ", StringComparison.Ordinal))
            throw UnauthorizedCaller();
        ClaimsPrincipal principal;
        try { principal = crypto.Validate(bearer[7..], "CP6.Services", "at+jwt"); }
        catch (Exception error) when (error is SecurityTokenException or ArgumentException or FormatException)
        {
            throw UnauthorizedCaller();
        }

        var client = Claim("client_id");
        if (client is null || Claim("sub") != "service:" + client || Claim("scope") != "cp6.services" ||
            Claim("aud") != "CP6.Services" ||
            !Guid.TryParseExact(Claim("tenant_id"), "D", out var tenantId) || Claim("tenant_id") != tenantId.ToString("D") ||
            !Guid.TryParseExact(Claim("jti"), "D", out var token) || Claim("jti") != token.ToString("D") || token == Guid.Empty ||
            !long.TryParse(Claim("exp"), NumberStyles.None, CultureInfo.InvariantCulture, out var expires) ||
            expires is <= 0 or > 253402300799)
            throw UnauthorizedCaller();
        if (!registration.Allows(tenantId, client) || Request.Headers["tenantid"].Count != 1 ||
            Request.Headers["tenantid"][0] != tenantId.ToString("D"))
            throw new OrderPriceCorrectionException("ACT_FILE_REFERENCE_FORBIDDEN", 403);
        tenant.CurrentTenantId = tenantId;
        return new(tenantId, client, oidc.Issuer, token.ToString("D"), DateTimeOffset.FromUnixTimeSeconds(expires));

        string? Claim(string type)
        {
            var found = principal.FindAll(type).Take(2).ToArray();
            return found.Length == 1 ? found[0].Value : null;
        }
    }

    private static OrderPriceCorrectionException UnauthorizedCaller() => new("ACT_FILE_SERVICE_AUTHENTICATION_REQUIRED", 401);
}
