using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CP6.WebApi.Services;

/// <summary>Explicit opt-in bridge for registered CRM BFFs. Populate secrets from the host secret store.</summary>
public sealed class CrmOidcOptions
{
    public bool Enabled { get; set; }
    public string Issuer { get; set; } = "";
    public bool AllowInsecureLoopback { get; set; }
    public string ActiveKeyId { get; set; } = "";
    public List<CrmOidcKey> Keys { get; set; } = [];
    public List<CrmOidcClient> Clients { get; set; } = [];
    public List<CrmOidcServiceClient> ServiceClients { get; set; } = [];
    public List<CrmOidcOrganization> Organizations { get; set; } = [];

    public void Validate(bool development)
    {
        if (!Enabled) return;
        bool ValidUri(string raw) => Uri.TryCreate(raw, UriKind.Absolute, out var u)
            && string.IsNullOrEmpty(u.UserInfo) && string.IsNullOrEmpty(u.Fragment)
            && (u.Scheme == "https" || development && AllowInsecureLoopback && u.IsLoopback && u.Scheme == "http");
        if (!ValidUri(Issuer) || new Uri(Issuer).AbsolutePath != "/" || new Uri(Issuer).Query != "" || Issuer.EndsWith('/'))
            throw new InvalidOperationException("CrmOidc:Issuer must be an HTTPS origin without a trailing slash.");
        if (Keys.Count == 0 || Keys.Select(k => k.Kid).Distinct(StringComparer.Ordinal).Count() != Keys.Count
            || Keys.Any(k => !Regex.IsMatch(k.Kid, "\\A[A-Za-z0-9_-]{1,80}\\z")) || Keys.Count(k => k.Kid == ActiveKeyId) != 1)
            throw new InvalidOperationException("CrmOidc requires a unique RSA key ring and active key id.");
        foreach (var key in Keys)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(key.Pem);
            if (rsa.KeySize < 2048) throw new InvalidOperationException("CrmOidc RSA keys must have at least 2048 bits.");
            if (key.Kid == ActiveKeyId) rsa.ExportParameters(true);
        }
        if (Clients.Count == 0 || Clients.Select(c => c.ClientId).Distinct().Count() != Clients.Count
            || Clients.Any(c => c.ClientId != "CP6.Web" || !Regex.IsMatch(c.SecretSha256, "^[A-Fa-f0-9]{64}$")
                || c.RedirectUris.Count == 0 || c.RedirectUris.Concat(c.PostLogoutRedirectUris).Any(u => !ValidUri(u))))
            throw new InvalidOperationException("CrmOidc requires CP6.Web, a SHA-256 client secret hash, and exact HTTPS redirect URIs.");
        if (Organizations.Select(o => o.TenantId).Distinct().Count() != Organizations.Count
            || Organizations.Select(o => o.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Organizations.Count
            || Organizations.Any(o => o.TenantId == Guid.Empty || !Regex.IsMatch(o.Slug, "^[a-z0-9][a-z0-9-]{0,62}$")
                || !Regex.IsMatch(o.Region, "^[a-z0-9][a-z0-9-]{0,31}$")))
            throw new InvalidOperationException("CrmOidc organization mappings require unique real tenant IDs, slugs, and regions.");
        var browserIds = Clients.Select(c => c.ClientId).ToHashSet(StringComparer.Ordinal);
        if (ServiceClients.Select(c => c.ClientId).Distinct(StringComparer.Ordinal).Count() != ServiceClients.Count
            || ServiceClients.Any(c => !Regex.IsMatch(c.ClientId, "\\A[A-Za-z0-9._-]{1,80}\\z")
                || browserIds.Contains(c.ClientId)
                || c.TenantId == Guid.Empty
                || !Regex.IsMatch(c.SecretSha256, "\\A[A-Fa-f0-9]{64}\\z")
                || c.AllowedScopes.Count != 1
                || c.AllowedScopes[0] != "cp6.services"
                || !Organizations.Any(o => o.TenantId == c.TenantId)))
            throw new InvalidOperationException("CrmOidc service clients require unique safe IDs, SHA-256 secrets, one mapped tenant, and only cp6.services scope.");
    }
}

public sealed class CrmOidcKey
{
    public string Kid { get; set; } = "";
    public string Pem { get; set; } = "";
}
public sealed class CrmOidcClient
{
    public string ClientId { get; set; } = "CP6.Web";
    public string SecretSha256 { get; set; } = "";
    public List<string> RedirectUris { get; set; } = [];
    public List<string> PostLogoutRedirectUris { get; set; } = [];
}
public sealed class CrmOidcServiceClient
{
    public string ClientId { get; set; } = "";
    public string SecretSha256 { get; set; } = "";
    public Guid TenantId { get; set; }
    public bool Enabled { get; set; }
    public List<string> AllowedScopes { get; set; } = [];
}
public sealed class CrmOidcOrganization
{
    public Guid TenantId { get; set; }
    public string Slug { get; set; } = "";
    public string Region { get; set; } = "";
    public bool CrmEnabled { get; set; }
}
