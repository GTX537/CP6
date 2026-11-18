using System.Data;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.ErpIntegration;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Services;

public sealed record ErpBusinessPartnerResultProof(string ProofContract, ErpBusinessPartnerOriginal Original,
    string EventId, int ResultVersion, string EventType, string PayloadSha256, byte[] Payload);

/// <summary>在来源、账户服务和对象资格均可核实时，仅交付指定 Outbox 事件的持久化字节。</summary>
public sealed class ErpBusinessPartnerResultReader(IDbContextFactory<ErpIntegrationContext> factory,
    ErpBusinessPartnerOriginalReader originals, ErpIntegrationRuntime erp, CrmOidcOptions oidc,
    CrmIdentityRuntime? identity = null, AccountC03ServicePrincipalRegistryV1? accounts = null)
{
    public const string Contract = "C03-BP-RESULT-PROOF/1";

    public async Task<ErpBusinessPartnerResultProof?> ReadAsync(Guid tenant, string clientId, string tokenId,
        long issued, long expires, Guid account, Guid request, int requestVersion, string originalId, string eventId,
        int resultVersion, string expectedDigest, CancellationToken ct)
    {
        if (identity?.ContractBundleSha256 != CrmIdentityRuntime.AccountBundleSha256 ||
            identity.Options.Issuer != oidc.Issuer || !identity.Options.Tenants.ContainsKey(tenant) ||
            accounts is not { Enabled: true, IdentityPublicationEnabled: true } || !accounts.IsPublished(tenant)) return null;
        var client = accounts.Sources.SingleOrDefault(s => s.Enabled && s.Client == clientId && s.Tenant == tenant);
        if (client is null) return null;
        await using var queue = await factory.CreateDbContextAsync(ct);
        if (!queue.Database.IsNpgsql()) return null;
        await using var tx = await queue.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using var core = await DatabaseSharedContext.CreateAsync<CP6Context>(queue, DatabaseContextKind.Core,
            options => new(options, new TenantContext { CurrentTenantId = tenant }), ct);
        await AuthenticationCompletionGateV1.AcquireAsync(core, ct);
        await IdentitySourceAuthorityV1.RequireCurrentAsync(core, identity, tenant, ct);
        if (!await core.CrmIdentityBootstrapStates.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
            x.CompletedAtUtc != null && x.ContractBundleSha256 == identity.ContractBundleSha256, ct) ||
            await TenantPendingSuspensionV1.ExistsAsync(core, tenant, ct)) return null;
        var organization = await core.Sys_Tenants.AsNoTracking().Where(t => t.Id == tenant && t.Enable &&
            t.AuthenticationPolicyRevision != Guid.Empty).Select(t => new { t.ExpireDate }).SingleOrDefaultAsync(ct);
        var published = await AccountC03ServiceIdentityIssuer.ReadPublishedSourceAsync(core, client, ct);
        if (organization is null || published is null || issued <= published.Value.Item1.UpdatedAtUtc.ToUnixTimeSeconds() ||
            await core.Sys_Users.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant && x.Id == client.Subject, ct)) return null;

        // C01 令牌的撤销与本次披露互斥。只接受在当前账户服务来源发布之后签发的读取令牌。
        var token = await core.CrmServiceTokenRecords.FromSqlInterpolated($"""
            SELECT * FROM crm_identity."ServiceToken"
            WHERE "Issuer"=CAST({oidc.Issuer} AS bpchar) AND "Jti"=CAST({tokenId} AS bpchar)
              AND "TenantId"={tenant} AND "ClientId"=CAST({clientId} AS bpchar)
              AND "RevokedAtUtc" IS NULL AND "ExpiresAtUtc">clock_timestamp() FOR SHARE
            """).AsNoTracking().SingleOrDefaultAsync(ct);
        if (token is null) return null;

        var receipt = await queue.Database.SqlQuery<byte[]>($"""
            SELECT "Payload" AS "Value" FROM erp_integration."CommandInbox"
            WHERE "TenantId"={tenant} AND "MessageId"=CAST({originalId} AS bpchar)
              AND "ConflictCount"=0 AND "LastConflictSha256" IS NULL FOR SHARE
            """).ToListAsync(ct);
        if (receipt.Count != 1) return null;
        var original = await originals.ReadInContextAsync(queue, tenant, clientId, account, request, requestVersion, originalId, ct);
        if (original is null || ErpEventContracts.Hash(receipt[0]) != original.OriginalSha256) return null;
        using var command = JsonDocument.Parse(receipt[0]);
        var grants = await queue.Database.SqlQuery<DateTimeOffset>($"""
            SELECT valid_until AS "Value" FROM erp_integration."BusinessPartnerResultReadV1"
            WHERE tenant_id={tenant} AND client_id={clientId} AND subject_id={client.Subject}
              AND authentication_epoch={client.AuthenticationEpoch} AND source_epoch={original.Epoch}
              AND account_id={account} AND request_id={request} AND request_version={requestVersion}
              AND original_message_id={originalId} AND event_id={eventId} AND result_version={resultVersion}
              AND payload_sha256={expectedDigest} AND NOT revoked
              AND valid_from<=clock_timestamp() AND valid_until>clock_timestamp() FOR SHARE
            """).ToListAsync(ct);
        if (grants.Count != 1) return null;

        var message = await queue.Set<Cp6OutboxMessage>().FromSqlInterpolated($"""
            SELECT * FROM erp_integration."Cp6_OutboxMessage"
            WHERE "TenantId"={tenant} AND "MessageId"=CAST({eventId} AS bpchar)
              AND "TopicName"=CAST({ErpEventContracts.ResultTopic} AS bpchar) FOR SHARE
            """).AsNoTracking().SingleOrDefaultAsync(ct);
        if (message is null || message.CausationId != originalId ||
            message.CorrelationId != command.RootElement.GetProperty("correlationid").GetString() ||
            message.AggregateId != account.ToString("D") ||
            message.AggregateVersion != resultVersion || message.PayloadSha256 != expectedDigest ||
            ErpEventContracts.Hash(message.Payload) != expectedDigest) return null;
        var envelope = new Cp6OutboxEnvelope(eventId, tenant, message.TopicName, message.PartitionKey, message.Payload,
            message.CorrelationId, message.CausationId, message.AggregateId, resultVersion);
        if (!erp.Validator.Validate(envelope).IsValid) return null;
        using var document = JsonDocument.Parse(message.Payload);
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString();
        var data = root.GetProperty("data");
        if (type is not (ErpEventContracts.BusinessPartnerSynchronized or ErpEventContracts.BusinessPartnerFailed) ||
            root.GetProperty("schemaversion").GetString() != original.ContractVersion ||
            root.GetProperty("region").GetString() != erp.Options.Tenants[tenant] ||
            data.GetProperty("requestId").GetGuid() != request || data.GetProperty("requestVersion").GetInt32() != requestVersion)
            return null;
        var occurred = root.GetProperty("time").GetDateTimeOffset();
        if (!erp.Options.BusinessPartnerOriginalSources.Any(s => s.TenantId == tenant && s.Epoch == original.Epoch &&
            s.FromUtc <= occurred && occurred < s.UntilUtc)) return null;

        var now = await queue.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        if (now >= grants[0] || now >= token.ExpiresAtUtc || now.ToUnixTimeSeconds() >= expires ||
            issued > now.ToUnixTimeSeconds() || organization.ExpireDate is { } end &&
            now >= new DateTimeOffset(DateTime.SpecifyKind(end, DateTimeKind.Local))) return null;
        await tx.CommitAsync(ct);
        return new(Contract, original, eventId, resultVersion, type, expectedDigest, message.Payload);
    }
}
