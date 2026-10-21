using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Mes;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

public partial class WorkOrderService
{
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true && _seq is MesSequenceService seq && seq.Owns(_db)?_owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<WorkOrderDto?> GetByNoAsync(string number)=>!_db.Database.IsNpgsql()?GetByNoCoreAsync(number):
        Owner.ExecuteAsync(OperationsOwnerV1.Mes,"query",false,async (a,ct)=>
        {await a.RequireWorkOrder(number,false,ct);return await GetByNoCoreAsync(number);});
    public Task<PagedResultDto<WorkOrderDto>> SearchAsync(WorkOrderSearchQuery q)
    {
        if(!_db.Database.IsNpgsql())return SearchCoreAsync(q);
        var frozen=OperationsOwnerV1.Freeze(q);
        return Owner.ExecuteAsync(OperationsOwnerV1.Mes,"query",false,(a,ct)=>SearchCoreAsync(frozen,a.WorkOrders()));
    }
    public Task<string> NextSequenceAsync()=>!_db.Database.IsNpgsql()?_seq.NextAsync(WorkOrderSeqKey):
        Owner.ExecuteAsync(OperationsOwnerV1.Mes,"add",true,(_,ct)=>_seq.NextAsync(WorkOrderSeqKey));
    public Task<string> CreateAsync(WorkOrderDto dto,string? userName)
    {
        if(!_db.Database.IsNpgsql())return CreateCoreAsync(dto,userName);
        var frozen=OperationsOwnerV1.Freeze(dto);ValidateNew(frozen);
        return Owner.ExecuteAsync(OperationsOwnerV1.Mes,"add",true,async (a,ct)=>
        {
            await a.RequireActiveAssociations(frozen.WebOrderNo,null,ct);
            var no=await CreateCoreAsync(frozen,a.Actor,true);
            a.AtCommit(t=>a.RequireWorkOrder(no,false,t));return no;
        });
    }
    public Task UpdateAsync(string number,WorkOrderDto dto,string? userName)
    {
        if(!_db.Database.IsNpgsql())return UpdateCoreAsync(number,dto,userName);
        var frozen=OperationsOwnerV1.Freeze(dto);ValidateNew(frozen);
        return Owner.ExecuteAsync(OperationsOwnerV1.Mes,"edit",true,async (a,ct)=>
        {
            await a.RequireWorkOrder(number,false,ct);await RequireUnconsumed(number,ct);
            await a.RequireActiveAssociations(frozen.WebOrderNo,null,ct);
            if(await _db.OutboundOrders.AnyAsync(o=>o.WorkOrderNo==number && !o.IsDeleted
                && o.Status!=CP6.Entity.DomainModels.Wms.OutboundOrderStatus.Cancelled,ct))
                throw new OrderPriceCorrectionException("WORK_ORDER_OUTBOUND_DEPENDENCY_ACTIVE",409);
            await UpdateOwnedGraph(number,frozen,a.Actor,ct);
            a.AtCommit(t=>a.RequireWorkOrder(number,false,t));return true;
        });
    }
    public Task DeleteAsync(string number,byte[]? version,string? userName)
    {
        if(!_db.Database.IsNpgsql())return DeleteCoreAsync(number,version,userName);
        var frozen=version?.ToArray();
        return Owner.ExecuteAsync(OperationsOwnerV1.Mes,"del",true,async (a,ct)=>
        {
            await a.RequireWorkOrder(number,false,ct);await RequireUnconsumed(number,ct);
            if(await _db.OutboundOrders.AnyAsync(o=>o.WorkOrderNo==number && !o.IsDeleted && o.Status!=CP6.Entity.DomainModels.Wms.OutboundOrderStatus.Cancelled,ct))
                throw new OrderPriceCorrectionException("WORK_ORDER_OUTBOUND_DEPENDENCY_ACTIVE",409);
            if(frozen!=null && !frozen.SequenceEqual(await _db.WorkOrders.AsNoTracking().Where(w=>w.WorkOrderNo==number).Select(w=>w.RowVersion).SingleAsync(ct)))
                throw new DbUpdateConcurrencyException("WORK_ORDER_VERSION_CHANGED");
            await DeleteCoreAsync(number,null,a.Actor);a.AtCommit(t=>a.RequireWorkOrder(number,true,t));return true;
        });
    }
    public async Task IssueAsync(string number,string? userName)
    {
        if(!_db.Database.IsNpgsql()){await IssueCoreAsync(number,userName);return;}
        var result=await Owner.ExecuteAsync(OperationsOwnerV1.Mes,"issue",true,async (a,ct)=>
        {
            await a.RequireWorkOrder(number,false,ct);
            var row=await _db.WorkOrders.AsNoTracking().SingleAsync(w=>w.WorkOrderNo==number,ct);
            await a.RequireActiveAssociations(row.WebOrderNo,null,ct);
            if(row.Status==WorkOrderStatus.Cancelled)throw new OrderPriceCorrectionException("SOURCE_NOT_ACTIVE",409);
            await IssueCoreAsync(number,a.Actor,true);a.AtCommit(t=>a.RequireWorkOrder(number,false,t));
            return (Changed:row.Status<2,Actor:a.Actor,Ticket:a.Delegate(number));
        });
        if(result.Changed)try
        {
            if(_wmsBridge is CP6.Core.Services.Wms.WmsBridgeHook bridge)await bridge.OnWorkOrderIssuedOwnedAsync(result.Ticket);
            else await _wmsBridge.OnWorkOrderIssuedAsync(number,result.Actor);
        }catch{/* Existing best-effort after confirmed commit. */}
    }
    public Task<bool> CancelAsync(string number,string reason,string? userName)
    {
        if(!_db.Database.IsNpgsql())return CancelCoreAsync(number,reason,userName);
        return Owner.ExecuteAsync(OperationsOwnerV1.Mes,"cancel",true,async (a,ct)=>
        {
            await a.RequireWorkOrder(number,false,ct);await RequireUnconsumed(number,ct);
            if(await _db.OutboundOrders.AnyAsync(o=>o.WorkOrderNo==number && !o.IsDeleted && o.Status!=CP6.Entity.DomainModels.Wms.OutboundOrderStatus.Cancelled,ct))
                throw new OrderPriceCorrectionException("WORK_ORDER_OUTBOUND_DEPENDENCY_ACTIVE",409);
            var result=await CancelCoreAsync(number,reason,a.Actor);a.AtCommit(t=>a.RequireWorkOrder(number,false,t));return result;
        });
    }
    internal Task<bool> CancelFromOrderAsync(string number,string reason,string actor)
    {
        if(!_db.Database.IsNpgsql() || _db.Database.CurrentTransaction==null)throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
        return CancelCoreAsync(number,reason,actor);
    }
    public Task<List<string>> ExpandFromOrderAsync(ExpandFromOrderRequest req,string? userName)
    {
        if(!_db.Database.IsNpgsql())return ExpandFromOrderCoreAsync(req,userName);
        var frozen=OperationsOwnerV1.Freeze(req);
        return Owner.ExecuteAsync(OperationsOwnerV1.Mes,"add",true,async (a,ct)=>
        {
            await a.RequireSourceOrder(frozen.WebOrderNo,false,ct);
            var numbers=await ExpandFromOrderCoreAsync(frozen,a.Actor,true);
            foreach(var no in numbers)a.AtCommit(t=>a.RequireWorkOrder(no,false,t));return numbers;
        });
    }
    internal Task<List<string>> ExpandDelegatedAsync(OperationsDelegationV1 ticket)=>
        Owner.ExecuteAsync(OperationsOwnerV1.Mes,"add",true,async(a,ct)=>
        {
            var numbers=await ExpandFromOrderCoreAsync(new(){WebOrderNo=ticket.Number},a.Actor,true);
            foreach(var number in numbers)a.AtCommit(t=>a.RequireGenerated(OperationsOwnerV1.Mes,number,ticket,t));
            return numbers;
        },delegation:ticket);
    private static void ValidateNew(WorkOrderDto dto)
    {
        if(dto.Status is not (0 or 1) || string.IsNullOrWhiteSpace(dto.ProductCd) || dto.ProductionQty<0
            || dto.Processes==null || dto.Materials==null || dto.Processes.Any(p=>p==null || p.ProcessStatus!=0)
            || dto.Materials.Any(m=>m==null || m.SupplyStatus!=0))throw new OrderPriceCorrectionException("WORK_ORDER_WRITE_INVALID",400);
    }
    private async Task RequireUnconsumed(string number,CancellationToken ct)
    {
        if(await _db.WorkOrders.AnyAsync(w=>w.WorkOrderNo==number && (w.CompletedQty!=0 || w.DefectQty!=0 || w.ActualStartDate!=null || w.ActualEndDate!=null),ct)
            || await _db.WorkOrderProcesses.AnyAsync(p=>p.WorkOrderNo==number && (p.GoodQty!=0 || p.DefectQty!=0 || p.ProcessStatus!=0 || p.ActualStartTime!=null || p.ActualEndTime!=null),ct)
            || await _db.WorkOrderMaterials.AnyAsync(m=>m.WorkOrderNo==number && (m.ActualQty!=0 || m.SupplyStatus!=0),ct))
            throw new OrderPriceCorrectionException("WORK_ORDER_ALREADY_PROCESSED",409);
    }
    private async Task UpdateOwnedGraph(string number,WorkOrderDto dto,string actor,CancellationToken ct)
    {
        var wo=await _db.WorkOrders.SingleAsync(w=>w.WorkOrderNo==number && !w.IsDeleted,ct);
        if(wo.Status is not (0 or 1))throw new InvalidOperationException("ME-MSG-042");
        wo.OrderNo1=dto.OrderNo1;wo.OrderNo2=dto.OrderNo2;wo.OrderNo3=dto.OrderNo3;wo.WebOrderNo=dto.WebOrderNo;
        wo.CustomerCd=dto.CustomerCd;wo.ProductCd=dto.ProductCd;wo.ProductName=dto.ProductName;wo.ProductionQty=dto.ProductionQty;
        wo.DeliveryDate=dto.DeliveryDate;wo.PlanStartDate=dto.PlanStartDate;wo.PlanEndDate=dto.PlanEndDate;wo.Priority=dto.Priority<=0?1:dto.Priority;
        wo.LotNo=dto.LotNo;wo.BaseCd=dto.BaseCd;wo.Remarks=dto.Remarks;wo.Status=dto.Status;wo.Modifier=actor;wo.ModifyDate=DateTime.Now;
        var oldProcs=await _db.WorkOrderProcesses.Where(p=>p.WorkOrderNo==number).ToArrayAsync(ct);
        var oldMats=await _db.WorkOrderMaterials.Where(m=>m.WorkOrderNo==number).ToArrayAsync(ct);
        var used=new HashSet<Guid>();
        foreach(var p in dto.Processes)
        {
            var row=await _db.WorkOrderProcesses.SingleOrDefaultAsync(x=>x.WorkOrderNo==number && x.ProcessCd==p.ProcessCd && x.TaskCd==p.TaskCd,ct);
            if(row==null){row=new(){WorkOrderNo=wo.WorkOrderNo,ProcessCd=p.ProcessCd,TaskCd=p.TaskCd,Creator=actor};_db.WorkOrderProcesses.Add(row);}
            if(!used.Add(row.Id))throw new OrderPriceCorrectionException("WORK_ORDER_DUPLICATE_PROCESS",400);
            row.ProcessName=p.ProcessName;row.SortOrder=p.SortOrder;row.MachineCd=p.MachineCd;row.WgCd=p.WgCd;
            row.PlanStartTime=p.PlanStartTime;row.PlanEndTime=p.PlanEndTime;row.PlanQty=p.PlanQty;row.StdLossRate=p.StdLossRate;
            row.LeadTime=p.LeadTime;row.PrevProcessCd=p.PrevProcessCd;row.Remarks=p.Remarks;row.Modifier=actor;row.ModifyDate=DateTime.Now;
        }
        _db.WorkOrderProcesses.RemoveRange(oldProcs.Where(p=>!used.Contains(p.Id)));
        used.Clear();
        foreach(var m in dto.Materials)
        {
            var row=await _db.WorkOrderMaterials.SingleOrDefaultAsync(x=>x.WorkOrderNo==number && x.ProcessCd==m.ProcessCd && x.MaterialCd==m.MaterialCd,ct);
            if(row==null){row=new(){WorkOrderNo=wo.WorkOrderNo,ProcessCd=m.ProcessCd,MaterialCd=m.MaterialCd,Creator=actor};_db.WorkOrderMaterials.Add(row);}
            if(!used.Add(row.Id))throw new OrderPriceCorrectionException("WORK_ORDER_DUPLICATE_MATERIAL",400);
            row.MaterialName=m.MaterialName;row.MaterialTypeDiv=m.MaterialTypeDiv;row.PlanQty=m.PlanQty;row.Unit=m.Unit;
            row.SortOrder=m.SortOrder;row.Remarks=m.Remarks;row.Modifier=actor;row.ModifyDate=DateTime.Now;
        }
        _db.WorkOrderMaterials.RemoveRange(oldMats.Where(m=>!used.Contains(m.Id)));await _db.SaveChangesAsync(ct);
    }
}
