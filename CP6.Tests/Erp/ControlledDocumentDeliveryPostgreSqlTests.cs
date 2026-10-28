using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private sealed class DocumentDecisionTransport(ApprovalDecisionDeliveryRouteV2 route,DocumentReviewFactReceiverV1 receiver):IDocumentDecisionTransportV1
    {
        public bool LoseAck=true;public int Sends,Queries;
        public ApprovalDecisionDeliveryRouteV2? Route(Guid tenant)=>tenant==route.Tenant?route:null;
        public async Task<ApprovalDecisionTransportResponseV2> SendAsync(ApprovalDecisionDeliveryRouteV2 selected,Guid id,byte[] bytes,bool query,CancellationToken ct)
        {
            Assert.Equal(route,selected);if(query)Queries++;else Sends++;
            var result=query?await receiver.ReceivedAsync(id,ct):await receiver.ReceiveAsync(bytes,ct);
            if(!query&&LoseAck){LoseAck=false;throw new IOException("Explicit lost decision response");}
            return new(result.Status,ApprovalCanonicalV2.Bytes(result.Body));
        }
    }
    private sealed class DocumentReceiptTransport(ToolApprovalDeliveryRouteV1 route,ApprovalReceiptIngestionV2 receiver):IDocumentApplicationTransportV1
    {
        public bool LoseAck=true;public int Sends,Queries;
        public ToolApprovalDeliveryRouteV1? Route(Guid tenant)=>tenant==route.Tenant?route:null;
        public async Task<ToolApprovalDeliveryResponseV1> SendAsync(ToolApprovalDeliveryRouteV1 selected,Guid id,byte[] bytes,bool query,CancellationToken ct)
        {
            Assert.Equal(route,selected);if(query)Queries++;else Sends++;
            var result=query?await receiver.OriginalAsync(id,ct):await receiver.ReceiveAsync(bytes,ct);
            if(!query&&LoseAck){LoseAck=false;throw new IOException("Explicit lost application receipt response");}
            return new(result.Status,ApprovalCanonicalV2.Bytes(result.Body));
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task DocumentReview_decision_and_application_receipt_delivery_recover_lost_responses_without_duplicate_effects()
    {
        await using var h=await Seed();var f=await DocumentApplicationSetup(h);var d=f.Document;var principal=f.Authority.Identity.Principal;
        var decisionRoute=new ApprovalDecisionDeliveryRouteV2(ScopeEnvironment,h.Tenant,principal,"document-workflow-producer","https://issuer.example","https://owner.example","1","DEMO",ApprovalBusinessProfileV2.DocumentOwner);
        var transport=new DocumentDecisionTransport(decisionRoute,f.Receiver);
        var sender=new DocumentDecisionDeliveryV1(h.Db,ScopeFence(h),transport);
        Assert.Equal("OUTCOME_UNKNOWN",(await sender.RunAsync(h.Tenant)).State);
        var owner=DocumentReviews(h,d.Files);var detail=await owner.SubmissionAsync(d.Document,d.Submission);
        Assert.Equal("APPROVED",detail.Application.Decision);Assert.False(detail.Submission.IsApproved);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_document_review_fact_v1.subject_grants SET can_receive=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant};
            UPDATE cp6_approval_delivery_v2.delivery SET lease_until=clock_timestamp()-interval '1 second',next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant};
            """);
        Assert.Equal("RECEIVED",(await sender.RunAsync(h.Tenant)).State);Assert.Equal(1,transport.Sends);Assert.Equal(2,transport.Queries);
        var applied=await owner.ApplyAsync(d.Document,detail.Application.Etag,new(Guid.NewGuid(),d.Submission,null,"Apply exact signed professional approval"));
        var receiptPrincipal=Guid.NewGuid();
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_approval_receipt_v2.registrations(environment_id,tenant_id,principal_id,owner,revision,enabled,can_receive,can_query,evidence_mode)
            VALUES({ScopeEnvironment},{h.Tenant},{receiptPrincipal},{ApprovalBusinessProfileV2.DocumentOwner},{Guid.NewGuid()},true,true,true,'DEMO');
            INSERT INTO cp6_approval_receipt_v2.subject_grants(environment_id,tenant_id,principal_id,submission_id,owner,revision,enabled,can_receive,can_query)
            VALUES({ScopeEnvironment},{h.Tenant},{receiptPrincipal},{d.Submission},{ApprovalBusinessProfileV2.DocumentOwner},{Guid.NewGuid()},true,true,true);
            """);
        var receiptAuthority=new IntakeAuthority(f.Authority.Identity with{Principal=receiptPrincipal,Client="document-owner-receipt"});
        var receiver=new ApprovalReceiptIngestionV2(h.Db,ScopeFence(h),receiptAuthority);
        var route=new ToolApprovalDeliveryRouteV1(ScopeEnvironment,h.Tenant,receiptPrincipal,"document-owner-receipt","https://issuer.example","https://workflow.example","1","DEMO");
        var receiptTransport=new DocumentReceiptTransport(route,receiver);var receiptSender=new DocumentApplicationDeliveryV1(h.Db,ScopeFence(h),receiptTransport);
        Assert.Equal("OUTCOME_UNKNOWN",(await receiptSender.RunAsync(h.Tenant)).State);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_approval_receipt_v2.subject_grants SET can_receive=false,revision={Guid.NewGuid()} WHERE tenant_id={h.Tenant};
            UPDATE cp6_document_application_delivery_v1.delivery SET lease_until=clock_timestamp()-interval '1 second',next_attempt=clock_timestamp()-interval '1 second' WHERE tenant_id={h.Tenant};
            """);
        Assert.Equal("RECORDED",(await receiptSender.RunAsync(h.Tenant)).State);Assert.Equal(1,receiptTransport.Sends);Assert.Equal(2,receiptTransport.Queries);
        var projection=await receiver.CurrentAsync(applied.Receipt.ReceiptStreamId);Assert.Equal(applied.Receipt,projection.Receipt);Assert.False(projection.PendingGap);
        Assert.Equal(1,await h.Db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM cp6_document_review_application_v1.applications WHERE tenant_id={h.Tenant}").SingleAsync());
    }
}
