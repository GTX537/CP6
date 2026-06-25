using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

// ── Mock OIDC IdP for CP6 SSO (S类#3 T10) browser QA ───────────────────────────
// Authorization Code + PKCE. Serves discovery/jwks/authorize(login form)/token.
// HTTPS via ASP.NET dev cert (trusted on this machine) so backend HttpDocumentRetriever
// (RequireHttps=true) + JWKS signature validation succeed.

const string ISSUER = "https://localhost:5099";
const string KID = "cp6-mock-idp-key-1";

// One RSA keypair for the process lifetime.
var rsa = RSA.Create(2048);
var signingKey = new RsaSecurityKey(rsa) { KeyId = KID };
var creds = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);

// code -> pending authorization (nonce/aud/email/sub captured at /authorize time).
var codes = new ConcurrentDictionary<string, PendingAuth>();

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// 1. Discovery document.
app.MapGet("/.well-known/openid-configuration", () => Results.Json(new
{
    issuer = ISSUER,
    authorization_endpoint = $"{ISSUER}/authorize",
    token_endpoint = $"{ISSUER}/token",
    jwks_uri = $"{ISSUER}/.well-known/jwks.json",
    response_types_supported = new[] { "code" },
    subject_types_supported = new[] { "public" },
    id_token_signing_alg_values_supported = new[] { "RS256" },
    scopes_supported = new[] { "openid", "email", "profile" },
    token_endpoint_auth_methods_supported = new[] { "client_secret_post" },
    claims_supported = new[] { "sub", "email", "email_verified", "nonce" },
}));

// 2. JWKS — RSA public key as a JWK.
app.MapGet("/.well-known/jwks.json", () =>
{
    var p = rsa.ExportParameters(false);
    return Results.Json(new
    {
        keys = new[]
        {
            new
            {
                kty = "RSA",
                use = "sig",
                kid = KID,
                alg = "RS256",
                n = Base64UrlEncoder.Encode(p.Modulus),
                e = Base64UrlEncoder.Encode(p.Exponent),
            }
        }
    });
});

// 3. Authorization endpoint — renders a minimal IdP login form so the browser QA
//    can pick the federated identity (default = a fresh JIT email).
app.MapGet("/authorize", (HttpRequest req) =>
{
    var q = req.Query;
    string client_id = q["client_id"]!;
    string redirect_uri = q["redirect_uri"]!;
    string state = q["state"]!;
    string nonce = q["nonce"]!;
    string defaultEmail = "sso.jit@example.com";

    var html = $$"""
    <!doctype html><html><head><meta charset="utf-8"><title>Mock OIDC IdP</title>
    <style>body{font-family:system-ui;max-width:420px;margin:60px auto;padding:24px;border:1px solid #ddd;border-radius:10px}
    h2{margin-top:0}label{display:block;margin:12px 0 4px;font-size:13px;color:#555}
    input{width:100%;padding:8px;box-sizing:border-box;border:1px solid #ccc;border-radius:6px}
    button{margin-top:18px;width:100%;padding:10px;background:#3b5bdb;color:#fff;border:0;border-radius:6px;font-size:15px;cursor:pointer}
    .hint{font-size:12px;color:#888;margin-top:14px}</style></head>
    <body>
    <h2>🔐 Mock OIDC IdP</h2>
    <form method="get" action="/authorize/consent" id="f">
      <input type="hidden" name="client_id" value="{{client_id}}">
      <input type="hidden" name="redirect_uri" value="{{redirect_uri}}">
      <input type="hidden" name="state" value="{{state}}">
      <input type="hidden" name="nonce" value="{{nonce}}">
      <label>Email (federated identity)</label>
      <input name="email" id="email" value="{{defaultEmail}}" autofocus>
      <label>email_verified</label>
      <input name="email_verified" value="true">
      <button type="submit" id="login">Sign in</button>
      <div class="hint">aud=client_id · returns to redirect_uri with code+state</div>
    </form></body></html>
    """;
    return Results.Content(html, "text/html");
});

// 3b. Consent/submit — generate code bound to the chosen identity + nonce, redirect back.
app.MapGet("/authorize/consent", (HttpRequest req) =>
{
    var q = req.Query;
    string client_id = q["client_id"]!;
    string redirect_uri = q["redirect_uri"]!;
    string state = q["state"]!;
    string nonce = q["nonce"]!;
    string email = q["email"]!;
    string emailVerified = string.IsNullOrEmpty(q["email_verified"]) ? "true" : q["email_verified"]!;

    var code = Guid.NewGuid().ToString("N");
    // Stable subject per email so re-login maps to the same federated identity (6a path).
    var sub = "mock|" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.ToLowerInvariant())))[..16];
    codes[code] = new PendingAuth(nonce, client_id, email, emailVerified, sub);

    var sep = redirect_uri.Contains('?') ? "&" : "?";
    var loc = $"{redirect_uri}{sep}code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}";
    return Results.Redirect(loc);
});

// 4. Token endpoint — exchange code for a signed id_token (no client_secret/PKCE check; mock).
app.MapPost("/token", async (HttpRequest req) =>
{
    var form = await req.ReadFormAsync();
    var code = form["code"].ToString();
    if (string.IsNullOrEmpty(code) || !codes.TryRemove(code, out var pend))
        return Results.BadRequest(new { error = "invalid_grant" });

    var now = DateTime.UtcNow;
    var claims = new List<Claim>
    {
        new("sub", pend.Sub),
        new("email", pend.Email),
        new("email_verified", pend.EmailVerified),
        new("nonce", pend.Nonce),
    };
    var jwt = new JwtSecurityToken(
        issuer: ISSUER,
        audience: pend.Aud,
        claims: claims,
        notBefore: now,
        expires: now.AddMinutes(5),
        signingCredentials: creds);
    var idToken = new JwtSecurityTokenHandler().WriteToken(jwt);

    return Results.Json(new
    {
        access_token = "mock-access-token",
        token_type = "Bearer",
        expires_in = 300,
        id_token = idToken,
    });
});

app.MapGet("/", () => "Mock OIDC IdP up. See /.well-known/openid-configuration");

app.Run();

record PendingAuth(string Nonce, string Aud, string Email, string EmailVerified, string Sub);
