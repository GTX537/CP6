using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.WarehouseClockRuntimeV1;
using static CP6.Core.Services.Wms.LocationRuleValuesV1;
namespace CP6.Core.Services.Wms;
internal sealed partial class WarehouseClockStoreV1(CP6Context db)
{
    internal const string Owner="Main.Wms.WarehouseClockPolicy.v1", Contract="WAREHOUSE-CLOCK-POLICY/1";
    private sealed class Row{public byte[] Body{get;init;}=[];public string Digest{get;init;}="";public Guid Id{get;init;}public long Revision{get;init;}}
    internal async Task Fence(bool write,CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_warehouse_clock_v1.binding,cp6_warehouse_clock_v1.heads,cp6_warehouse_clock_v1.versions,cp6_warehouse_clock_v1.observations"+(write?" IN SHARE ROW EXCLUSIVE MODE":" IN SHARE MODE"),ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_warehouse_clock_v1.ready() AS \"Value\"").SingleAsync(ct))throw Error("CLOCK_POLICY_STORE_UNAVAILABLE",503);
    }
    internal async Task<WarehouseClockPolicyV1?> Read(Guid warehouse,long? version,CancellationToken ct)
    {
        var r=await db.Database.SqlQuery<Row>($"""
            SELECT v.body AS "Body",v.digest AS "Digest",h.id AS "Id",v.revision AS "Revision"
            FROM cp6_warehouse_clock_v1.heads h JOIN cp6_warehouse_clock_v1.versions v
            ON v.tenant_id=h.tenant_id AND v.id=h.id AND v.revision=COALESCE({version},h.revision)
            WHERE h.tenant_id={db.CurrentTenantId} AND h.warehouse_id={warehouse}
            """).SingleOrDefaultAsync(ct);
        if(r==null)return null;
        var o=JsonSerializer.Deserialize<WarehouseClockOriginalV1>(r.Body,EngineeringProductStoreV1.Json);
        if(o==null||o.TenantId!=db.CurrentTenantId||o.Id!=r.Id||o.WarehouseId!=warehouse||o.Version!=r.Revision.ToString(CultureInfo.InvariantCulture)
            ||EngineeringProductStoreV1.Digest(r.Body)!=r.Digest)throw Error("CLOCK_POLICY_ORIGINAL_UNAVAILABLE",503);
        Validate(o.Definition);
        if(o.EnvironmentId==Guid.Empty||o.Source.Reference!=o.Definition.Source||o.Source.Authority.EnvironmentId!=o.EnvironmentId
            ||o.Source.Reference.Digest!=EngineeringProductStoreV1.Digest(EngineeringProductStoreV1.Encode(o.Source.Authority)))throw Error("CLOCK_POLICY_ORIGINAL_UNAVAILABLE",503);
        return new(new(WarehouseClockStoreV1.Owner,o.Id,o.Version,r.Digest),o,Encoding.UTF8.GetString(r.Body));
    }
    internal async Task<WarehouseClockPolicyV1> Append(Guid warehouse,WarehouseClockCommandV1 c,Guid actor,
        WarehouseClockPolicyV1? before,Guid environment,WarehouseClockSourceV1 source,CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_warehouse_clock_v1.binding(singleton,environment_id) VALUES(true,{environment}) ON CONFLICT(singleton) DO NOTHING",ct);
        if(await db.Database.SqlQueryRaw<Guid>("SELECT environment_id AS \"Value\" FROM cp6_warehouse_clock_v1.binding").SingleAsync(ct)!=environment)throw Error("CLOCK_ENVIRONMENT_MISMATCH",503);
        if(before!=null&&EngineeringProductStoreV1.Encode(before.Original.Definition).AsSpan().SequenceEqual(EngineeringProductStoreV1.Encode(c.Definition)))return before;
        var old=before==null?0:EngineeringProductValuesV1.Revision(before.Reference.Version);
        if(old==long.MaxValue)throw Error("CLOCK_POLICY_VERSION_EXHAUSTED",503);
        var id=before?.Reference.Id??Guid.NewGuid();var next=old+1;
        if(before==null)await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_warehouse_clock_v1.heads(tenant_id,id,warehouse_id,revision) VALUES({db.CurrentTenantId},{id},{warehouse},1)
            """,ct);
        else if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_warehouse_clock_v1.heads SET revision={next} WHERE tenant_id={db.CurrentTenantId} AND id={id} AND revision={old}
            """,ct)!=1)throw Error("CLOCK_POLICY_CHANGED",412);
        var o=new WarehouseClockOriginalV1(db.CurrentTenantId,id,warehouse,next.ToString(CultureInfo.InvariantCulture),c.OperationId,actor,DateTimeOffset.UtcNow,environment,c.Definition,source,c.Reason);
        var body=EngineeringProductStoreV1.Encode(o);var digest=EngineeringProductStoreV1.Digest(body);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_warehouse_clock_v1.versions(tenant_id,id,revision,operation_id,actor_id,body,digest)
            VALUES({db.CurrentTenantId},{id},{next},{c.OperationId},{actor},{body},{digest})
            """,ct);
        return (await Read(warehouse,next,ct))!;
    }
}
