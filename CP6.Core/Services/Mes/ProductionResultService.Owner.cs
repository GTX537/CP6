using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DTOs.Mes;
using CP6.Entity.DomainModels.Mes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Mes;

public partial class ProductionResultService
{
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true?_owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<PagedResultDto<ProductionResultDto>> SearchAsync(ProductionResultSearchQuery query)
    {
        if(!_db.Database.IsNpgsql())return SearchCoreAsync(query);
        var frozen=OperationsOwnerV1.Freeze(query);
        return Owner.ExecuteAsync(OperationsOwnerV1.Production,"query",false,(a,ct)=>SearchCoreAsync(frozen,a.ProductionResults()));
    }
    public Task<WorkOrderDto?> GetWorkOrderSummaryAsync(string no)=>!_db.Database.IsNpgsql()?_woService.GetByNoAsync(no):
        Owner.ExecuteAsync(OperationsOwnerV1.Production,"query",false,async(a,ct)=>
        {
            await a.RequireProductionWorkOrder(no,ct);
            if(_woService is not WorkOrderService service || !service.Owns(_db))throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
            return await service.ReadProductionSummaryAsync(no,a);
        });
    private async Task<string> WriteOwnedAsync(ProductionResultRequest request,string? user,int type)
    {
        if(!_db.Database.IsNpgsql())return await WriteCoreAsync(request,user,type);
        var frozen=OperationsOwnerV1.Freeze(request);
        if(frozen.GoodQty<0 || frozen.DefectQty<0 || frozen.ActualStartTime>frozen.ActualEndTime)
            throw new OrderPriceCorrectionException("PRODUCTION_WRITE_INVALID",400);
        var action=type switch{1=>"start",2 or 3=>"suspend",4=>"complete",5=>"report",_=>throw new ArgumentOutOfRangeException(nameof(type))};
        var effects=new List<Func<Task>>();
        var result=await Owner.ExecuteAsync(OperationsOwnerV1.Production,action,true,async(a,ct)=>
        {
            if(_seq is not MesSequenceService seq || !seq.Owns(_db))throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
            if(_backflush!=null && (_backflush is not BackflushService backflush || !backflush.Owns(_db)))throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
            await a.RequireProductionWorkOrder(frozen.WorkOrderNo,ct);
            var wo=await _db.WorkOrders.AsNoTracking().SingleAsync(w=>w.WorkOrderNo==frozen.WorkOrderNo,ct);
            // ME040 permits supplemental quantity reports after completion; those do not replay completion hooks.
            if(wo.Status is not (WorkOrderStatus.Issued or WorkOrderStatus.InProgress or WorkOrderStatus.Interrupted)
                && !(type==5 && wo.Status is WorkOrderStatus.Completed or WorkOrderStatus.Inspected))
                throw new OrderPriceCorrectionException("PRODUCTION_STATE_INVALID",409);
            await a.RequireActiveAssociations(wo.WebOrderNo,null,ct);
            var number=await WriteCoreAsync(frozen,a.Actor,type,effects,a);
            a.AtCommit(t=>a.RequireProductionWorkOrder(frozen.WorkOrderNo,t));return number;
        });
        foreach(var effect in effects)try{await effect();}catch{/* The production result is confirmed; never return a retryable write failure. */}
        return result;
    }
    public Task RecalculateProcessHoursAsync(string no,string process,string? task,string? user)=>!_db.Database.IsNpgsql()
        ?RecalculateProcessHoursCoreAsync(no,process,task,user)
        :Owner.ExecuteAsync(OperationsOwnerV1.Production,"recalculate",true,async(a,ct)=>
        {
            await a.RequireProductionWorkOrder(no,ct);var row=await _db.WorkOrders.AsNoTracking().SingleAsync(w=>w.WorkOrderNo==no,ct);
            await a.RequireActiveAssociations(row.WebOrderNo,no,ct);await RecalculateProcessHoursCoreAsync(no,process,task,a.Actor);
            a.AtCommit(t=>a.RequireProductionWorkOrder(no,t));return true;
        });
    private async Task PublishOwnedResult(ProductionResultRequest req,int type,int status,OperationsDelegationV1? ticket)
    {
        try{await _notifier.NotifyProductionReportedAsync(req.WorkOrderNo,req.ProcessCd,req.GoodQty,req.DefectQty);}catch{}
        if(type is 1 or 2 or 3 or 4)try{await _notifier.NotifyWorkOrderStatusChangedAsync(req.WorkOrderNo,status);}catch{}
        if(ticket==null)return;
        if(_wmsBridge is WmsBridgeHook bridge)try{await bridge.OnProductionCompletedOwnedAsync(ticket);}catch{}
        var backflushOk=true;
        if(_backflush is BackflushService backflush)
        {
            try{await backflush.BackflushCompletedAsync(ticket);}
            catch(Exception ex){backflushOk=false;_logger?.LogWarning(ex,"Completion backflush unavailable for {WorkOrder}; cost collection skipped",req.WorkOrderNo);}
        }
        if(backflushOk && _finBridge!=null)try{await _finBridge.OnWorkOrderCompletedAsync(req.WorkOrderNo,ticket.Actor);}catch{}
    }
}
