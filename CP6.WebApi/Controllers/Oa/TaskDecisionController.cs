using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController]
[Route("api/oa/tasks")]
[Authorize]
public sealed class TaskDecisionController : ControllerBase
{
    private readonly ITaskDecisionService _decisions;
    private readonly ICurrentPermissionContext _context;
    private readonly IDelegateService _delegates;

    public TaskDecisionController(
        ITaskDecisionService decisions, ICurrentPermissionContext context, IDelegateService delegates)
    {
        _decisions = decisions;
        _context = context;
        _delegates = delegates;
    }

    [HttpPost("{taskId:guid}/decision")]
    [RequirePermission("oa-inbox", "approve")]
    public async Task<IActionResult> Decide(Guid taskId, [FromBody] DecisionRequest request, CancellationToken ct)
    {
        try
        {
            var actual = (await _context.GetAsync()).UserId;
            var effective = actual;
            var header = Request.Headers["X-Acting-As"].ToString();
            if (Guid.TryParse(header, out var actingAs) && actingAs != Guid.Empty && actingAs != actual)
            {
                await _delegates.AssertActiveGrantAsync(actual, actingAs);
                effective = actingAs;
            }
            var result = await _decisions.DecideAsync(new(
                taskId, actual, effective, request.Decision, request.Comment,
                request.DataPatch, Decode(request.ExpectedFormDataRowVersion)), ct);
            return Ok(new { code = 0, message = "OK", data = result });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { code = ex.Message, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            var code = ex.Message.Split(':')[0];
            var status = code == "E-WF-049" ? 409 : code == "E-WF-004" ? 409 : 400;
            return StatusCode(status, new { code, message = ex.Message });
        }
    }

    private static byte[]? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { throw new InvalidOperationException("E-WF-049"); }
    }

    public sealed record DecisionRequest(
        string Decision, string? Comment, JsonElement DataPatch, string? ExpectedFormDataRowVersion);
}
