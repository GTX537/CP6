using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>
/// MSBBWM020 — 在庫照会 / 在庫変動 Web API
/// </summary>
[ApiController]
[Route("api/wms/stock")]
[Authorize]
[CP6.WebApi.Controllers.Erp.OrderOwnerResult]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class StockController : ControllerBase
{
    private readonly CP6Context _db;
    private readonly IStockMovementService _mover;
    private readonly StockOperationsService? _reads;
    public StockController(CP6Context db, IStockMovementService mover, StockOperationsService? reads=null)
    {
        _db = db;
        _reads=reads;
        _mover = mover;
    }

    private StockOperationsService Reads=>_reads??throw new CP6.Core.Services.Erp.OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    private string? CurrentUser => User?.Identity?.Name;

    private IStockOriginalOperationsV1 Original=>_mover as IStockOriginalOperationsV1
        ??throw new CP6.Core.Services.Erp.OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);

    private IStockCreationOperationsV2 Creation=>_mover as IStockCreationOperationsV2
        ??throw new CP6.Core.Services.Erp.OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE",503);
    [HttpGet("creation-context/{operation}")]
    public async Task<IActionResult> CreationContext(string operation,[FromQuery] string warehouseCd,[FromQuery] string locationCd,
        [FromQuery] string productCd,[FromQuery] string lotNo="",CancellationToken ct=default)
        =>Ok(new{code=0,message="OK",data=await Creation.CreationContextAsync(operation,new(warehouseCd,locationCd,productCd,lotNo),ct)});
    [HttpPost("commands/v2/apply")]
    public async Task<IActionResult> ApplyCreationCommand([FromBody] StockApplyCommandV2 request,CancellationToken ct)
    {
        try{return Ok(new{code=0,message="OK",data=await Creation.ApplyCommandAsync(request,ct)});}
        catch(InsufficientStockException ex){return BadRequest(new{code=400,message=ex.Message});}
        catch(ArgumentException ex){return BadRequest(new{code=400,message=ex.Message});}
    }
    [HttpPost("commands/v2/move")]
    public async Task<IActionResult> MoveCreationCommand([FromBody] StockMoveCommandV2 request,CancellationToken ct)
    {
        try{return Ok(new{code=0,message="OK",data=await Creation.MoveCommandAsync(request,ct)});}
        catch(InsufficientStockException ex){return BadRequest(new{code=400,message=ex.Message});}
        catch(ArgumentException ex){return BadRequest(new{code=400,message=ex.Message});}
    }

    // The Owner chooses current query for an original and adjust/move only for a new command.
    [HttpPost("commands/v1/apply")]
    public async Task<IActionResult> ApplyCommand([FromBody] StockApplyCommandV1 request,CancellationToken ct)
    {
        try { return Ok(new {code=0,message="OK",data=await Original.ApplyCommandAsync(request,ct)}); }
        catch(InsufficientStockException ex) {return BadRequest(new {code=400,message=ex.Message});}
        catch(ArgumentException ex) {return BadRequest(new {code=400,message=ex.Message});}
    }
    [HttpPost("commands/v1/move")]
    public async Task<IActionResult> MoveCommand([FromBody] StockMoveCommandV1 request,CancellationToken ct)
    {
        try { return Ok(new {code=0,message="OK",data=await Original.MoveCommandAsync(request,ct)}); }
        catch(InsufficientStockException ex) {return BadRequest(new {code=400,message=ex.Message});}
        catch(ArgumentException ex) {return BadRequest(new {code=400,message=ex.Message});}
    }
    [HttpGet("operations/v1/scope")]
    [RequirePermission("wms-stock","query")]
    public async Task<IActionResult> OperationScope(CancellationToken ct)
        =>Ok(new {code=0,message="OK",data=await Original.OperationScopeAsync(ct)});
    [HttpGet("operations/v1/{operation:guid}")]
    [RequirePermission("wms-stock","query")]
    public async Task<IActionResult> ReadOperation(Guid operation,CancellationToken ct)
        =>Ok(new {code=0,message="OK",data=await Original.ReadOperationAsync(operation,ct)});
    [HttpPost("operations/v1/{operation:guid}/close")]
    [RequirePermission("wms-stock","query")]
    public async Task<IActionResult> CloseOperation(Guid operation,CancellationToken ct)
        =>Ok(new {code=0,message="OK",data=await Original.CloseOperationAsync(operation,ct)});

    /// <summary>在庫照会（多条件 + ページング簡易版）</summary>
    [HttpGet]
    [RequirePermission("wms-stock", "query")]
    public async Task<IActionResult> Search(
        [FromQuery] string? warehouseCd,
        [FromQuery] string? locationCd,
        [FromQuery] string? productCd,
        [FromQuery] string? lotNo,
        [FromQuery] string? ownerType,
        [FromQuery] string? ownerCd,
        [FromQuery] bool? hasStockOnly,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if(_db.Database.IsNpgsql())
        {
            var result=await Reads.SearchAsync(new(){WarehouseCd=warehouseCd,LocationCd=locationCd,ProductCd=productCd,LotNo=lotNo,
                OwnerType=ownerType,OwnerCd=ownerCd,HasStockOnly=hasStockOnly,Page=page,PageSize=pageSize},HttpContext.RequestAborted);
            return Ok(new {code=0,message="OK",data=result});
        }
        var q = _db.Stocks.AsNoTracking().Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(warehouseCd)) q = q.Where(x => x.WarehouseCd == warehouseCd);
        if (!string.IsNullOrWhiteSpace(locationCd)) q = q.Where(x => x.LocationCd == locationCd);
        if (!string.IsNullOrWhiteSpace(productCd)) q = q.Where(x => x.ProductCd.Contains(productCd));
        if (!string.IsNullOrWhiteSpace(lotNo)) q = q.Where(x => x.LotNo == lotNo);
        if (!string.IsNullOrWhiteSpace(ownerType)) q = q.Where(x => x.OwnerType == ownerType);
        if (!string.IsNullOrWhiteSpace(ownerCd)) q = q.Where(x => x.OwnerCd == ownerCd);
        if (hasStockOnly == true) q = q.Where(x => x.PhysicalQty != 0);

        var total = await q.CountAsync();
        var items = await q
            .OrderBy(x => x.WarehouseCd).ThenBy(x => x.LocationCd)
            .ThenBy(x => x.ProductCd).ThenBy(x => x.LotNo)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        return Ok(new { code = 0, message = "OK", data = new { total, page, pageSize, items } });
    }

    /// <summary>在庫の変動履歴照会</summary>
    [HttpGet("{stockId:guid}/history")]
    [RequirePermission("wms-stock", "query")]
    public async Task<IActionResult> History(Guid stockId, [FromQuery] int days = 90)
    {
        if(_db.Database.IsNpgsql())return Ok(new{code=0,message="OK",data=await Reads.HistoryAsync(stockId,days,HttpContext.RequestAborted)});
        var s = await _db.Stocks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == stockId);
        if (s == null) return NotFound(new { code = 404, message = "WM-MSG-070" });

        var since = DateTime.Today.AddDays(-days);
        var txns = await _db.StockTransactions.AsNoTracking()
            .Where(t => t.WarehouseCd == s.WarehouseCd &&
                        t.LocationCd == s.LocationCd &&
                        t.ProductCd == s.ProductCd &&
                        t.LotNo == s.LotNo &&
                        t.TxnDateTime >= since)
            .OrderByDescending(t => t.TxnDateTime)
            .ToListAsync();

        return Ok(new { code = 0, message = "OK", data = new { stock = s, transactions = txns } });
    }

    /// <summary>在庫変動 1 件適用（汎用エンドポイント）</summary>
    [HttpPost("apply")]
    [RequirePermission("wms-stock", "adjust")]
    public async Task<IActionResult> Apply([FromBody] StockMovementRequest req, CancellationToken ct)
    {
        req.OperatorCd ??= CurrentUser;
        try
        {
            var txnNo = await _mover.ApplyAsync(req, ct);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { txnNo } });
        }
        catch (InsufficientStockException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>棚移動</summary>
    [HttpPost("move")]
    [RequirePermission("wms-stock", "move")]
    public async Task<IActionResult> Move([FromBody] StockMoveRequest req, CancellationToken ct)
    {
        req.OperatorCd ??= CurrentUser;
        try
        {
            var (outNo, inNo) = await _mover.MoveAsync(req, ct);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { outTxnNo = outNo, inTxnNo = inNo } });
        }
        catch (InsufficientStockException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = 400, message = ex.Message });
        }
    }

    /// <summary>トランザクション一覧（伝票NO / 期間検索）</summary>
    [HttpGet("transactions")]
    [RequirePermission("wms-stock", "query")]
    public async Task<IActionResult> Transactions(
        [FromQuery] string? productCd,
        [FromQuery] string? lotNo,
        [FromQuery] string? txnType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        if(_db.Database.IsNpgsql())return Ok(new{code=0,message="OK",data=await Reads.TransactionsAsync(new(){
            ProductCd=productCd,LotNo=lotNo,TxnType=txnType,From=from,To=to,Page=page,PageSize=pageSize},HttpContext.RequestAborted)});
        var q = _db.StockTransactions.AsNoTracking().Where(t => !t.IsDeleted);
        if (!string.IsNullOrWhiteSpace(productCd)) q = q.Where(t => t.ProductCd == productCd);
        if (!string.IsNullOrWhiteSpace(lotNo)) q = q.Where(t => t.LotNo == lotNo);
        if (!string.IsNullOrWhiteSpace(txnType)) q = q.Where(t => t.TxnType == txnType);
        if (from.HasValue) q = q.Where(t => t.TxnDateTime >= from.Value);
        if (to.HasValue) q = q.Where(t => t.TxnDateTime <= to.Value);

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(t => t.TxnDateTime)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
        return Ok(new { code = 0, message = "OK", data = new { total, page, pageSize, items } });
    }
}
