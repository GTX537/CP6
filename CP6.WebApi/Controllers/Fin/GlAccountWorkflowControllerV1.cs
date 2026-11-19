using CP6.Core.Services.Erp;
using CP6.Core.Services.Fin;
using CP6.WebApi.Controllers.Erp;
using CP6.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Fin;

[ApiController, Authorize, OrderOwnerResult]
[Route("api/fin/account/owner-v1")]
public sealed class GlAccountWorkflowControllerV1(GlAccountWorkflowOwnerV1 owner) : ControllerBase
{
    private const string FinanceContext = "X-Finance-Context";

    [HttpGet("options")]
    public Task<object> Options(CancellationToken ct) => owner.Options(CurrentActor, ct);

    [HttpGet("roots")]
    public Task<object> Roots([FromHeader(Name = FinanceContext)] Guid context, CancellationToken ct)
        => owner.Roots(context, CurrentActor, ct);

    [HttpGet("parent-options")]
    public Task<object> ParentOptions([FromHeader(Name = FinanceContext)] Guid context, DateOnly date, CancellationToken ct)
        => owner.ParentOptions(context, date, CurrentActor, ct);

    [HttpGet("roots/{root:guid}/revisions")]
    public Task<object> Revisions([FromHeader(Name = FinanceContext)] Guid context, Guid root, CancellationToken ct)
        => owner.Revisions(context, root, CurrentActor, ct);

    [HttpGet("roots/{root:guid}/workflow")]
    public Task<object> Detail([FromHeader(Name = FinanceContext)] Guid context, Guid root, CancellationToken ct)
        => owner.Detail(context, root, CurrentActor, ct);

    [HttpGet("roots/{root:guid}/history")]
    public Task<object> History([FromHeader(Name = FinanceContext)] Guid context, Guid root, Guid proposalId, long revision, CancellationToken ct)
        => owner.History(context, root, proposalId, revision, CurrentActor, ct);

    [HttpGet("commands/{operation:guid}")]
    public Task<object> Original([FromHeader(Name = FinanceContext)] Guid context, Guid operation, CancellationToken ct)
        => owner.Original(context, operation, CurrentActor, ct);

    [HttpPost("commands")]
    public async Task<IActionResult> Command([FromHeader(Name = FinanceContext)] Guid context, GlCommandV1 command,
        CancellationToken ct)
    {
        try
        {
            var result = await owner.Execute(context, command, CurrentActor, ct);
            if (result is GlReceiptV1 { ErrorStatus: int status, ErrorCode: string code })
                return StatusCode(status, new { code = status, message = code, command.OperationRef });
            return Ok(result);
        }
        catch (OrderPriceCorrectionException error) when (error.Message == "IAM_OPERATION_UNKNOWN")
        {
            return Accepted(new { kind = "OUTCOME_UNKNOWN", command.OperationRef, provesPermanentNoEffect = false });
        }
    }

    [HttpPost("roots/{root:guid}/publish-preview")]
    public Task<object> Preview([FromHeader(Name = FinanceContext)] Guid context, Guid root, GlPublishPreviewV1 request,
        CancellationToken ct)
        => owner.PublishPreview(context, root, request, CurrentActor, ct);

    [HttpPost("templates/preview")]
    public Task<object> TemplatePreview([FromHeader(Name = FinanceContext)] Guid context, GlTemplatePreviewInputV1 request,
        CancellationToken ct)
        => owner.TemplatePreview(context, request, CurrentActor, ct);

    [HttpGet("templates/legacy")]
    public Task<object> Legacy([FromHeader(Name = FinanceContext)] Guid context, CancellationToken ct)
        => owner.LegacyOptions(context, CurrentActor, ct);

    [HttpGet("roles/{role}")]
    public Task<object> Role([FromHeader(Name = FinanceContext)] Guid context, string role, DateOnly? date,
        CancellationToken ct)
        => owner.Role(context, role, date, CurrentActor, ct);

    [HttpGet("roots/{root:guid}/eligibility")]
    public Task<object> Eligibility([FromHeader(Name = FinanceContext)] Guid context, Guid root, string purpose, string currency,
        DateOnly? date, CancellationToken ct)
        => owner.Eligibility(context, root, purpose, currency, date, CurrentActor, ct);

    private Task<Guid> CurrentActor(CancellationToken ct) => IamAdministrationHttpV1.CurrentActor(HttpContext, ct);
}
