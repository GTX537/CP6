using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;

namespace CP6.WebApi.Services;

/// <summary>用当前服务登记签发一次账户收件身份。账户操作权与对象范围由 CRM 在最终事务内决定。</summary>
public sealed class AccountC03ServiceIdentityIssuer(CP6Context db, ITenantContext tenant,
    AccountC03ServicePrincipalRegistryV1 registry, CrmIdentityRuntime runtime, CrmOidcOptions oidc, CrmOidcCrypto crypto)
{
    public const string Contract = "account-c03-service-identity/v1";
    public const string Audience = "urn:cp6:crm:account-c03-ingest";
    public const string Purpose = "service:account.c03.ingest";

    public async Task<string> IssueAsync(string nonce, StringValues authorization, CancellationToken ct)
    {
        if (!InquiryPermissionSigningKeyV1.ValidServiceNonce(nonce))
            throw new InquiryPermissionExceptionV1(400, "invalid_request");
        var client = registry.Authenticate(authorization) ?? throw new InquiryPermissionExceptionV1(401, "invalid_client");
        if (!registry.IdentityPublicationEnabled || !registry.IsPublished(client.Tenant) || !db.Database.IsNpgsql() ||
            db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null ||
            db.ChangeTracker.Entries().Any() || runtime.ContractBundleSha256 != CrmIdentityRuntime.AccountBundleSha256 ||
            runtime.Options.Issuer != oidc.Issuer || !runtime.Options.Tenants.ContainsKey(client.Tenant))
            throw new InquiryPermissionExceptionV1(503, "account_c03_identity_unavailable");
        var previous = tenant.CurrentTenantId;
        try
        {
            tenant.CurrentTenantId = client.Tenant;
            if (db.CurrentTenantId != client.Tenant) throw new InquiryPermissionExceptionV1(503, "account_c03_identity_unavailable");
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            await IdentitySourceAuthorityV1.RequireCurrentAsync(db, runtime, client.Tenant, ct);
            var now = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
            var organization = await db.Sys_Tenants.AsNoTracking().Where(x => x.Id == client.Tenant && x.Enable &&
                x.AuthenticationPolicyRevision != Guid.Empty).Select(x => new { x.ExpireDate }).SingleOrDefaultAsync(ct);
            var expires = now.AddSeconds(60);
            if (organization?.ExpireDate is { } end)
            {
                var deadline = new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Local));
                if (deadline < expires) expires = deadline;
            }
            if (organization is null || expires <= now || await TenantPendingSuspensionV1.ExistsAsync(db, client.Tenant, ct) ||
                await db.Sys_Users.IgnoreQueryFilters().AnyAsync(x => x.TenantId == client.Tenant && x.Id == client.Subject, ct))
                throw new InquiryPermissionExceptionV1(403, "account_c03_identity_denied");
            if (!await db.CrmIdentityBootstrapStates.AsNoTracking().AnyAsync(x => x.TenantId == client.Tenant &&
                x.CompletedAtUtc != null && x.ContractBundleSha256 == runtime.ContractBundleSha256, ct))
                throw new InquiryPermissionExceptionV1(503, "account_c03_identity_unavailable");
            var published = await ReadPublishedSourceAsync(db, client, ct)
                ?? throw new InquiryPermissionExceptionV1(403, "account_c03_identity_denied");
            var (row, source) = published;

            // 签入当前角色、部门及租户的来源摘要，收件方须与持锁发布逐项对应。
            var expected = source.Roles.Select(role => "permission:" + role)
                .Append("department:" + client.Department.ToString("D")).Append("tenant:" + client.Tenant.ToString("D")).ToArray();
            var facts = await db.CrmIdentitySnapshots.AsNoTracking().Where(x => x.TenantId == client.Tenant && expected.Contains(x.AggregateId))
                .Select(x => new { x.AggregateId, x.Version, x.PayloadSha256 }).ToListAsync(ct);
            if (facts.Count != expected.Length) throw new InquiryPermissionExceptionV1(503, "account_c03_identity_unavailable");
            var factsDigest = IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(string.Join("\n", facts.OrderBy(x => x.AggregateId, StringComparer.Ordinal)
                .Select(x => string.Concat(x.AggregateId, "/", x.Version.ToString(CultureInfo.InvariantCulture), "/", x.PayloadSha256)))));
            var jwt = crypto.Sign(Audience, [
                new Claim("sub", client.Subject.ToString("D")), new Claim("client_id", client.Client),
                new Claim("tenant_id", client.Tenant.ToString("D")), new Claim("authentication_epoch", client.AuthenticationEpoch.ToString("D")),
                new Claim("purpose", Purpose), new Claim("nonce", nonce), new Claim("source_profile", source.SourceProfile),
                new Claim("source_version", row.Version.ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer32),
                new Claim("source_sha256", row.PayloadSha256), new Claim("facts_sha256", factsDigest),
                new Claim("bundle_sha256", runtime.ContractBundleSha256)
            ], expires, Contract);
            await tx.CommitAsync(ct);
            return jwt;
        }
        finally { tenant.CurrentTenantId = previous; }
    }

    // 签发与原件读取均在同一认证完成锁下核对实际发布。
    internal static async Task<(CP6.Entity.DomainModels.Sys.CrmIdentitySnapshot, ServicePrincipalIdentityData)?>
        ReadPublishedSourceAsync(CP6Context db, ServicePrincipalRegistryV1.Registration client, CancellationToken ct)
    {
        var aggregate = "service-principal:" + client.Subject.ToString("D");
        var row = await db.CrmIdentitySnapshots.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantId == client.Tenant && x.AggregateId == aggregate, ct);
        if (row is null || row.IsDeleted || row.EventType != IdentityEventContracts.AccountServicePrincipalChanged ||
            row.PayloadSha256 != IdentityEventContracts.Hash(Encoding.UTF8.GetBytes(row.PayloadJson)))
            return null;
        var source = JsonSerializer.Deserialize<ServicePrincipalIdentityData>(row.PayloadJson, IdentityEventContracts.Json);
        if (source is null || !source.Enabled || source.SourceProfile != IdentityEventContracts.AccountServiceProfile ||
            source.Version != row.Version || source.TenantId != client.Tenant || source.ClientId != client.Client ||
            source.PrincipalId != client.Subject || source.DepartmentId != client.Department ||
            source.AuthenticationEpoch != client.AuthenticationEpoch || source.RegistrationVersion != client.Version ||
            source.RegistrationRevision != client.Revision || !source.Roles.SequenceEqual(client.Roles.Select(IdentityEventContracts.RoleSubject)))
            return null;
        return (row, source);
    }
}
