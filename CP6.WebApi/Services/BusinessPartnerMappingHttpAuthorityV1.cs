using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using NSec.Cryptography;

namespace CP6.WebApi.Services;

internal sealed class BusinessPartnerMappingHttpAuthorityV1(HttpClient http, IConfiguration configuration, IWebHostEnvironment environment)
    : BusinessPartnerMappingAuthorityV1
{
    private const int MaxOriginalBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan OwnerTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly SignatureAlgorithm Algorithm = SignatureAlgorithm.Ed25519;

    public override async Task<BusinessPartnerMappingOriginalV1?> ReadAsync(BusinessPartnerMappingAuthorityBindingV1 binding,
        string operation, byte[] requestBody, CancellationToken ct)
    {
        var path = (binding.Purpose, operation) switch
        {
            ("ACCOUNT_IDENTITY", "ACCOUNT") => "/internal/crm/v1/account-identities/read",
            ("BP_MAPPING_DECISION", "DECISION_EVIDENCE") => "/internal/main/v1/bp/decision-evidence/read",
            ("BP_MAPPING_DECISION", "CORRECTION_EVIDENCE") => "/internal/main/v1/bp/mapping-issuance-evidence/read",
            ("CRM_PROJECTION", "PROJECTION") => "/internal/crm/v1/account-erp-mapping-projections/read",
            ("CRM_PROJECTION", "CONFLICTS") => "/internal/crm/v1/account-erp-mapping-conflicts/read",
            _ => null
        };
        if (path is null) return null;
        using var request = Request(binding, path, requestBody);
        if (request is null) return null;
        var nonce = Guid.NewGuid().ToString("D");
        request.Headers.Add("X-BP-Owner-Nonce", nonce);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(OwnerTimeout);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/json")
                return null;
            var body = await ReadBounded(response.Content, timeout.Token);
            if (body is null || !response.Headers.TryGetValues("X-BP-Owner-Proof", out var headers)) return null;
            var values = headers.Take(2).ToArray();
            if (values.Length != 1 || values[0].Length > 100) return null;
            var signature = Convert.FromBase64String(values[0]);
            var bodyHash = Hex(body);
            var requestHash = Hex(requestBody);
            var signed = SigningBytes(binding, path, nonce, requestHash, bodyHash);
            if (!VerifyDecision(binding, signed, signature)) return null;
            var trust = JsonSerializer.SerializeToUtf8Bytes(new
            {
                binding.TenantId, binding.OrganizationId, binding.LegalEntityKey, binding.Purpose,
                binding.Authority, binding.Epoch, binding.KeyId, path, nonce, requestHash, bodyHash,
                signatureBase64 = values[0], algorithm = "Ed25519"
            }, Json);
            return new(body, trust);
        }
        catch (Exception ex) when (TransportUnknown(ex, ct)) { return null; }
    }

    public override async Task<BusinessPartnerMappingDeliveryResultV1?> DeliverAsync(BusinessPartnerMappingAuthorityBindingV1 binding,
        byte[] originalBody, string bodyHash, CancellationToken ct)
    {
        if (binding.Purpose != "CRM_PROJECTION" || Hex(originalBody) != bodyHash) return null;
        using var request = Request(binding, "/internal/crm/v1/account-erp-mappings", originalBody);
        if (request is null) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(OwnerTimeout);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var body = await ReadBounded(response.Content, timeout.Token);
            return new((int)response.StatusCode, body is null ? null : Hex(body), body);
        }
        catch (Exception ex) when (TransportUnknown(ex, ct)) { return null; }
    }

    public override byte[]? SignDecision(BusinessPartnerMappingAuthorityBindingV1 binding, byte[] signingBytes)
    {
        if (binding.Purpose != "ACC_MAPPING_AUTHORITY") return null;
        var encoded = configuration[$"BusinessPartnerMapping:Routes:{binding.RouteKey}:SigningKey"];
        if (string.IsNullOrEmpty(encoded)) return null;
        byte[] material;
        try { material = Convert.FromBase64String(encoded); }
        catch (FormatException) { return null; }
        try
        {
            using var key = Key.Import(Algorithm, material, KeyBlobFormat.RawPrivateKey);
            if (!CryptographicOperations.FixedTimeEquals(key.PublicKey.Export(KeyBlobFormat.RawPublicKey), binding.PublicKey))
                return null;
            return Algorithm.Sign(key, signingBytes);
        }
        catch (FormatException) { return null; }
        finally { CryptographicOperations.ZeroMemory(material); }
    }

    public override bool VerifyDecision(BusinessPartnerMappingAuthorityBindingV1 binding, byte[] signingBytes, byte[] signature)
        => PublicKey.TryImport(Algorithm, binding.PublicKey, KeyBlobFormat.RawPublicKey, out var key) && key is not null &&
           Algorithm.Verify(key, signingBytes, signature);

    internal static byte[] SigningBytes(BusinessPartnerMappingAuthorityBindingV1 binding, string path, string nonce,
        string requestHash, string bodyHash)
        => Encoding.UTF8.GetBytes(string.Join('\n', "BP-OWNER-ORIGINAL/1", binding.TenantId.ToString("D"),
            binding.OrganizationId.ToString("D"), binding.LegalEntityKey, binding.Purpose, binding.Authority,
            binding.Epoch, binding.KeyId, path, nonce, requestHash, bodyHash));

    private HttpRequestMessage? Request(BusinessPartnerMappingAuthorityBindingV1 binding, string path, byte[] body)
    {
        var section = configuration.GetSection($"BusinessPartnerMapping:Routes:{binding.RouteKey}");
        if (!Uri.TryCreate(section["Endpoint"], UriKind.Absolute, out var endpoint) ||
            endpoint.UserInfo.Length != 0 ||
            endpoint.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() && endpoint.IsLoopback && endpoint.Scheme == Uri.UriSchemeHttp))
            return null;
        var token = section["Bearer"];
        if (string.IsNullOrWhiteSpace(token)) return null;
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, path)) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-BP-Legal-Entity", binding.LegalEntityKey);
        return request;
    }

    private static async Task<byte[]?> ReadBounded(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength > MaxOriginalBytes) return null;
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + count > MaxOriginalBytes) return null;
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

    private static bool TransportUnknown(Exception ex, CancellationToken ct)
        => ex is HttpRequestException or IOException or FormatException ||
           ex is OperationCanceledException && !ct.IsCancellationRequested;

    private static string Hex(byte[] body) => Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
}
