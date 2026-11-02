using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Pub;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Pub;

[ApiController, Authorize]
[Route("api/pub/codegen/metadata-v1/{metadataId:guid}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CodeGenMetadataControllerV1(IServiceScopeFactory scopes, ITenantContext tenant) : ControllerBase
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record OriginalRequest([Required] string OperationRef);

    [HttpGet]
    public Task<IActionResult> Current(Guid metadataId, CancellationToken ct)
        => Execute(metadataId, async owner => await owner.ReadAsync(ct: ct), ct);
    [HttpGet("versions/{revision:long}")]
    public Task<IActionResult> Version(Guid metadataId, long revision, CancellationToken ct)
        => Execute(metadataId, async owner => await owner.ReadAsync(revision, ct), ct);
    [HttpPost("saves"), RequestSizeLimit(CodeGenService.PreviewMaxRequestBytes)]
    public Task<IActionResult> Save(Guid metadataId, CodeGenMetadataSaveV1 command, CancellationToken ct)
        => Execute(metadataId, async owner => await owner.SaveAsync(command, ct), ct);
    [HttpPost("saves/read"), RequestSizeLimit(CodeGenService.PreviewMaxRequestBytes)]
    public Task<IActionResult> Original(Guid metadataId, OriginalRequest request, CancellationToken ct)
        => Execute(metadataId, async owner => await owner.ReadOperationAsync(request.OperationRef, ct), ct);

    private async Task<IActionResult> Execute(Guid metadataId, Func<CodeGenMetadataOwnerV1, Task<object?>> action, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope(); var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContext>().CurrentTenantId = tenant.CurrentTenantId;
        var db = services.GetRequiredService<CP6Context>();
        try
        {
            var owner = new CodeGenMetadataOwnerV1(() => db, tenant.CurrentTenantId, metadataId,
                (_, token) => IamAdministrationHttpV1.CurrentActor(HttpContext, services, token));
            var value = await action(owner);
            return value == null ? NotFound(new { code = "CODEGEN_ORIGINAL_NOT_FOUND" }) : Ok(value);
        }
        catch (CodeGenMetadataExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
        catch (IamAdministrationExceptionV1 e) { return StatusCode(e.Status, new { code = e.Message }); }
        catch (CodeGenPreviewValidationException e) { return BadRequest(new { code = "CODEGEN_INPUT_INVALID", message = e.Message }); }
    }
}
