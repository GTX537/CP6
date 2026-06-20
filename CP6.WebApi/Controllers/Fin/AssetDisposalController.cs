using CP6.Core.Auth;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>资产处置 REST（A3 §7.4）。Create/Confirm/Reverse + List/Get。</summary>
[ApiController]
[Route("api/fin/asset-disposal")]
[Authorize]
public class AssetDisposalController : ControllerBase
{
    private readonly IAssetDisposalService _svc;
    public AssetDisposalController(IAssetDisposalService svc) => _svc = svc;

    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    public sealed class ReasonReq { public string Reason { get; set; } = string.Empty; }

    [HttpGet]
    [RequirePermission("fin-asset-disposal", "view")]
    public async Task<IActionResult> List([FromQuery] AssetDisposalStatus? status, [FromQuery] Guid? assetCardId)
        => Ok2(await _svc.ListAsync(status, assetCardId));

    [HttpGet("{id}")]
    [RequirePermission("fin-asset-disposal", "view")]
    public async Task<IActionResult> Get(Guid id) => Ok2(await _svc.GetAsync(id));

    [HttpPost]
    [RequirePermission("fin-asset-disposal", "add")]
    public async Task<IActionResult> Create([FromBody] AssetDisposal d)
    {
        var r = await _svc.CreateAsync(d, CurrentUser);
        return r.Ok ? Ok2(new { id = d.Id, no = d.No }) : Fin(r);
    }

    [HttpPost("{id}/confirm")]
    [RequirePermission("fin-asset-disposal", "confirm")]
    public async Task<IActionResult> Confirm(Guid id) => Fin(await _svc.ConfirmAsync(id, CurrentUser));

    [HttpPost("{id}/reverse")]
    [RequirePermission("fin-asset-disposal", "reverse")]
    public async Task<IActionResult> Reverse(Guid id, [FromBody] ReasonReq r)
        => Fin(await _svc.ReverseAsync(id, CurrentUser, r.Reason));
}
