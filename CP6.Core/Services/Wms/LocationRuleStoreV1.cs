using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.LocationRuleValuesV1;
namespace CP6.Core.Services.Wms;
internal sealed class LocationRuleStoreV1(CP6Context db)
{
    internal sealed class Head
    {
        public Guid Id{get;init;}public Guid Location{get;init;}public Guid Selector{get;init;}
        public long Version{get;init;}public long Revision{get;init;}public Guid Token{get;init;}public string State{get;init;}="";
        public string Etag=>"\""+Token.ToString("N")+"\"";
    }
    private sealed class VersionRow{public byte[] Body{get;init;}=[];public string Digest{get;init;}="";}
    internal IQueryable<Head> Heads()=>db.Database.SqlQuery<Head>($"""
        SELECT id AS "Id",location_id AS "Location",selector_id AS "Selector",version AS "Version",revision AS "Revision",token AS "Token",state AS "State"
        FROM cp6_location_rule_v1.proposals WHERE tenant_id={db.CurrentTenantId}
        """);
    internal async Task Fence(bool write,CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_location_rule_v1.selectors,cp6_location_rule_v1.proposals,cp6_location_rule_v1.versions"+(write?" IN SHARE ROW EXCLUSIVE MODE":" IN SHARE MODE"),ct);
        if(!await db.Database.SqlQueryRaw<bool>("SELECT cp6_location_rule_v1.ready() AS \"Value\"").SingleAsync(ct))throw Error("RULE_STORE_UNAVAILABLE",503);
    }
    internal async Task<Head> HeadFor(Guid location,Guid id,CancellationToken ct)=>await Heads().SingleOrDefaultAsync(x=>x.Id==id&&x.Location==location,ct)??throw Error("RULE_PROPOSAL_NOT_FOUND",404);
    internal async Task<(LocationRuleOriginalV1 Original,EngineeringSourceRefV1 Reference,string Json)> Read(Head h,long? version,CancellationToken ct)
    {
        var n=version??h.Version;
        var row=await db.Database.SqlQuery<VersionRow>($"""
            SELECT body AS "Body",digest AS "Digest" FROM cp6_location_rule_v1.versions WHERE tenant_id={db.CurrentTenantId} AND id={h.Id} AND version={n}
            """).SingleOrDefaultAsync(ct)??throw Error("RULE_VERSION_NOT_FOUND",404);
        var o=JsonSerializer.Deserialize<LocationRuleOriginalV1>(row.Body,EngineeringProductStoreV1.Json);
        if(o==null||o.TenantId!=db.CurrentTenantId||o.Id!=h.Id||o.LocationId!=h.Location||o.SelectorId!=h.Selector||o.Version!=n.ToString(CultureInfo.InvariantCulture)
            ||o.ReferenceResolution is not ("UNRESOLVED" or "RESOLVED")||o.ReferenceResolution=="RESOLVED"!=(o.ReferenceBinding!=null)
            ||EngineeringProductStoreV1.Digest(row.Body)!=row.Digest)throw Error("RULE_ORIGINAL_UNAVAILABLE",503);
        Draft(o.Draft);return(o,new(Owner,h.Id,o.Version,row.Digest),Encoding.UTF8.GetString(row.Body));
    }
    internal async Task<LocationRuleSelectorV1[]> Selectors(Guid location,CancellationToken ct)
        =>await db.Database.SqlQuery<LocationRuleSelectorV1>($"""
            SELECT id AS "Id",family AS "RuleFamily",metric AS "MetricId",domain AS "ApplicabilityDomainId",revision::text AS "Revision",current_rule_version_id AS "CurrentRuleVersionId"
            FROM cp6_location_rule_v1.selectors WHERE tenant_id={db.CurrentTenantId} AND location_id={location} ORDER BY family,metric,domain,id
            """).ToArrayAsync(ct);
    internal async Task<Head> Create(Guid location,Guid operation,Guid actor,LocationRuleDraftV1 draft,CancellationToken ct)
    {
        var k=draft.SelectorKey;var selectors=await Selectors(location,ct);
        var selector=selectors.SingleOrDefault(x=>x.RuleFamily==k.RuleFamily&&x.MetricId==k.MetricId&&x.ApplicabilityDomainId==k.ApplicabilityDomainRef.Id)?.Id??Guid.NewGuid();
        if(!selectors.Any(x=>x.Id==selector))await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_location_rule_v1.selectors(tenant_id,id,location_id,family,metric,domain,revision,current_rule_version_id,operation_id,actor_id)
            VALUES({db.CurrentTenantId},{selector},{location},{k.RuleFamily},{k.MetricId},{k.ApplicabilityDomainRef.Id},0,NULL,{operation},{actor})
            """,ct);
        var id=Guid.NewGuid();var token=Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_location_rule_v1.proposals(tenant_id,id,location_id,selector_id,version,revision,token,state,operation_id,actor_id)
            VALUES({db.CurrentTenantId},{id},{location},{selector},1,1,{token},'DRAFT',{operation},{actor})
            """,ct);
        var h=await HeadFor(location,id,ct);await Append(h,operation,actor,draft,ct);return h;
    }
    internal async Task<Head> Change(Head before,Guid operation,Guid actor,LocationRuleDraftV1? draft,CancellationToken ct,RuleReferenceBindingV1? binding=null)
    {
        if(before.Revision==long.MaxValue||draft!=null&&before.Version==long.MaxValue)throw Error("RULE_VERSION_EXHAUSTED",503);
        var version=before.Version+(draft==null?0:1);var state=draft==null?"WITHDRAWN":"DRAFT";var token=Guid.NewGuid();
        if(await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_location_rule_v1.proposals SET version={version},revision=revision+1,token={token},state={state},operation_id={operation},actor_id={actor}
            WHERE tenant_id={db.CurrentTenantId} AND id={before.Id} AND revision={before.Revision} AND token={before.Token} AND state='DRAFT'
            """,ct)!=1)throw Error("RULE_PREDECESSOR_CHANGED",412);
        var h=await HeadFor(before.Location,before.Id,ct);if(draft!=null)await Append(h,operation,actor,draft,ct,binding);return h;
    }
    private async Task Append(Head h,Guid operation,Guid actor,LocationRuleDraftV1 draft,CancellationToken ct,RuleReferenceBindingV1? binding=null)
    {
        var o=new LocationRuleOriginalV1(db.CurrentTenantId,h.Id,h.Location,h.Selector,h.Version.ToString(CultureInfo.InvariantCulture),operation,actor,DateTimeOffset.UtcNow,draft,binding==null?"UNRESOLVED":"RESOLVED",binding);
        var body=EngineeringProductStoreV1.Encode(o);var digest=EngineeringProductStoreV1.Digest(body);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_location_rule_v1.versions(tenant_id,id,version,operation_id,actor_id,body,digest)
            VALUES({db.CurrentTenantId},{h.Id},{h.Version},{operation},{actor},{body},{digest})
            """,ct);
    }
}
