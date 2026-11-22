using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.WebApi.Configuration;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace CP6.WebApi.Controllers.Platform;

[ApiController, AllowAnonymous, ApiExplorerSettings(IgnoreApi = true)]
[Route("internal/main/v1/contact-history-exports")]
[RequestSizeLimit(65536)]
public sealed class ContactHistoryExportControllerV11(ContactHistoryExportRegistrationV11 registration, CrmOidcOptions oidc,
    CrmOidcCrypto crypto, ICrmOidcServiceDirectory directory, ITenantContext tenant, TimeProvider clock) : ControllerBase
{
    private CancellationToken Aborted => HttpContext.RequestAborted;
    private ContactHistoryExportOwnerV11 Owner => HttpContext.RequestServices.GetRequiredService<ContactHistoryExportOwnerV11>();

    [HttpGet("dataset")]
    public Task<IActionResult> Dataset() => ExecuteAsync((owner, caller) => owner.DescribeAsync(caller, Aborted));

    [HttpGet("authority")]
    public Task<IActionResult> Authority() => ExecuteAsync((owner, caller) => owner.GetAuthorityAsync(caller, Aborted));

    [HttpPost("requests")]
    public Task<IActionResult> Submit([FromBody] JsonElement body) =>
        ReceiveAsync<ContactHistoryRequestV11, ContactHistoryReplyV11>(body, (owner, caller, value) => owner.SubmitAsync(caller, value, Aborted));

    [HttpGet("requests/{requestId:guid}")]
    public Task<IActionResult> Original(Guid requestId) =>
        ExecuteAsync((owner, caller) => owner.OriginalAsync(caller, requestId, Aborted));

    [HttpGet("requests/{requestId:guid}/results")]
    public Task<IActionResult> Results(Guid requestId) =>
        ExecuteAsync((owner, caller) => owner.ResultsAsync(caller, requestId, Request.Query["after"].FirstOrDefault(), Aborted), "after");

    [HttpPost("proofs/authenticate")]
    public Task<IActionResult> Authenticate([FromBody] JsonElement body) =>
        ReceiveAsync<ContactHistoryEvidenceLookupV11, ContactHistoryTrustV11>(body, (owner, caller, value) => owner.AuthenticateAsync(caller, value, Aborted));

    [HttpPost("proofs/replay")]
    public Task<IActionResult> Replay([FromBody] JsonElement body) =>
        ReceiveAsync<ContactHistoryReplayV11, bool>(body, (owner, caller, value) => owner.VerifyReplayAsync(caller, value, Aborted));

    [HttpPost("proofs/checkpoint")]
    public Task<IActionResult> VerifyCheckpoint([FromBody] JsonElement body) =>
        ReceiveAsync<ContactHistoryCheckpointVerifyV11, bool>(body, (owner, caller, value) => owner.VerifyCheckpointAsync(caller, value, Aborted));

    [HttpPost("requests/{requestId:guid}/conflicts")]
    public Task<IActionResult> Conflict(Guid requestId, [FromBody] JsonElement body) =>
        ReceiveAsync<ContactHistoryConflictNoticeV11, object>(body, (owner, caller, value) => owner.RecordConflictAsync(caller, requestId, value, Aborted));

    [HttpPost("requests/{requestId:guid}/checkpoints")]
    public Task<IActionResult> Checkpoint(Guid requestId, [FromBody] JsonElement body) =>
        ReceiveAsync<ContactHistoryCheckpointRequestV11, ContactHistoryPublicationV11>(body, (owner, caller, value) => owner.CheckpointAsync(caller, requestId, value, Aborted));

    [HttpPost("requests/{requestId:guid}/artifacts/revoke")]
    public Task<IActionResult> Revoke(Guid requestId, [FromBody] JsonElement body) => ChangeArtifact(requestId, body, false);

    [HttpPost("requests/{requestId:guid}/artifacts/redact")]
    public Task<IActionResult> Redact(Guid requestId, [FromBody] JsonElement body) => ChangeArtifact(requestId, body, true);

    private Task<IActionResult> ChangeArtifact(Guid requestId, JsonElement body, bool redact) =>
        ReceiveAsync<ContactHistoryArtifactChangeV11, ContactHistoryPublicationV11>(body,
            (owner, caller, value) => owner.ChangeArtifactAsync(caller, requestId, value, redact, Aborted));

    [HttpPost("permits")]
    public Task<IActionResult> AuthorizeRead([FromBody] JsonElement body) =>
        ReceiveAsync<ContactHistoryReadV11, ContactHistoryPermitV11>(body, (owner, caller, value) => owner.AuthorizeReadAsync(caller, value, Aborted));

    [HttpGet("downloads/{permitId:guid}")]
    public async Task<IActionResult> Download(Guid permitId)
    {
        PreventCaching();
        try
        {
            var caller = await RequireCallerAsync();
            if (Request.Query.Count != 0 || Request.Headers.ContainsKey("Range")) return EmptyStatus(400);
            await Owner.DownloadAsync(caller, permitId, WriteAsync, Aborted);
            return new EmptyResult();
        }
        catch (ContactHistoryExportExceptionV11 error) { return EmptyStatus(error.Status); }
        catch (Exception error) when (error is DbException or TimeoutException or IdentityReadException) { return EmptyStatus(503); }
        catch (Exception error) when (error is IOException or OperationCanceledException) { return EmptyStatus(403); }
    }

    private async Task WriteAsync(ContactHistoryDownloadSliceV11 slice, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (slice.Offset == 0)
        {
            Response.ContentType = "text/csv; charset=utf-8";
            Response.ContentLength = slice.Length;
            Response.Headers.ContentDisposition = "attachment; filename=\"contact-history.csv\"";
            Response.Headers["X-Content-SHA256"] = slice.Sha256;
        }
        await Response.Body.WriteAsync(slice.Bytes, ct);
        await Response.Body.FlushAsync(ct);
    }

    private IActionResult EmptyStatus(int status)
    {
        if (Response.HasStarted || Aborted.IsCancellationRequested) HttpContext.Abort();
        else
        {
            Response.StatusCode = status;
            Response.ContentLength = 0;
            Response.ContentType = null;
            Response.Headers.Remove("Content-Disposition");
            Response.Headers.Remove("X-Content-SHA256");
        }
        return new EmptyResult();
    }

    private Task<IActionResult> ReceiveAsync<T, TResult>(JsonElement body, Func<ContactHistoryExportOwnerV11, ContactHistoryCallerV11, T, Task<TResult>> action)
        where T : class => ExecuteAsync((owner, caller) => action(owner, caller, ContactHistoryExportWireV11.Read<T>(body)));

    private async Task<IActionResult> ExecuteAsync<T>(Func<ContactHistoryExportOwnerV11, ContactHistoryCallerV11, Task<T>> action, string? query = null)
    {
        PreventCaching();
        try
        {
            var caller = await RequireCallerAsync();
            if (Request.Query.Any(item => item.Key != query || item.Value.Count != 1)) return Error(400, "CONTACT_EXPORT_SCHEMA_INVALID");
            return Ok(await action(Owner, caller));
        }
        catch (JsonException) { return Error(400, "CONTACT_EXPORT_SCHEMA_INVALID"); }
        catch (ContactHistoryExportExceptionV11 error) { return Error(error.Status, error.Message); }
        catch (PostgresException error) when (error is { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "requests_tenant_id_source_event_id_key" })
        { return Error(409, "CONTACT_EXPORT_SOURCE_EVENT_BOUND"); }
        catch (Exception error) when (error is DbException or TimeoutException or IdentityReadException)
        { return Error(503, "CONTACT_EXPORT_OUTCOME_UNPROVEN"); }
    }

    private async Task<ContactHistoryCallerV11> RequireCallerAsync()
    {
        if (!registration.Enabled) throw new ContactHistoryExportExceptionV11("CONTACT_EXPORT_NOT_VISIBLE", 404);
        if (!Request.IsHttps) throw new ContactHistoryExportExceptionV11("CONTACT_EXPORT_TLS_REQUIRED", 403);
        var header = Request.Headers.Authorization;
        if (header.Count != 1 || header[0] is not { Length: > 7 and <= 8192 } bearer || !bearer.StartsWith("Bearer ", StringComparison.Ordinal))
            throw UnauthorizedService();
        ClaimsPrincipal principal;
        try { principal = crypto.Validate(bearer[7..], "CP6.Services", "at+jwt"); }
        catch (Exception error) when (error is SecurityTokenException or ArgumentException or FormatException) { throw UnauthorizedService(); }

        var client = Claim("client_id");
        var id = Claim("tenant_id");
        var jti = Claim("jti");
        if (client is null || Claim("sub") != "service:" + client || Claim("scope") != "cp6.services" || Claim("aud") != "CP6.Services" ||
            !Guid.TryParseExact(id, "D", out var tenantId) || id != tenantId.ToString("D") ||
            !Guid.TryParseExact(jti, "D", out var tokenId) || jti != tokenId.ToString("D") ||
            !long.TryParse(Claim("exp"), NumberStyles.None, CultureInfo.InvariantCulture, out var expires) ||
            expires is <= 0 or > 253402300799) throw UnauthorizedService();
        if (!registration.TryResolve(tenantId, client, out var reconcile) || Request.Headers["tenantid"].Count != 1 ||
            Request.Headers["tenantid"][0] != id)
            throw new ContactHistoryExportExceptionV11("CONTACT_EXPORT_ACCESS_DENIED", 403);
        var correlation = Request.Headers["correlationid"];
        if (correlation.Count != 1 || correlation[0] is not { Length: > 0 and <= 128 } requestId || !requestId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            throw new ContactHistoryExportExceptionV11("CONTACT_EXPORT_SCHEMA_INVALID", 400);
        tenant.CurrentTenantId = tenantId;
        if (!await directory.IsServiceTenantActiveAsync(tenantId, clock.GetUtcNow().UtcDateTime, Aborted))
            throw new ContactHistoryExportExceptionV11("CONTACT_EXPORT_ACCESS_DENIED", 403);
        return new(tenantId, client, oidc.Issuer, jti, DateTimeOffset.FromUnixTimeSeconds(expires), reconcile);

        string? Claim(string name)
        {
            var claims = principal.FindAll(name).Take(2).ToArray();
            return claims.Length == 1 ? claims[0].Value : null;
        }
    }

    private static ContactHistoryExportExceptionV11 UnauthorizedService() => new("CONTACT_EXPORT_SERVICE_REQUIRED", 401);
    private IActionResult Error(int status, string code) => StatusCode(status, new { code });
    private void PreventCaching()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
}
