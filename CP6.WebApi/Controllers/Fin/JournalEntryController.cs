using CP6.Core.Auth;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

/// <summary>
/// 记账凭证 REST —— 财务章01 §5/§6。/api/fin/journal。
/// 凭证不可改不可删：无 PUT/DELETE，只有 submit/post/reject/reverse 状态机动作。
/// </summary>
[ApiController]
[Route("api/fin/journal")]
[Authorize]
public class JournalEntryController : ControllerBase
{
    private readonly IJournalEntryService _svc;
    public JournalEntryController(IJournalEntryService svc) => _svc = svc;

    private string CurrentUser => User?.Identity?.Name ?? "anonymous";
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Fin(FinResult r) => r.Ok ? Ok2() : BadRequest(new { code = 400, message = r.Code, args = r.Args });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? periodId, [FromQuery] JournalStatus? status)
        => Ok2(await _svc.ListAsync(periodId, status));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(Guid id)
    {
        var e = await _svc.GetAsync(id);
        return e == null ? BadRequest(new { code = 400, message = "E-FIN-130" }) : Ok2(e);
    }

    /// <summary>新建草稿。</summary>
    [HttpPost]
    [RequirePermission("fin-journal", "add")]
    public async Task<IActionResult> Create([FromBody] JournalEntry entry)
        => Ok2(new { id = await _svc.CreateDraftAsync(entry, CurrentUser) });

    [HttpPost("{id}/submit")]
    [RequirePermission("fin-journal", "submit")]
    public async Task<IActionResult> Submit(Guid id) => Fin(await _svc.SubmitForReviewAsync(id));

    /// <summary>过账（过账人=当前登录用户，须≠制单人）。</summary>
    [HttpPost("{id}/post")]
    [RequirePermission("fin-journal", "post")]
    public async Task<IActionResult> Post(Guid id) => Fin(await _svc.PostAsync(id, CurrentUser));

    [HttpPost("{id}/reject")]
    [RequirePermission("fin-journal", "reject")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ReasonReq r) => Fin(await _svc.RejectAsync(id, r.Reason));

    /// <summary>手工红冲（走复核，autoPost=false）。</summary>
    [HttpPost("{id}/reverse")]
    [RequirePermission("fin-journal", "reverse")]
    public async Task<IActionResult> Reverse(Guid id, [FromBody] ReasonReq r)
        => Fin(await _svc.ReverseAsync(id, CurrentUser, r.Reason, autoPost: false));

    public record ReasonReq(string Reason);
}
