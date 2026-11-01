using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

// Validate once at the external ERP/Finance boundary; internal observation storage trusts this result.
internal static class RmaCommercialBoundaryV1
{
    private static void Require(bool condition) { if (!condition) throw Error("RMA_COMMERCIAL_OWNER_RESPONSE_INVALID",502); }
    private static void Shape(JsonElement value,string fields)
    {
        var names=fields.Split(' '); Require(value.ValueKind==JsonValueKind.Object);
        Require(value.EnumerateObject().Select(x=>x.Name).Order().SequenceEqual(names.Order()));
    }
    private static string Text(JsonElement value,int max=200)
    { var text=value.GetString(); Require(!string.IsNullOrWhiteSpace(text) && text.Length<=max); return text!; }
    private static FulfilmentRefV1 Ref(JsonElement value)
    {
        Shape(value,"owner id version digest"); var r=value.Deserialize<FulfilmentRefV1>(RmaFormalStoreV1.Json)!;
        Require(!string.IsNullOrWhiteSpace(r.Owner) && r.Owner.Length<=64 && !string.IsNullOrWhiteSpace(r.Id) && r.Id.Length<=200
            && !string.IsNullOrWhiteSpace(r.Version) && r.Version.Length<=64 && r.Digest!=null && Regex.IsMatch(r.Digest,@"\A[a-f0-9]{64}\z"));return r;
    }
    private static FulfilmentRefV1? NullableRef(JsonElement value) => value.ValueKind==JsonValueKind.Null ? null : Ref(value);
    private static JsonElement[] Array(JsonElement value,int min,int max)
    { var items=value.EnumerateArray().ToArray();Require(items.Length>=min && items.Length<=max);return items; }
    internal static BigInteger Number(string value)
    {
        Require(value!=null && Regex.IsMatch(value,@"\A-?(0|[1-9][0-9]{0,29})(\.[0-9]{1,8})?\z",RegexOptions.CultureInvariant));
        var dot=value.IndexOf('.'); var scale=dot<0 ? 0 : value.Length-dot-1;
        return BigInteger.Parse(value.Replace(".",""),CultureInfo.InvariantCulture)*BigInteger.Pow(10,8-scale);
    }
    private static string Currency(JsonElement value)
    { var currency=Text(value,3);Require(Regex.IsMatch(currency,@"\A[A-Z]{3}\z"));return currency; }
    private static (BigInteger Amount,string Currency) Money(JsonElement value)
    { Shape(value,"value currency");return (Number(Text(value.GetProperty("value"),40)),Currency(value.GetProperty("currency"))); }
    private static void Reasons(JsonElement value) { foreach(var code in Array(value,0,50)) _=Text(code); }
    internal static void Validate(RmaCommercialOwnerRequestV1 request,RmaCommercialObservationV1 observation)
    {
        try
        {
            Require(observation.TenantId==request.Request.TenantId && observation.RequestRef==request.RequestRef
                && observation.State is "FOUND" or "STALE_HEAD" or "NOT_OBSERVED" or "UNKNOWN" or "PENDING" or "REQUIRED_FENCED"
                && observation.ObservedAt!=default && observation.EvidenceMode is "REAL" or "SIMULATED" or "UNKNOWN"
                && observation.ReasonCodes!=null && observation.ReasonCodes.Length<=50);
            Require((observation.State=="FOUND")==observation.CommercialOutcome.HasValue);
            if(observation.CommercialOutcome is {} commercial) Commercial(request,commercial);
            if(observation.FinancialOutcome is {} financial) Financial(request,financial);
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or NullReferenceException)
        { throw Error("RMA_COMMERCIAL_OWNER_RESPONSE_INVALID",502); }
        catch(CP6.Core.Services.Erp.OrderPriceCorrectionException e) when(e.Status!=502)
        { throw Error("RMA_COMMERCIAL_OWNER_RESPONSE_INVALID",502); }
    }
    private static void Commercial(RmaCommercialOwnerRequestV1 request,JsonElement result)
    {
        Shape(result,"owner requestRef outcomeRef resultVersion resultDigest rmaId components approvalRef currentHeadProof previousOutcomeRef recordedAt terminalAt");
        Require(result.GetProperty("owner").GetString()=="ERP-CREDIT-01" && Ref(result.GetProperty("requestRef"))==request.RequestRef
            && result.GetProperty("rmaId").GetGuid()==request.Request.ApplicationId);
        _=Ref(result.GetProperty("outcomeRef"));_=Text(result.GetProperty("resultVersion"));
        Require(Regex.IsMatch(Text(result.GetProperty("resultDigest"),64),@"\A[a-f0-9]{64}\z"));
        _=Ref(result.GetProperty("approvalRef"));_=Ref(result.GetProperty("currentHeadProof"));_=NullableRef(result.GetProperty("previousOutcomeRef"));
        Require(result.GetProperty("recordedAt").GetDateTimeOffset()<=result.GetProperty("terminalAt").GetDateTimeOffset());
        var components=Array(result.GetProperty("components"),1,20);var used=new Dictionary<FulfilmentRefV1,List<Ranges.Segment>>();
        var credits=new Dictionary<FulfilmentRefV1,(BigInteger Amount,string Currency)>();var refunds=new Dictionary<FulfilmentRefV1,BigInteger>();
        var componentRefs=new HashSet<FulfilmentRefV1>();var effectKeys=new HashSet<string>();
        foreach(var component in components)
        {
            var kind=Text(component.GetProperty("kind"));Require(componentRefs.Add(Ref(component.GetProperty("componentRef"))));
            if(kind is "RETURN_ONLY" or "REJECTED") Require(components.Length==1);
            if(kind=="CREDIT")
            {
                Shape(component,"kind componentRef invoiceAllocations approvedQuantity approvalRef stableEffectKey");
                BigInteger amount=0;string? currency=null;decimal quantity=0;
                foreach(var allocation in Array(component.GetProperty("invoiceAllocations"),1,100))
                {
                    Shape(allocation,"allocationId invoiceLineRef coveredClaimRefs coveredRanges quantity net tax gross");
                    Require(allocation.GetProperty("allocationId").GetGuid()!=Guid.Empty);
                    var invoice=Ref(allocation.GetProperty("invoiceLineRef"));
                    quantity+=Cover(request,allocation,used,invoice);
                    var net=Money(allocation.GetProperty("net"));var tax=Money(allocation.GetProperty("tax"));var gross=Money(allocation.GetProperty("gross"));
                    Require(net.Currency==tax.Currency && net.Currency==gross.Currency && net.Amount+tax.Amount==gross.Amount && gross.Amount>=0);
                    currency ??= gross.Currency;Require(currency==gross.Currency);amount+=gross.Amount;
                }
                Quantity(component.GetProperty("approvedQuantity"),request,quantity);
                credits.Add(Ref(component.GetProperty("componentRef")),(amount,currency!));
                _=Ref(component.GetProperty("approvalRef"));Require(effectKeys.Add(Text(component.GetProperty("stableEffectKey"))));
            }
            else if(kind=="REFUND_AUTHORIZATION")
            {
                Shape(component,"kind componentRef creditComponentRef refundAuthorizationRef authorizedAmount currency stableEffectKey");
                var money=Money(component.GetProperty("authorizedAmount"));Require(money.Currency==Currency(component.GetProperty("currency")) && money.Amount>=0);
                _=Ref(component.GetProperty("refundAuthorizationRef"));Require(effectKeys.Add(Text(component.GetProperty("stableEffectKey"))));
            }
            else if(kind is "REPLACEMENT" or "SUPPLEMENTARY_SHIPMENT")
            {
                Shape(component,"kind componentRef coveredClaimRefs coveredRanges quantity newDemandRef approvalRef stableEffectKey");
                _=Cover(request,component,used);_=Ref(component.GetProperty("newDemandRef"));_=Ref(component.GetProperty("approvalRef"));
                Require(effectKeys.Add(Text(component.GetProperty("stableEffectKey"))));
            }
            else if(kind is "RETURN_ONLY" or "REJECTED")
            {
                Shape(component,"kind componentRef coveredClaimRefs coveredRanges quantity "+(kind=="RETURN_ONLY" ? "reasonCode" : "reasonCodes"));
                _=Cover(request,component,used);if(kind=="RETURN_ONLY") _=Text(component.GetProperty("reasonCode"));else Reasons(component.GetProperty("reasonCodes"));
            }
            else Require(false);
        }
        foreach(var component in components.Where(x=>x.GetProperty("kind").GetString()=="REFUND_AUTHORIZATION"))
        {
            var credit=Ref(component.GetProperty("creditComponentRef"));var refund=Money(component.GetProperty("authorizedAmount"));
            Require(credits.TryGetValue(credit,out var original) && original.Currency==refund.Currency);
            refunds[credit]=refunds.GetValueOrDefault(credit)+refund.Amount;Require(refunds[credit]<=original.Amount);
        }
    }
    private static decimal Cover(RmaCommercialOwnerRequestV1 request,JsonElement component,Dictionary<FulfilmentRefV1,List<Ranges.Segment>> used,FulfilmentRefV1? invoice=null)
    {
        var claims=Array(component.GetProperty("coveredClaimRefs"),1,100).Select(Ref).ToArray();Require(claims.Distinct().Count()==claims.Length);
        var ranges=component.GetProperty("coveredRanges").Deserialize<FulfilmentRangeV1[]>(RmaFormalStoreV1.Json)!;
        var parsed=Ranges.Input(ranges.Select(x=>new RmaDeliveryRangeV1(x.From,x.To)).ToArray());decimal measure=0;
        foreach(var claim in claims)
        {
            var lines=request.Request.Lines.Where(x=>x.Share.RangeClaimRef==claim).ToArray();Require(lines.Length==1);
            Require(invoice==null || lines[0].Source.InvoiceLineRef==invoice);
            Require(Ranges.Subset(parsed,Ranges.Read(lines[0].Share.Ranges)));
            if(!used.TryGetValue(claim,out var previous)) used[claim]=previous=[];
            Require(!Ranges.Intersects(previous,parsed));previous.AddRange(parsed);measure+=Ranges.Measure(parsed);
        }
        Quantity(component.GetProperty("quantity"),request,measure);return measure;
    }
    private static void Quantity(JsonElement value,RmaCommercialOwnerRequestV1 request,decimal measure)
    {
        Shape(value,"value uom");var qty=value.Deserialize<FulfilmentQtyV1>(RmaFormalStoreV1.Json)!;
        Require(qty.Uom==request.Request.Lines[0].Share.Quantity.Uom && Number(qty.Value)==Number(Ranges.Text(measure)));
    }
    private static void Financial(RmaCommercialOwnerRequestV1 request,JsonElement result)
    {
        Shape(result,"rmaId policyRef policyVersion axes aggregateState observedAt sourceCutRef");
        Require(result.GetProperty("rmaId").GetGuid()==request.Request.ApplicationId);_=NullableRef(result.GetProperty("policyRef"));
        if(result.GetProperty("policyVersion").ValueKind!=JsonValueKind.Null) _=Text(result.GetProperty("policyVersion"));
        _=Ref(result.GetProperty("sourceCutRef"));_=result.GetProperty("observedAt").GetDateTimeOffset();
        var states=new List<string>();var axes=new HashSet<string>();
        foreach(var axis in Array(result.GetProperty("axes"),1,20))
        {
            Shape(axis,"axis owner state physicalReceiptRefs qualityDecisionRef commercialOutcomeRef sourceCutRef resultRef amount currency postingReceiptRefs reasonCodes observedAt coverage");
            var name=Text(axis.GetProperty("axis"));Require(name is "AR_CREDIT" or "REFUND_CASH" or "INVENTORY_VALUE" or "COGS" or "GL_JOURNAL");
            var owner=Text(axis.GetProperty("owner"));Require(axes.Add(name+":"+owner));
            var state=Text(axis.GetProperty("state"));Require(state is "NOT_APPLICABLE" or "WAITING_POLICY" or "WAITING_PRICE" or "WAITING_COST" or "DRAFT" or "PENDING" or "POSTED" or "PARTIAL" or "REVERSED" or "UNKNOWN" or "REQUIRED_FENCED");states.Add(state);
            var receipts=Array(axis.GetProperty("physicalReceiptRefs"),0,100).Select(Ref).ToArray();
            var commercial=NullableRef(axis.GetProperty("commercialOutcomeRef"));
            Require(name is not ("AR_CREDIT" or "REFUND_CASH") || commercial!=null);
            Require(name is not ("INVENTORY_VALUE" or "COGS") || receipts.Length>0);
            _=NullableRef(axis.GetProperty("qualityDecisionRef"));_=Ref(axis.GetProperty("sourceCutRef"));var effect=NullableRef(axis.GetProperty("resultRef"));
            var amount=axis.GetProperty("amount");var currency=axis.GetProperty("currency");
            Require((amount.ValueKind==JsonValueKind.Null)==(currency.ValueKind==JsonValueKind.Null));
            if(amount.ValueKind!=JsonValueKind.Null) { _=Number(Text(amount,40));_=Currency(currency); }
            var postings=Array(axis.GetProperty("postingReceiptRefs"),0,200).Select(Ref).ToArray();
            var coverage=Text(axis.GetProperty("coverage"));Require(coverage is "COMPLETE" or "PARTIAL" or "UNKNOWN");
            Require(state!="POSTED" || effect!=null && postings.Length>0 && amount.ValueKind!=JsonValueKind.Null && coverage=="COMPLETE");
            Reasons(axis.GetProperty("reasonCodes"));_=axis.GetProperty("observedAt").GetDateTimeOffset();
        }
        var aggregate=Text(result.GetProperty("aggregateState"));Require(aggregate is "NOT_STARTED" or "PENDING" or "PARTIAL" or "COMPLETE" or "REVERSED" or "UNKNOWN");
        Require(aggregate!="COMPLETE" || states.All(x=>x is "POSTED" or "NOT_APPLICABLE"));
        Require(aggregate!="REVERSED" || states.All(x=>x is "REVERSED" or "NOT_APPLICABLE"));
        Require(!states.Contains("UNKNOWN") || aggregate=="UNKNOWN");
        Require(!states.Contains("POSTED") || states.All(x=>x is "POSTED" or "NOT_APPLICABLE") || aggregate is "PARTIAL" or "UNKNOWN");
    }
}
