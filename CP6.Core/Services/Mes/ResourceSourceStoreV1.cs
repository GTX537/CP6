using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Mes;

internal sealed class ResourceSourceStoreV1(CP6Context db)
{
    private IDbContextTransaction? fencedTransaction;
    internal const string Owner = "Main.Mes.ResourceSource.v1";
    internal static OrderPriceCorrectionException Error(string code, int status = 422) => new(code, status);
    internal static string Resource(string kind) => kind switch
    { "MACHINE" => OperationsOwnerV1.Machine, "WORK_CENTER" => OperationsOwnerV1.WorkCenter, _ => throw Error("RESOURCE_KIND_INVALID", 400) };
    internal static long Revision(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
        && n > 0 && Text(n) == value ? n : throw Error("RESOURCE_VERSION_INVALID", 400);
    private static string Text(long n) => n.ToString(CultureInfo.InvariantCulture);
    internal async Task Fence(bool write, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null || db.CurrentTenantId == Guid.Empty)
            throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        // Also hold original tables when adopted by another root through its real source Owner.
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"M_Machine\",public.\"T_WorkCenter\""
            + (write ? " IN SHARE ROW EXCLUSIVE MODE" : " IN SHARE MODE"), ct);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_resource_source_v1.heads,cp6_resource_source_v1.versions"
            + (write ? " IN SHARE ROW EXCLUSIVE MODE" : " IN SHARE MODE"), ct);
        fencedTransaction = db.Database.CurrentTransaction;
    }
    private sealed class SourceRow
    {
        public long Revision { get; set; }
        public long Master { get; set; }
        public long Capability { get; set; }
        public long Assignment { get; set; }
        public long Control { get; set; }
        public bool Present { get; set; }
        public string Json { get; set; } = "";
        public string Digest { get; set; } = "";
    }
    internal async Task<ResourceSourceVersionV1> Read(OperationsAccessV1 access, string kind, Guid id, long? version, CancellationToken ct)
    {
        if (fencedTransaction == null || !ReferenceEquals(fencedTransaction, db.Database.CurrentTransaction))
            throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await access.RequireResourceSource(db, kind, id, ct);
        var table = kind == "MACHINE" ? "public.\"M_Machine\"" : "public.\"T_WorkCenter\"";
        if (!await db.Database.SqlQuery<bool>($"""
            SELECT count(*)=2 AS "Value" FROM pg_catalog.pg_trigger WHERE tgrelid=CAST({table} AS regclass)
              AND tgname IN ('resource_source_capture','resource_source_no_truncate') AND tgenabled IN ('O','A') AND NOT tgisinternal
            """).SingleAsync(ct)) throw Error("RESOURCE_SOURCE_UNAVAILABLE", 503);
        // Only a closed choice of original table names is interpolated. Tenant/UUID/kind remain parameters.
        var current = await db.Database.SqlQueryRaw<SourceRow>("""
            SELECT v.revision AS "Revision",v.master_revision AS "Master",v.capability_revision AS "Capability",
              v.assignment_revision AS "Assignment",v.control_revision AS "Control",v.source_present AS "Present",
              v.source_json AS "Json",v.source_digest AS "Digest"
            FROM cp6_resource_source_v1.heads h JOIN cp6_resource_source_v1.versions v USING(tenant_id,kind,resource_id,revision)
            JOIN
            """ + " " + table + " " + """
             p ON p."TenantId"=h.tenant_id AND p."Id"=h.resource_id
            WHERE h.tenant_id={0} AND h.kind={1} AND h.resource_id={2} AND v.source_present
              AND v.source_json=cp6_resource_source_v1.payload(h.kind,to_jsonb(p))
            """, db.CurrentTenantId, kind, id).SingleOrDefaultAsync(ct) ?? throw Error("RESOURCE_SOURCE_UNAVAILABLE", 503);
        var selected = version == null || version == current.Revision ? current : await db.Database.SqlQuery<SourceRow>($"""
            SELECT revision AS "Revision",master_revision AS "Master",capability_revision AS "Capability",
              assignment_revision AS "Assignment",control_revision AS "Control",source_present AS "Present",source_json AS "Json",source_digest AS "Digest"
            FROM cp6_resource_source_v1.versions WHERE tenant_id={db.CurrentTenantId} AND kind={kind} AND resource_id={id} AND revision={version.Value}
            """).SingleOrDefaultAsync(ct) ?? throw Error("RESOURCE_VERSION_NOT_FOUND", 404);
        if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selected.Json))).ToLowerInvariant() != selected.Digest)
            throw Error("RESOURCE_SOURCE_UNAVAILABLE", 503);
        using var doc = JsonDocument.Parse(selected.Json);
        var source = doc.RootElement.Clone(); var control = source.GetProperty("control");
        return new(db.CurrentTenantId, new(Owner, kind, id, Text(selected.Revision), selected.Digest),
            new(Text(selected.Master), Text(selected.Capability), Text(selected.Assignment), Text(selected.Control)),
            selected.Revision == current.Revision, selected.Present,
            selected.Present && control.GetProperty("enabled").GetBoolean() && !control.GetProperty("isDeleted").GetBoolean(), source, selected.Json);
    }
}
