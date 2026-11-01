using System.Text.Json;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.RmaFormalStoreV1;
using Ranges = CP6.Core.Services.Wms.RmaRangeSetV1;

namespace CP6.Core.Services.Wms;

public sealed partial class RmaFormalOwnerV1
{
    private const string CommercialJournal="Main.Wms.RmaCommercialCommand.v1";
    private static string CommercialCoordinate(RmaSourceCoordinateV1 source) => Digest(new{source.SourceOwner,source.ShipmentLineRef.Owner,source.ShipmentLineRef.Id});
    private async Task<RmaCommercialWorkV1[]> CommercialWorks(Guid application,CancellationToken ct)
        => (await db.Database.SqlQuery<byte[]>($"SELECT body AS \"Value\" FROM cp6_rma_formal_v1.commercial_requests WHERE tenant_id={db.CurrentTenantId} AND application_id={application}")
            .ToArrayAsync(ct)).Select(x=>JsonSerializer.Deserialize<RmaCommercialWorkV1>(x,RmaFormalStoreV1.Json)!).OrderBy(x=>x.Request.RequestedAt).ToArray();
    private async Task<RmaCommercialWorkV1> CommercialWork(Guid application,Guid request,CancellationToken ct)
        => (await CommercialWorks(application,ct)).SingleOrDefault(x=>x.Request.RequestId==request) ?? throw Error("RMA_COMMERCIAL_REQUEST_NOT_FOUND",404);
    private async Task CommercialSave(RmaCommercialWorkV1 work,bool create,CancellationToken ct)
    {
        var id=work.Request.RequestId;var body=Encode(work);
        if(create) await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_rma_formal_v1.commercial_requests(tenant_id,request_id,application_id,revision,body) VALUES({db.CurrentTenantId},{id},{work.Request.ApplicationId},{work.Revision},{body})",ct);
        else await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE cp6_rma_formal_v1.commercial_requests SET revision={work.Revision},body={body} WHERE tenant_id={db.CurrentTenantId} AND request_id={id}",ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_rma_formal_v1.commercial_history(tenant_id,request_id,revision,body) VALUES({db.CurrentTenantId},{id},{work.Revision},{body})",ct);
    }
    public Task<RmaCommercialContextV1> CommercialContextAsync(Guid application,CancellationToken ct=default)
        => Run("commercial-query",false,async(store,a,t)=>
        {
            var app=await Current(store,a,application,t);var auth=await AuthorizationCurrent(application,t);
            return new RmaCommercialContextV1(View(app).ApplicationRef,auth,auth?.AuthorizedShares.Select(ShareReference).ToArray() ?? [],
                await CommercialWorks(application,t),new[]{"commercial-request","commercial-dispatch"}.Where(x=>a.Authorities[0].Actor.ActionKeys.Contains("wms-rma:"+x)).ToArray());
        },ct);
    public Task<RmaCommercialWorkV1> RequestCommercialAsync(Guid application,Guid command,RmaCommercialRequestV1 input,CancellationToken ct=default)
    {
        Reference(input.ExpectedRmaHeadRef);Reference(input.ExpectedAuthorizationRef);
        if(application==Guid.Empty || command==Guid.Empty || input.ReturnShareRefs==null || input.ReturnShareRefs.Length is <1 or >100
            || input.ReturnShareRefs.Distinct().Count()!=input.ReturnShareRefs.Length || string.IsNullOrWhiteSpace(input.ReasonCode) || input.ReasonCode.Length>200
            || input.RequestedOutcome is not ("CREDIT" or "REFUND" or "REPLACEMENT" or "SUPPLEMENTARY_SHIPMENT" or "RETURN_ONLY" or "REJECT")) throw Error("VALIDATION_FAILED",400);
        foreach(var share in input.ReturnShareRefs) Reference(share);
        var bytes=Encode(new{application,input});var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Run("commercial-request",true,async(store,a,t)=>
        {
            if(original!=null) return await DiscloseCommercial(store,a,application,original,bytes,t);
            var app=await Current(store,a,application,t);WriteFields(a,false);var auth=await ReceivingAuthorization(app,t);
            if(View(app).ApplicationRef!=input.ExpectedRmaHeadRef || auth.DecisionRef!=input.ExpectedAuthorizationRef) throw Error("CAS_CONFLICT",412);
            var shares=auth.AuthorizedShares.ToDictionary(ShareReference);var lines=new List<RmaCommercialLineV1>();
            foreach(var reference in input.ReturnShareRefs)
            {
                if(!shares.TryGetValue(reference,out var share)) throw Error("RETURN_SHARE_NOT_CURRENT",409);
                var source=await (returnSources ?? throw Error("RMA_RETURN_SOURCE_NOT_CONFIGURED",503)).ReadAsync(db.CurrentTenantId,share.Original,t);
                if(input.RequestedOutcome is "CREDIT" or "REFUND" && source.Coordinate.InvoiceLineRef==null) throw Error("RMA_INVOICE_SOURCE_REQUIRED",409);
                var ranges=Ranges.Read(share.Ranges);
                var segments=await ClaimSegments().Where(x=>x.ClaimId==Guid.Parse(share.RangeClaimRef.Id)).ToArrayAsync(t);
                if(!Ranges.Subset(ranges,segments.Where(x=>x.State is "ACTIVE_RESERVED" or "CONSUMED_BY_RECEIPT").Select(x=>new Ranges.Segment(x.From,x.To)))
                    || Ranges.Intersects(ranges,Ranges.Read(source.CreditedRanges))) throw Error("RETURN_RANGE_NOT_AVAILABLE",409);
                // A request retains its range even while external commitment is unknown. Corrections use an Owner successor.
                var coordinate=CommercialCoordinate(source.Coordinate);
                foreach(var range in ranges)
                    if(await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM cp6_rma_formal_v1.commercial_ranges WHERE tenant_id={db.CurrentTenantId} AND coordinate_key={coordinate} AND range_from<{range.To} AND range_to>{range.From}").AnyAsync(t))
                        throw Error("RMA_COMMERCIAL_RANGE_ALREADY_REQUESTED",409);
                lines.Add(new(reference,share,source.Coordinate));
            }
            if(lines.Select(x=>x.Share.Quantity.Uom).Distinct().Count()!=1) throw Error("RMA_COMMERCIAL_UNIT_MISMATCH",422);
            var physical=(await QualitySources(a,application,t)).Select(x=>x.Reference).ToArray();
            var at=DateTimeOffset.UtcNow;var request=new RmaCommercialOriginalV1(command,db.CurrentTenantId,application,app.Scope,View(app).ApplicationRef,auth.DecisionRef,
                lines.ToArray(),input.RequestedOutcome,input.ReasonCode,physical,at);
            var requestRef=new FulfilmentRefV1("Main.Wms.RmaCommercialRequest.v1",command.ToString(),"1",Digest(request));
            var work=new RmaCommercialWorkV1(requestRef,request,1,"NOT_OBSERVED",null,null,at,[],"UNKNOWN");
            await CommercialSave(work,true,t);
            foreach(var line in lines)
                foreach(var range in Ranges.Read(line.Share.Ranges))
                    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO cp6_rma_formal_v1.commercial_ranges(tenant_id,request_id,coordinate_key,range_from,range_to) VALUES({db.CurrentTenantId},{command},{CommercialCoordinate(line.Source)},{range.From},{range.To})",t);
            await journal.Store(CommercialJournal,"WMS-RMA.COMMERCIAL/1",command,a.ActorId,bytes,work,[application],new{requestRef,auth.DecisionRef},t);
            a.AtCommit(async token=>{if((await ReceivingAuthorization(app,token)).DecisionRef!=auth.DecisionRef) throw Error("RETURN_AUTHORIZATION_CHANGED",409);});
            return work;
        },ct,async t=>{original=await journal.Read(CommercialJournal,command,t);return original==null ? "commercial-request" : "commercial-query";});
    }
    private async Task<RmaCommercialWorkV1> DiscloseCommercial(RmaFormalStoreV1 store,OperationsAccessV1 a,Guid application,
        MainOwnerOperationJournalV1.Original original,byte[]? input,CancellationToken ct)
    {
        if(original.Actor!=a.ActorId || !original.Objects.Contains(application)) throw Error("OBJECT_FORBIDDEN",403);
        if(input!=null && !MainOwnerOperationJournalV1.Matches(original,input)) throw Error("COMMAND_BINDING_CONFLICT",409);
        await Current(store,a,application,ct);return JsonSerializer.Deserialize<RmaCommercialWorkV1>(original.Result)!;
    }
    public Task<RmaCommercialCommandResultV1> QueryCommercialCommandAsync(RmaCommercialQueryV1 query,CancellationToken ct=default)
        => Run("commercial-query",false,async(store,a,t)=>
        {
            await Current(store,a,query.ApplicationId,t);var original=await new MainOwnerOperationJournalV1(db).Read(CommercialJournal,query.CommandId,t);
            return original==null ? new RmaCommercialCommandResultV1("NOT_OBSERVED",null)
                : new("FOUND",await DiscloseCommercial(store,a,query.ApplicationId,original,null,t));
        },ct);
    public async Task<RmaCommercialWorkV1> ObserveCommercialAsync(Guid application,Guid request,bool submit,CancellationToken ct=default)
    {
        var gateway=commercialGateway ?? throw Error("RMA_COMMERCIAL_OWNER_NOT_CONFIGURED",503);
        var before=await Run(submit ? "commercial-dispatch" : "commercial-query",submit,async(store,a,t)=>
        {
            var app=await Current(store,a,application,t);var work=await CommercialWork(application,request,t);
            if(submit)
            {
                if((await ReceivingAuthorization(app,t)).DecisionRef!=work.Request.AuthorizationRef) throw Error("RETURN_AUTHORIZATION_CHANGED",409);
                foreach(var line in work.Request.Lines)
                {
                    var claim=Guid.Parse(line.Share.RangeClaimRef.Id);
                    var available=await ClaimSegments().Where(x=>x.ClaimId==claim && (x.State=="ACTIVE_RESERVED" || x.State=="CONSUMED_BY_RECEIPT")).ToArrayAsync(t);
                    if(!Ranges.Subset(Ranges.Read(line.Share.Ranges),available.Select(x=>new Ranges.Segment(x.From,x.To)))) throw Error("RETURN_RANGE_NOT_AVAILABLE",409);
                }
                // The original intent is already committed. An external timeout never rolls it back or permits a new key.
                work=work with{Revision=work.Revision+1,ObservationState="UNKNOWN",ObservedAt=DateTimeOffset.UtcNow,ReasonCodes=["OWNER_DISPATCH_STARTED"]};
                await CommercialSave(work,false,t);
            }
            return work;
        },ct);
        var observation=await gateway.ObserveAsync(new(before.RequestRef,before.Request),submit,ct);
        return await Run("commercial-query",true,async(store,a,t)=>
        {
            await Current(store,a,application,t);var current=await CommercialWork(application,request,t);
            // A slower read must not replace a newer observation.
            if(current.Revision!=before.Revision) return current;
            var commercial=observation.CommercialOutcome ?? current.CommercialOutcome;
            if(observation.CommercialOutcome is {} next && current.CommercialOutcome is {} previous)
            {
                if(next.GetProperty("outcomeRef").Deserialize<FulfilmentRefV1>(RmaFormalStoreV1.Json)==previous.GetProperty("outcomeRef").Deserialize<FulfilmentRefV1>(RmaFormalStoreV1.Json))
                { if(!System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(next.GetRawText()),System.Text.Json.Nodes.JsonNode.Parse(previous.GetRawText()))) throw Error("COMMERCIAL_ORIGINAL_RESULT_CHANGED",502);commercial=previous; }
                else if(next.GetProperty("previousOutcomeRef").Deserialize<FulfilmentRefV1>(RmaFormalStoreV1.Json)!=previous.GetProperty("outcomeRef").Deserialize<FulfilmentRefV1>(RmaFormalStoreV1.Json))
                    throw Error("COMMERCIAL_SUCCESSOR_MISMATCH",502);
            }
            var financial=observation.FinancialOutcome ?? current.FinancialOutcome;
            if(observation.FinancialOutcome is {} fin)
            {
                var receipts=(await QualitySources(a,application,t)).Select(x=>x.Reference).ToHashSet();
                foreach(var axis in fin.GetProperty("axes").EnumerateArray())
                {
                    var link=axis.GetProperty("commercialOutcomeRef");
                    if(link.ValueKind!=JsonValueKind.Null && (commercial==null || link.Deserialize<FulfilmentRefV1>(RmaFormalStoreV1.Json)!=commercial.Value.GetProperty("outcomeRef").Deserialize<FulfilmentRefV1>(RmaFormalStoreV1.Json)))
                        throw Error("FINANCIAL_COMMERCIAL_SOURCE_MISMATCH",502);
                    var axisName=axis.GetProperty("axis").GetString();
                    if(axisName is "AR_CREDIT" or "REFUND_CASH" && !commercial!.Value.GetProperty("components").EnumerateArray().Any(x=>x.GetProperty("kind").GetString()==(axisName=="AR_CREDIT" ? "CREDIT" : "REFUND_AUTHORIZATION")))
                        throw Error("FINANCIAL_COMMERCIAL_COMPONENT_MISMATCH",502);
                    if(axis.GetProperty("physicalReceiptRefs").EnumerateArray().Any(x=>!receipts.Contains(x.Deserialize<FulfilmentRefV1>(RmaFormalStoreV1.Json)!)))
                        throw Error("FINANCIAL_PHYSICAL_SOURCE_MISMATCH",502);
                }
                if(current.FinancialOutcome is {} old && fin.GetProperty("observedAt").GetDateTimeOffset()<old.GetProperty("observedAt").GetDateTimeOffset())
                    throw Error("FINANCIAL_OBSERVATION_STALE",409);
            }
            var saved=current with{Revision=current.Revision+1,ObservationState=observation.State,CommercialOutcome=commercial,FinancialOutcome=financial,
                ObservedAt=observation.ObservedAt,ReasonCodes=observation.ReasonCodes,EvidenceMode=observation.EvidenceMode};
            await CommercialSave(saved,false,t);return saved;
        },ct);
    }
}
