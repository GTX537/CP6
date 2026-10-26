using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Space;

// Read-only witness of the original physical Space Site. No second Site or legal-entity master.
internal static class PhysicalSiteIdentityReaderV1
{
    internal static async Task<EngineeringSiteChoiceV1> ReadOwnedAsync(CP6Context db, OperationsAccessV1 access, Guid id, CancellationToken ct)
    {
        access.RequirePhysicalSiteSourceAccess(db);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"Space_Site\",cp6_physical_site_identity_v1.heads,cp6_physical_site_identity_v1.versions IN SHARE MODE", ct);
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT count(*)=2 AS "Value" FROM pg_catalog.pg_trigger WHERE tgrelid='public."Space_Site"'::regclass
            AND tgname IN ('physical_site_identity_capture','physical_site_identity_no_truncate')
            AND tgenabled IN ('O','A') AND NOT tgisinternal
            """).SingleAsync(ct)) throw Error("PHYSICAL_SITE_SOURCE_UNAVAILABLE", 503);
        var row = await db.Database.SqlQuery<Row>($"""
            SELECT v.revision AS "Revision",v.source_json AS "Json",v.source_digest AS "Digest"
            FROM cp6_physical_site_identity_v1.heads h JOIN cp6_physical_site_identity_v1.versions v USING(tenant_id,site_id,revision)
            JOIN public."Space_Site" s ON s."TenantId"=h.tenant_id AND s."Id"=h.site_id
            WHERE h.tenant_id={db.CurrentTenantId} AND h.site_id={id} AND v.source_present
              AND v.source_json=cp6_physical_site_identity_v1.payload(to_jsonb(s))
            """).SingleOrDefaultAsync(ct) ?? throw Error("PHYSICAL_SITE_UNAVAILABLE");
        if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row.Json))).ToLowerInvariant() != row.Digest)
            throw Error("PHYSICAL_SITE_SOURCE_UNAVAILABLE", 503);
        using var document = JsonDocument.Parse(row.Json); var value = document.RootElement;
        if (value.GetProperty("siteId").GetGuid() != id || value.GetProperty("tenantId").GetGuid() != db.CurrentTenantId)
            throw Error("PHYSICAL_SITE_SOURCE_UNAVAILABLE", 503);
        return new(new(SiteOwner, id, row.Revision.ToString(CultureInfo.InvariantCulture), row.Digest),
            value.GetProperty("siteCode").GetString()!, value.GetProperty("siteName").GetString()!,
            value.GetProperty("enabled").GetBoolean() && !value.GetProperty("isDeleted").GetBoolean());
    }
    private sealed class Row
    {
        public long Revision { get; init; }
        public string Json { get; init; } = "";
        public string Digest { get; init; } = "";
    }
}
