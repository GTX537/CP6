using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    internal IQueryable<SampleStock> SampleRecords()
    {
        var rows=db.SampleStocks.AsNoTracking().Where(x=>!x.IsDeleted);
        if(authority.Scope==1)rows=rows.Where(x=>x.Creator==Actor);
        else if(authority.Scope!=5)
        {var ids=StandaloneDepartmentScopeV1.Objects(db,authority,"sample-stock");rows=rows.Where(x=>ids.Contains(x.Id));}
        if(!authority.Actor.RoleIds.Contains(1))
        {
            var grants=db.WmsRoleScopes.Where(g=>authority.Actor.RoleIds.Contains(g.RoleId));
            // Missing location is unknown, not an invented stock/location binding. Only an all-area grant
            // can cover it; a missing warehouse additionally requires a wildcard warehouse grant.
            rows=rows.Where(x=>grants.Any(g=>(g.WarehouseCd=="*"||x.WarehouseCd!=null&&x.WarehouseCd!=""&&g.WarehouseCd==x.WarehouseCd)
                &&(g.AreaCd==null||g.AreaCd==""||g.AreaCd=="*"||x.WarehouseCd!=null&&x.WarehouseCd!=""&&x.LocationCd!=null&&x.LocationCd!=""
                    &&db.Warehouses.Any(w=>!w.IsDeleted&&w.WarehouseCd==x.WarehouseCd)
                    &&db.Locations.Any(l=>!l.IsDeleted&&l.WarehouseCd==x.WarehouseCd&&l.LocationCd==x.LocationCd&&l.AreaCd==g.AreaCd))));
        }
        return rows;
    }
    internal void RequireSampleFieldRead(string field)
    {if(!authority.Fields.Fields[field].CanRead)throw new OrderPriceCorrectionException("FIELD_READ_FORBIDDEN",403);}
    internal async Task<SampleStock> RequireSample(Guid id,CancellationToken ct)
        =>await SampleRecords().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new OrderPriceCorrectionException("SAMPLE_NOT_FOUND",404);
    internal SampleObservationV1 ProjectSample(SampleStock x)
    {
        bool Read(string key)=>authority.Fields.Fields[key].CanRead;
        return new(x.Id,x.SampleNo,x.SampleType,InventoryReportValuesV1.Exact(x.Quantity).ToString(),x.UnitCd,x.Status,
            x.WarehouseCd,x.LocationCd,x.RegisteredAt,x.LentAt,x.ExpectedReturnDate,x.ReturnedAt,
            Read("CustomerCd")?x.CustomerCd:null,Read("CustomerName")?x.CustomerName:null,Read("ProductCd")?x.ProductCd:null,
            Read("ProductName")?x.ProductName:null,Read("LentTo")?x.LentTo:null,Read("Remarks")?x.Remarks:null,
            Read("CustomerCd"),Read("CustomerName"),Read("ProductCd"),Read("ProductName"),Read("LentTo"),Read("Remarks"),
            x.ReturnedAt.HasValue?"RETURN_RECORDED_UNVERIFIED":x.LentAt.HasValue||x.Status==SampleStatus.LentOut?"OUTSTANDING_UNKNOWN":"NO_LOAN_RECORDED",MasterEtag(x));
    }
}
