using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.LocationRuleReferencesValuesV1;
namespace CP6.Core.Services.Wms;
internal static class LocationRuleReferenceResolverV1
{
    internal static async Task<RuleReferenceUnitV1[]> Units(CP6Context db,OperationsAccessV1 a,RuleReferenceDefinitionV1 definition,CancellationToken ct)
    {
        var u=new UomProfileStoreV1(db);await u.Fence(false,ct);var source=await a.SourceAsync(OperationsOwnerV1.UomProfile,ct);
        var result=new List<RuleReferenceUnitV1>();
        foreach(var reference in definition.Metrics.SelectMany(m=>m.Units).Distinct().OrderBy(x=>x.Id))
        {
            var p=await UomProfileOwnerV1.CurrentExact(source,u,reference,ct);var basis=await UomProfileOwnerV1.CurrentBase(source,u,p,ct);
            if(definition.Metrics.Where(m=>m.Units.Contains(reference)).Any(m=>m.DimensionKey!=p.Profile.DimensionKey))throw Error("RULE_METRIC_DIMENSION_MISMATCH");
            result.Add(new(reference,basis,p.Profile.Code,p.Profile.DimensionKey));
            // Do not register new validation callbacks from inside final validation.
            a.AtCommit(async token=>
            {
                await source.RequireUomProfile(reference.Id,token);await source.RequireUomProfile(basis.Id,token);
                var current=await u.Read(reference.Id,null,token);var b=await u.Read(basis.Id,null,token);
                if(current.Reference!=reference||b.Reference!=basis||!current.Profile.Enabled!.Value||!b.Profile.Enabled!.Value)
                    throw Error("RULE_UNIT_SOURCE_CHANGED",412);
            });
        }
        foreach(var metric in definition.Metrics)
            if(result.Where(u=>metric.Units.Contains(u.Reference)).Select(u=>u.BaseReference).Distinct().Count()!=1)
                throw Error("RULE_METRIC_BASE_MISMATCH");
        return result.ToArray();
    }
    internal static async Task<(Guid Warehouse,RuleReferencePolicyV1? Policy)> Policy(CP6Context db,OperationsAccessV1 a,Guid location,CancellationToken ct)
    {
        var l=await a.RequireLocationRuleTarget(location,ct);var source=await a.SourceAsync(OperationsOwnerV1.Warehouse,ct);
        var w=await source.MasterWarehouses().SingleOrDefaultAsync(w=>w.WarehouseCd==l.WarehouseCd,ct)??throw Error("MASTER_NOT_FOUND",404);
        a.AtCommit(async t=>{await source.RequireMaster("warehouses",w.Id,t);});
        var s=new LocationRuleReferencesStoreV1(db);await s.Fence(false,ct);return(w.Id,await s.Read(w.Id,null,ct));
    }
    internal static RuleReferenceAuthorityV1[] Authorities(RuleReferencePolicyV1? p)=>p==null?[]:
        new[]{"metricRef","uomRef","precisionPolicyRef","applicabilityDomainRef","allowedProductTypes","allowedOperations","validity"}
        .Select(f=>new RuleReferenceAuthorityV1(f,f=="uomRef"?UomProfileValuesV1.Owner:Owner,Registry(p),p.Original.Definition.Enabled?"CONFIGURED":"DISABLED")).ToArray();
    internal static async Task<RuleReferenceBindingV1> Resolve(CP6Context db,OperationsAccessV1 a,Guid location,
        LocationRuleDraftV1 d,EngineeringSourceRefV1 expected,CancellationToken ct)
    {
        var (warehouse,p)=await Policy(db,a,location,ct);
        if(p==null)throw Error("RULE_DICTIONARY_UNAVAILABLE");
        if(expected!=p.Reference)throw Error("RULE_DICTIONARY_CHANGED",412);
        if(!p.Original.Definition.Enabled)throw Error("RULE_DICTIONARY_DISABLED");
        var definition=p.Original.Definition;var k=d.SelectorKey;var c=d.Capacity;var version=p.Reference.Version;
        var domain=definition.Domains.SingleOrDefault(x=>x.Id==k.ApplicabilityDomainRef.Id);
        var metric=definition.Metrics.SingleOrDefault(x=>x.Id==k.MetricId);
        if(domain==null||metric==null||k.ApplicabilityDomainRef.Version!=version||!domain.MetricIds.Contains(metric.Id,StringComparer.Ordinal)
            ||!domain.RuleFamilies.Contains(k.RuleFamily,StringComparer.Ordinal))throw Error("RULE_DOMAIN_REFERENCE_INVALID");
        if(c.Mode=="UNKNOWN")throw Error("RULE_REFERENCE_INCOMPLETE");
        if(c.Mode=="UNLIMITED"&&!domain.AllowUnlimited)throw Error("RULE_UNLIMITED_NOT_ALLOWED");
        if(d.AllowedProductTypes.Except(domain.ProductTypes,StringComparer.Ordinal).Any()||d.AllowedOperations.Except(domain.Operations,StringComparer.Ordinal).Any()
            ||d.AllowedProductTypes.Length==0&&!domain.AllowEmptyProducts||d.AllowedOperations.Length==0&&!domain.AllowEmptyOperations
            ||d.IsPickable&&!domain.AllowPickable)throw Error("RULE_DOMAIN_POLICY_MISMATCH");
        if(d.ValidFrom==null||d.ValidTo==null&&!domain.AllowOpenEnded)throw Error("RULE_VALIDITY_INCOMPLETE");
        var units=await Units(db,a,definition with{Metrics=[metric]},ct);var registry=Registry(p);
        var fields=new List<RuleReferenceBindingFieldV1>
        {new("metricRef",Owner,metric.Id,version,registry),new("applicabilityDomainRef",Owner,domain.Id,version,registry),new("validity",Owner,domain.Id,version,registry)};
        if(c.Mode=="LIMITED")
        {
            var precision=definition.Precisions.SingleOrDefault(x=>x.Id==c.PrecisionPolicyRef!.Id);
            if(c.MetricRef!.Version!=version||c.PrecisionPolicyRef!.Version!=version||precision==null||!domain.PrecisionIds.Contains(precision.Id,StringComparer.Ordinal))throw Error("RULE_PRECISION_REFERENCE_INVALID");
            var unit=units.SingleOrDefault(x=>x.Code==c.UomRef!.Id&&x.Reference.Version==c.UomRef.Version)??throw Error("RULE_UNIT_REFERENCE_INVALID");
            var quantity=Quantity(c.Value!);var point=quantity.IndexOf('.');
            if(point>=0&&quantity.Length-point-1>precision.Scale)throw Error("RULE_PRECISION_EXCEEDED");
            var u=new UomProfileStoreV1(db);await u.Fence(false,ct);var current=await u.Read(unit.Reference.Id,null,ct);
            UomProfileValuesV1.ConvertExact(quantity,current.Profile,current.Profile);
            fields.Add(new("uomRef",UomProfileValuesV1.Owner,unit.Code,unit.Reference.Version,registry));
            fields.Add(new("precisionPolicyRef",Owner,precision.Id,version,registry));
            units=[unit];
        }
        fields.AddRange(d.AllowedProductTypes.Select(x=>new RuleReferenceBindingFieldV1("allowedProductTypes",Owner,x,version,registry)));
        fields.AddRange(d.AllowedOperations.Select(x=>new RuleReferenceBindingFieldV1("allowedOperations",Owner,x,version,registry)));
        var store=new LocationRuleReferencesStoreV1(db);
        a.AtCommit(async token=>{await store.Fence(false,token);if((await store.Read(warehouse,null,token))?.Reference!=p.Reference)throw Error("RULE_DICTIONARY_CHANGED",412);});
        return new(p.Reference,registry,fields.OrderBy(f=>f.Field,StringComparer.Ordinal).ThenBy(f=>f.Id,StringComparer.Ordinal).ToArray(),units);
    }
}
