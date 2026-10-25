using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class StockTakeService
{
    private OperationsOwnerV1 Owner=>_owner?.Owns(_db)==true && _seq is WmsSequenceService seq && seq.Owns(_db)
        && _stock is StockMovementService stock && stock.Owns(_db)?_owner:throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    public Task<List<StockTake>> SearchAsync(StockTakeSearchQuery q)
    {
        if(!_db.Database.IsNpgsql())return SearchCoreAsync(q);
        var frozen=OperationsOwnerV1.Freeze(q);
        return Owner.ExecuteAsync(OperationsOwnerV1.StockTake,"query",false,async(a,ct)=>
        {
            var rows=await SearchCoreAsync(frozen,a.StockTakes());
            if(!a.AmountReadable)foreach(var row in rows)row.ApprovalThresholdAmount=null;
            return rows;
        });
    }
    public Task<StockTakeDto?> GetAsync(string no)=>!_db.Database.IsNpgsql()?GetCoreAsync(no):
        Owner.ExecuteAsync(OperationsOwnerV1.StockTake,"query",false,async(a,ct)=>
        {
            await a.RequireStockTake(no,ct);var dto=await GetCoreAsync(no);
            if(dto!=null)
            {
                if(!a.AmountReadable)dto.ApprovalThresholdAmount=null;
                foreach(var row in dto.Details)
                {
                    if(!a.PriceReadable)row.UnitPrice=null;
                    if(!a.AmountReadable || !a.PriceReadable)row.DiffAmount=null;
                }
            }
            return dto;
        });
    public Task<string> CreatePlanAsync(StockTakePlanDto dto,string? userName)
    {
        if(!_db.Database.IsNpgsql())return CreatePlanCoreAsync(dto,userName);
        var frozen=OperationsOwnerV1.Freeze(dto);
        return Owner.ExecuteAsync(OperationsOwnerV1.StockTake,"add",true,async(a,ct)=>
        {
            a.RequirePriceWrite();a.RequireAmountWrite();var source=await a.SourceAsync(OperationsOwnerV1.Stock,ct);source.RequirePriceRead();
            var snapshot=_db.Stocks.AsNoTracking().Where(s=>!s.IsDeleted && s.WarehouseCd==frozen.TargetWarehouseCd);
            if(!string.IsNullOrWhiteSpace(frozen.TargetLocationPrefix))snapshot=snapshot.Where(s=>s.LocationCd.StartsWith(frozen.TargetLocationPrefix));
            if(!string.IsNullOrWhiteSpace(frozen.TargetProductCd))snapshot=snapshot.Where(s=>s.ProductCd==frozen.TargetProductCd);
            var allowed=source.InventoryStocks(a.InventoryStocks()).Select(s=>s.Id);
            if(await snapshot.AnyAsync(s=>!allowed.Contains(s.Id),ct))throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
            var no=await CreatePlanCoreAsync(frozen,a.Actor);a.AtCommit(t=>a.RequireStockTake(no,t));return no;
        });
    }
    public Task StartCountAsync(string no,string? userName)=>!_db.Database.IsNpgsql()?StartCountCoreAsync(no,userName):
        Owner.ExecuteAsync(OperationsOwnerV1.StockTake,"count",true,async(a,ct)=>
        {
            await a.RequireStockTake(no,ct);a.RequirePriceWrite();
            var source=await a.SourceAsync(OperationsOwnerV1.Stock,ct);source.RequirePriceRead();
            var header=await GetHeaderAsync(no);
            if(header.Status!=StockTakeStatus.Planned)throw new OrderPriceCorrectionException("STOCKTAKE_STATE_INVALID",409);
            // Freeze begins here, so refresh the planned snapshot under the same stock fence.
            foreach(var line in await _db.StockTakeDetails.Where(d=>d.StockTakeNo==no && !d.IsDeleted).ToArrayAsync(ct))
            {
                var stock=await source.InventoryStocks(a.InventoryStocks()).SingleOrDefaultAsync(s=>s.Id==line.StockId
                    && s.WarehouseCd==line.WarehouseCd && s.LocationCd==line.LocationCd && s.ProductCd==line.ProductCd && s.LotNo==line.LotNo,ct)
                    ??throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
                line.BookQty=stock.PhysicalQty;line.UnitPrice=stock.UnitPrice;line.Modifier=a.Actor;line.ModifyDate=DateTime.Now;
            }
            await StartCountCoreAsync(no,a.Actor);a.AtCommit(t=>a.RequireStockTake(no,t));return true;
        });
    public Task UpdateCountsAsync(string no,List<StockTakeCountInput> inputs,string? userName)
    {
        if(!_db.Database.IsNpgsql())return UpdateCountsCoreAsync(no,inputs,userName);
        var frozen=OperationsOwnerV1.Freeze(inputs);
        return Owner.ExecuteAsync(OperationsOwnerV1.StockTake,"count",true,async(a,ct)=>
        {
            await a.RequireStockTake(no,ct);a.RequirePriceRead();a.RequireAmountWrite();
            await UpdateCountsCoreAsync(no,frozen,a.Actor);a.AtCommit(t=>a.RequireStockTake(no,t));return true;
        });
    }
    public Task SubmitForReviewAsync(string no,string? userName)=>!_db.Database.IsNpgsql()?SubmitForReviewCoreAsync(no,userName):
        Owner.ExecuteAsync(OperationsOwnerV1.StockTake,"submit",true,async(a,ct)=>
        {
            await a.RequireStockTake(no,ct);a.RequireAmountRead();await SubmitForReviewCoreAsync(no,a.Actor);
            a.AtCommit(t=>a.RequireStockTake(no,t));return true;
        });
    public async Task ApproveAndApplyAsync(string no,string? userName)
    {
        if(!_db.Database.IsNpgsql()){await ApproveAndApplyCoreAsync(no,userName);return;}
        var effects=new List<StockTransaction>();
        await Owner.ExecuteAsync(OperationsOwnerV1.StockTake,"approve",true,async(a,ct)=>
        {
            await a.RequireStockTake(no,ct);a.RequirePriceRead();a.RequireAmountRead();
            var stock=(StockMovementService)_stock;
            foreach(var line in await _db.StockTakeDetails.AsNoTracking().Where(d=>d.StockTakeNo==no && !d.IsDeleted).ToArrayAsync(ct))
            {
                if(!await a.InventoryStocks().AnyAsync(s=>s.Id==line.StockId && s.WarehouseCd==line.WarehouseCd
                    && s.LocationCd==line.LocationCd && s.ProductCd==line.ProductCd && s.LotNo==line.LotNo,ct))
                    throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN",403);
                if(!await _db.Stocks.AnyAsync(s=>s.Id==line.StockId && s.PhysicalQty==line.BookQty,ct))
                    throw new OrderPriceCorrectionException("STOCKTAKE_SNAPSHOT_CHANGED",409);
            }
            using var inheritance=stock.InheritTransactions(a);
            await ApproveAndApplyCoreAsync(no,a.Actor,r=>stock.ApplyStockTakeWithinOwnerAsync(r,no,ct));
            effects.AddRange(_db.ChangeTracker.Entries<StockTransaction>().Select(e=>e.Entity));
            a.AtCommit(t=>a.RequireStockTake(no,t));return true;
        });
        foreach(var effect in effects)await ((StockMovementService)_stock).PublishCommittedCancellationAsync(effect);
    }
    public Task CancelAsync(string no,string? userName)=>!_db.Database.IsNpgsql()?CancelCoreAsync(no,userName):
        Owner.ExecuteAsync(OperationsOwnerV1.StockTake,"cancel",true,async(a,ct)=>
        {await a.RequireStockTake(no,ct);await CancelCoreAsync(no,a.Actor);a.AtCommit(t=>a.RequireStockTake(no,t));return true;});
}
