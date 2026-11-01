using System.Numerics;
using static CP6.Core.Services.Wms.Replenish.ReplenishPlanningValuesV1;
namespace CP6.Core.Services.Wms.Replenish;

internal static class ReplenishPlanningCalculatorV1
{
    internal sealed record Allocation(RepRowV1 Row,RepSplitV1[] Splits,string Slot);
    internal static Allocation Calculate(RepObservationV1 observation,RepRuleV1 rule,RepRefV1 target,
        RepIntentV1[] intents,RepSpanV1[] reserved,DateTimeOffset now)
    {
        var slot=Slot(rule,target);var line=Hash(new{rule=rule.Ref,target});var unit=rule.BaseUomRef;
        var current=observation.Targets.SingleOrDefault(x=>Slot(x)==slot);
        var incoming=observation.Incoming.Where(x=>x.DemandSlotKey==slot).ToArray();
        var own=intents.Where(x=>x.DemandSlotKey==slot).ToArray();
        RepRowV1 Row(RepQuantityV1? onHand,RepQuantityV1? open,RepQuantityV1? effective,RepQuantityV1? needed,
            RepQuantityV1? proposed,RepSourceV1[] sources,string coverage,params string[] reasons)
            =>new(line,rule.Ref,target,rule.ProductRef,rule.TechnicalRef,rule.OwnerRef,unit,onHand,open,effective,needed,proposed,null,sources,coverage,reasons);
        if(rule.ValidFrom>now||rule.ValidTo<=now)throw Error("REP_RULE_NOT_CURRENT",412);
        if(observation.Coverage!="COMPLETE"||rule.Coverage!="COMPLETE"||current?.Coverage!="COMPLETE"||!current.Initialized
            ||incoming.Any(x=>x.Coverage!="COMPLETE")||own.Any(x=>x.Coverage!="COMPLETE"||x.OpenIncoming==null))
            return new(Row(null,null,null,null,null,[],"UNKNOWN","COMPLETE_CUT_REQUIRED"),[],slot);
        // Local intent IDs are authoritative: transported Order/Task/Transfer aliases are not additional obligations.
        var localKeys=intents.Select(x=>x.Ref.Id).ToHashSet(StringComparer.Ordinal);
        var external=incoming.Where(x=>!localKeys.Contains(x.IntentKey)).GroupBy(x=>x.IntentKey,StringComparer.Ordinal)
            .SelectMany(x=>x.SelectMany(y=>y.OpenSpans)).ToArray();
        var openAmount=Union(external)+own.Aggregate(BigInteger.Zero,(n,x)=>n+Number(x.OpenIncoming!.Value,total:true));
        var hand=Number(current.OnHand!.Value,total:true);var effectiveAmount=hand+openAmount;
        var onHand=Q(hand,unit);var open=Q(openAmount,unit);var effective=Q(effectiveAmount,unit);
        if(effectiveAmount>=Number(rule.MinQuantity.Value))return new(Row(onHand,open,effective,Q(0,unit),Q(0,unit),[],"COMPLETE","NO_NEED"),[],slot);
        var need=Number(rule.MaxQuantity.Value)-effectiveAmount;
        var candidates=observation.Roots.Where(x=>x.Movable&&x.LocationRef!=target&&SameDimension(rule,x)&&x.SourceRuleRefs.Contains(rule.SourceRuleRef))
            .OrderBy(x=>x.ExpiryAt??DateTimeOffset.MaxValue).ThenBy(x=>x.ReceivedAt??DateTimeOffset.MaxValue)
            .ThenBy(x=>x.LocationRef.Id,StringComparer.Ordinal).ThenBy(x=>x.Span.RootRef.Id,StringComparer.Ordinal).ThenBy(x=>Number(x.Span.From))
            .SelectMany(x=>Subtract(x.Span,reserved.Where(r=>SameRoot(r.RootRef,x.Span.RootRef))).Select(span=>x with{Span=span})).ToArray();
        var total=candidates.Aggregate(BigInteger.Zero,(n,x)=>n+SpanAmount(x.Span));var proposed=BigInteger.Min(need,total);
        if(rule.LotMultiple is {} multiple){var lot=Number(multiple.Value,positive:true);proposed=proposed/lot*lot;}
        var chosen=new List<RepRootObservationV1>();var remaining=proposed;
        foreach(var source in candidates)
        {if(remaining==0)break;var amount=BigInteger.Min(remaining,SpanAmount(source.Span));chosen.Add(source with{Span=source.Span with{To=Text(Number(source.Span.From)+amount)}});remaining-=amount;}
        var splits=chosen.GroupBy(x=>x.LocationRef).Select(g=>
        {
            var first=g.First();var spans=g.Select(x=>x.Span).ToArray();var quantity=Q(g.Aggregate(BigInteger.Zero,(n,x)=>n+SpanAmount(x.Span)),unit);
            var source=new RepSourceV1(g.Key,spans,quantity,observation.CutRef,first.ExpiryAt,first.ReceivedAt,first.MasterConditionRef);
            return new RepSplitV1(Hash(new{line,location=g.Key}),g.Key,source,quantity);
        }).ToArray();
        if(splits.Length>200)throw Error("REP_ALLOCATION_SPLIT_LIMIT",422);
        var sources=splits.Select(x=>x.Source).ToArray();
        if(!current.CapacityKnown||current.CapacityDomains.Any(x=>x.Mode=="UNKNOWN"))return new(Row(onHand,open,effective,Q(need,unit),Q(proposed,unit),sources,"UNKNOWN","CAPACITY_UNKNOWN"),[],slot);
        foreach(var domain in current.CapacityDomains)
        {
            BigInteger delta=0;
            foreach(var root in chosen)
            {
                var rates=root.CapacityDeltas.Where(x=>x.DomainRef==domain.DomainRef&&x.MetricRef==domain.MetricRef&&x.UomRef==domain.UomRef).ToArray();
                if(rates.Length==0)return new(Row(onHand,open,effective,Q(need,unit),Q(proposed,unit),sources,"UNKNOWN","CAPACITY_CONVERSION_UNKNOWN"),[],slot);
                foreach(var rate in rates)
                {var amount=BigInteger.DivRem(SpanAmount(root.Span)*SignedNumber(rate.Numerator),Number(rate.Denominator,positive:true),out var remainder);
                 if(remainder!=0)throw Error("REP_CAPACITY_PRECISION_INVALID",422);delta+=amount;}
            }
            if(domain.Mode=="LIMITED"&&delta>Number(domain.FreeValue,total:true))
                return new(Row(onHand,open,effective,Q(need,unit),Q(proposed,unit),sources,"UNKNOWN","CAPACITY_INSUFFICIENT"),[],slot);
        }
        return new(Row(onHand,open,effective,Q(need,unit),Q(proposed,unit),sources,"COMPLETE",
            proposed==0?(total==0?"NO_MOVABLE_SOURCE":"NO_FULL_MULTIPLE"):"READY"),splits,slot);
    }
    internal static IEnumerable<RepSpanV1> Subtract(RepSpanV1 span,IEnumerable<RepSpanV1> exclusions)
    {
        var at=Number(span.From);var end=Number(span.To);
        foreach(var x in exclusions.OrderBy(x=>Number(x.From)))
        {var low=Number(x.From);var high=Number(x.To);if(high<=at)continue;if(low>=end)break;
         if(low>at)yield return span with{From=Text(at),To=Text(BigInteger.Min(low,end))};at=BigInteger.Max(at,high);if(at>=end)yield break;}
        if(at<end)yield return span with{From=Text(at),To=Text(end)};
    }
}
