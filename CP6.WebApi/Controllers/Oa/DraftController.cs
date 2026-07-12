using CP6.Core.Auth;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>
/// 草稿（暂存）REST（Phase B）。/api/oa/draft — 新建/更新/列表/删除/提交草稿。
/// 草稿 = Wf_FlowInstance.Status=Draft；提交后经 L0 引擎推进。
/// </summary>
[ApiController]
[Route("api/oa/draft")]
[Authorize]
public class DraftController : LocalizedControllerBase
{
    private readonly IDraftService _draft;
    private readonly ICurrentPermissionContext _ctx;

    public DraftController(IDraftService draft, ICurrentPermissionContext ctx)
    {
        _draft = draft;
        _ctx = ctx;
    }

    private async Task<Guid> CurrentUserIdAsync() => (await _ctx.GetAsync()).UserId;
    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });
    private IActionResult Err(InvalidOperationException e) => BadRequest(new { code = 400, message = e.Message });

    // ── 列表 ──

    [HttpGet("list")]
    public async Task<IActionResult> List()
    {
        var me = await CurrentUserIdAsync();
        return Ok2(await _draft.ListDraftsAsync(me));
    }

    // ── 新建草稿 ──

    [HttpPost("save")]
    [RequirePermission("oa-form-catalog", "add")]
    public async Task<IActionResult> Save([FromBody] SaveDraftReq r)
    {
        try
        {
            var me = await CurrentUserIdAsync();
            var id = await _draft.SaveDraftAsync(me, r.FlowKey, r.VarsJson ?? "{}");
            return Ok2(new { id });
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ── 更新草稿 ──

    [HttpPost("update")]
    [RequirePermission("oa-form-catalog", "edit")]
    public async Task<IActionResult> Update([FromBody] UpdateDraftReq r)
    {
        try
        {
            var me = await CurrentUserIdAsync();
            await _draft.UpdateDraftAsync(me, r.Id, r.VarsJson ?? "{}");
            return Ok2();
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ── 提交草稿 ──

    [HttpPost("submit")]
    [RequirePermission("oa-form-catalog", "submit")]
    public async Task<IActionResult> Submit([FromBody] IdReq r)
    {
        try
        {
            var me = await CurrentUserIdAsync();
            await _draft.SubmitDraftAsync(me, r.Id);
            return Ok2();
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    // ── 删除草稿 ──

    [HttpPost("delete")]
    [RequirePermission("oa-form-catalog", "del")]
    public async Task<IActionResult> Delete([FromBody] IdReq r)
    {
        try
        {
            var me = await CurrentUserIdAsync();
            await _draft.DeleteDraftAsync(me, r.Id);
            return Ok2();
        }
        catch (InvalidOperationException e) { return Err(e); }
    }

    public record IdReq(Guid Id);
    public record SaveDraftReq(string FlowKey, string? VarsJson);
    public record UpdateDraftReq(Guid Id, string? VarsJson);
}
