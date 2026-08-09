using CP6.Core.Auth;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Space;

[ApiController]
[Authorize]
[SpaceDesignV1Contract]
[Route(
    "api/space/planning/v1/sites/{siteId:guid}/scenario-branches/" +
    "{branchId:guid}/exports")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status400BadRequest,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status401Unauthorized,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status403Forbidden,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status404NotFound,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status409Conflict,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status422UnprocessableEntity,
    "application/problem+json")]
[ProducesResponseType(
    typeof(SpaceDesignProblemDetails),
    StatusCodes.Status500InternalServerError,
    "application/problem+json")]
public sealed class SpacePlanningExchangeController(
    ISpacePlanningExchangeService service) : ControllerBase
{
    [HttpGet("gltf")]
    [RequirePermission("space", "planning:exchange:read")]
    [ProducesResponseType(
        typeof(FileContentResult),
        StatusCodes.Status200OK,
        "model/gltf-binary")]
    public async Task<IActionResult> DownloadGlb(
        Guid siteId,
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        var export = await service.ExportGlbAsync(
            siteId,
            branchId,
            cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ETag = $"\"{export.Sha256}\"";
        Response.Headers["X-Space-Exchange-Schema"] = export.SchemaVersion;
        Response.Headers["X-Space-Exchange-Sha256"] = export.Sha256;
        return File(export.Content, export.ContentType, export.FileName);
    }
}
