using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Sys;

[ApiController, Route("api/iam/inquiry-scopes"), Authorize]
public sealed class IamInquiryScopeController(IamInquiryScopeAdministrationV1 scopes, IamAdministrationV1 authority) : ControllerBase
{
    [HttpGet("{roleId:int}")]
    public Task<IActionResult> Read(int roleId) => Run(async () => await scopes.ReadAsync(roleId, Actor, HttpContext.RequestAborted));
    [HttpPut("{roleId:int}")]
    public Task<IActionResult> Change(int roleId, IamInquiryScopeCommandV1 command) => Run(async () => await scopes.ChangeAsync(roleId, command, Actor, HttpContext.RequestAborted));
    [HttpGet("operations/{id:guid}")]
    public Task<IActionResult> Operation(Guid id) => Resolve(id, false);
    [HttpPost("operations/{id:guid}/close")]
    public Task<IActionResult> Close(Guid id) => Resolve(id, true);
    private Task<IActionResult> Resolve(Guid id, bool close) => Run(async () => await authority.Resolve(id, IamInquiryScopeAdministrationV1.Kind, close, Actor, HttpContext.RequestAborted));
    private Task<IActionResult> Run(Func<Task<object>> action) => IamAdministrationHttpV1.Execute(this, action);
    private Task<Guid> Actor(CancellationToken ct) => IamAdministrationHttpV1.CurrentActor(HttpContext, ct);
}
