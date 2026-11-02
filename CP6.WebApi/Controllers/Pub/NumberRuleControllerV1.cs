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

[ApiController]
[Authorize]
[Route("api/pub/seq/owner-v1/{ruleId:guid}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NumberRuleControllerV1(IServiceScopeFactory scopes, ITenantContext tenant) : ControllerBase
{
    // The request cannot choose tenant, SourceOwner, domain, period or initial value.
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ChangeRequest(
        [Required(AllowEmptyStrings = true)] string OperationRef,
        [Required(AllowEmptyStrings = true)] string ExpectedRuleVersion,
        [Required(AllowEmptyStrings = true)] string NewVersion,
        [Required(AllowEmptyStrings = true)] string Prefix,
        string? DateFormat, [Required] int? MinimumWidth);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record VersionRequest([Required(AllowEmptyStrings = true)] string Version);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record OriginalRequest([Required(AllowEmptyStrings = true)] string OperationRef);

    [HttpGet]
    public Task<IActionResult> Current(Guid ruleId, CancellationToken ct)
        => Execute(ruleId, async owner => (object?)await owner.ReadRuleAsync(ct: ct), ct);

    // JSON bodies preserve the Owner's unrestricted original keys, including NUL/Unicode;
    // these reads neither consume a number nor reserve one. Existing CSRF policy applies.
    [HttpPost("versions/read")]
    public Task<IActionResult> Version(Guid ruleId, VersionRequest request, CancellationToken ct)
        => Execute(ruleId, async owner => (object?)await owner.ReadRuleAsync(request.Version, ct), ct);

    [HttpPost("changes")]
    public Task<IActionResult> Change(Guid ruleId, ChangeRequest request, CancellationToken ct)
        => Execute(ruleId, async owner => (object?)await owner.ChangeRuleAsync(new(request.OperationRef,
            request.ExpectedRuleVersion, request.NewVersion, request.Prefix, request.DateFormat, request.MinimumWidth!.Value), ct), ct);

    [HttpPost("changes/read")]
    public Task<IActionResult> Original(Guid ruleId, OriginalRequest request, CancellationToken ct)
        => Execute(ruleId, async owner => (object?)await owner.ReadRuleChangeAsync(request.OperationRef, ct), ct);

    [HttpGet("preview")]
    public Task<IActionResult> Preview(Guid ruleId, CancellationToken ct)
        => Execute(ruleId, async owner => (object?)await owner.PreviewAsync(DateTimeOffset.UtcNow, ct), ct);

    private async Task<IActionResult> Execute(Guid ruleId, Func<NumberAllocationOwnerV1, Task<object?>> action, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope(); var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContext>().CurrentTenantId = tenant.CurrentTenantId;
        var db = services.GetRequiredService<CP6Context>();
        try
        {
            var registration = await NumberAllocationOwnerV1.FindRegisteredScopeAsync(db, ruleId, ct);
            if (registration == null) return NotFound(new { code = "E-PUB-051" });
            var owner = new NumberAllocationOwnerV1(() => db, registration,
                new(services.GetRequiredService<IHttpContextAccessor>(),
                    (_, token) => IamAdministrationHttpV1.CurrentActor(HttpContext, services, token), "pub-seq", "add", "query"));
            var result = await action(owner);
            return result == null ? NotFound(new { code = "NUMBER_ORIGINAL_NOT_FOUND" }) : Ok(result);
        }
        catch (NumberAllocationExceptionV1 error) { return StatusCode(error.Status, new { code = error.Message }); }
        catch (IamAdministrationExceptionV1 error) { return StatusCode(error.Status, new { code = error.Message }); }
    }
}
