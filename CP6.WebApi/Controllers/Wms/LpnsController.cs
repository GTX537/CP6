using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

[ApiController]
[Route("api/v2/wms/lpns")]
[Authorize]
public sealed class LpnsController : ControllerBase
{
    private readonly ILpnService _service;
    public LpnsController(ILpnService service) => _service = service;

    [HttpGet]
    [RequirePermission("wms-mobile", "view")]
    public Task<PagedResult<LogisticsUnitDto>> Get(
        string? warehouseCd,
        string? locationCd,
        string? search,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
        => _service.GetAsync(
            warehouseCd, locationCd, search, page, pageSize, ct);

    [HttpGet("{lpnNo}")]
    [RequirePermission("wms-mobile", "view")]
    public async Task<ActionResult<LogisticsUnitDto>> GetOne(
        string lpnNo,
        CancellationToken ct)
    {
        var row = await _service.GetOneAsync(lpnNo, ct);
        return row is null ? NotFound(new { code = "WM-LPN-NOT-FOUND" }) : Ok(row);
    }

    [HttpPost]
    [RequirePermission("wms-mobile", "lpn-manage")]
    public Task<ActionResult<LogisticsUnitDto>> Create(
        CreateLpnRequest request, CancellationToken ct)
        => Execute(() => _service.CreateAsync(request, User.Identity?.Name, ct));

    [HttpPost("{lpnNo}/pack")]
    [RequirePermission("wms-mobile", "lpn-manage")]
    public Task<ActionResult<LogisticsUnitDto>> Pack(
        string lpnNo, PackLpnRequest request, CancellationToken ct)
        => Execute(() => _service.PackAsync(lpnNo, request, User.Identity?.Name, ct));

    [HttpPost("{lpnNo}/unpack")]
    [RequirePermission("wms-mobile", "lpn-manage")]
    public Task<ActionResult<LogisticsUnitDto>> Unpack(
        string lpnNo, UnpackLpnRequest request, CancellationToken ct)
        => Execute(() => _service.UnpackAsync(lpnNo, request, User.Identity?.Name, ct));

    [HttpPost("{lpnNo}/move")]
    [RequirePermission("wms-mobile", "lpn-manage")]
    public Task<ActionResult<LogisticsUnitDto>> Move(
        string lpnNo, MoveLpnRequest request, CancellationToken ct)
        => Execute(() => _service.MoveAsync(lpnNo, request, User.Identity?.Name, ct));

    [HttpPost("{lpnNo}/split")]
    [RequirePermission("wms-mobile", "lpn-manage")]
    public Task<ActionResult<LogisticsUnitDto>> Split(
        string lpnNo, SplitLpnRequest request, CancellationToken ct)
        => Execute(() => _service.SplitAsync(lpnNo, request, User.Identity?.Name, ct));

    [HttpPost("{lpnNo}/merge")]
    [RequirePermission("wms-mobile", "lpn-manage")]
    public Task<ActionResult<LogisticsUnitDto>> Merge(
        string lpnNo, MergeLpnRequest request, CancellationToken ct)
        => Execute(() => _service.MergeAsync(lpnNo, request, User.Identity?.Name, ct));

    [HttpPost("policies")]
    [RequirePermission("wms-mobile", "lpn-manage")]
    public async Task<ActionResult<LpnPolicyRequest>> Policy(
        LpnPolicyRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _service.UpsertPolicyAsync(
                request, User.Identity?.Name, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { code = ex.Message, message = ex.Message });
        }
    }

    private static async Task<ActionResult<LogisticsUnitDto>> Execute(
        Func<Task<LogisticsUnitDto>> action)
    {
        try { return new OkObjectResult(await action()); }
        catch (MobileTaskNotFoundException ex)
        {
            return new NotFoundObjectResult(new { code = ex.Message, message = ex.Message });
        }
        catch (MobileTaskConflictException ex)
        {
            return new ConflictObjectResult(new { code = ex.Code, message = ex.Code });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new BadRequestObjectResult(new { code = ex.Message, message = ex.Message });
        }
    }
}
