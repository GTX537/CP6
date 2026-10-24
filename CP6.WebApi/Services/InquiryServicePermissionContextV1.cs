using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Entity.DomainModels.Sys;
using NSec.Cryptography;

namespace CP6.WebApi.Services;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InquiryServiceContextRequestV1(string Purpose, Guid SiteId, Guid FormId, string RequestNonce);

public sealed partial class InquiryPermissionSigningKeyV1
{
    internal string Issuer => issuer;
    public const string ServiceContextVersion = "cp6-inquiry-service-context-v1";
    internal static bool ValidServiceNonce(string? nonce) => nonce is not null && Regex.IsMatch(nonce, "\\A[A-Za-z0-9_-]{43}\\z")
        && Convert.ToBase64String(Convert.FromBase64String(nonce.Replace('-', '+').Replace('_', '/') + "="))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_') == nonce;

    // The outer signature binds the exact unchanged R3 wire value to the actual authenticated
    // client and committed source. Domain separation prevents reuse as a permission signature.
    internal string SignServiceContext(string? projection, string nonce, InquiryServicePrincipalRegistryV1.Registration source,
        CrmIdentitySnapshot row, IReadOnlyList<CrmIdentitySnapshot> roles, string bundleSha256, InquiryPermissionRequestV1 request, DateTimeOffset issued, DateTimeOffset expires)
    {
        if (!Enabled || key is null || revoked || issued < from || until is { } end && issued >= end)
            throw new InquiryPermissionExceptionV1(503,"inquiry_signer_unavailable");
        if (!WireId(source.Tenant) || !WireId(source.Subject) || !WireId(source.AuthenticationEpoch) || !WireId(request.SiteId) || !WireId(request.FormId))
            throw new InquiryPermissionExceptionV1(422,"inquiry_projection_identity_not_representable");
        if (expires <= issued || expires-issued > TimeSpan.FromMinutes(5))
            throw new InquiryPermissionExceptionV1(401,"invalid_token");
        var body = new JsonObject
        {
            ["schemaVersion"] = ServiceContextVersion, ["issuer"] = issuer, ["keyId"] = keyId,
            ["algorithm"] = "Ed25519", ["catalogVersion"] = CatalogVersion,
            ["requestNonce"] = nonce, ["clientId"] = source.Client,
            ["purpose"] = request.Purpose, ["siteId"] = request.SiteId.ToString("D"), ["formId"] = request.FormId.ToString("D"),
            ["issuedAtUtc"] = issued.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",System.Globalization.CultureInfo.InvariantCulture),
            ["expiresAtUtc"] = expires.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",System.Globalization.CultureInfo.InvariantCulture),
            ["organizationId"] = source.Tenant.ToString("D"), ["subjectId"] = source.Subject.ToString("D"),
            ["authenticationEpoch"] = source.AuthenticationEpoch.ToString("D"),
            ["registrationVersion"] = source.Version, ["registrationRevision"] = source.Revision,
            ["sourceVersion"] = row.Version, ["sourcePayloadSha256"] = row.PayloadSha256,
            ["contractBundleSha256"] = bundleSha256, ["permissionJson"] = projection,
            ["roleSources"] = new JsonArray(roles.OrderBy(x => x.AggregateId, StringComparer.Ordinal).Select(x => (JsonNode?)new JsonObject
            {
                ["aggregateId"] = x.AggregateId, ["eventType"] = x.EventType,
                ["version"] = x.Version, ["payloadSha256"] = x.PayloadSha256
            }).ToArray())
        };
        static JsonNode? Sort(JsonNode? value) => value switch
        {
            JsonObject obj => new JsonObject(obj.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new KeyValuePair<string, JsonNode?>(x.Key, Sort(x.Value)))),
            JsonArray array => new JsonArray(array.Select(Sort).ToArray()),
            _ => value?.DeepClone()
        };
        var ordered = Sort(body)!;
        var bytes = Encoding.UTF8.GetBytes(ServiceContextVersion + "\n" + ordered.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        body["signature"] = Convert.ToBase64String(SignatureAlgorithm.Ed25519.Sign(key!, bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return body.ToJsonString();
    }
}
