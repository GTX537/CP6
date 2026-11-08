using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController, Authorize, Route("api/oa/v2/definitions/FORM")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormAuthoringController(FormAuthoringOwnerV2 owner) : ControllerBase
{
    [HttpGet("/api/oa/v2/legacy-form-sources/preview")]
    public Task<IActionResult> LegacySourcePreview([FromQuery]Guid legacyVersionId,[FromQuery]string targetKey,[FromQuery]Guid targetVersionId,CancellationToken ct)
        =>Run(async()=>await owner.LegacySourcePreviewAsync(legacyVersionId,targetKey,targetVersionId,ct));
    [HttpPost("/api/oa/v2/legacy-form-sources"),RequestSizeLimit(65536)]
    public Task<IActionResult> LegacySource(LegacyFormSourceCommandV1 input,CancellationToken ct)=>Run(async()=>await owner.ConfigureLegacySourceAsync(input,ct));
    [HttpGet("/api/oa/v2/legacy-form-sources/operations/{id:guid}")]
    public Task<IActionResult> LegacySourceOriginal(Guid id,CancellationToken ct)=>Run(async()=>await owner.LegacySourceOperationAsync(id,false,ct));
    [HttpPost("/api/oa/v2/legacy-form-sources/operations/{id:guid}/close")]
    public Task<IActionResult> LegacySourceClose(Guid id,CancellationToken ct)=>Run(async()=>await owner.LegacySourceOperationAsync(id,true,ct));
    [HttpGet("{key}/compare")]
    public Task<IActionResult> Compare(string key, [FromQuery] Guid leftVersionId, [FromQuery] Guid rightVersionId, [FromQuery] int page = 1, CancellationToken ct = default)
        => Run(async () => await owner.CompareAsync(key, leftVersionId, rightVersionId, page, ct));
    [HttpGet("/api/oa/v2/form-option-sources")]
    public Task<IActionResult> OptionSources([FromQuery]string search="",[FromQuery]int page=1,CancellationToken ct=default)=>Run(async()=>await owner.OptionSourcesAsync(search,page,ct));
    [HttpGet("entry")]
    public Task<IActionResult> Entry(CancellationToken ct) => Run(async () => await owner.EntryAsync(ct));
    [HttpGet]
    public Task<IActionResult> List([FromQuery] int page = 1, CancellationToken ct = default) => Run(async () => await owner.ListAsync(page, ct));
    [HttpGet("{key}/draft")]
    public Task<IActionResult> Read(string key, CancellationToken ct) => Run(async () => await owner.ReadAsync(key, ct));
    [HttpPost("{key}/rule-preview"), RequestSizeLimit(1024 * 1024)]
    public Task<IActionResult> Rules(string key,CancellationToken ct)=>Run(async()=>await owner.PreviewRulesAsync(key,await Raw(ct),ct));
    [HttpPut("{key}/draft"), RequestSizeLimit(1024 * 1024)]
    public Task<IActionResult> Save(string key, CancellationToken ct) => Run(async () => await owner.SaveAsync(key, await Raw(ct), ct));
    [HttpGet("{key}")]
    public Task<IActionResult> Head(string key, CancellationToken ct) => Run(async () => await owner.ReadHeadAsync(key, ct));
    [HttpGet("{key}/versions")]
    public Task<IActionResult> Versions(string key, [FromQuery] int page = 1, CancellationToken ct = default) => Run(async () => await owner.VersionsAsync(key, page, ct: ct));
    [HttpGet("{key}/versions/{version:guid}")]
    public Task<IActionResult> Version(string key, Guid version, CancellationToken ct) => Run(async () => (await owner.VersionsAsync(key, version: version, ct: ct)).Single());
    [HttpPost("{key}/publish-preview")]
    public Task<IActionResult> Preview(string key, FormPublicationPreviewInputV2 input, CancellationToken ct) => Run(async () => await owner.PreviewPublicationAsync(key, input, ct));
    [HttpPost("{key}/publish"), RequestSizeLimit(1024 * 1024)]
    public Task<IActionResult> Publish(string key, CancellationToken ct) => Run(async () => await owner.PublishAsync(key, await Raw(ct), ct));
    [HttpPost("{key}/clone"), RequestSizeLimit(1024 * 1024)]
    public Task<IActionResult> Clone(string key, CancellationToken ct) => Run(async () => await owner.CloneAsync(key, await Raw(ct), ct));
    [HttpPost("{key}/drafts"), RequestSizeLimit(1024 * 1024)]
    public Task<IActionResult> Next(string key, CancellationToken ct) => Run(async () => await owner.CreateNextDraftAsync(key, await Raw(ct), ct));
    [HttpGet("/api/oa/v2/activations/FORM")]
    public Task<IActionResult> ActivationList([FromQuery] int page = 1, CancellationToken ct = default) => Run(async () => await owner.ActivationListAsync(page, ct));
    [HttpGet("{key}/activation")]
    public Task<IActionResult> Activation(string key, [FromQuery] int page = 1, CancellationToken ct = default) => Run(async () => await owner.ActivationPreviewAsync(key, page, ct));
    [HttpGet("{key}/activation-history")]
    public Task<IActionResult> ActivationHistory(string key, [FromQuery] int page = 1, CancellationToken ct = default) => Run(async () => await owner.ActivationHistoryAsync(key, page, ct));
    [HttpPost("{key}/activation"), RequestSizeLimit(1024 * 1024)]
    public Task<IActionResult> Activate(string key, CancellationToken ct) => Run(async () => await owner.SetActivationAsync(key, await Raw(ct), ct));
    private async Task<byte[]> Raw(CancellationToken ct)
    {
        using var bytes = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await Request.Body.ReadAsync(buffer, ct)) != 0)
        {
            if (bytes.Length + count > 1024 * 1024) throw new ScopeAuthorizationExceptionV1("JSON_SIZE_LIMIT", 400);
            await bytes.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return bytes.ToArray();
    }
    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(new { code = 0, data = await action() }); }
        catch (ScopeAuthorizationExceptionV1 e)
        {
            return StatusCode(e.Status, new { code = e.Message, messageKey = e.Message, fieldErrors = DefinitionDiagnosticsV2.From(e),
                retryMode = e.Status == 503 ? "READ_ORIGINAL" : e.Status == 409 ? "REFRESH_AND_NEW_INTENT" : "NONE" });
        }
        catch (Exception e) when (e is Npgsql.NpgsqlException or CurrentPermissionUnavailableException)
        { return StatusCode(503, new { code = "AUTHORITY_FENCE_UNAVAILABLE", retryMode = "READ_ORIGINAL" }); }
    }
}

