using CP6.Core.Auth;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

/// <summary>工作日历（年历）管理页后端（WFS infra ①，A-T4；spec §2）。当前租户 <see cref="Sys_WorkCalendar"/>
/// 例外表：列一年 / 反转某日 / 回归默认 / 空态导入日本法定假日。
///
/// 权限点（menuKey <c>oa-work-calendar</c>，MenuAction Calendar.View/Edit）：Edit=反转/清除/导入（写），
/// View=列一年（只读 GET，循 OA 兄弟控制器约定不贴细粒度键，读授权=登录态+租户隔离，NoReadOnlyGetAction 守卫禁 GET 贴键）。
/// ★菜单/权限/i18n **种子落库归 F-T1 收口**（本任务仅贴 [RequirePermission] 贴点；键面清单交接 F-T1，
///   与波③ E-T1/F-T2、波④ B-T2 既定分工先例一致）。种子落地前生产端 Edit 端点 fail-closed 403 = 既定中间态。
///
/// 计入 <see cref="OawfPermissionAttributeTests"/> fail-closed 守卫扫描面（计数 17→18，Edit 端点×3 贴键）。</summary>
[ApiController]
[Route("api/oa/work-calendar")]
[Authorize]
public class WorkCalendarController : LocalizedControllerBase
{
    private readonly IWorkCalendarService _svc;

    public WorkCalendarController(IWorkCalendarService svc) { _svc = svc; }

    private IActionResult Ok2(object? data = null) => Ok(new { code = 0, message = "OK", data });

    /// <summary>列某年例外 + 空态标志（一次往返：前端据 isEmpty 渲染「导入」引导）。</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int year, CancellationToken ct)
    {
        var y = year > 0 ? year : DateTime.UtcNow.Year;
        var items = await _svc.ListYearAsync(y, ct);
        var isEmpty = await _svc.IsEmptyAsync(ct);
        return Ok2(new { year = y, isEmpty, items });
    }

    /// <summary>反转某日（补班/假日/备注 upsert）。</summary>
    [HttpPost("toggle")]
    [RequirePermission("oa-work-calendar", "Calendar.Edit")]
    public async Task<IActionResult> Toggle([FromBody] ToggleReq req, CancellationToken ct)
    {
        await _svc.SetDayAsync(req.Date, req.IsWorkday, req.Note, ct);
        return Ok2();
    }

    /// <summary>回归默认态（删例外行）。date 路由段格式 yyyy-MM-dd。</summary>
    [HttpDelete("{date:datetime}")]
    [RequirePermission("oa-work-calendar", "Calendar.Edit")]
    public async Task<IActionResult> Clear(DateTime date, CancellationToken ct)
    {
        await _svc.ClearDayAsync(date, ct);
        return Ok2();
    }

    /// <summary>空态导入日本法定假日到当前租户（幂等）。返回本次新增行数。</summary>
    [HttpPost("import-jp")]
    [RequirePermission("oa-work-calendar", "Calendar.Edit")]
    public async Task<IActionResult> ImportJp(CancellationToken ct)
        => Ok2(new { inserted = await _svc.ImportJapaneseHolidaysAsync(ct) });

    public record ToggleReq(DateTime Date, bool IsWorkday, string? Note);
}
