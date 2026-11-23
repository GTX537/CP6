using CP6.Core.Auth;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController, Authorize, Route("api/oa/work-calendar/versions")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class WorkCalendarVersionsController(VersionedWorkCalendarV1 owner) : ControllerBase
{
    private const string Menu = "oa-work-calendar";

    [HttpGet]
    public Task<IActionResult> Year([FromQuery] int year, Guid? calendarId, long? revision, CancellationToken ct) =>
        Run(() => owner.YearAsync(year == 0 ? DateTime.UtcNow.Year : year, calendarId, revision, ct));

    [HttpGet("calculate")]
    public Task<IActionResult> Calculate(Guid calendarId, long revision, DateOnly from, int workdays,
        TimeOnly localTime, int? ambiguousOffsetMinutes, string? expectedTimeZoneRulesVersion, CancellationToken ct) =>
        Run(() => owner.CalculateAsync(calendarId, revision, from, workdays, localTime, ambiguousOffsetMinutes, expectedTimeZoneRulesVersion, ct));

    [HttpPost("changes"), RequirePermission(Menu, "Calendar.Edit"), CurrentWriteAuthority(RollbackOnFailure = true)]
    public Task<IActionResult> Change(WorkCalendarChangeV1 command, CancellationToken ct) =>
        Run(() => owner.ChangeAsync(command, ct));

    [HttpPost("import-jp"), RequirePermission(Menu, "Calendar.Edit"), CurrentWriteAuthority(RollbackOnFailure = true)]
    public Task<IActionResult> Import(WorkCalendarImportV1 command, CancellationToken ct) =>
        Run(() => owner.ImportAsync(command, ct));

    [HttpPost("deadline-previews"), RequirePermission(Menu, "Calendar.Recalculate"), CurrentWriteAuthority(RollbackOnFailure = true)]
    public Task<IActionResult> Preview(DeadlineRecalculationPreviewInputV1 command, CancellationToken ct) =>
        Run(() => owner.PreviewRecalculationAsync(command, ct));

    [HttpPost("deadline-recalculations"), RequirePermission(Menu, "Calendar.Recalculate"), CurrentWriteAuthority(RollbackOnFailure = true)]
    public Task<IActionResult> Recalculate(DeadlineRecalculationInputV1 command, CancellationToken ct) =>
        Run(() => owner.RecalculateAsync(command, ct));

    private async Task<IActionResult> Run<T>(Func<Task<T>> run)
    {
        try { return Ok(new { code = 0, data = await run() }); }
        catch (ScopeAuthorizationExceptionV1 error) { return StatusCode(error.Status, new { code = error.Message }); }
        catch (Exception error) when (error is CurrentPermissionUnavailableException or Npgsql.NpgsqlException)
        {
            return StatusCode(503, new { code = "AUTHORITY_FENCE_UNAVAILABLE" });
        }
    }
}
