using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

public partial class DefectRecordService
{
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true?_owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<DefectRecordDto?> GetByNoAsync(string no)=>!_db.Database.IsNpgsql()?GetByNoCoreAsync(no):
        Owner.ExecuteAsync(OperationsOwnerV1.Defect,"query",false,async(a,ct)=>
            await GetByNoCoreAsync(no,a.Defects(await a.SourceAsync(OperationsOwnerV1.Mes,ct))));
    public Task<PagedResultDto<DefectRecordDto>> SearchAsync(DefectRecordSearchQuery query)
    {
        if(!_db.Database.IsNpgsql())return SearchCoreAsync(query);
        var frozen=OperationsOwnerV1.Freeze(query);
        return Owner.ExecuteAsync(OperationsOwnerV1.Defect,"query",false,async(a,ct)=>
            await SearchCoreAsync(frozen,a.Defects(await a.SourceAsync(OperationsOwnerV1.Mes,ct))));
    }
    public Task<List<DefectCategoryDto>> GetAllCategoriesAsync()=>!_db.Database.IsNpgsql()?GetAllCategoriesCoreAsync():
        Owner.ExecuteAsync(OperationsOwnerV1.Defect,"query",false,(a,ct)=>GetAllCategoriesCoreAsync());
    public async Task<string> CreateAsync(DefectRecordDto dto,string? userName)
    {
        if(!_db.Database.IsNpgsql())return await CreateCoreAsync(dto,userName);
        var frozen=OperationsOwnerV1.Freeze(dto);
        var no=await Owner.ExecuteAsync(OperationsOwnerV1.Defect,"add",true,async(a,ct)=>
        {
            if(_seq is not MesSequenceService sequence || !sequence.Owns(_db))throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
            var source=await a.SourceAsync(OperationsOwnerV1.Mes,ct);
            await a.RequireDefectLinks(frozen.WorkOrderNo,frozen.ProcessCd,frozen.InspectionNo,source,ct);
            var result=await CreateCoreAsync(frozen,a.Actor,notify:false);
            a.AtCommit(t=>a.RequireDefect(result,source,t));return result;
        });
        // A lost commit acknowledgement must never publish an apparently confirmed result or invite retry.
        try{await _notifier.NotifyDefectIssuedAsync(no,frozen.WorkOrderNo,frozen.CategoryCd);}catch{}
        return no;
    }
    public async Task UpdateAsync(string no,DefectRecordDto dto,string? userName)
    {
        if(!_db.Database.IsNpgsql()){await UpdateCoreAsync(no,dto,userName);return;}
        var frozen=OperationsOwnerV1.Freeze(dto);
        await Owner.ExecuteAsync(OperationsOwnerV1.Defect,"edit",true,async(a,ct)=>
        {
            var source=await a.SourceAsync(OperationsOwnerV1.Mes,ct);await a.RequireDefect(no,source,ct);
            var old=await _db.DefectRecords.AsNoTracking().SingleAsync(d=>d.DefectNo==no,ct);
            await a.RequireDefectLinks(frozen.WorkOrderNo,frozen.ProcessCd,frozen.InspectionNo,source,ct);
            await UpdateCoreAsync(no,frozen,a.Actor);
            a.AtCommit(t=>a.RequireDefectLinks(old.WorkOrderNo,old.ProcessCd,old.InspectionNo,source,t));
            a.AtCommit(t=>a.RequireDefect(no,source,t));return true;
        });
    }
    public async Task DeleteAsync(string no,string? userName)
    {
        if(!_db.Database.IsNpgsql()){await DeleteCoreAsync(no,userName);return;}
        await Owner.ExecuteAsync(OperationsOwnerV1.Defect,"del",true,async(a,ct)=>
        {
            var source=await a.SourceAsync(OperationsOwnerV1.Mes,ct);await a.RequireDefect(no,source,ct);
            await DeleteCoreAsync(no,a.Actor);a.AtCommit(t=>a.RequireDefect(no,source,t,includeDeleted:true));return true;
        });
    }
}
