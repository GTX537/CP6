using CP6.Core.Services.Erp;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;
namespace CP6.Core.Services.Integration;
internal sealed partial class OperationsAccessV1
{
    private async Task<OperationsAccessV1?> LinkedSource(string resource,CancellationToken ct)
    {
        try{return await SourceAsync(resource,ct);}
        catch(OrderPriceCorrectionException e)when(e.Status==403&&e.Message=="ACTION_FORBIDDEN"){return null;}
    }
    internal async Task<IQueryable<PlateMoldStock>> ScopedToolInstances(CancellationToken ct)
    {
        var rows=ToolInstances();var links=ToolRegistrationLinkRowV1.Query(db);var visible=links;
        if(await links.AnyAsync(x=>x.CustomerId!=null,ct))
        {
            var source=await LinkedSource(OperationsOwnerV1.BusinessPartner,ct);
            if(source==null)visible=visible.Where(x=>x.CustomerId==null);
            else{var allowed=source.BusinessPartners(includeDeleted:true);visible=visible.Where(x=>x.CustomerId==null||allowed.Any(p=>p.Id==x.CustomerId));}
        }
        if(await links.AnyAsync(x=>x.ProductId!=null,ct))
        {
            var source=await LinkedSource(OperationsOwnerV1.Product,ct);
            if(source==null)visible=visible.Where(x=>x.ProductId==null);
            else{var allowed=source.Products(includeDeleted:true);visible=visible.Where(x=>x.ProductId==null||allowed.Any(p=>p.Id==x.ProductId));}
        }
        if(await links.AnyAsync(x=>x.LocationId!=null,ct))
        {
            var source=await LinkedSource(OperationsOwnerV1.Location,ct);var warehouse=await LinkedSource(OperationsOwnerV1.Warehouse,ct);
            if(source==null||warehouse==null)visible=visible.Where(x=>x.LocationId==null);
            else{var allowed=await source.MasterLocations(ct);var ws=warehouse.MasterWarehouses();
                visible=visible.Where(x=>x.LocationId==null||allowed.Any(l=>l.Id==x.LocationId)&&ws.Any(w=>w.Id==x.WarehouseId));}
        }
        // A legacy native writer must not silently change a linked UUID's stored code association.
        // Apply before counts/paging. Exact historic refs remain valid observations after source renames.
        return rows.Where(x=>!links.Any(l=>l.ObjectId==x.Id)||visible.Any(l=>l.ObjectId==x.Id
            &&(l.CustomerId==null||l.CustomerCd==x.CustomerCd)&&(l.ProductId==null||l.ProductCd==x.ProductCd)
            &&(l.LocationId==null||l.WarehouseCd==x.WarehouseCd&&l.LocationCd==x.LocationCd)));
    }
    internal async Task<bool> LinkedCustomerNameReadable(Guid id,CancellationToken ct)
    {
        if(!await ToolRegistrationLinkRowV1.Query(db).AnyAsync(x=>x.ObjectId==id&&x.CustomerId!=null,ct))return true;
        return (await SourceAsync(OperationsOwnerV1.BusinessPartner,ct)).BusinessPartnerNameReadable;
    }
}
