using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.Services.Erp;
namespace CP6.Core.Services.Wms;
internal static class LocationRuleValuesV1
{
    internal const string Owner="Main.Wms.LocationRuleProposal.v1",Contract="LOCATION-RULE-PROPOSAL/1";
    internal static readonly string[] Fields=["AllowedOperations","AllowedProductTypes","Capacity","IsPickable","Selector","Validity"];
    internal static OrderPriceCorrectionException Error(string code,int status=400)=>new(code,status);
    internal static bool Text(string? v,int max)=>!string.IsNullOrWhiteSpace(v)&&v.Length<=max&&v==v.Trim()&&!v.Any(char.IsControl)&&ValidUnicode(v);
    private static bool ValidUnicode(string v)
    {for(var i=0;i<v.Length;i++)if(char.IsSurrogate(v[i])&&(!char.IsHighSurrogate(v[i])||++i>=v.Length||!char.IsLowSurrogate(v[i])))return false;return true;}
    internal static void Reference(LocationRuleCodeRefV1? r)
    {if(r==null||!Text(r.Id,100)||!Text(r.Version,64))throw Error("RULE_REFERENCE_INVALID");}
    internal static LocationRuleCapacityV1 Capacity(LocationRuleCapacityV1? c)
    {
        if(c==null)throw Error("RULE_CAPACITY_REQUIRED");
        switch(c.Mode)
        {
            case "LIMITED":
                Reference(c.MetricRef);Reference(c.UomRef);Reference(c.PrecisionPolicyRef);
                if(c.Reason!=null||c.Value==null||!Regex.IsMatch(c.Value,@"\A[0-9]{1,13}(?:\.[0-9]{1,8})?\z",RegexOptions.CultureInvariant|RegexOptions.NonBacktracking))throw Error("RULE_CAPACITY_INVALID");
                return c with{Value=decimal.Parse(c.Value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture).ToString("0.00000000",CultureInfo.InvariantCulture)};
            case "UNLIMITED":
                if(c.Value!=null||c.MetricRef!=null||c.UomRef!=null||c.PrecisionPolicyRef!=null||c.Reason!=null)throw Error("RULE_CAPACITY_INVALID");return c;
            case "UNKNOWN":
                if(c.Value!=null||c.MetricRef!=null||c.UomRef!=null||c.PrecisionPolicyRef!=null||!Text(c.Reason,1000))throw Error("RULE_CAPACITY_INVALID");return c with{Reason=c.Reason!.Normalize(NormalizationForm.FormC)};
            default:throw Error("RULE_CAPACITY_MODE_INVALID");
        }
    }
    private static string[] Codes(string[]? values,int length)
    {
        if(values==null||values.Length>100||values.Any(v=>!Text(v,length))||values.Distinct(StringComparer.Ordinal).Count()!=values.Length)throw Error("RULE_CODES_INVALID");
        return values.Order(StringComparer.Ordinal).ToArray();
    }
    private static string? Instant(string? value)
    {
        if(value==null)return null;
        if(!Regex.IsMatch(value,@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2})\z",RegexOptions.CultureInvariant|RegexOptions.NonBacktracking)
            ||!DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out var parsed))throw Error("RULE_INSTANT_INVALID");
        return parsed.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz",CultureInfo.InvariantCulture);
    }
    internal static LocationRuleDraftV1 Draft(LocationRuleDraftV1? d)
    {
        if(d?.SelectorKey==null||!Text(d.SelectorKey.RuleFamily,64)||!Text(d.SelectorKey.MetricId,100))throw Error("RULE_SELECTOR_INVALID");
        Reference(d.SelectorKey.ApplicabilityDomainRef);var capacity=Capacity(d.Capacity);
        if(capacity.Mode=="LIMITED"&&capacity.MetricRef!.Id!=d.SelectorKey.MetricId)throw Error("RULE_METRIC_MISMATCH");
        var from=Instant(d.ValidFrom);var to=Instant(d.ValidTo);
        if(from!=null&&to!=null&&DateTimeOffset.Parse(from,CultureInfo.InvariantCulture)>=DateTimeOffset.Parse(to,CultureInfo.InvariantCulture))throw Error("RULE_INTERVAL_INVALID");
        return d with{Capacity=capacity,AllowedProductTypes=Codes(d.AllowedProductTypes,50),AllowedOperations=Codes(d.AllowedOperations,100),ValidFrom=from,ValidTo=to};
    }
    internal static string[] PatchFields(LocationRulePatchV1? p)
    {
        if(p==null)throw Error("RULE_PATCH_REQUIRED");
        var items=new[]{("Capacity",p.Capacity),("AllowedProductTypes",p.AllowedProductTypes),("AllowedOperations",p.AllowedOperations),("IsPickable",p.IsPickable),("Validity",p.ValidFrom),("Validity",p.ValidTo)};
        return items.Where(x=>x.Item2.ValueKind!=JsonValueKind.Undefined).Select(x=>x.Item1).Distinct().ToArray();
    }
    private static T? Change<T>(JsonElement e,T? original,bool clear=false)
    {
        if(e.ValueKind==JsonValueKind.Undefined)return original;
        if(e.ValueKind!=JsonValueKind.Object||!e.TryGetProperty("op",out var op)||op.ValueKind!=JsonValueKind.String)throw Error("RULE_PATCH_INVALID");
        var fields=e.EnumerateObject().Select(x=>x.Name).ToArray();
        if(op.GetString()=="CLEAR"&&clear&&fields.SequenceEqual(new[]{"op"}))return default;
        if(op.GetString()!="SET"||fields.Length!=2||fields.Distinct().Count()!=2||!e.TryGetProperty("value",out var value)||value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)throw Error("RULE_PATCH_INVALID");
        try{return value.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw Error("RULE_PATCH_INVALID");}
        catch(JsonException){throw Error("RULE_PATCH_INVALID");}
    }
    internal static LocationRuleDraftV1 Patch(LocationRuleDraftV1 before,LocationRulePatchV1 p)=>Draft(before with
    {
        Capacity=Change(p.Capacity,before.Capacity)!,AllowedProductTypes=Change(p.AllowedProductTypes,before.AllowedProductTypes)!,
        AllowedOperations=Change(p.AllowedOperations,before.AllowedOperations)!,IsPickable=Change(p.IsPickable,before.IsPickable),
        ValidFrom=Change(p.ValidFrom,before.ValidFrom,true),ValidTo=Change(p.ValidTo,before.ValidTo,true)
    });
}
