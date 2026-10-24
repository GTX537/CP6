using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using NSec.Cryptography;

namespace CP6.WebApi.Services;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InquiryHumanContextRequestV1(string Purpose, Guid SiteId, Guid FormId, string RequestNonce);
internal sealed record InquiryHumanSourceV1(CrmIdentitySnapshot User, Guid Epoch, IReadOnlyList<CrmIdentitySnapshot> Roles);

public sealed partial class InquiryPermissionIssuerV1
{
    public bool HumanContextEnabled => Enabled && identityRuntime?.Options.Enabled == true;
    public Task<string> IssueHumanContextAsync(InquiryHumanContextRequestV1 request,
        Func<CP6Context, CancellationToken, Task<InquiryPermissionSubjectV1?>> currentBearer, CancellationToken ct)
    {
        if (request is null || !InquiryPermissionSigningKeyV1.ValidServiceNonce(request.RequestNonce))
            throw new InquiryPermissionExceptionV1(400, "invalid_request");
        if (request.Purpose is not ("intake:list" or "intake:detail" or "intake:release" or "intake:reject"))
            throw new InquiryPermissionExceptionV1(400, "unsupported_projection_purpose");
        if (!HumanContextEnabled) throw new InquiryPermissionExceptionV1(503, "inquiry_human_context_unavailable");
        return IssueCoreAsync(new(request.Purpose, request.SiteId, request.FormId), currentBearer, request.RequestNonce, ct);
    }

    // Caller holds the same Main authentication root as the permission and final Bearer recheck.
    // Read committed sources only; do not repair publications while issuing credentials.
    private async Task<InquiryHumanSourceV1> ReadHumanSource(InquiryPermissionSubjectV1 subject, CancellationToken ct)
    {
        static InquiryPermissionExceptionV1 Unavailable() => new(503, "inquiry_human_source_unavailable");
        if (identityRuntime is null || identityRuntime.Options.Issuer != signing.Issuer
            || !identityRuntime.Options.Tenants.ContainsKey(subject.TenantId)
            || !await db.CrmIdentityBootstrapStates.AsNoTracking().AnyAsync(x => x.TenantId == subject.TenantId
                && x.CompletedAtUtc != null && x.ContractBundleSha256 == identityRuntime.ContractBundleSha256, ct)) throw Unavailable();
        var user = await db.Sys_Users.AsNoTracking().SingleAsync(x => x.Id == subject.SubjectId, ct);
        var assigned = await db.Sys_UserRoles.AsNoTracking().Where(x => x.UserId == subject.SubjectId).Select(x => x.RoleId).ToListAsync(ct);
        if (user.RoleId is {} primary) assigned.Add(primary);
        var roles = assigned.Distinct().Order().Select(IdentityEventContracts.RoleSubject).ToArray();
        var source = await db.CrmIdentitySnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == subject.TenantId
            && x.AggregateId == "user:" + subject.SubjectId.ToString("D"), ct);
        if (source is null || source.IsDeleted || source.EventType is not (IdentityEventContracts.UserChanged or IdentityEventContracts.UserChangedV2)
            || source.Version < 1 || source.PayloadSha256 != IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(source.PayloadJson))) throw Unavailable();
        UserIdentityData? data;
        try { data = JsonSerializer.Deserialize<UserIdentityData>(source.PayloadJson, IdentityEventContracts.Json); }
        catch (JsonException) { throw Unavailable(); }
        if (data is null || !user.Enable || !data.Enabled || data.TenantId != subject.TenantId || data.UserId != subject.SubjectId
            || data.Version != source.Version || data.AuthenticationEpoch != user.AuthenticationEpoch || user.AuthenticationEpoch == Guid.Empty
            || data.DeptId != user.DeptId || !data.Roles.SequenceEqual(roles)) throw Unavailable();
        var aggregates = roles.Select(x => "permission:" + x).ToArray();
        var sources = await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == subject.TenantId && aggregates.Contains(x.AggregateId)).ToArrayAsync(ct);
        // Include inactive and zero-grant roles: omission would hide a source-version change.
        if (sources.Length != aggregates.Length || sources.Any(x => x.Version < 1
            || x.EventType is not (IdentityEventContracts.PermissionChanged or IdentityEventContracts.PermissionChangedV2 or IdentityEventContracts.PermissionChangedV3)
            || x.PayloadSha256 != IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(x.PayloadJson)))) throw Unavailable();
        return new(source, user.AuthenticationEpoch, sources);
    }
}

public sealed partial class InquiryPermissionSigningKeyV1
{
    public const string HumanContextVersion = "cp6-inquiry-human-context-v1";
    internal string SignHumanContext(string projection, string nonce, InquiryHumanSourceV1 source, string bundle,
        InquiryPermissionRequestV1 request, DateTimeOffset issued, DateTimeOffset expires)
    {
        if (!Enabled || key is null || revoked || issued < from || until is {} end && issued >= end)
            throw new InquiryPermissionExceptionV1(503, "inquiry_signer_unavailable");
        if (source.Epoch == Guid.Empty) throw new InquiryPermissionExceptionV1(422, "inquiry_projection_identity_not_representable");
        static JsonObject Ref(CrmIdentitySnapshot row) => new()
        { ["aggregateId"] = row.AggregateId, ["eventType"] = row.EventType, ["version"] = row.Version,
          ["payloadSha256"] = row.PayloadSha256, ["isDeleted"] = row.IsDeleted };
        var body = new JsonObject
        {
            ["schemaVersion"] = HumanContextVersion, ["issuer"] = issuer, ["keyId"] = keyId, ["algorithm"] = "Ed25519",
            ["catalogVersion"] = CatalogVersion, ["requestNonce"] = nonce, ["organizationId"] = source.User.TenantId.ToString("D"),
            ["subjectId"] = source.User.AggregateId[5..], ["authenticationEpoch"] = source.Epoch.ToString("D"),
            ["purpose"] = request.Purpose, ["siteId"] = request.SiteId.ToString("D"), ["formId"] = request.FormId.ToString("D"),
            ["issuedAtUtc"] = Wire(issued), ["expiresAtUtc"] = Wire(expires), ["contractBundleSha256"] = bundle,
            ["permissionJson"] = projection, ["userSource"] = Ref(source.User),
            ["roleSources"] = new JsonArray(source.Roles.OrderBy(x => x.AggregateId, StringComparer.Ordinal).Select(x => (JsonNode?)Ref(x)).ToArray())
        };
        static JsonNode? Sort(JsonNode? value) => value switch
        {
            JsonObject o => new JsonObject(o.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new KeyValuePair<string, JsonNode?>(x.Key, Sort(x.Value)))),
            JsonArray a => new JsonArray(a.Select(Sort).ToArray()), _ => value?.DeepClone()
        };
        var bytes = Encoding.UTF8.GetBytes(HumanContextVersion + "\n" + Sort(body)!.ToJsonString());
        body["signature"] = Convert.ToBase64String(SignatureAlgorithm.Ed25519.Sign(key, bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return body.ToJsonString();
    }
}
