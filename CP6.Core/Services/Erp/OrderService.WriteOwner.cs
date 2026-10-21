using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DTOs;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Erp;

public partial class OrderService
{
    private OrderWriteOwnerV1 WriteOwner => _writeOwner?.Owns(_db)==true ? _writeOwner
        : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);

    private async Task<string> CreateOwnedAsync(OrderDto dto)
    {
        var result=await WriteOwner.ExecuteAsync("add",null,dto,async (frozen,actor) =>
        {
            foreach(var detail in frozen!.Details)
            {
                detail.ApprovalStatus=0; detail.WfApprovalFlg=false; detail.McTransferFlg=false;
                detail.McOrderNo=null; detail.McOrderDetailNo=null; detail.Status=0;
            }
            return await CreateCoreAsync(frozen,actor,null,deferBridges:true);
        });
        // Preserve the existing best-effort create hooks only after a confirmed authorized commit.
        // A hook failure cannot turn a known committed order into a retryable create failure.
        try { if(_wmsBridge is CP6.Core.Services.Wms.WmsBridgeHook wms)
                await wms.OnOrderCreatedOwnedAsync(result.Delegation!);
            else await _wmsBridge.OnOrderCreatedAsync(result.Number,result.UserName); }
        catch(Exception error) { _logger.LogError(error,"WMS create hook failed after committed order {Number}",result.Number); }
        try { if(_mesBridge is CP6.Core.Services.Mes.MesBridgeHook mes)
                await mes.OnOrderCreatedOwnedAsync(result.Delegation!);
            else await _mesBridge.OnOrderCreatedAsync(result.Number,result.UserName); }
        catch(Exception error) { _logger.LogError(error,"MES create hook failed after committed order {Number}",result.Number); }
        return result.Number;
    }
    private async Task UpdateOwnedAsync(string number,OrderDto dto)
    {
        await WriteOwner.ExecuteAsync("edit",number,dto,async (frozen,actor) =>
        { await UpdateCoreAsync(number,frozen!,actor,preserveOwners:true); return number; });
    }
    private async Task DeleteOwnedAsync(string number,byte[]? version)
    {
        var frozen=version?.ToArray();
        await WriteOwner.ExecuteAsync("del",number,null,async (_,actor) =>
        { await DeleteCoreAsync(number,frozen,actor); return number; });
    }
    private static void PreserveOrderDetailAuthority(OrderDetail row,OrderDetail? prior)
    {
        if(prior==null)
        {
            row.Status=0; row.McOrderNo=null; row.McOrderDetailNo=null;
            row.McTransferFlg=false; row.WfApprovalFlg=false; row.ApprovalStatus=0;
            return;
        }
        // Full replacement retains the identity of each stored business line, never the request's creator/status.
        row.Id=prior.Id; row.Creator=prior.Creator; row.CreateDate=prior.CreateDate; row.IsDeleted=prior.IsDeleted;
        row.McOrderNo=prior.McOrderNo; row.McOrderDetailNo=prior.McOrderDetailNo; row.Status=prior.Status;
        row.McTransferFlg=prior.McTransferFlg; row.WfApprovalFlg=prior.WfApprovalFlg; row.ApprovalStatus=prior.ApprovalStatus;
        row.ShippedQty=prior.ShippedQty; row.ShipStatus=prior.ShipStatus; row.LastShipDate=prior.LastShipDate;
        row.LastOutboundNo=prior.LastOutboundNo; row.BackorderQty=prior.BackorderQty;
    }
}
