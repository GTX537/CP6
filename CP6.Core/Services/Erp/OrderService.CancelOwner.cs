using CP6.Core.Services.Integration;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public partial class OrderService
{
    private async Task<OrderCancelResult> CancelOwnedAsync(string number,string reason,bool force)
    {
        if(string.IsNullOrWhiteSpace(number) || string.IsNullOrWhiteSpace(reason))
            throw new OrderPriceCorrectionException("ORDER_CANCEL_INVALID",400);
        // Strings/bool are immutable request snapshots. Never accept the caller's userName as authority.
        OrderCancelResult? result=null;
        OrderCancelBridgeHook.OwnedCascade? cascade=null;
        await WriteOwner.ExecuteCancellationAsync(number,async (actor,inheritance)=>
        {
            if(_cancelBridge is OrderCancelBridgeHook bridge)
                cascade=await bridge.PrepareOwnedAsync(_db,number,actor,inheritance);
            else if(_cancelBridge is not NoOpOrderCancelBridgeHook)
                throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
            result=await CancelCoreAsync(number,reason,force,actor,cascade==null?null:cascade.RunAsync);
            return number;
        });
        if(cascade!=null) await cascade.PublishCommittedAsync();
        return result!;
    }
}
