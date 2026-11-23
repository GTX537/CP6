using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Oa;

[ApiController, Authorize, Route("api/oa/conn/v1")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ConnectorVersionControlController(ConnectorVersionControlService owner) : ControllerBase
{
    private const string Family="{family:regex(^(triggers|connectors)$)}";
    private static string Kind(string family) => family=="triggers" ? "TRIGGER" : "CONNECTOR";

    [HttpGet("options")]
    public Task<IActionResult> Options([FromQuery] string kind,CancellationToken ct) => Run(()=>owner.OptionsAsync(kind,ct));

    [HttpPost("creation-contexts"), RequestSizeLimit(65536)]
    public Task<IActionResult> Creation([FromQuery] string kind,[FromBody] ConnSelectionV1 selection,CancellationToken ct) =>
        Run(()=>owner.CreationAsync(kind,selection,ct));

    [HttpGet(Family)]
    public Task<IActionResult> List(string family,[FromQuery] int page=1,CancellationToken ct=default) =>
        Run(()=>owner.ListAsync(Kind(family),page,ct));

    [HttpGet(Family+"/{id:guid}/context"), HttpGet(Family+"/{id:guid}/versions/{version:guid}")]
    public Task<IActionResult> Context(string family,Guid id,Guid? version,CancellationToken ct) =>
        Run(()=>owner.ContextAsync(Kind(family),id,version,ct));

    [HttpGet("commands/{commandId}")]
    public Task<IActionResult> Command(string commandId,CancellationToken ct) => Run(()=>owner.CommandAsync(commandId,ct));

    [HttpGet("control-impact-previews/{id:guid}")]
    public Task<IActionResult> Impact(Guid id,[FromQuery] int page=1,CancellationToken ct=default) =>
        Run(()=>owner.PreviewAsync(id,page,ct));

    [HttpGet("review-subjects"), HttpGet("review-subjects/{subject:guid}/context")]
    public Task<IActionResult> Reviews([FromQuery] string kind,Guid? subject,[FromQuery] int page=1,CancellationToken ct=default) =>
        Run(()=>owner.ReviewsAsync(kind,subject,null,page,ct));

    [HttpGet(Family+"/{id:guid}/review-context")]
    public Task<IActionResult> ReviewContext(string family,Guid id,[FromQuery] Guid? subject,CancellationToken ct) =>
        Run(()=>owner.ReviewsAsync(Kind(family),subject,id,1,ct));

    [HttpPost(Family),RequestSizeLimit(1048576)]
    public Task<IActionResult> Create(string family,CancellationToken ct) => Write(Kind(family),null,"CREATE",ct);

    [HttpPost(Family+"/{id:guid}/{actionName}"),RequestSizeLimit(1048576)]
    public Task<IActionResult> Change(string family,Guid id,string actionName,CancellationToken ct)
    {
        var operation=actionName switch {
            "versions"=>"REVISE","submit-review"=>"SUBMIT","decisions"=>"DECIDE","control-impact-previews"=>"PREVIEW",
            "publish"=>"PUBLISH","enable"=>"ENABLE","activate"=>"ACTIVATE","disable"=>"DISABLE","retire"=>"RETIRE",
            _=>null
        };
        return operation==null ? Task.FromResult<IActionResult>(NotFound()) : Write(Kind(family),id,operation,ct);
    }

    private Task<IActionResult> Write(string kind,Guid? id,string operation,CancellationToken ct) => Run(async()=>
    {
        using var buffer=new MemoryStream();
        await Request.Body.CopyToAsync(buffer,ct);
        return await owner.WriteAsync(kind,id,operation,buffer.ToArray(),ct);
    });
    private async Task<IActionResult> Run(Func<Task<System.Text.Json.JsonElement>> action)
    {
        try { return Ok(new {code=0,data=await action()}); }
        catch(ScopeAuthorizationExceptionV1 e) { return StatusCode(e.Status,new {code=e.Message}); }
        catch(CurrentPermissionUnavailableException) { return StatusCode(403,new {code="ACTION_FORBIDDEN"}); }
        catch(Npgsql.NpgsqlException) { return StatusCode(503,new {code="CONTROL_UNAVAILABLE"}); }
    }
}
