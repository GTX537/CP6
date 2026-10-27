using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Wms;
using CP6.Core.Services.Erp;

internal sealed class ToolInstanceStoreV1(CP6Context db)
{
    internal const string Owner = "Main.Wms.ToolInstance.v1";
    private IDbContextTransaction? fencedTransaction;
    internal static OrderPriceCorrectionException Error(string code, int status = 422) => new(code, status);
    internal static long Revision(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
        && n > 0 && Text(n) == value ? n : throw Error("TOOL_INSTANCE_VERSION_INVALID", 400);
    private static string Text(long n) => n.ToString(CultureInfo.InvariantCulture);
    internal async Task Fence(CancellationToken ct, bool write=false)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null || db.CurrentTenantId == Guid.Empty)
            throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE public.\"T_PlateMoldStock\" " + (write ? "IN SHARE ROW EXCLUSIVE MODE" : "IN SHARE MODE"), ct);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_tool_instance_v1.heads,cp6_tool_instance_v1.versions " + (write ? "IN SHARE ROW EXCLUSIVE MODE" : "IN SHARE MODE"), ct);
        fencedTransaction = db.Database.CurrentTransaction;
    }
    private sealed class SourceRow
    {
        public long Revision { get; set; }
        public bool Present { get; set; }
        public string Json { get; set; } = "";
        public string Digest { get; set; } = "";
        public string NativeToken { get; set; } = "";
        public DateTimeOffset ObservedAt { get; set; }
    }
    internal async Task<ToolInstanceViewV1> Read(OperationsAccessV1 access, Guid id, long? version, CancellationToken ct)
    {
        if (fencedTransaction == null || !ReferenceEquals(fencedTransaction, db.Database.CurrentTransaction))
            throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
        await access.RequireToolInstance(db, id, ct);
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT count(*)=2 AS "Value" FROM pg_catalog.pg_trigger WHERE tgrelid='public."T_PlateMoldStock"'::regclass
              AND tgname IN ('tool_instance_source_capture','tool_instance_source_no_truncate') AND tgenabled IN ('O','A') AND NOT tgisinternal
            """).SingleAsync(ct)) throw Error("TOOL_INSTANCE_UNAVAILABLE", 503);
        var current = await db.Database.SqlQuery<SourceRow>($"""
            SELECT v.revision AS "Revision",v.source_present AS "Present",v.source_json AS "Json",v.source_digest AS "Digest",
              v.native_token AS "NativeToken",v.observed_at AS "ObservedAt"
            FROM cp6_tool_instance_v1.heads h JOIN cp6_tool_instance_v1.versions v USING(tenant_id,object_id,revision)
            JOIN public."T_PlateMoldStock" p ON p."TenantId"=h.tenant_id AND p."Id"=h.object_id
            WHERE h.tenant_id={db.CurrentTenantId} AND h.object_id={id} AND v.source_present
              AND v.source_json=cp6_tool_instance_v1.payload(to_jsonb(p))
            """).SingleOrDefaultAsync(ct) ?? throw Error("TOOL_INSTANCE_UNAVAILABLE", 503);
        var selected = version == null || version == current.Revision ? current : await db.Database.SqlQuery<SourceRow>($"""
            SELECT revision AS "Revision",source_present AS "Present",source_json AS "Json",source_digest AS "Digest",
              native_token AS "NativeToken",observed_at AS "ObservedAt"
            FROM cp6_tool_instance_v1.versions WHERE tenant_id={db.CurrentTenantId} AND object_id={id} AND revision={version.Value}
            """).SingleOrDefaultAsync(ct) ?? throw Error("TOOL_INSTANCE_VERSION_NOT_FOUND", 404);
        if (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selected.Json))).ToLowerInvariant() != selected.Digest)
            throw Error("TOOL_INSTANCE_UNAVAILABLE", 503);
        var token = await db.Database.SqlQuery<string>($"""
            SELECT encode("RowVersion",'base64') AS "Value" FROM public."T_PlateMoldStock" WHERE "TenantId"={db.CurrentTenantId} AND "Id"={id}
            """).SingleAsync(ct);
        using var doc = JsonDocument.Parse(selected.Json);
        access.AtCommit(last => access.RequireToolInstance(db, id, last));
        return new(db.CurrentTenantId,new(Owner,id,Text(selected.Revision),selected.Digest),selected.Revision==current.Revision,
            "\""+token+"\"",selected.NativeToken,selected.ObservedAt,await access.MaskToolSource(doc.RootElement,ct),access.ToolReadableFields,access.ToolWritableFields);
    }
    internal async Task<ToolInstanceViewV1> HoldExactAsync(OperationsAccessV1 access,ToolInstanceRefV1 expected,CancellationToken ct)
    {
        access.RequireToolSourceAccess(db);
        if(expected==null||expected.Owner!=Owner||expected.Id==Guid.Empty)throw Error("TOOL_INSTANCE_REF_INVALID",400);
        Revision(expected.Version);await Fence(ct);
        var current=await Read(access,expected.Id,null,ct);
        if(current.Reference!=expected)throw Error("TOOL_INSTANCE_CHANGED",412);
        // Lock protects other connections; final re-read detects writes by this same UoW as well.
        access.AtCommit(async t=>{if((await ReadCore(access,expected.Id,t))!=expected)throw Error("TOOL_INSTANCE_CHANGED",412);});
        return current;
    }
    private async Task<ToolInstanceRefV1> ReadCore(OperationsAccessV1 access,Guid id,CancellationToken ct)
    {
        await access.RequireToolInstance(db,id,ct);
        var row=await db.Database.SqlQuery<SourceRow>($"""
            SELECT v.revision AS "Revision",v.source_digest AS "Digest",v.source_json AS "Json",v.source_present AS "Present",
              v.native_token AS "NativeToken",v.observed_at AS "ObservedAt"
            FROM cp6_tool_instance_v1.heads h JOIN cp6_tool_instance_v1.versions v USING(tenant_id,object_id,revision)
            JOIN public."T_PlateMoldStock" p ON p."TenantId"=h.tenant_id AND p."Id"=h.object_id
            WHERE h.tenant_id={db.CurrentTenantId} AND h.object_id={id} AND v.source_present
              AND v.source_json=cp6_tool_instance_v1.payload(to_jsonb(p))
            """).SingleOrDefaultAsync(ct)??throw Error("TOOL_INSTANCE_UNAVAILABLE",503);
        return new(Owner,id,Text(row.Revision),row.Digest);
    }
}
