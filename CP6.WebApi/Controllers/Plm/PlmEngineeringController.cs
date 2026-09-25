using System.Security.Claims;
using CP6.Core.Services.Common;
using CP6.Core.Services.Plm;
using CP6.Entity.DTOs.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace CP6.WebApi.Controllers.Plm;

[ApiController]
[PlmModelValidation]
[Authorize]
[Route("api/plm/engineering")]
public sealed class PlmEngineeringController(IPlmEngineeringService service, ITenantContext tenant) : ControllerBase
{
    private (Guid TenantId, string Actor) Subject()
    {
        var raw = User.FindFirst("tenant_id")?.Value;
        if (!Guid.TryParse(raw, out var id) || id == Guid.Empty || id != tenant.CurrentTenantId)
            throw new PlmException("PLM_TENANT_MISMATCH", 403, "Authenticated tenant is required and must match the request context.");
        var actor = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(actor)) throw new PlmException("PLM_ACTOR_REQUIRED", 401, "Authenticated actor is required.");
        return (id, actor);
    }

    private async Task<IActionResult> Handle(Func<Guid, string, Task<IActionResult>> work)
    {
        try
        {
            var (id, actor) = Subject();
            return await work(id, actor);
        }
        catch (PlmException ex) { return Problem(ex); }
        catch (DbUpdateConcurrencyException) { return Problem(PlmException.Conflict("PLM_CANDIDATE_STALE")); }
        catch (DbUpdateException) { return Problem(PlmException.Conflict("PLM_STORAGE_CONFLICT")); }
        catch (SqlException ex) when (ex.Number is 1205 or -2)
        {
            return Problem(new PlmException("PLM_OWNER_BUSY", 503, "Owner lock retry exhausted."));
        }
        catch (SqlException) { return Problem(new PlmException("PLM_STORAGE_UNAVAILABLE", 503, "PLM storage operation failed.")); }
    }
    private IActionResult Problem(PlmException ex)
    {
        var details = new ProblemDetails { Status = ex.HttpStatus, Title = ex.Code, Detail = ex.Message, Type = "about:blank" };
        details.Extensions["code"] = ex.Code;
        details.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return StatusCode(ex.HttpStatus, details);
    }
    private static IActionResult CreatedOrOk<T>(PlmOperation<T> result) =>
        new ObjectResult(result.Value) { StatusCode = result.Created ? 201 : 200 };

    [HttpPost("iteration-candidates")]
    public Task<IActionResult> CreateIterationCandidate(CreateIterationCandidateCommand command, CancellationToken ct) =>
        Handle(async (id, actor) => CreatedOrOk(await service.CreateIterationCandidateAsync(id, actor, command, ct)));

    [HttpPost("candidates")]
    public Task<IActionResult> CreateEngineeringCandidate(CreateEngineeringCandidateCommand command, CancellationToken ct) =>
        Handle(async (id, actor) => CreatedOrOk(await service.CreateEngineeringCandidateAsync(id, actor, command, ct)));

    [HttpPost("candidates/{candidateId:guid}/manifests")]
    public Task<IActionResult> CaptureTechnicalManifest(Guid candidateId, CaptureTechnicalManifestCommand command, CancellationToken ct) =>
        Handle(async (id, actor) => CreatedOrOk(await service.CaptureTechnicalManifestAsync(id, actor, candidateId, command, ct)));

    [HttpPost("candidates/{candidateId:guid}/manifests/{manifestId:guid}/validate")]
    public Task<IActionResult> ValidateTechnicalManifest(Guid candidateId, Guid manifestId, ValidateTechnicalManifestCommand command, CancellationToken ct) =>
        Handle(async (id, actor) => Ok(await service.ValidateTechnicalManifestAsync(id, actor, candidateId, manifestId, command, ct)));

    [HttpPost("candidates/{candidateId:guid}/baselines/freeze")]
    public Task<IActionResult> FreezeEngineeringBaseline(Guid candidateId, FreezeEngineeringBaselineCommand command, CancellationToken ct) =>
        Handle(async (id, actor) => CreatedOrOk(await service.FreezeEngineeringBaselineAsync(id, actor, candidateId, command, ct)));

    [HttpGet("manifests/{manifestId:guid}")]
    public Task<IActionResult> GetTechnicalManifest(Guid manifestId, CancellationToken ct) =>
        Handle(async (id, _) => Ok(await service.GetTechnicalManifestAsync(id, manifestId, ct)));

    [HttpGet("baselines/{baselineId:guid}")]
    public Task<IActionResult> GetEngineeringBaseline(Guid baselineId, CancellationToken ct) =>
        Handle(async (id, _) => Ok(await service.GetEngineeringBaselineAsync(id, baselineId, ct)));

    [HttpGet("baselines/resolve")]
    public Task<IActionResult> ResolveBaselineIdentity([FromQuery] Guid candidateId, [FromQuery] int candidateVersion, CancellationToken ct) =>
        Handle(async (id, _) => Ok(await service.ResolveBaselineIdentityAsync(id, candidateId, candidateVersion, ct)));

    [HttpGet("baselines/by-freeze-request/{freezeRequestId:guid}")]
    public Task<IActionResult> ResolveFreezeRequest(Guid freezeRequestId, CancellationToken ct) =>
        Handle(async (id, _) => Ok(await service.ResolveFreezeRequestAsync(id, freezeRequestId, ct)));

    [HttpGet("manifests/by-capture-request/{captureRequestId:guid}")]
    public Task<IActionResult> ResolveCaptureRequest(Guid captureRequestId, CancellationToken ct) =>
        Handle(async (id, _) => Ok(await service.ResolveCaptureRequestAsync(id, captureRequestId, ct)));

    [HttpPost("baselines/{baselineId:guid}/supersede")]
    public Task<IActionResult> SupersedeEngineeringBaseline(Guid baselineId, SupersedeEngineeringBaselineCommand command, CancellationToken ct) =>
        Handle(async (id, actor) => Ok(await service.SupersedeEngineeringBaselineAsync(id, actor, baselineId, command, ct)));
}

// Runs ahead of ApiController's default model-state filter so PLM errors keep a stable code.
[AttributeUsage(AttributeTargets.Class)]
public sealed class PlmModelValidationAttribute : ActionFilterAttribute
{
    public PlmModelValidationAttribute() => Order = -3000;

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;
        var problem = new ProblemDetails
        {
            Status = 422, Title = "PLM_CANONICAL_SCHEMA", Detail = "The PLM request is invalid.", Type = "about:blank"
        };
        problem.Extensions["code"] = "PLM_CANONICAL_SCHEMA";
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        context.Result = new ObjectResult(problem) { StatusCode = 422 };
    }
}
