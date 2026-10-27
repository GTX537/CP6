using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public sealed record BusinessPartnerIdentityRefV1(string Owner, Guid Id, string Version, string Digest);
public sealed record BusinessPartnerIdentityViewV1(Guid TenantId, BusinessPartnerIdentityRefV1 Reference,
    bool IsCurrent, bool SourcePresent, DateTimeOffset ObservedAt, JsonElement Source, string? OriginalJson,
    string Projection, string TradingEligibility = "NOT_ATTESTED");
public sealed record BusinessPartnerIdentityQueryV1(int Page = 1, int PageSize = 30, string? Code = null, bool CustomersOnly = false);
public sealed record BusinessPartnerIdentityPageV1(BusinessPartnerIdentityViewV1[] Items, int Total, int Page, int PageSize);

/// <summary>Minimal observations of the native customer/partner UUID. No CRM identity or credit/transaction eligibility assertion.</summary>
public sealed class BusinessPartnerIdentityOwnerV1(CP6Context db, OperationsOwnerV1 authority)
{
    public const string Owner = "Main.Erp.BusinessPartnerIdentity.v1";
    private static OrderPriceCorrectionException Error(string code, int status = 503) => new(code, status);
    private static string Text(long n) => n.ToString(CultureInfo.InvariantCulture);
    private static long Revision(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
        && n > 0 && Text(n) == value ? n : throw Error("BUSINESS_PARTNER_IDENTITY_INPUT_INVALID", 400);
    private void RequireOwner()
    { if (!authority.Owns(db)) throw Error("AUTHORITY_FENCE_UNAVAILABLE"); }
    public Task<BusinessPartnerIdentityViewV1> ReadAsync(Guid id, string? version = null, CancellationToken ct = default)
    {
        RequireOwner(); if (id == Guid.Empty) throw Error("BUSINESS_PARTNER_IDENTITY_INPUT_INVALID", 400);
        var exact = version == null ? (long?)null : Revision(version);
        return authority.ExecuteAsync(OperationsOwnerV1.BusinessPartner, "query", false,
            (a, t) => ReadOwnedAsync(db, a, id, exact, t), ct);
    }
    public Task<BusinessPartnerIdentityPageV1> ListAsync(BusinessPartnerIdentityQueryV1 input, CancellationToken ct = default)
    {
        RequireOwner(); var q = OperationsOwnerV1.Freeze(input);
        if (q.Page < 1 || q.PageSize is < 1 or > 100 || (long)(q.Page - 1) * q.PageSize > int.MaxValue || q.Code?.Length > 20)
            throw Error("BUSINESS_PARTNER_IDENTITY_INPUT_INVALID", 400);
        return authority.ExecuteAsync(OperationsOwnerV1.BusinessPartner, "query", false, async (a, t) =>
        {
            await Fence(db, a, t); var rows = a.BusinessPartners();
            if (q.Code != null) rows = rows.Where(p => p.BpCd.Contains(q.Code));
            if (q.CustomersOnly) rows = rows.Where(p => p.CustomerFlg);
            var total = await rows.CountAsync(t);
            var ids = await rows.OrderBy(p => p.BpCd).ThenBy(p => p.Id).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).Select(p => p.Id).ToArrayAsync(t);
            var items = new List<BusinessPartnerIdentityViewV1>();
            foreach (var id in ids) items.Add(await ReadOwnedAsync(db, a, id, null, t));
            return new BusinessPartnerIdentityPageV1(items.ToArray(), total, q.Page, q.PageSize);
        }, ct);
    }
    private static async Task Fence(CP6Context db, OperationsAccessV1 a, CancellationToken ct)
    {
        a.RequireBusinessPartnerSourceAccess(db);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"T_WebBusinessPartner\" IN SHARE MODE", ct);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_business_partner_identity_v1.heads,cp6_business_partner_identity_v1.versions IN SHARE MODE", ct);
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT count(*)=2 AS "Value" FROM pg_catalog.pg_trigger WHERE tgrelid='public."T_WebBusinessPartner"'::regclass
              AND tgname IN ('business_partner_identity_capture','business_partner_identity_no_truncate')
              AND tgenabled IN ('O','A') AND NOT tgisinternal
            """).SingleAsync(ct)) throw Error("BUSINESS_PARTNER_IDENTITY_SOURCE_UNAVAILABLE");
    }
    private sealed class Row
    {
        public long Revision { get; init; }
        public bool Present { get; init; }
        public string Json { get; init; } = "";
        public string Digest { get; init; } = "";
        public DateTimeOffset ObservedAt { get; init; }
    }
    private static BusinessPartnerIdentityRefV1 Reference(Guid id, Row row) => new(Owner, id, Text(row.Revision), row.Digest);
    private static async Task<Row> Current(CP6Context db, OperationsAccessV1 a, Guid id, CancellationToken ct)
    {
        await Fence(db, a, ct); await a.RequireBusinessPartner(db, id, ct);
        var row = await db.Database.SqlQuery<Row>($"""
            SELECT v.revision AS "Revision",v.source_present AS "Present",v.source_json AS "Json",v.source_digest AS "Digest",v.observed_at AS "ObservedAt"
            FROM cp6_business_partner_identity_v1.heads h JOIN cp6_business_partner_identity_v1.versions v USING(tenant_id,partner_id,revision)
            JOIN public."T_WebBusinessPartner" p ON p."TenantId"=h.tenant_id AND p."Id"=h.partner_id
            WHERE h.tenant_id={db.CurrentTenantId} AND h.partner_id={id} AND v.source_present
              AND v.source_json=cp6_business_partner_identity_v1.payload(to_jsonb(p))
            """).SingleOrDefaultAsync(ct) ?? throw Error("BUSINESS_PARTNER_IDENTITY_SOURCE_UNAVAILABLE");
        ValidateOriginal(db, id, row); return row;
    }
    private static void ValidateOriginal(CP6Context db, Guid id, Row row)
    {
        if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.Json))).ToLowerInvariant() != row.Digest)
            throw Error("BUSINESS_PARTNER_IDENTITY_SOURCE_UNAVAILABLE");
        using var document = JsonDocument.Parse(row.Json); var source = document.RootElement;
        if (source.GetProperty("businessPartnerId").GetGuid() != id || source.GetProperty("tenantId").GetGuid() != db.CurrentTenantId)
            throw Error("BUSINESS_PARTNER_IDENTITY_SOURCE_UNAVAILABLE");
    }
    internal static async Task<BusinessPartnerIdentityViewV1> ReadOwnedAsync(CP6Context db, OperationsAccessV1 a, Guid id, long? exact, CancellationToken ct)
    {
        var current = await Current(db, a, id, ct);
        var selected = exact == null || exact == current.Revision ? current : await db.Database.SqlQuery<Row>($"""
            SELECT revision AS "Revision",source_present AS "Present",source_json AS "Json",source_digest AS "Digest",observed_at AS "ObservedAt"
            FROM cp6_business_partner_identity_v1.versions WHERE tenant_id={db.CurrentTenantId} AND partner_id={id} AND revision={exact.Value}
            """).SingleOrDefaultAsync(ct) ?? throw Error("BUSINESS_PARTNER_IDENTITY_VERSION_NOT_FOUND", 404);
        ValidateOriginal(db, id, selected);
        var tx = db.Database.CurrentTransaction; var currentRef = Reference(id, current);
        a.AtCommit(async t =>
        {
            if (!ReferenceEquals(tx, db.Database.CurrentTransaction)) throw Error("AUTHORITY_FENCE_UNAVAILABLE");
            if (Reference(id, await Current(db, a, id, t)) != currentRef) throw Error("BUSINESS_PARTNER_IDENTITY_CHANGED", 412);
        });
        var projected = JsonNode.Parse(selected.Json)!.AsObject();
        if (!a.BusinessPartnerNameReadable) projected["bpName"] = null;
        return new(db.CurrentTenantId, Reference(id, selected), current.Revision == selected.Revision, selected.Present, selected.ObservedAt,
            JsonSerializer.SerializeToElement(projected), a.BusinessPartnerNameReadable ? selected.Json : null,
            a.BusinessPartnerNameReadable ? "FULL" : "NAME_REDACTED");
    }
    // Internal consumer only: uses the original current query authority, native lock and caller's exact Main transaction.
    // A source witness does not authorize a downstream write or attest trading eligibility.
    internal static async Task<BusinessPartnerIdentityViewV1> HoldExactAsync(CP6Context db, OperationsAccessV1 a, BusinessPartnerIdentityRefV1 expected, CancellationToken ct)
    {
        a.RequireBusinessPartnerSourceAccess(db);
        if (expected == null || expected.Owner != Owner || expected.Id == Guid.Empty) throw Error("BUSINESS_PARTNER_IDENTITY_INPUT_INVALID", 400);
        Revision(expected.Version);
        var current = await ReadOwnedAsync(db, a, expected.Id, null, ct);
        if (current.Reference != expected) throw Error("BUSINESS_PARTNER_IDENTITY_CHANGED", 412);
        // Consumers receive exactly the same current field projection, never an internal unmasked escape hatch.
        return current;
    }
}
