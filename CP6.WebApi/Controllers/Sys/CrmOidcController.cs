using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

namespace CP6.WebApi.Controllers.Sys;

/// <summary>Opt-in CRM authorization-code and service-token provider. Legacy JWT authentication remains separate.</summary>
[ApiController]
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CrmOidcController(CrmOidcOptions options, CrmOidcCrypto crypto,
    ICrmOidcGrantStore grants, CrmOidcDirectory directory, IConfiguration configuration,
    ITokenBlacklistService blacklist, IAuthCookieWriter cookies,
    CrmOidcServiceTokens? serviceTokens = null) : ControllerBase
{
    [HttpGet("/.well-known/openid-configuration")]
    public IActionResult Discovery() => !options.Enabled ? NotFound() : Ok(new
    {
        issuer = options.Issuer,
        authorization_endpoint = options.Issuer + "/connect/authorize",
        token_endpoint = options.Issuer + "/connect/token",
        userinfo_endpoint = options.Issuer + "/connect/userinfo",
        end_session_endpoint = options.Issuer + "/connect/end-session",
        jwks_uri = options.Issuer + "/.well-known/jwks.json",
        response_types_supported = new[] { "code" }, response_modes_supported = new[] { "query" },
        grant_types_supported = new[] { "authorization_code", "client_credentials" }, subject_types_supported = new[] { "public" },
        scopes_supported = new[] { "openid", "profile", "crm", "cp6.services" },
        id_token_signing_alg_values_supported = new[] { "RS256" },
        token_endpoint_auth_methods_supported = new[] { "client_secret_basic" },
        code_challenge_methods_supported = new[] { "S256" },
        claims_supported = new[] { "sub", "name", "org_id", "tenant_id", "sid", "jti", "iat", "nbf", "exp", "nonce" }
    });

    [HttpGet("/.well-known/jwks.json")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false)]
    public IActionResult Jwks()
    {
        if (!options.Enabled)
        {
            PreventCaching();
            return NotFound();
        }
        Response.Headers.CacheControl = "public, max-age=60, must-revalidate";
        return Ok(crypto.Jwks());
    }

    [HttpGet("/connect/organizations/{slug}")]
    public async Task<IActionResult> Organization(string slug, [FromQuery(Name = "client_id")] string? clientId)
    {
        if (!options.Enabled || !options.Clients.Any(c => c.ClientId == clientId)) return NotFound();
        var organization = await directory.ResolveOrganizationAsync(slug);
        return organization == null ? NotFound() : Ok(organization);
    }

    [HttpGet("/connect/authorize")]
    [HttpGet("/api/auth/crm-authorize")]
    public async Task<IActionResult> AuthorizeClient()
    {
        if (!options.Enabled) return NotFound();
        if (Request.Query.Any(q => q.Value.Count != 1)) return OAuthError("invalid_request");
        string Q(string key) => Request.Query[key].ToString();
        var client = options.Clients.SingleOrDefault(c => c.ClientId == Q("client_id"));
        var redirect = Q("redirect_uri");
        // Never send an error to a URI until exact registration has been established.
        if (client == null || !client.RedirectUris.Contains(redirect, StringComparer.Ordinal)) return OAuthError("invalid_request");
        if (Q("response_type") != "code" || Q("response_mode") is not ("" or "query")
            || Q("code_challenge_method") != "S256" || !Regex.IsMatch(Q("code_challenge"), "^[A-Za-z0-9_-]{43}$")
            || Q("state").Length is < 16 or > 512 || Q("nonce").Length is < 16 or > 256
            || Q("prompt") is not ("" or "none") || Request.Query.ContainsKey("max_age")
            || !Q("scope").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal)
                .SetEquals(["openid", "profile", "crm"])) return OAuthError("invalid_request");
        var source = await ReadSourceAsync();
        if (source == null)
        {
            if (Q("prompt") == "none") return Redirect(QueryHelpers.AddQueryString(redirect,
                new Dictionary<string, string?> { ["error"] = "login_required", ["state"] = Q("state") }));
            var returnPath = "/connect/authorize" + Request.QueryString;
            return Redirect(options.Issuer + "/login?oidc_return=" + Uri.EscapeDataString(returnPath)
                + (Request.Cookies.ContainsKey(AuthCookieWriter.AccessCookie) ? "&oidc_reauthenticate=1" : ""));
        }
        var (user, principal) = source.Value;
        if (user == null) return Redirect(QueryHelpers.AddQueryString(redirect,
            new Dictionary<string, string?> { ["error"] = "access_denied", ["state"] = Q("state") }));
        if (Request.Query.ContainsKey("organization_id") && Q("organization_id") != user.TenantId.ToString())
            return Redirect(QueryHelpers.AddQueryString(redirect,
                new Dictionary<string, string?> { ["error"] = "access_denied", ["state"] = Q("state") }));
        if (Request.Path == "/connect/authorize")
            return Redirect(options.Issuer + "/api/auth/crm-authorize" + Request.QueryString);
        var sourceRefreshHash = principal.FindFirst("cp6_session")?.Value;
        var sourceFamily = new CrmOidcGrant { SourceRefreshHash = sourceRefreshHash ?? "", SubjectId = user.Id, OrganizationId = user.TenantId };
        var rawRefresh = Request.Cookies[AuthCookieWriter.RefreshCookie];
        if (rawRefresh == null || sourceRefreshHash == null
            || await directory.BindRefreshAsync(rawRefresh, user) == null
            || !await directory.RefreshFamilyMatchesAsync(sourceFamily, rawRefresh))
            return Redirect(QueryHelpers.AddQueryString(redirect,
                new Dictionary<string, string?> { ["error"] = "login_required", ["state"] = Q("state") }));
        var now = DateTime.UtcNow;
        var sourceExpires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(principal.FindFirst("exp")!.Value)).UtcDateTime;
        var raw = CrmOidcCrypto.RandomToken();
        var accessExpires = sourceExpires < now.AddMinutes(5) ? sourceExpires : now.AddMinutes(5);
        await grants.SaveAsync(new CrmOidcGrant
        {
            Id = Guid.NewGuid(), CodeHash = CrmOidcCrypto.Hash(raw), ClientId = client.ClientId,
            RedirectUri = redirect, Challenge = Q("code_challenge"), Nonce = Q("nonce"),
            SubjectId = user.Id, OrganizationId = user.TenantId, SourceJti = principal.FindFirst("jti")!.Value,
            SourceRefreshHash = sourceRefreshHash,
            SecurityStamp = CrmOidcDirectory.SecurityStamp(user), ExpiresAtUtc = now.AddSeconds(60),
            SourceExpiresAtUtc = sourceExpires, AccessExpiresAtUtc = accessExpires
        });
        return Redirect(QueryHelpers.AddQueryString(redirect,
            new Dictionary<string, string?> { ["code"] = raw, ["state"] = Q("state"), ["iss"] = options.Issuer }));
    }

    [HttpPost("/connect/token")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Token()
    {
        if (!options.Enabled) return NotFound();
        PreventCaching();
        if (!Microsoft.Net.Http.Headers.MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType)
            || !string.Equals(contentType.MediaType.Value, "application/x-www-form-urlencoded",
                StringComparison.OrdinalIgnoreCase)) return OAuthError("invalid_request");
        if (Request.ContentLength > 8192) return OAuthError("invalid_request");
        IFormCollection form;
        try
        {
            form = await Request.ReadFormAsync(Request.HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            return OAuthError("invalid_request");
        }
        if (form.Any(f => f.Value.Count != 1)) return OAuthError("invalid_request");
        var grantType = form["grant_type"].ToString();
        if (grantType.Length == 0) return OAuthError("invalid_request");
        if (grantType == "client_credentials") return await IssueServiceTokenAsync(form);
        if (grantType != "authorization_code") return OAuthError("unsupported_grant_type");
        return await RedeemAuthorizationCodeAsync(form);
    }

    private async Task<IActionResult> RedeemAuthorizationCodeAsync(IFormCollection form)
    {
        var client = ReadClient();
        if (client == null)
        {
            if (serviceTokens?.Authenticate(Request.Headers.Authorization).IsAuthenticated == true)
                return OAuthError("unauthorized_client");
            return OAuthError("invalid_client", 401);
        }
        if (form.ContainsKey("client_secret") || form.ContainsKey("client_id")
            || !CrmOidcCrypto.ValidVerifier(form["code_verifier"].ToString())
            || !Regex.IsMatch(form["code"].ToString(), "^[A-Za-z0-9_-]{43}$")) return OAuthError("invalid_request");
        if (!client.RedirectUris.Contains(form["redirect_uri"].ToString(), StringComparer.Ordinal)) return OAuthError("invalid_grant");
        var grant = await grants.ConsumeAsync(CrmOidcCrypto.Hash(form["code"].ToString()), client.ClientId,
            form["redirect_uri"].ToString(), CrmOidcCrypto.Challenge(form["code_verifier"].ToString()));
        if (grant == null || grant.AccessExpiresAtUtc <= DateTime.UtcNow) return OAuthError("invalid_grant");
        var user = await directory.FindActiveAsync(grant.SubjectId, grant.OrganizationId, grant.SourceJti, grant.SecurityStamp);
        if (user == null || !await directory.RefreshFamilyMatchesAsync(grant)) return OAuthError("invalid_grant");
        if (grant.AccessExpiresAtUtc <= DateTime.UtcNow.AddSeconds(1)) return OAuthError("invalid_grant");
        var common = new[] { new Claim("sub", user.Id.ToString()), new Claim("org_id", user.TenantId.ToString()),
            new Claim("tenant_id", user.TenantId.ToString()), new Claim("sid", grant.Id.ToString()),
            new Claim("name", user.NickName ?? user.UserName) };
        var expires = new DateTimeOffset(DateTime.SpecifyKind(grant.AccessExpiresAtUtc, DateTimeKind.Utc));
        var access = crypto.Sign("CP6.Web", common.Concat([new Claim("jti", grant.Id.ToString()),
            new Claim("scope", "openid profile crm")]), expires, "at+jwt");
        var identity = crypto.Sign(client.ClientId, common.Concat([new Claim("nonce", grant.Nonce),
            new Claim("jti", Guid.NewGuid().ToString())]), expires, "JWT");
        return Ok(new { access_token = access, id_token = identity, token_type = "Bearer",
            expires_in = Math.Max(0, (int)(expires - DateTimeOffset.UtcNow).TotalSeconds), scope = "openid profile crm" });
    }

    private async Task<IActionResult> IssueServiceTokenAsync(IFormCollection form)
    {
        if (form.Keys.Any(key => key is not ("grant_type" or "scope")))
            return OAuthError("invalid_request");
        if (!IsServiceTransportAllowed()) return OAuthError("invalid_request");
        var authentication = serviceTokens?.Authenticate(Request.Headers.Authorization);
        if (authentication?.IsAuthenticated != true)
        {
            if (ReadClient() != null) return OAuthError("unauthorized_client");
            return OAuthError("invalid_client", 401);
        }
        if (form["scope"].ToString() != "cp6.services") return OAuthError("invalid_scope");
        var issue = await serviceTokens!.IssueAsync(authentication.Client!, Request.HttpContext.RequestAborted);
        return issue.Status switch
        {
            CrmOidcServiceTokenIssueStatus.Success => Ok(new
            {
                access_token = issue.AccessToken,
                token_type = "Bearer",
                expires_in = issue.ExpiresIn,
                scope = "cp6.services"
            }),
            CrmOidcServiceTokenIssueStatus.Unauthorized => OAuthError("unauthorized_client"),
            _ => OAuthError("temporarily_unavailable", StatusCodes.Status503ServiceUnavailable)
        };
    }

    [HttpGet("/connect/userinfo")]
    public async Task<IActionResult> UserInfo()
    {
        if (!options.Enabled) return NotFound();
        var session = await ReadBearerAsync();
        if (session == null) return OAuthError("invalid_token", 401);
        return Ok(new { sub = session.Value.User.Id.ToString(), name = session.Value.User.NickName ?? session.Value.User.UserName,
            org_id = session.Value.User.TenantId.ToString(), tenant_id = session.Value.User.TenantId.ToString(),
            sid = session.Value.Grant.Id.ToString(), jti = session.Value.Grant.Id.ToString() });
    }

    [HttpGet("/connect/crm-context")]
    public async Task<IActionResult> Context()
    {
        if (!options.Enabled) return NotFound();
        var session = await ReadBearerAsync();
        return session == null ? OAuthError("invalid_token", 401) : Ok(await directory.ProjectAsync(session.Value.User));
    }

    [HttpPost("/connect/end-session")]
    [Consumes("application/x-www-form-urlencoded")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> EndSession()
    {
        if (!options.Enabled) return NotFound();
        var client = ReadClient();
        if (client == null)
        {
            if (serviceTokens?.Authenticate(Request.Headers.Authorization).IsAuthenticated == true)
                return OAuthError("unauthorized_client");
            return OAuthError("invalid_client", 401);
        }
        var form = await Request.ReadFormAsync();
        if (form.Any(q => q.Value.Count != 1)) return OAuthError("invalid_request");
        ClaimsPrincipal principal;
        // An expired ID token is proof only for revoking its original family, never for authentication.
        try { principal = crypto.Validate(form["id_token_hint"].ToString(), client.ClientId, "JWT", validateLifetime: false); }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException) { return OAuthError("invalid_request"); }
        var redirect = form["post_logout_redirect_uri"].ToString();
        if (!client.PostLogoutRedirectUris.Contains(redirect, StringComparer.Ordinal)
            || !Guid.TryParse(principal.FindFirst("sid")?.Value, out var sid)) return OAuthError("invalid_request");
        var grant = await grants.FindForLogoutAsync(sid);
        if (grant == null || grant.ClientId != client.ClientId || principal.FindFirst("sub")?.Value != grant.SubjectId.ToString()
            || principal.FindFirst("tenant_id")?.Value != grant.OrganizationId.ToString()) return OAuthError("invalid_request");
        var state = form["state"].ToString();
        if (state.Length > 512) return OAuthError("invalid_request");
        await grants.RevokeSourceFamilyAsync(grant);
        var ttl = grant.SourceExpiresAtUtc - DateTime.UtcNow;
        if (ttl > TimeSpan.Zero) await blacklist.BlacklistAsync(grant.SourceJti, ttl);
        var rawTicket = CrmOidcCrypto.RandomToken();
        await grants.SaveLogoutAsync(new CrmOidcLogout { TicketHash = CrmOidcCrypto.Hash(rawTicket), GrantId = grant.Id,
            RedirectUri = state.Length == 0 ? redirect : QueryHelpers.AddQueryString(redirect, "state", state), ExpiresAtUtc = DateTime.UtcNow.AddMinutes(1) });
        return Ok(new { logoutContinuationUri = options.Issuer + "/api/auth/crm-logout?ticket=" + rawTicket });
    }

    [HttpGet("/api/auth/crm-logout")]
    public IActionResult LogoutLanding([FromQuery] string ticket)
    {
        if (!options.Enabled) return NotFound();
        if (!Regex.IsMatch(ticket, "^[A-Za-z0-9_-]{43}$")) return OAuthError("invalid_request");
        var nonce = CrmOidcCrypto.RandomToken();
        Response.Headers.ContentSecurityPolicy = $"default-src 'none'; script-src 'nonce-{nonce}'; base-uri 'none'; frame-ancestors 'none'";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        // Starting a new same-origin navigation sends the Strict /api/auth refresh cookie for exact-family comparison.
        return Content($"<!doctype html><meta charset=\"utf-8\"><title>Signing out</title><p>Signing out…</p><script nonce=\"{nonce}\">location.replace('/api/auth/crm-logout-complete?ticket={ticket}')</script>", "text/html");
    }

    [HttpGet("/api/auth/crm-logout-complete")]
    public async Task<IActionResult> LogoutComplete([FromQuery] string ticket)
    {
        if (!options.Enabled) return NotFound();
        if (!Regex.IsMatch(ticket, "^[A-Za-z0-9_-]{43}$")) return OAuthError("invalid_request");
        var completion = await grants.ConsumeLogoutAsync(CrmOidcCrypto.Hash(ticket));
        if (completion == null) return OAuthError("invalid_request");
        var grant = await grants.FindForLogoutAsync(completion.GrantId);
        var rawRefresh = Request.Cookies[AuthCookieWriter.RefreshCookie];
        if (grant != null && rawRefresh != null && await directory.RefreshFamilyMatchesAsync(grant, rawRefresh))
            cookies.ClearAuthCookies(Response);
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return Redirect(completion.RedirectUri);
    }

    private CrmOidcClient? ReadClient()
    {
        if (!CrmOidcServiceTokens.TryDecodeBasic(Request.Headers.Authorization, out var id, out var secret))
            return null;
        var client = options.Clients.SingleOrDefault(c => c.ClientId == id);
        return client != null && CrmOidcCrypto.EqualsSecret(secret, client.SecretSha256) ? client : null;
    }

    private async Task<(Sys_User? User, ClaimsPrincipal Principal)?> ReadSourceAsync()
    {
        var raw = Request.Cookies[AuthCookieWriter.AccessCookie];
        if (string.IsNullOrEmpty(raw)) return null;
        ClaimsPrincipal principal;
        try
        {
            var jwt = configuration.GetSection("JWT");
            principal = new JwtSecurityTokenHandler().ValidateToken(raw, new TokenValidationParameters
            {
                ValidIssuer = jwt["Issuer"], ValidAudience = jwt["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Secret"]!)),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ValidateIssuer = true, ValidateAudience = true,
                ValidateLifetime = true, ValidateIssuerSigningKey = true, RequireExpirationTime = true, ClockSkew = TimeSpan.Zero
            }, out _);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException) { return null; }
        if (principal.HasClaim(c => c.Type is "token_use" or "impersonator_id")
            || principal.FindFirst("must_change_password")?.Value != "false"
            || !Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var sub)
            || !Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var org)
            || !long.TryParse(principal.FindFirst("iat")?.Value, out var iat)
            || !principal.HasClaim(c => c.Type == "cp6_auth_version") || !principal.HasClaim(c => c.Type == "cp6_session")) return null;
        var user = await directory.FindActiveAsync(sub, org, principal.FindFirst("jti")?.Value ?? "", issuedAt: iat);
        if (user != null && (principal.FindFirst("cp6_auth_version")?.Value != AuthSessionVersion.For(user)
            || !await directory.HasCurrentBrowserAuthenticationAsync(principal.FindFirst("cp6_session")!.Value, user))) return null;
        return (user, principal);
    }

    private async Task<(Sys_User User, CrmOidcGrant Grant)?> ReadBearerAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
        ClaimsPrincipal principal;
        try { principal = crypto.Validate(header[7..], "CP6.Web", "at+jwt"); }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException) { return null; }
        if (!Guid.TryParse(principal.FindFirst("jti")?.Value, out var id) || principal.FindFirst("sid")?.Value != id.ToString()) return null;
        var grant = await grants.FindSessionAsync(id);
        if (grant == null || grant.ClientId != "CP6.Web" || principal.FindFirst("sub")?.Value != grant.SubjectId.ToString()
            || principal.FindFirst("tenant_id")?.Value != grant.OrganizationId.ToString()
            || await blacklist.IsBlacklistedAsync(id.ToString())) return null;
        var user = await directory.FindActiveAsync(grant.SubjectId, grant.OrganizationId, grant.SourceJti, grant.SecurityStamp);
        return user == null || !await directory.RefreshFamilyMatchesAsync(grant) ? null : (user, grant);
    }

    private ObjectResult OAuthError(string error, int status = 400)
    {
        PreventCaching();
        if (status == 401) Response.Headers.WWWAuthenticate = error == "invalid_client" ? "Basic" : "Bearer error=\"invalid_token\"";
        return StatusCode(status, new { error });
    }

    private void PreventCaching()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
    }

    private bool IsServiceTransportAllowed()
    {
        if (Request.IsHttps) return true;
        if (options.AllowInsecureLoopback
            && Uri.TryCreate(options.Issuer, UriKind.Absolute, out var issuer)
            && issuer.Scheme == Uri.UriSchemeHttp && issuer.IsLoopback
            && Uri.TryCreate("http://" + Request.Host, UriKind.Absolute, out var requestOrigin)
            && requestOrigin.IsLoopback
            && Request.HttpContext.Connection.RemoteIpAddress is { } loopbackPeer
            && System.Net.IPAddress.IsLoopback(loopbackPeer)) return true;
        var remote = Request.HttpContext.Connection.RemoteIpAddress;
        if (remote == null) return false;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        if (!options.TrustedTokenProxyAddresses.Any(raw =>
                CrmOidcOptions.TryParseTrustedTokenProxyAddress(raw, out var trusted)
                && trusted.Equals(remote))) return false;
        var forwardedProto = Request.Headers["X-Forwarded-Proto"];
        return forwardedProto.Count == 1
            && string.Equals(forwardedProto[0], "https", StringComparison.OrdinalIgnoreCase);
    }

}
