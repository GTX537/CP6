using CP6.Core.Auth;
using CP6.Core.Services.Pur;
using CP6.Entity.DomainModels.Pur;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Pur;

/// <summary>采购价表 REST —— 采购章01 §3/§4。/api/pur/supplier-price。供应商×物料阶梯价 + 带价解析。</summary>
[ApiController]
[Route("api/pur/supplier-price")]
[Authorize]
public class SupplierPriceController : ControllerBase
{
    private readonly ISupplierPriceService _svc;
    public SupplierPriceController(ISupplierPriceService svc) => _svc = svc;

    private string? CurrentUser => User?.Identity?.Name;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    /// <summary>列出某供应商（可再按物料过滤）的价表。</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string supplierId, [FromQuery] string? itemId = null)
        => Ok2(await _svc.ListAsync(supplierId, itemId));

    /// <summary>解析采购单价（带价规则：满足 MinQty≤qty ∧ 当时有效，取 MinQty 最大档）。</summary>
    [HttpGet("resolve")]
    public async Task<IActionResult> Resolve([FromQuery] string supplierId, [FromQuery] string itemId,
        [FromQuery] decimal qty, [FromQuery] DateTime? onDate = null)
        => Ok2(new { price = await _svc.ResolvePriceAsync(supplierId, itemId, qty, onDate ?? DateTime.Now) });

    [HttpPost]
    [RequirePermission("pur-supplier-price", "add")]
    public async Task<IActionResult> Save([FromBody] SupplierPrice price)
    {
        try { return Ok2(await _svc.SaveAsync(price, CurrentUser)); }
        catch (InvalidOperationException e) { return Err(e); }
    }

    [HttpDelete("{id}")]
    [RequirePermission("pur-supplier-price", "delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try { await _svc.DeleteAsync(id, CurrentUser); return Ok2(); }
        catch (InvalidOperationException e) { return Err(e); }
    }
}