[ApiController, Authorize, Route("api/iam/form-authoring-policy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormAuthoringPolicyController(FormAuthoringOwnerV2 owner) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Read(CancellationToken ct) => Run(async () => await owner.PolicyAsync(ct));
    [HttpPost]
    public Task<IActionResult> Save(FormAuthoringPolicyCommandV2 command, CancellationToken ct) => Run(async () => await owner.ConfigureAsync(command, ct));
    [HttpGet("operations/{operation:guid}")]
    public Task<IActionResult> Original(Guid operation, CancellationToken ct) => Run(async () => await owner.PolicyOriginalAsync(operation, ct));
    private async Task<IActionResult> Run(Func<Task<object?>> action)
    {
        try { return Ok(new { code = 0, data = await action() }); }
        catch (ScopeAuthorizationExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
        catch (Exception e) when (e is Npgsql.NpgsqlException or CurrentPermissionUnavailableException)
        { return StatusCode(503, new { code = "AUTHORITY_FENCE_UNAVAILABLE" }); }
    }
}

[ApiController, Authorize, Route("api/iam/form-publication-policy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FormPublicationPolicyController(FormAuthoringOwnerV2 owner) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Read(CancellationToken ct) => Run(async () => await owner.PublicationPolicyAsync(ct));
    [HttpPost]
    public Task<IActionResult> Save(FormPublicationPolicyCommandV2 command, CancellationToken ct) => Run(async () => await owner.ConfigurePublicationAsync(command, ct));
    [HttpGet("operations/{operation:guid}")]
    public Task<IActionResult> Original(Guid operation, CancellationToken ct) => Run(async () => await owner.PublicationPolicyOriginalAsync(operation, ct));
    private async Task<IActionResult> Run(Func<Task<object?>> action)
    {
        try { return Ok(new { code = 0, data = await action() }); }
        catch (ScopeAuthorizationExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
        catch (Exception e) when (e is Npgsql.NpgsqlException or CurrentPermissionUnavailableException)
        { return StatusCode(503, new { code = "AUTHORITY_FENCE_UNAVAILABLE" }); }
    }
}
