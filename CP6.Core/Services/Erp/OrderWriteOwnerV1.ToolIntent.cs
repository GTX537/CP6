using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Erp;

public sealed record ToolOrderIntentReceiptV1(Guid OperationId, string Kind, string State, Guid ToolId,
    ToolNativeOrderRefV1 Order, string RequestDigest, string OriginalLifecycle);

public sealed partial class OrderWriteOwnerV1
{
    // Original Order Owner durably accepts a change/cancel request; accepting is not applying it.
    // The native order lifecycle defaults to CONFIRMED. Transmission0 is never proof of a deletable draft.
    internal async Task<ToolOrderIntentReceiptV1> EnlistToolIntent(OperationsAccessV1 root, Guid operation,
        string kind, PlateMoldSourceRefV1 tool, ToolNativeOrderRefV1 expected, PlateMoldDto proposal, CancellationToken ct)
    {
        var tx=root.RequireToolOrderTransaction(db,true);
        var action=kind switch {"CHANGE_REQUEST"=>"edit","CANCEL_REQUEST"=>"cancel",_=>throw new OrderPriceCorrectionException("ORDER_INTENT_INVALID",400)};
        if(operation==Guid.Empty || tool.Id==Guid.Empty || expected.Deleted)throw new OrderPriceCorrectionException("ORDER_INTENT_INVALID",400);
        var initial=await AuthorityAsync(action,ct);
        if(initial.Source.SubjectId!=root.ActorId)throw new OrderPriceCorrectionException("REAUTH_REQUIRED",401);
        RequireFields(initial,action);await RequireObject(initial,expected.Number,false,ct);
        await LockGraph(expected.Number,ct);
        async Task Validate(CancellationToken token)
        {
            if(!ReferenceEquals(tx,root.RequireToolOrderTransaction(db,true)))throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
            var final=await AuthorityAsync(action,token);
            if(final.Source.SubjectId!=initial.Source.SubjectId || final.Source.SourcePermissionVersion!=initial.Source.SourcePermissionVersion
                || final.Source.RegistryVersion!=initial.Source.RegistryVersion || final.Scope!=initial.Scope
                || final.OrganizationVersion!=initial.OrganizationVersion || final.Source.TenantExpiresAt<=DateTime.Now)
                throw new OrderPriceCorrectionException("AUTHORITY_CHANGED",403);
            RequireFields(final,action);await RequireObject(final,expected.Number,false,token);
            var order=await db.Orders.AsNoTracking().SingleOrDefaultAsync(o=>o.Id==expected.OrderId,token);
            var line=await db.OrderDetails.AsNoTracking().SingleOrDefaultAsync(d=>d.Id==expected.LineId,token);
            if(order==null || line==null || order.IsDeleted || line.IsDeleted || order.WebOrderNo!=expected.Number
                || line.WebOrderNo!=expected.Number || line.WebOrderDetailNo!=expected.LineNumber || line.HaibaiNo1!=expected.ArrangeNumber
                || Convert.ToBase64String(order.RowVersion)!=expected.OrderRowVersion || Convert.ToBase64String(line.RowVersion)!=expected.LineRowVersion)
                throw new OrderPriceCorrectionException("ORIGINAL_ORDER_CHANGED",412);
        }
        await Validate(ct);root.AtCommit(Validate);
        var frozen=OperationsOwnerV1.Freeze(new {kind,tool,expected,proposal.OperationReason,proposal.CustomerCd,
            proposal.DecisionEstimateNo,proposal.VrsnName,proposal.DecisionAmount,proposal.DeliveryDate,proposal.EarningsCd});
        var bytes=MainOwnerOperationJournalV1.Encode(frozen);
        var lifecycle=await db.Orders.Where(o=>o.Id==expected.OrderId).Select(o=>o.OrderStatus).SingleAsync(ct);
        var result=new ToolOrderIntentReceiptV1(operation,kind,"REQUESTED",tool.Id,expected,MainOwnerOperationJournalV1.Digest(bytes),lifecycle);
        await new MainOwnerOperationJournalV1(db).Store("Main.Order.ToolCommercialIntent.v1","Main.Order.ToolCommercialIntent.commands.v1",
            operation,root.ActorId,bytes,result,[expected.OrderId,expected.LineId,tool.Id],new{action="accept-tool-commercial-request",actor=root.ActorId,result},ct);
        return result;
    }
}
