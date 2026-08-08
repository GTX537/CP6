using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController]
[Route("api/oa")]
[Authorize]
public sealed class DefinitionController : ControllerBase
{
    private readonly IFlowDefService _flows;
    private readonly IFormService _forms;
    private readonly ICurrentPermissionContext _permission;

    public DefinitionController(IFlowDefService flows, IFormService forms, ICurrentPermissionContext permission)
    {
        _flows = flows;
        _forms = forms;
        _permission = permission;
    }

    [HttpGet("flow-defs/{flowKey}/draft")]
    public async Task<IActionResult> GetFlowDraft(string flowKey)
    {
        var draft = await _flows.GetDraftAsync(flowKey);
        if (draft is null) return NotFound(new { code = 404, message = "Not found" });
        var head = await _flows.GetDefAsync(flowKey);
        return OkEnvelope(new
        {
            draft.DefinitionId, draft.VersionId, draft.Version, draft.Name, draft.SchemaJson,
            draft.RowVersion, draft.Status, FormKey = head?.FormKey
        });
    }

    [HttpPut("flow-defs/{flowKey}/draft")]
    [RequirePermission("oa-designer", "edit")]
    public async Task<IActionResult> SaveFlowDraft(string flowKey, [FromBody] FlowDraftRequest request)
    {
        try
        {
            var user = (await _permission.GetAsync()).UserId.ToString();
            return OkEnvelope(await _flows.SaveDraftAsync(flowKey, request.Name, request.FormKey,
                request.SchemaJson, Decode(request.RowVersion), user));
        }
        catch (InvalidOperationException ex) { return Error(ex); }
    }

    [HttpPost("flow-defs/{flowKey}/publish")]
    [RequirePermission("oa-designer", "edit")]
    public async Task<IActionResult> PublishFlow(string flowKey, [FromBody] PublishRequest request)
    {
        try
        {
            var user = (await _permission.GetAsync()).UserId;
            return OkEnvelope(await _flows.PublishAsync(flowKey, Decode(request.RowVersion), user));
        }
        catch (InvalidOperationException ex) { return Error(ex); }
    }

    [HttpGet("flow-defs/{flowKey}/versions")]
    public async Task<IActionResult> FlowVersions(string flowKey) => OkEnvelope(await _flows.ListVersionsAsync(flowKey));

    [HttpGet("flow-defs/{flowKey}/versions/{version:int}")]
    public async Task<IActionResult> FlowVersion(string flowKey, int version)
        => ToResult(await _flows.GetVersionAsync(flowKey, version));

    [HttpGet("form-defs/{formKey}/draft")]
    public async Task<IActionResult> GetFormDraft(string formKey)
        => ToResult(await _forms.GetDraftAsync(formKey));

    [HttpPut("form-defs/{formKey}/draft")]
    [RequirePermission("oa-designer", "form-save")]
    public async Task<IActionResult> SaveFormDraft(string formKey, [FromBody] FormDraftRequest request)
    {
        try
        {
            var user = (await _permission.GetAsync()).UserId.ToString();
            return OkEnvelope(await _forms.SaveDraftAsync(formKey, request.Name, request.SchemaJson,
                Decode(request.RowVersion), user));
        }
        catch (InvalidOperationException ex) { return Error(ex); }
    }

    [HttpPost("form-defs/{formKey}/publish")]
    [RequirePermission("oa-designer", "form-save")]
    public async Task<IActionResult> PublishForm(string formKey, [FromBody] PublishRequest request)
    {
        try
        {
            var user = (await _permission.GetAsync()).UserId;
            return OkEnvelope(await _forms.PublishAsync(formKey, Decode(request.RowVersion), user));
        }
        catch (InvalidOperationException ex) { return Error(ex); }
    }

    [HttpGet("form-defs/{formKey}/versions")]
    public async Task<IActionResult> FormVersions(string formKey) => OkEnvelope(await _forms.ListVersionsAsync(formKey));

    [HttpGet("form-defs/{formKey}/versions/{version:int}")]
    public async Task<IActionResult> FormVersion(string formKey, int version)
        => ToResult(await _forms.GetVersionAsync(formKey, version));

    private IActionResult ToResult(object? value) =>
        value == null ? NotFound(new { code = 404, message = "Not found" }) : OkEnvelope(value);

    private IActionResult OkEnvelope(object value) => Ok(new { code = 0, message = "OK", data = value });

    private IActionResult Error(InvalidOperationException ex) =>
        ex.Message == "E-WF-045"
            ? Conflict(new { code = ex.Message, message = ex.Message })
            : BadRequest(new { code = ex.Message, message = ex.Message });

    private static byte[]? Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { throw new InvalidOperationException("E-WF-045"); }
    }

    public sealed record FlowDraftRequest(string Name, string? FormKey, string SchemaJson, string? RowVersion);
    public sealed record FormDraftRequest(string Name, string SchemaJson, string? RowVersion);
    public sealed record PublishRequest(string? RowVersion);
}
