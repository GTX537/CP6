using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.LocationRuleReferencesValuesV1;
namespace CP6.Core.Services.Wms;
internal sealed class LocationRuleReferencesStoreV1(CP6Context db)
{
    private sealed class Row{public byte[] Body{get;init;}=[];public string Digest{get;init;}="";public Guid Id{get;init;}public long Revision{get;init;}}
    internal async Task Fence(bool write,CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_location_rule_refs_v1.heads,cp6_location_rule_refs_v1.versions"+(write?" IN SHARE ROW EXCLUSIVE MODE":" IN SHARE MODE"),ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_location_rule_refs_v1.ready() AS \"Value\"").SingleAsync(ct))throw Error("RULE_DICTIONARY_STORE_UNAVAILABLE",503);
    }
    internal async Task<RuleReferencePolicyV1?> Read(Guid warehouse,long? version,CancellationToken ct)
    {
        var r=await db.Database.SqlQuery<Row>($"""
            SELECT v.body AS "Body",v.digest AS "Digest",h.id AS "Id",v.revision AS "Revision"
            FROM cp6_location_rule_refs_v1.heads h JOIN cp6_location_rule_refs_v1.versions v
            ON v.tenant_id=h.tenant_id AND v.id=h.id AND v.revision=COALESCE({version},h.revision)
            WHERE h.tenant_id={db.CurrentTenantId} AND h.warehouse_id={warehouse}
            """).SingleOrDefaultAsync(ct);
        if(r==null)return null;
        var o=JsonSerializer.Deserialize<RuleReferenceOriginalV1>(r.Body,EngineeringProductStoreV1.Json);
        if(o==null||o.TenantId!=db.CurrentTenantId||o.Id!=r.Id||o.WarehouseId!=warehouse||o.Version!=r.Revision.ToString(CultureInfo.InvariantCulture)
            ||EngineeringProductStoreV1.Digest(r.Body)!=r.Digest)throw Error("RULE_DICTIONARY_ORIGINAL_UNAVAILABLE",503);
        Validate(o.Definition);
        return new(new(Owner,o.Id,o.Version,r.Digest),o,Encoding.UTF8.GetString(r.Body));
    }
    internal async Task<RuleReferencePolicyV1> Append(Guid warehouse,RuleReferenceCommandV1 c,Guid actor,
        RuleReferencePolicyV1? before,RuleReferenceUnitV1[] units,CancellationToken ct)
    {
        if(before!=null&&EngineeringProductStoreV1.Encode(before.Original.Definition).AsSpan().SequenceEqual(EngineeringProductStoreV1.Encode(c.Definition)))return before;
        var old=before==null?0:EngineeringProductValuesV1.Revision(before.Reference.Version);
        if(old==long.MaxValue)throw Error("RULE_DICTIONARY_VERSION_EXHAUSTED",503);
        var id=before?.Reference.Id??Guid.NewGuid();var next=old+1;
        if(before==null)await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_location_rule_refs_v1.heads(tenant_id,id,warehouse_id,revision) VALUES({db.CurrentTenantId},{id},{warehouse},1)
            """,ct);
        else if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_location_rule_refs_v1.heads SET revision={next} WHERE tenant_id={db.CurrentTenantId} AND id={id} AND revision={old}
            """,ct)!=1)throw Error("RULE_DICTIONARY_CHANGED",412);
        var o=new RuleReferenceOriginalV1(db.CurrentTenantId,id,warehouse,next.ToString(CultureInfo.InvariantCulture),c.OperationId,actor,DateTimeOffset.UtcNow,c.Definition,units,c.Reason);
        var body=EngineeringProductStoreV1.Encode(o);var digest=EngineeringProductStoreV1.Digest(body);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_location_rule_refs_v1.versions(tenant_id,id,revision,operation_id,actor_id,body,digest)
            VALUES({db.CurrentTenantId},{id},{next},{c.OperationId},{actor},{body},{digest})
            """,ct);
        return (await Read(warehouse,next,ct))!;
    }
}
