using System.Data.Common;
using CP6.Core.Auth;
using CP6.Core.Services.Sys;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

[ApiController]
[Authorize]
[Route("api/lang")]
public sealed class LanguageOwnerControllerV1(LanguageEntriesV1 entries, LanguagePublicationV1 publication) : ControllerBase
{
    [HttpGet("entries-v1/{scope}")]
    public Task<IActionResult> List(string scope, string? keyword, int page = 1, int pageSize = 20)
        => Execute(this, () => entries.List(HttpContext, Global(scope), keyword, page, pageSize));

    [HttpGet("entries-v1/{scope}/export")]
    public Task<IActionResult> Export(string scope, [FromQuery] string[] fields)
        => Execute(this, () => entries.Export(HttpContext, Global(scope), fields));

    [HttpPost("entries-v1/{scope}/commands")]
    public Task<IActionResult> Apply(string scope, LanguageEntryCommand command)
        => Execute(this, () => entries.Apply(HttpContext, Global(scope), command));

    [HttpPost("entries-v1/{scope}/import")]
    public Task<IActionResult> Import(string scope, [FromBody] LanguageEntryCommand?[] commands)
        => Execute(this, () => entries.Import(HttpContext, Global(scope), commands));

    [HttpPost("entries-v1/{scope}/review")]
    public Task<IActionResult> Review(string scope, LanguageReviewCommand command)
        => Execute(this, () => entries.Review(HttpContext, Global(scope), command));

    [HttpGet("overlay-v1/{language}")]
    public Task<IActionResult> Overlay(string language, string version)
        => Execute(this, () => entries.Overlay(HttpContext, language, version, publication));

    [HttpGet("publication-v1")]
    public Task<IActionResult> Preview()
        => Execute(this, () => publication.Preview(HttpContext));

    [HttpGet("publication-v1/operations/{operation:guid}")]
    public Task<IActionResult> Recover(Guid operation)
        => Execute(this, () => publication.Recover(HttpContext, operation));

    [HttpPost("publication-v1/commands")]
    public Task<IActionResult> Publish(LanguagePublicationCommand command)
        => Execute(this, () => publication.Apply(HttpContext, command));

    internal static async Task<IActionResult> Execute<T>(ControllerBase controller, Func<Task<T>> action)
    {
        controller.Response.Headers.CacheControl = "private, no-store";
        try { return controller.Ok(await action()); }
        catch (LanguageProblemV1 error) { return controller.StatusCode(error.Status, new { code = error.Message }); }
        catch (IamAdministrationExceptionV1 error) { return controller.StatusCode(error.Status, new { code = error.Message }); }
        catch (PlatformAdminAuthorizationException) { return controller.StatusCode(403, new { code = "LANG_SCOPE_FORBIDDEN" }); }
        catch (Exception error) when (error is DbException or DbUpdateException or CurrentPermissionUnavailableException)
        { return controller.StatusCode(503, new { code = "LANG_AUTHORITY_UNAVAILABLE" }); }
    }

    private static bool Global(string scope) => scope switch
    {
        "global" => true, "tenant" => false,
        _ => throw new LanguageProblemV1(404, "LANG_SCOPE_NOT_FOUND")
    };
}
