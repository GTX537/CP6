using System.Text.Json;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.ErpIntegration;

/// <summary>由 Main.C03 登记的原件来源期间，以收件账的服务器时间确定所属 epoch。</summary>
public sealed record ErpOriginalSourcePeriod(Guid TenantId, string Epoch, DateTimeOffset FromUtc,
    DateTimeOffset UntilUtc, string[] ReaderClientIds);

public sealed record ErpBusinessPartnerOriginal(string ProofContract, string Authority, string ContractId,
    string ContractVersion, string Epoch, Guid TenantId, Guid AccountId, Guid RequestId, int RequestVersion,
    string OriginalMessageId, string OriginalSha256, string InputSha256);

public sealed class ErpBusinessPartnerOriginalReader(IDbContextFactory<ErpIntegrationContext> factory,
    ErpIntegrationRuntime runtime)
{
    public const string ProofContract = "C03-BP-REQUEST-PROOF/1";
    public const string ContractId = "ACC-C03-V1";

    public async Task<ErpBusinessPartnerOriginal?> ReadAsync(Guid tenant, string authenticatedClient,
        Guid account, Guid requestId, int requestVersion, string originalId, CancellationToken ct)
    {
        if (!runtime.Options.Tenants.TryGetValue(tenant, out var region) ||
            !runtime.Options.ReaderClientIds.Contains(authenticatedClient, StringComparer.Ordinal) ||
            account == Guid.Empty || requestId == Guid.Empty || requestVersion < 1 ||
            string.IsNullOrWhiteSpace(originalId) || originalId.Length > 128) return null;

        await using var db = await factory.CreateDbContextAsync(ct);
        if (!db.Database.IsNpgsql()) return null;
        // 单条查询绑定受信租户和原业务键，两份账在同一 PostgreSQL 快照中读取。
        var pair = await (from receipt in db.Inbox.AsNoTracking()
                          join request in db.Requests.AsNoTracking() on
                              new { receipt.TenantId, receipt.AggregateId } equals new { request.TenantId, request.AggregateId }
                          where receipt.TenantId == tenant && receipt.MessageId == originalId &&
                              receipt.EventType == ErpEventContracts.BusinessPartnerRequested &&
                              receipt.AggregateId == account && receipt.ConflictCount == 0 &&
                              receipt.LastConflictSha256 == null && request.Kind == ErpRequestKind.BusinessPartner &&
                              request.AccountId == account && request.RequestId == requestId && request.RequestVersion == requestVersion
                          select new { receipt, request.InputSha256 }).SingleOrDefaultAsync(ct);
        if (pair is null) return null;
        var original = pair.receipt;
        var source = runtime.Options.BusinessPartnerOriginalSources.SingleOrDefault(s => s.TenantId == tenant &&
            s.FromUtc <= original.ReceivedAtUtc && original.ReceivedAtUtc < s.UntilUtc);
        if (source is null || !source.ReaderClientIds.Contains(authenticatedClient, StringComparer.Ordinal) ||
            ErpEventContracts.Hash(original.Payload) != original.PayloadSha256) return null;

        var delivery = new Cp6InboxDelivery("cp6-erp-commands-v1", originalId, tenant, ErpEventContracts.RequestTopic,
            $"{tenant:D}:{account:D}", original.Payload, account.ToString("D"), original.AggregateVersion);
        if (!runtime.Validator.Validate(delivery).IsValid) return null;
        using var document = JsonDocument.Parse(original.Payload);
        var root = document.RootElement;
        if (root.GetProperty("type").GetString() != ErpEventContracts.BusinessPartnerRequested ||
            root.GetProperty("source").GetString() != ErpEventContracts.RequestSource ||
            root.GetProperty("region").GetString() != region || root.GetProperty("schemaversion").GetString() != "1.0.0") return null;
        var command = root.GetProperty("data").Deserialize<ErpBusinessPartnerRequested>(ErpEventContracts.Json);
        if (command != new ErpBusinessPartnerRequested(tenant, requestId, account, requestVersion) ||
            ErpEventContracts.BusinessPartnerInputSha256(command) != pair.InputSha256) return null;

        return new(ProofContract, ErpEventContracts.ResultSource, ContractId, "1.0.0", source.Epoch, tenant,
            account, requestId, requestVersion, originalId, original.PayloadSha256, pair.InputSha256);
    }
}
