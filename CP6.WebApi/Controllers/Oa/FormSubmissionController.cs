using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController]
[Route("api/oa/forms/{formKey}/submissions")]
[Authorize]
public sealed class FormSubmissionController : ControllerBase
{
    private readonly IFormSubmissionService _submissions;
    private readonly ICurrentPermissionContext _permission;

    public FormSubmissionController(IFormSubmissionService submissions, ICurrentPermissionContext permission)
    {
        _submissions = submissions;
        _permission = permission;
    }

    [HttpPost]
    [RequirePermission("oa-form-catalog", "submit")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Submit(string formKey, [FromBody] SubmissionRequest request, CancellationToken ct)
    {
        if (!HttpContext.Request.Headers.TryGetValue("Idempotency-Key", out var values) ||
            string.IsNullOrWhiteSpace(values.FirstOrDefault()))
            return BadRequest(new { code = "E-WF-044", message = "E-WF-044" });
        try
        {
            var actor = (await _permission.GetAsync()).UserId;
            var result = await _submissions.SubmitAsync(
                new SubmitFormCommand(formKey, actor, values.First()!, request.Data, request.DraftId), ct);
            return Ok(new { code = 0, message = "OK", data = result });
        }
        catch (InvalidOperationException ex)
        {
            var status = ex.Message.StartsWith("E-WF-044", StringComparison.Ordinal) ? 409 : 400;
            return StatusCode(status, new { code = ex.Message.Split(':')[0], message = ex.Message });
        }
    }

    public sealed record SubmissionRequest(JsonElement Data, Guid? DraftId);
}
