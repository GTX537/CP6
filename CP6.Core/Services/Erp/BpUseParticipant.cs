using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Integration;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

internal sealed record BpUseSourceRef(string Owner, string Id, string Version, string Digest, string Purpose);
internal sealed record BpUseReference(Guid BpId, string BpCd, string LegalEntityKey, string Revision, string ContentHash);
internal sealed record BpUseMapping(Guid AccountId, string LegalEntityKey, string MappingRevision, string State,
    string BusinessPartnerKey, string SemanticHash);
internal sealed record BpUseMappingProof(DbTransaction Transaction, Guid Tenant, Guid Partner, long ExpectedRevision,
    BpUseMapping Current, bool OwnerConflict);
internal sealed record BpUseFinanceAuthority(OperationsAccessV1 Access, BusinessPartnerFinanceReferenceReader Reader, Guid Context);
internal sealed record BpUseFinanceGroup(Guid BpId, long Revision, BusinessPartnerFinanceUseV1[] Links);
internal sealed record VerifiedBpUseRequest(Guid Tenant, string LegalEntityKey, Guid BpId, Guid? AccountId,
    string Purpose, BpUseSourceRef Consumer, long ExpectedBpRevision, long? ExpectedMappingRevision, Guid UseKey,
    string Actor, string BusinessEffectKey, BpUseMappingProof? MappingProof = null, BpUseFinanceAuthority? Finance = null);
internal sealed record BpUseReceipt(Guid UseKey, BpUseReference Bp, Guid? AccountId, BpUseMapping? Mapping, string Purpose,
    BpUseSourceRef Consumer, Dictionary<string, bool> Roles, string Currency, BpUseSourceRef[] DependencyRefs,
    string ValidatedAt, string BusinessEffectKey, string ReceiptHash)
{
    public bool IsReusablePermit => false;
}

internal sealed record BpUseResult(BpUseReceipt Receipt, bool Replayed);

/// <summary>I02 在真实下游事务内检查并保存一次用途凭证。协调者持有最终提交责任。</summary>
internal sealed class BpUseParticipant(CP6Context db, IDataProtectionProvider protection, TimeProvider clock)
{
    internal const string SalesOwner = "Main.Sales", PurchaseOwner = "Main.Pur";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Stored
    {
        public byte[] Fingerprint { get; init; } = [];
        public string Body { get; init; } = "";
    }

    internal async Task<BpUseResult> EnlistAsync(DbTransaction tx, VerifiedBpUseRequest request, CancellationToken ct)
    {
        BpUseScopeGate.RequireTransaction(db, tx, serializable: true);
        var role = RequireRequest(request);
        if (!await BpUseScopeGate.IsCutoverAsync(db, ct)) throw BpUseScopeGate.Unproven();
        await BpUseScopeGate.RequireBeforeEstimateAndQuoteAsync(db, ct);
        await BpUseScopeGate.HoldIamAsync(db, ct);
        await BpUseScopeGate.LockAsync(db, request.LegalEntityKey, false, ct);
        // 保持同一物理数据库的安装事实和 Scope 版本直至外层事务结束。
        await db.Database.ExecuteSqlRawAsync("""LOCK TABLE public."T_WebBusinessPartner" IN ROW SHARE MODE""", ct);
        await BpUseScopeGate.RequireGuardsAsync(db, ct);
        var scope = await db.Database.SqlQuery<bool>($"""
            SELECT s.enabled AS "Value"
              FROM main_bp.scopes s JOIN main_bp.use_writer_scopes w USING(tenant_id,legal_entity_key)
            WHERE s.tenant_id={request.Tenant} AND s.legal_entity_key={request.LegalEntityKey}
                AND w.primary_writer='WORKSPACE' AND s.coverage_complete
            FOR SHARE OF s,w
            """).ToArrayAsync(ct);

        var fingerprint = SHA256.HashData(Canonical(new
        {
            request.Tenant, request.LegalEntityKey, request.BpId, request.AccountId, request.Purpose, request.Consumer,
            request.ExpectedBpRevision, request.ExpectedMappingRevision, request.UseKey, request.Actor, request.BusinessEffectKey
        }));
        var prior = await db.Database.SqlQuery<Stored>($"""
            SELECT fingerprint AS "Fingerprint",receipt AS "Body" FROM main_bp.use_receipts
            WHERE tenant_id={request.Tenant} AND consumer_owner={request.Consumer.Owner} AND use_key={request.UseKey}
            """).SingleOrDefaultAsync(ct);
        if (prior is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(prior.Fingerprint, fingerprint)) throw Conflict();
            return new(ReadOriginal(prior.Body), true);
        }

