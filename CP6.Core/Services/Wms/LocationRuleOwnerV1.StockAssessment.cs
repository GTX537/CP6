using System.Globalization;
using System.Text.Json.Serialization;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.LocationRuleValuesV1;
namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LocationStockRequestV1(EngineeringSourceRefV1 Proposal);
public sealed record LocationStockFactV1(Guid StockId,string ProductCode,string LotNumber,string? UnitCode,
    string PhysicalQuantity,string AllocatedQuantity,string AvailableQuantity,string? RowVersion);
public sealed record LocationStockAssessmentV1(Guid TenantId,Guid ActorId,Guid LocationId,Guid WarehouseId,
    EngineeringSourceRefV1 Proposal,string Etag,DateTimeOffset ObservedAt,string StockCoverage,
    bool Truncated,LocationStockFactV1[] Facts,string[] MissingEvidence)
{
    public string SourceOwner=>"Main.Wms.NativeStockObservation.v1";
    public string Qualification=>"CURRENT_NATIVE_STOCK_ONLY";
    public string OccupancyCoverage=>"UNKNOWN";
    public string? CapacityQuantity=>null;
    public bool AdmissionGrant=>false;
    public bool RequiresFinalEffectRecheck=>true;
}

public sealed partial class LocationRuleOwnerV1
{
    public Task<LocationStockAssessmentV1> StockAssessmentAsync(Guid location,Guid id,string? etag,
        LocationStockRequestV1 input,CancellationToken ct=default)
    {
        if(location==Guid.Empty||id==Guid.Empty||input?.Proposal==null||input.Proposal.Id!=id
            ||input.Proposal.Owner!=Owner)throw Error("STOCK_ASSESSMENT_REQUEST_INVALID");
        if(string.IsNullOrEmpty(etag))throw Error("RULE_ETAG_REQUIRED",428);
        var expected=OperationsOwnerV1.Freeze(input.Proposal);
        return Authority.ExecuteAsync(OperationsOwnerV1.LocationRule,"query",false,async(a,t)=>
        {
            await Target(a,location,t);
            if(a.LocationRuleFields().Any(f=>!f.CanRead))throw Error("FIELD_READ_FORBIDDEN",403);
            var native=await a.RequireLocationRuleTarget(location,t);
            var warehouse=await db.Warehouses.AsNoTracking().Where(x=>x.WarehouseCd==native.WarehouseCd)
                .Select(x=>x.Id).SingleAsync(t);
            var wa=await a.SourceAsync(OperationsOwnerV1.Warehouse,t);await wa.RequireMaster("warehouses",warehouse,t);
            var rules=new LocationRuleStoreV1(db);await rules.Fence(false,t);
            var head=await rules.HeadFor(location,id,t);var candidate=await rules.Read(head,null,t);
            if(head.Etag!=etag||candidate.Reference!=expected)throw Error("RULE_PREDECESSOR_CHANGED",412);
            var stock=await a.SourceAsync(OperationsOwnerV1.Stock,t);
            var whole=await stock.HasWholeStockLocationScope(native,t);
            var rows=await stock.InventoryStocks().Where(x=>x.WarehouseCd==native.WarehouseCd&&x.LocationCd==native.LocationCd)
                .OrderBy(x=>x.Id).Select(x=>new{x.Id,x.ProductCd,x.LotNo,x.UnitCd,x.PhysicalQty,x.AllocatedQty,x.AvailableQty,x.RowVersion})
                .Take(101).ToArrayAsync(t);
            var truncated=rows.Length>100;
            // Archived balances are outside the active Stock read contract. Diagnose them
            // only with whole-location authority, without exposing amounts or identities.
            var archived=whole&&await db.Stocks.IgnoreQueryFilters().AsNoTracking().AnyAsync(x=>x.TenantId==db.CurrentTenantId
                &&x.WarehouseCd==native.WarehouseCd&&x.LocationCd==native.LocationCd&&x.IsDeleted
                &&(x.PhysicalQty!=0||x.AllocatedQty!=0||x.AvailableQty!=0),t);
            var coverage=truncated?"TRUNCATED":!whole?"VISIBLE_ROWS_ONLY":archived?"ARCHIVED_BALANCE_UNRESOLVED":"COMPLETE_CURRENT_ROWS";
            static string Qty(decimal x)=>x.ToString("0.00000000",CultureInfo.InvariantCulture);
            var facts=rows.Take(100).Select(x=>new LocationStockFactV1(x.Id,x.ProductCd,x.LotNo,x.UnitCd,
                Qty(x.PhysicalQty),Qty(x.AllocatedQty),Qty(x.AvailableQty),x.RowVersion==null?null:Convert.ToBase64String(x.RowVersion))).ToArray();
            var missing=new List<string>{"NON_STOCK_OBLIGATION_COVERAGE_UNPROVEN","EFFECT_OWNER_PROTECTION_UNPROVEN"};
            if(coverage!="COMPLETE_CURRENT_ROWS")missing.Add(coverage);
            if(facts.Length>0)missing.Add("STOCK_CAPACITY_MEASUREMENT_UNPROVEN");
            if(rows.Any(x=>x.PhysicalQty<0||x.AllocatedQty<0||x.AllocatedQty>x.PhysicalQty||x.AvailableQty!=x.PhysicalQty-x.AllocatedQty))
                missing.Add("NATIVE_STOCK_BALANCE_INCONSISTENT");
            var observed=await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(t);
            var result=new LocationStockAssessmentV1(db.CurrentTenantId,a.ActorId,location,warehouse,expected,etag,observed,
                coverage,truncated,facts,missing.ToArray());
            a.AtCommit(async final=>
            {
                await wa.RequireMaster("warehouses",warehouse,final);
                await rules.Fence(false,final);
                if((await rules.HeadFor(location,id,final)).Etag!=etag)throw Error("RULE_PREDECESSOR_CHANGED",412);
                // Native tables remain under the OperationsOwner transaction's SHARE locks.
                // Source authorities, including Stock, are revalidated by its final fence.
            });
            return result;
        },ct);
    }
}
