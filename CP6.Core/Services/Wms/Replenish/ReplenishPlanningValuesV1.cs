using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.Services.Erp;
namespace CP6.Core.Services.Wms.Replenish;

internal static class ReplenishPlanningValuesV1
{
    internal const string Owner="Main.Wms.Replenish.v1";
    internal static readonly JsonSerializerOptions RepJson=new(JsonSerializerDefaults.Web);
    internal static readonly BigInteger Scale=100000000;
    internal static OrderPriceCorrectionException Error(string code,int status=400)=>new(code,status);
    internal static byte[] Bytes<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value,RepJson);
    internal static string Hash<T>(T value)=>Convert.ToHexString(SHA256.HashData(Bytes(value))).ToLowerInvariant();
    internal static RepRefV1 Ref(string kind,Guid id,object value)=>new(Owner,id.ToString("D"),"1","REP/"+kind,"1",Hash(value));
    internal static string Key(string? v)
        =>!string.IsNullOrWhiteSpace(v)&&v.Length<=160&&v==v.Trim()&&!v.Any(char.IsControl)?v:throw Error("REP_KEY_INVALID");
    internal static void Reference(RepRefV1? r)
    {if(r==null)throw Error("REP_REFERENCE_REQUIRED");Key(r.Owner);Key(r.Id);Key(r.Version);Key(r.DigestDomain);Key(r.CodecVersion);
     if(r.Digest==null||!Regex.IsMatch(r.Digest,@"\A[0-9a-f]{64}\z",RegexOptions.NonBacktracking))throw Error("REP_REFERENCE_INVALID");}
    internal static BigInteger Number(string? value,bool total=false,bool positive=false)
    {
        var digits=total?30:13;
        if(value==null||!Regex.IsMatch(value,@"\A(?:0|[1-9][0-9]{0,"+(digits-1)+@"})(?:\.[0-9]{1,8})?\z",RegexOptions.NonBacktracking))throw Error("REP_QUANTITY_INVALID");
        var parts=value.Split('.');var n=BigInteger.Parse(parts[0],CultureInfo.InvariantCulture)*Scale;
        if(parts.Length==2)n+=BigInteger.Parse(parts[1].PadRight(8,'0'),CultureInfo.InvariantCulture);
        if(positive&&n==0)throw Error("REP_POSITIVE_QUANTITY_REQUIRED");return n;
    }
    internal static string Text(BigInteger value)
    {
        if(value<0||value>=BigInteger.Pow(10,38))throw Error("REP_TOTAL_OVERFLOW",422);
        return (value/Scale).ToString(CultureInfo.InvariantCulture)+"."+(value%Scale).ToString("D8",CultureInfo.InvariantCulture);
    }
    internal static BigInteger SignedNumber(string value)=>value.StartsWith('-')?-Number(value[1..]):Number(value);
    internal static RepQuantityV1 Q(BigInteger n,RepRefV1 unit)=>new(Text(n),unit);
    internal static BigInteger SpanAmount(RepSpanV1 s)
    {var from=Number(s.From);var to=Number(s.To);if(to<=from)throw Error("REP_SPAN_INVALID");return to-from;}
    internal static string Slot(RepRefV1 target,RepRefV1 product,RepRefV1 technical,RepRefV1 owner,RepRefV1 unit)
        =>Hash(new{target,product,technical,owner,unit});
    internal static string Slot(RepRuleV1 r,RepRefV1 target)=>Slot(target,r.ProductRef,r.TechnicalRef,r.OwnerRef,r.BaseUomRef);
    internal static string Slot(RepRootObservationV1 r)=>Slot(r.LocationRef,r.ProductRef,r.TechnicalRef,r.OwnerRef,r.BaseUomRef);
    internal static string Slot(RepTargetObservationV1 r)=>Slot(r.LocationRef,r.ProductRef,r.TechnicalRef,r.OwnerRef,r.BaseUomRef);
    internal static bool SameDimension(RepRuleV1 r,RepRootObservationV1 s)=>r.ProductRef==s.ProductRef&&r.TechnicalRef==s.TechnicalRef&&r.OwnerRef==s.OwnerRef&&r.BaseUomRef==s.BaseUomRef;
    internal static bool SameRoot(RepRefV1 a,RepRefV1 b)=>a.Owner==b.Owner&&a.Id==b.Id;
    internal static string SelectionDigest(RepSelectionV1[] selected)=>Hash(selected.OrderBy(x=>x.LineKey,StringComparer.Ordinal).ToArray());
    internal static BigInteger Union(IEnumerable<RepSpanV1> spans)
    {
        BigInteger sum=0;
        foreach(var group in spans.GroupBy(x=>x.RootRef))
        {
            BigInteger from=0,to=0;bool first=true;
            foreach(var s in group.OrderBy(x=>Number(x.From)))
            {var lo=Number(s.From);var hi=lo+SpanAmount(s);if(first){from=lo;to=hi;first=false;}
             else if(lo<=to)to=BigInteger.Max(to,hi);else{sum+=to-from;from=lo;to=hi;}}
            if(!first)sum+=to-from;
        }
        return sum;
    }
    internal static void ValidateObservation(RepObservationV1 o)
    {
        Reference(o.CutRef);Reference(o.Scope.SiteRef);Reference(o.Scope.WarehouseRef);
        if(o.DataKind is not ("SIMULATED" or "OBSERVED")||o.Coverage is not ("COMPLETE" or "PARTIAL" or "UNKNOWN"))throw Error("REP_OBSERVATION_INVALID");
        if(o.Locations.Select(x=>x.Ref).Distinct().Count()!=o.Locations.Length||o.Locations.Select(x=>x.NativeLocationId).Distinct().Count()!=o.Locations.Length)throw Error("REP_LOCATION_MAPPING_DUPLICATED");
        foreach(var l in o.Locations){Reference(l.Ref);if(l.NativeLocationId==Guid.Empty)throw Error("REP_LOCATION_MAPPING_INVALID");}
        bool Mapped(RepRefV1 r)=>o.Locations.Any(x=>x.Ref==r);
        if(o.Rules.Select(x=>x.Ref.Id).Distinct().Count()!=o.Rules.Length||o.Targets.Select(Slot).Distinct().Count()!=o.Targets.Length)throw Error("REP_OBSERVATION_DUPLICATED");
        foreach(var r in o.Rules)
        {
            foreach(var x in new[]{r.Ref,r.ProductRef,r.TechnicalRef,r.OwnerRef,r.BaseUomRef,r.SourceRuleRef})Reference(x);
            if(r.Coverage is not ("COMPLETE" or "UNKNOWN")||r.TargetLocationRefs.Length==0||r.TargetLocationRefs.Any(x=>!Mapped(x))
                ||r.ValidTo<=r.ValidFrom||r.MinQuantity.UomRef!=r.BaseUomRef||r.MaxQuantity.UomRef!=r.BaseUomRef
                ||Number(r.MinQuantity.Value)>Number(r.MaxQuantity.Value,positive:true))throw Error("REP_RULE_INVALID");
            if(r.LotMultiple is {} lot){if(lot.UomRef!=r.BaseUomRef)throw Error("REP_UNIT_MISMATCH");Number(lot.Value,positive:true);}
        }
        foreach(var target in o.Targets)
        {
            if(!Mapped(target.LocationRef)||target.Coverage is not ("COMPLETE" or "UNKNOWN"))throw Error("REP_TARGET_INVALID");
            foreach(var q in new[]{target.OnHand}.OfType<RepQuantityV1>())
            {if(q.UomRef!=target.BaseUomRef)throw Error("REP_UNIT_MISMATCH");Number(q.Value,total:true);}
            if(target.Coverage=="COMPLETE"&&(!target.Initialized||target.OnHand==null))throw Error("REP_EMPTY_TARGET_UNPROVEN");
            if(target.CapacityDomains.Select(x=>new{x.DomainRef,x.MetricRef,x.UomRef}).Distinct().Count()!=target.CapacityDomains.Length)throw Error("REP_CAPACITY_DUPLICATED");
            foreach(var d in target.CapacityDomains)
            {Reference(d.DomainRef);Reference(d.MetricRef);Reference(d.UomRef);
             if(d.Mode=="LIMITED")Number(d.FreeValue,total:true);else if(d.Mode is not ("UNLIMITED" or "UNKNOWN")||d.FreeValue!=null)throw Error("REP_CAPACITY_INVALID");}
        }
        foreach(var root in o.Roots)
        {if(!Mapped(root.LocationRef))throw Error("REP_SOURCE_LOCATION_INVALID");Reference(root.Span.RootRef);SpanAmount(root.Span);Reference(root.StockObservationRef);Reference(root.MasterConditionRef);
         foreach(var r in root.SourceRuleRefs)Reference(r);
         foreach(var d in root.CapacityDeltas){Reference(d.DomainRef);Reference(d.MetricRef);Reference(d.UomRef);SignedNumber(d.Numerator);Number(d.Denominator,positive:true);}}
        foreach(var group in o.Roots.GroupBy(x=>new{x.Span.RootRef.Owner,x.Span.RootRef.Id}))
        {
            if(group.Select(x=>x.Span.RootRef).Distinct().Count()!=1)throw Error("REP_SOURCE_VERSION_AMBIGUOUS");
            BigInteger end=-1;foreach(var r in group.OrderBy(x=>Number(x.Span.From)))
            {var from=Number(r.Span.From);if(from<end)throw Error("REP_SOURCE_OVERLAP");end=Number(r.Span.To);}
        }
        foreach(var i in o.Incoming)
        {Key(i.IntentKey);Key(i.DemandSlotKey);if(i.Coverage is not ("COMPLETE" or "UNKNOWN"))throw Error("REP_INCOMING_INVALID");foreach(var s in i.OpenSpans){Reference(s.RootRef);SpanAmount(s);}}
    }
}