        if (scope is not [true]) throw BpUseScopeGate.Unproven();
        if (await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM main_bp.use_receipts
             WHERE tenant_id={request.Tenant} AND consumer_owner={request.Consumer.Owner}
                AND consumer_id={request.Consumer.Id} AND consumer_version={request.Consumer.Version}
                AND purpose={request.Purpose}) AS "Value"
            """).SingleAsync(ct)) throw Conflict();

        var mapping = VerifyMapping(tx, request);
        BusinessPartnerWorkspaceV1.UsePartner partner;
        try
        {
            partner = await BusinessPartnerWorkspaceV1.ReadUsePartnerAsync(db, protection, request.BpId,
                request.LegalEntityKey, role, request.ExpectedBpRevision, ct);
        }
        catch (Exception error) when (error is CryptographicException or JsonException)
        {
            throw BpUseScopeGate.Unproven();
        }
        if (mapping is not null && mapping.BusinessPartnerKey != partner.Code) throw BpUseScopeGate.Unproven();
        var dependencies = partner.Dependencies.ToList();
        foreach (var group in partner.FinanceGroups)
        {
            if (request.Finance is not { } finance) throw BpUseScopeGate.Unproven();
            var proof = await finance.Reader.EnlistAsync(finance.Access, tx, group.BpId, finance.Context,
                Text(group.Revision), group.Links, ct);
            if (proof.Decision != "VALID" || !proof.HeldUntilTransactionEnd) throw BpUseScopeGate.Unproven();
            // 信用资料仅证明当前来源。下游信用 Owner 仍决定该商业动作。
            dependencies.AddRange(group.Links.Select(r =>
                new BpUseSourceRef(r.Reference.Owner, r.Reference.Id, r.Reference.Version, r.Reference.Digest, r.Reference.Purpose)));
        }

        var recorded = clock.GetUtcNow().ToUniversalTime();
        var receipt = new BpUseReceipt(request.UseKey,
            new(request.BpId, partner.Code, partner.Legal, Text(partner.Revision), partner.Hash), request.AccountId,
            mapping, request.Purpose, request.Consumer, partner.Roles, partner.Currency, dependencies.ToArray(),
            recorded.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            request.BusinessEffectKey, "");
        receipt = receipt with { ReceiptHash = ReceiptDigest(receipt) };
        var raw = JsonSerializer.Serialize(receipt, Json);
        if (await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO main_bp.use_receipts (tenant_id,consumer_owner,use_key,legal_entity_key,bp_id,bp_revision,
                account_id,mapping_revision,purpose,consumer_id,consumer_version,business_effect_key,actor_display,recorded_at,fingerprint,receipt)
            VALUES ({request.Tenant},{request.Consumer.Owner},{request.UseKey},{request.LegalEntityKey},{request.BpId},
                {partner.Revision},{request.AccountId},{request.ExpectedMappingRevision},{request.Purpose},{request.Consumer.Id},
                {request.Consumer.Version},{request.BusinessEffectKey},{request.Actor},{recorded},{fingerprint},{raw})
            ON CONFLICT DO NOTHING
            """, ct) != 1) throw Conflict();
        return new(receipt, false);
    }

    internal static BpUseReceipt ReadOriginal(string raw)
    {
        var original = JsonSerializer.Deserialize<BpUseReceipt>(raw, Json);
        if (original is null || !string.Equals(original.ReceiptHash, ReceiptDigest(original), StringComparison.Ordinal))
            throw BpUseScopeGate.Unproven();
        return original;
    }

    private static string ReceiptDigest(BpUseReceipt receipt) => Hash(new
    {
        receipt.UseKey, receipt.Bp, receipt.AccountId, receipt.Mapping, receipt.Purpose, receipt.Consumer, receipt.Roles,
        receipt.Currency, receipt.DependencyRefs, receipt.ValidatedAt, receipt.BusinessEffectKey, receipt.IsReusablePermit
    });

    private string RequireRequest(VerifiedBpUseRequest request)
    {
        var sales = request.Purpose is "SALES_NEW_SOURCE" or "SALES_CONFIRM";
        var purchase = request.Purpose is "PURCHASE_NEW_SOURCE" or "PURCHASE_RELEASE";
        if (request.Tenant != db.CurrentTenantId || request.Tenant == Guid.Empty || request.BpId == Guid.Empty ||
            request.UseKey == Guid.Empty || (!sales && !purchase) || request.Consumer.Purpose != request.Purpose ||
            request.Consumer.Owner != (sales ? SalesOwner : PurchaseOwner) || !Key(request.LegalEntityKey, 64) ||
            !Key(request.Consumer.Id, 160) || !Key(request.Consumer.Version, 64) || !Digest(request.Consumer.Digest) ||
            !Key(request.Actor, 160) || !Key(request.BusinessEffectKey, 160) ||
            request.ExpectedBpRevision <= 0 || request.AccountId == Guid.Empty ||
            request.ExpectedMappingRevision is <= 0 || (!sales && request.AccountId is not null) ||
            (request.AccountId is null && (request.MappingProof is not null || request.ExpectedMappingRevision is not null)))
            throw BpUseScopeGate.Unproven();
        return sales ? "Customer" : "Supplier";
    }

    private static BpUseMapping? VerifyMapping(DbTransaction tx, VerifiedBpUseRequest request)
    {
        if (request.AccountId is null) return null;
        if (request.MappingProof is not { } proof || !ReferenceEquals(proof.Transaction, tx) ||
            proof.Tenant != request.Tenant || proof.Partner != request.BpId || proof.OwnerConflict ||
            proof.ExpectedRevision != request.ExpectedMappingRevision ||
            proof.Current.AccountId != request.AccountId || proof.Current.LegalEntityKey != request.LegalEntityKey ||
            proof.Current.State != "LINKED" || !Key(proof.Current.BusinessPartnerKey, 20) ||
            proof.Current.MappingRevision != Text(proof.ExpectedRevision) || !Digest(proof.Current.SemanticHash))
            throw BpUseScopeGate.Unproven();
        return proof.Current;
    }

    private static bool Key(string value, int length) => value.Length > 0 && value.Length <= length &&
        value.Length == value.Trim().Length && !value.Any(char.IsControl);
    private static bool Digest(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static byte[] Canonical<T>(T value) => BusinessPartnerWorkspaceV1.Canonical(value);
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Canonical(value))).ToLowerInvariant();
    private static OrderPriceCorrectionException Conflict() => new("USE_IDEMPOTENCY_CONFLICT", 409);
}
