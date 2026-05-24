using CP6.Core.EFDbContext;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>
/// MSBBWM080 — 出荷梱包 照会 Web API
/// </summary>
[ApiController]
[Route("api/wms/shipping")]
[Authorize]
public class ShippingController : ControllerBase
{
    private readonly CP6Context _db;
    public ShippingController(CP6Context db) => _db = db;

    /// <summary>梱包一覧</summary>
    [HttpGet("packages")]
    public async Task<IActionResult> SearchPackages(
        [FromQuery] string? packageNo,
        [FromQuery] string? outboundNo,
        [FromQuery] string? trackingNo,
        [FromQuery] string? carrierCd,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var q = _db.ShippingPackages.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(packageNo)) q = q.Where(x => x.PackageNo.Contains(packageNo));
        if (!string.IsNullOrWhiteSpace(outboundNo)) q = q.Where(x => x.OutboundNo == outboundNo);
        if (!string.IsNullOrWhiteSpace(trackingNo)) q = q.Where(x => x.TrackingNo == trackingNo);
        if (!string.IsNullOrWhiteSpace(carrierCd)) q = q.Where(x => x.CarrierCd == carrierCd);
        if (from.HasValue) q = q.Where(x => x.DepartureTime >= from.Value);
        if (to.HasValue) q = q.Where(x => x.DepartureTime <= to.Value);

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(x => x.DepartureTime)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
        return Ok(new { code = 0, message = "OK", data = new { total, page, pageSize, items } });
    }

    [HttpGet("packages/{no}")]
    public async Task<IActionResult> GetPackage(string no)
    {
        var pkg = await _db.ShippingPackages.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PackageNo == no && !x.IsDeleted);
        if (pkg == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = pkg });
    }
}
