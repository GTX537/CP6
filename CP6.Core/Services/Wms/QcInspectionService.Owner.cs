using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class QcInspectionService
{
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true && _seq is WmsSequenceService seq && seq.Owns(_db)
        ?_owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<List<QcInspection>> SearchAsync(QcInspectionSearchQuery query)
    {
        if(!_db.Database.IsNpgsql())return SearchCoreAsync(query);
        var frozen=OperationsOwnerV1.Freeze(query);
        return Owner.ExecuteAsync(OperationsOwnerV1.InboundQc,"query",false,async(a,ct)=>
            await SearchCoreAsync(frozen,a.InboundInspections(await a.OptionalInboundSource(ct))));
    }
    public Task<QcInspectionDto?> GetAsync(string number)=>!_db.Database.IsNpgsql()?GetCoreAsync(number):
        Owner.ExecuteAsync(OperationsOwnerV1.InboundQc,"query",false,async(a,ct)=>
            await GetCoreAsync(number,a.InboundInspections(await a.OptionalInboundSource(ct))));
    public Task<string> CreateFromInboundAsync(string number,string? userName)=>!_db.Database.IsNpgsql()?CreateFromInboundCoreAsync(number,userName):
        Owner.ExecuteAsync(OperationsOwnerV1.InboundQc,"add",true,async(a,ct)=>
        {
            var source=await a.SourceAsync(OperationsOwnerV1.Inbound,ct);
            await a.RequireInbound(number,false,ct);await source.RequireInbound(number,false,ct);
            var no=await CreateFromInboundCoreAsync(number,a.Actor);
            a.AtCommit(t=>a.RequireInboundInspection(no,source,t));return no;
        });
    public Task<string> CreateDirectAsync(QcInspectionDto dto,string? userName)
    {
        if(!_db.Database.IsNpgsql())return CreateDirectCoreAsync(dto,userName);
        var frozen=OperationsOwnerV1.Freeze(dto);
        return Owner.ExecuteAsync(OperationsOwnerV1.InboundQc,"add",true,async(a,ct)=>
        {
            var no=await CreateDirectCoreAsync(frozen,a.Actor);
            a.AtCommit(t=>a.RequireInboundInspection(no,null,t));return no;
        });
    }
    public Task SaveItemsAsync(string number,List<QcInspectionItemDto> items,string? userName)
    {
        if(!_db.Database.IsNpgsql())return SaveItemsCoreAsync(number,items,userName);
        var frozen=OperationsOwnerV1.Freeze(items);
        return Owner.ExecuteAsync(OperationsOwnerV1.InboundQc,"edit",true,async(a,ct)=>
        {
            var source=await a.OptionalInboundSource(ct);await a.RequireInboundInspection(number,source,ct);
            await SaveItemsCoreAsync(number,frozen,a.Actor);a.AtCommit(t=>a.RequireInboundInspection(number,source,t));return true;
        });
    }
    public async Task<JudgeResult> JudgeAsync(string number,JudgeRequest request,string? userName)
    {
        if(!_db.Database.IsNpgsql())return await JudgeCoreAsync(number,request,userName);
        var frozen=OperationsOwnerV1.Freeze(request);var effects=new List<StockTransaction>();
        InboundService? receiver=null;string? warehouse=null;
        var result=await Owner.ExecuteAsync(OperationsOwnerV1.InboundQc,"judge",true,async(a,ct)=>
        {
            var source=await a.OptionalInboundSource(ct);await a.RequireInboundInspection(number,source,ct);
            var judged=await JudgeCoreAsync(number,frozen,a.Actor,async dto=>
            {
                receiver=_inbound as InboundService;
                if(receiver==null)throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
                warehouse=dto.WarehouseCd;
                return await receiver.ReceiveInspectionWithinOwnerAsync(a,number,dto,effects,ct);
            });
            a.AtCommit(t=>a.RequireInboundInspection(number,source,t));return judged;
        });
        if(result.GeneratedReceiptNo!=null && receiver!=null)
            await receiver.PublishInspectionReceiptAsync(result.GeneratedReceiptNo,warehouse!,effects);
        return result;
    }
    public Task CancelAsync(string number,string? userName)=>!_db.Database.IsNpgsql()?CancelCoreAsync(number,userName):
        Owner.ExecuteAsync(OperationsOwnerV1.InboundQc,"cancel",true,async(a,ct)=>
        {
            var source=await a.OptionalInboundSource(ct);await a.RequireInboundInspection(number,source,ct);
            await CancelCoreAsync(number,a.Actor);a.AtCommit(t=>a.RequireInboundInspection(number,source,t));return true;
        });
}
