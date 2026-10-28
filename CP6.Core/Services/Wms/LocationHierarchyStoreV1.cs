using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;
internal sealed class LocationHierarchyStoreV1(CP6Context db)
{
    internal const string Owner="Main.Wms.LocationHierarchy.v1",Contract="LOCATION-HIERARCHY/1";
    internal static OrderPriceCorrectionException Error(string code,int status=422)=>new(code,status);
    internal async Task Fence(bool write,CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_location_hierarchy_v1.heads,cp6_location_hierarchy_v1.versions"+
            (write?" IN SHARE ROW EXCLUSIVE MODE":" IN SHARE MODE"),ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_location_hierarchy_v1.ready() AS \"Value\"").SingleAsync(ct))
            throw Error("HIERARCHY_STORE_UNAVAILABLE",503);
    }
    internal sealed class Head
    {
        public Guid Id {get;init;}
        public long Revision {get;init;}
        public long Hierarchy {get;init;}
    }
    internal Task<Head?> HeadFor(Guid warehouse,CancellationToken ct)=>db.Database.SqlQuery<Head>($"""
        SELECT id AS "Id",revision AS "Revision",hierarchy_revision AS "Hierarchy" FROM cp6_location_hierarchy_v1.heads
        WHERE tenant_id={db.CurrentTenantId} AND warehouse_id={warehouse}
        """).SingleOrDefaultAsync(ct);
    private sealed class Row {public byte[] Body {get;init;}=[];public string Digest {get;init;}="";}
    internal async Task<LocationHierarchyPolicyV1?> Read(Guid warehouse,long? version,CancellationToken ct)
    {
        var r=await db.Database.SqlQuery<Row>($"""
            SELECT v.body AS "Body",v.digest AS "Digest" FROM cp6_location_hierarchy_v1.heads h
            JOIN cp6_location_hierarchy_v1.versions v ON v.tenant_id=h.tenant_id AND v.id=h.id AND v.revision=COALESCE({version},h.revision)
            WHERE h.tenant_id={db.CurrentTenantId} AND h.warehouse_id={warehouse}
            """).SingleOrDefaultAsync(ct);
        if(r==null)return null;
        var o=JsonSerializer.Deserialize<LocationHierarchyOriginalV1>(r.Body,EngineeringProductStoreV1.Json);
        if(o==null||o.TenantId!=db.CurrentTenantId||o.WarehouseId!=warehouse||EngineeringProductStoreV1.Digest(r.Body)!=r.Digest)
            throw Error("HIERARCHY_ORIGINAL_UNAVAILABLE",503);
        Validate(o.RootLevels,o.Edges);
        return new(new(Owner,o.Id,o.Version,r.Digest),o,Encoding.UTF8.GetString(r.Body));
    }
    internal static void Validate(int[]? roots,LocationHierarchyEdgeV1[]? edges)
    {
        if(roots==null||edges==null||roots.Length is <1 or >5||edges.Length>10||roots.Any(x=>x is <1 or >5)
            ||roots.Distinct().Count()!=roots.Length||edges.Any(e=>e==null||e.ParentLevel is <1 or >4||e.ChildLevel>5||e.ChildLevel<=e.ParentLevel)
            ||edges.Distinct().Count()!=edges.Length)throw Error("HIERARCHY_POLICY_INVALID",400);
        var reachable=roots.ToHashSet();foreach(var e in edges.OrderBy(e=>e.ParentLevel))if(reachable.Contains(e.ParentLevel))reachable.Add(e.ChildLevel);
        if(edges.Any(e=>!reachable.Contains(e.ParentLevel)))throw Error("HIERARCHY_POLICY_UNREACHABLE",400);
    }
    internal async Task<bool> Compatible(Warehouse warehouse,int[] roots,LocationHierarchyEdgeV1[] edges,CancellationToken ct)
    {
        // Resolve all native parent codes using the real PostgreSQL comparison in one query.
        // Full graph compatibility discloses no hidden node identities or counts.
        var locations=db.Locations.IgnoreQueryFilters().AsNoTracking().Where(x=>x.TenantId==db.CurrentTenantId&&!x.IsDeleted&&x.WarehouseCd==warehouse.WarehouseCd);
        var rows=await (from l in locations join parent in locations on l.ParentLocationCd equals parent.LocationCd into parents
            from p in parents.DefaultIfEmpty()
            select new{l.Id,l.LocationLevel,HasParent=l.ParentLocationCd!=null,ParentId=(Guid?)p.Id,ParentLevel=(int?)p.LocationLevel}).ToArrayAsync(ct);
        foreach(var group in rows.GroupBy(x=>x.Id))
        {
            if(group.Count()!=1)return false;var l=group.Single();
            if(!l.HasParent){if(!roots.Contains(l.LocationLevel))return false;continue;}
            if(l.ParentId==null||l.ParentId==l.Id||l.ParentLevel==null||!edges.Contains(new(l.ParentLevel.Value,l.LocationLevel)))return false;
        }
        // Every accepted edge increases the explicit level, also excluding indirect cycles.
        return true;
    }
    internal async Task<LocationHierarchyPolicyV1> Append(Guid warehouse,LocationHierarchyCommandV1 c,Guid actor,
        LocationHierarchyPolicyV1? before,CancellationToken ct)
    {
        if(before!=null&&before.Original.Enabled==c.Enabled&&before.Original.RootLevels.SequenceEqual(c.RootLevels)
            &&before.Original.Edges.SequenceEqual(c.Edges))return before;
        var old=before==null?0:EngineeringProductValuesV1.Revision(before.Reference.Version);
        if(old==long.MaxValue)throw Error("HIERARCHY_VERSION_EXHAUSTED",503);
        var id=before?.Reference.Id??Guid.NewGuid();var next=old+1;
        if(before==null)await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_location_hierarchy_v1.heads(tenant_id,id,warehouse_id,revision,hierarchy_revision)
            VALUES({db.CurrentTenantId},{id},{warehouse},1,1)
            """,ct);
        else if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_location_hierarchy_v1.heads SET revision={next} WHERE tenant_id={db.CurrentTenantId} AND id={id} AND revision={old}
            """,ct)!=1)throw Error("HIERARCHY_POLICY_CHANGED",412);
        var o=new LocationHierarchyOriginalV1(db.CurrentTenantId,id,warehouse,next.ToString(CultureInfo.InvariantCulture),
            c.OperationId,actor,DateTimeOffset.UtcNow,c.Enabled,c.RootLevels,c.Edges,c.Reason);
        var body=EngineeringProductStoreV1.Encode(o);var digest=EngineeringProductStoreV1.Digest(body);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_location_hierarchy_v1.versions(tenant_id,id,revision,operation_id,actor_id,body,digest)
            VALUES({db.CurrentTenantId},{id},{next},{c.OperationId},{actor},{body},{digest})
            """,ct);
        return (await Read(warehouse,next,ct))!;
    }
}
