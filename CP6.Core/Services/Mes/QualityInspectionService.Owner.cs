using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Wms;
using CP6.Entity.DTOs.Mes;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Mes;

public partial class QualityInspectionService
{
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true && _seq is MesSequenceService seq && seq.Owns(_db)
        ?_owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<QualityInspectionDto?> GetByNoAsync(string number)=>!_db.Database.IsNpgsql()?GetByNoCoreAsync(number):
        Owner.ExecuteAsync(OperationsOwnerV1.Quality,"query",false,async(a,ct)=>
            await GetByNoCoreAsync(number,a.QualityInspections(await a.SourceAsync(OperationsOwnerV1.Mes,ct))));
    public Task<PagedResultDto<QualityInspectionDto>> SearchAsync(QualityInspectionSearchQuery query)
    {
        if(!_db.Database.IsNpgsql())return SearchCoreAsync(query);
        var frozen=OperationsOwnerV1.Freeze(query);
        return Owner.ExecuteAsync(OperationsOwnerV1.Quality,"query",false,async(a,ct)=>
            await SearchCoreAsync(frozen,a.QualityInspections(await a.SourceAsync(OperationsOwnerV1.Mes,ct))));
    }
    public Task<List<InspectionTemplateDto>> GetTemplateAsync(string code)=>!_db.Database.IsNpgsql()?GetTemplateCoreAsync(code):
        Owner.ExecuteAsync(OperationsOwnerV1.Quality,"query",false,(a,ct)=>GetTemplateCoreAsync(code));
    public Task<List<string>> GetAllTemplateCodesAsync()=>!_db.Database.IsNpgsql()?GetAllTemplateCodesCoreAsync():
        Owner.ExecuteAsync(OperationsOwnerV1.Quality,"query",false,(a,ct)=>GetAllTemplateCodesCoreAsync());
    public Task<string> CreateAsync(QualityInspectionDto dto,string? userName)
    {
        if(!_db.Database.IsNpgsql())return CreateCoreAsync(dto,userName);
        var frozen=OperationsOwnerV1.Freeze(dto);
        return Owner.ExecuteAsync(OperationsOwnerV1.Quality,"add",true,async(a,ct)=>
        {
            var source=await a.SourceAsync(OperationsOwnerV1.Mes,ct);
            await a.RequireDefectLinks(frozen.WorkOrderNo,frozen.ProcessCd,null,source,ct);
            await a.RequireActiveAssociations(null,frozen.WorkOrderNo,ct);
            var no=await CreateCoreAsync(frozen,a.Actor,owned:true);
            if(frozen.OverallResult==2)
            {
                if(_stockQc is not StockQcService stock || !stock.Owns(_db))throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
                await stock.MarkFailedFromQualityAsync(a,no,ct);
            }
            a.AtCommit(t=>a.RequireQualityInspection(no,source,t));return no;
        });
    }
    public Task UpdateAsync(string number,QualityInspectionDto dto,string? userName)
    {
        if(!_db.Database.IsNpgsql())return UpdateCoreAsync(number,dto,userName);
        var frozen=OperationsOwnerV1.Freeze(dto);
        return Owner.ExecuteAsync(OperationsOwnerV1.Quality,"edit",true,async(a,ct)=>
        {
            var source=await a.SourceAsync(OperationsOwnerV1.Mes,ct);await a.RequireQualityInspection(number,source,ct);
            var old=await _db.QualityInspections.AsNoTracking().SingleAsync(q=>q.InspectionNo==number,ct);
            await a.RequireDefectLinks(frozen.WorkOrderNo,frozen.ProcessCd,null,source,ct);
            await a.RequireActiveAssociations(null,frozen.WorkOrderNo,ct);
            await UpdateCoreAsync(number,frozen,a.Actor,owned:true);
            a.AtCommit(t=>a.RequireDefectLinks(old.WorkOrderNo,old.ProcessCd,null,source,t));
            a.AtCommit(t=>a.RequireQualityInspection(number,source,t));return true;
        });
    }
}
