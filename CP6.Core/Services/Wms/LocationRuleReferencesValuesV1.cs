using CP6.Core.Services.Erp;
namespace CP6.Core.Services.Wms;
internal static class LocationRuleReferencesValuesV1
{
    internal const string Owner="Main.Wms.LocationRuleReferences.v1",Contract="LOCATION-RULE-REFERENCES/1";
    internal static OrderPriceCorrectionException Error(string code,int status=422)=>new(code,status);
    private static void Key(string? s,int n=100)
    {if(!LocationRuleValuesV1.Text(s,n))throw Error("RULE_DICTIONARY_KEY_INVALID",400);}
    private static string[] Codes(string[]? a,int size=100,int count=100,bool empty=false)
    {
        if(a==null||a.Length>count||!empty&&a.Length==0)throw Error("RULE_DICTIONARY_CODES_INVALID",400);
        foreach(var s in a)Key(s,size);
        if(a.Distinct(StringComparer.Ordinal).Count()!=a.Length)throw Error("RULE_DICTIONARY_CODES_DUPLICATE",400);
        return a.Order(StringComparer.Ordinal).ToArray();
    }
    internal static RuleReferenceDefinitionV1 Validate(RuleReferenceDefinitionV1? d)
    {
        if(d==null||d.Metrics==null||d.Precisions==null||d.Domains==null||d.Metrics.Length is <1 or >100
            ||d.Precisions.Length is <1 or >100||d.Domains.Length is <1 or >100)throw Error("RULE_DICTIONARY_INVALID",400);
        var metrics=d.Metrics.Select(m=>
        {
            if(m==null)throw Error("RULE_DICTIONARY_INVALID",400);Key(m.Id);Key(m.DimensionKey,128);
            if(m.Units==null||m.Units.Length is <1 or >100)throw Error("RULE_DICTIONARY_UNITS_REQUIRED",400);
            foreach(var u in m.Units)UomProfileValuesV1.Reference(u);
            if(m.Units.Select(u=>u.Id).Distinct().Count()!=m.Units.Length)throw Error("RULE_DICTIONARY_UNIT_DUPLICATE",400);
            return m with{Units=m.Units.OrderBy(u=>u.Id).ToArray()};
        }).OrderBy(m=>m.Id,StringComparer.Ordinal).ToArray();
        var precisions=d.Precisions.Select(p=>
        {if(p==null)throw Error("RULE_DICTIONARY_INVALID",400);Key(p.Id);if(p.Scale is <0 or >8||p.Rounding!="REJECT")throw Error("RULE_DICTIONARY_PRECISION_INVALID",400);return p;}).OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray();
        var domains=d.Domains.Select(p=>
        {
            if(p==null)throw Error("RULE_DICTIONARY_INVALID",400);Key(p.Id);
            return p with{RuleFamilies=Codes(p.RuleFamilies,64),MetricIds=Codes(p.MetricIds),PrecisionIds=Codes(p.PrecisionIds),
                ProductTypes=Codes(p.ProductTypes,50,100,true),Operations=Codes(p.Operations,100,100,true)};
        }).OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray();
        if(metrics.Select(m=>m.Id).Distinct().Count()!=metrics.Length||precisions.Select(m=>m.Id).Distinct().Count()!=precisions.Length
            ||domains.Select(m=>m.Id).Distinct().Count()!=domains.Length)throw Error("RULE_DICTIONARY_DUPLICATE",400);
        if(domains.Any(d=>d.MetricIds.Except(metrics.Select(m=>m.Id),StringComparer.Ordinal).Any()
            ||d.PrecisionIds.Except(precisions.Select(m=>m.Id),StringComparer.Ordinal).Any()))throw Error("RULE_DICTIONARY_REFERENCE_INVALID",400);
        var units=metrics.SelectMany(m=>m.Units).ToArray();
        if(units.GroupBy(u=>u.Id).Any(g=>g.Distinct().Count()!=1))throw Error("RULE_DICTIONARY_UNIT_VERSION_CONFLICT",400);
        return d with{Metrics=metrics,Precisions=precisions,Domains=domains};
    }
    internal static string Registry(RuleReferencePolicyV1 p)=>p.Reference.Id.ToString("D")+"/"+p.Reference.Version;
    internal static string Quantity(string s)=>s.Contains('.')?s.TrimEnd('0').TrimEnd('.'):s;
}
