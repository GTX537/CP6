using System.Text;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Erp;

[ApiController, Authorize, Route("api/main/v1/bp")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BusinessPartnerWorkspaceController(BusinessPartnerWorkspaceV1 owner) : ControllerBase
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [HttpGet("scopes")]
    public Task<IActionResult> Scopes(CancellationToken ct) => Get("SCOPES", null, null, ct);

    [HttpGet("options")]
    public Task<IActionResult> Options([FromQuery] string? legalEntityKey, CancellationToken ct) => Get("OPTIONS", null, legalEntityKey, ct);

    [HttpGet("{bp:guid}")]
    public Task<IActionResult> Detail(Guid bp, CancellationToken ct) => Get("DETAIL", bp, null, ct);

    [HttpGet("{bp:guid}/edit-context")]
    public Task<IActionResult> EditContext(Guid bp, CancellationToken ct) => Get("EDIT_CONTEXT", bp, null, ct);

    [HttpGet("{bp:guid}/status-context")]
    public Task<IActionResult> StatusContext(Guid bp, CancellationToken ct) => Get("STATUS_CONTEXT", bp, null, ct);

    [HttpGet("commands/{command:guid}")]
    public Task<IActionResult> Command(Guid command, CancellationToken ct) => Get("COMMAND_READ", command, null, ct);

    [HttpPost("query")]
    public Task<IActionResult> Query([FromBody] JsonElement body, CancellationToken ct) => Post("QUERY", body, ct);

    [HttpPost("export")]
    public Task<IActionResult> Export([FromBody] JsonElement body, CancellationToken ct) => Post("EXPORT", body, ct);

    [HttpPost("history")]
    public Task<IActionResult> History([FromBody] JsonElement body, CancellationToken ct) => Post("HISTORY", body, ct);

    [HttpPost("create-previews")]
    public Task<IActionResult> CreatePreview([FromBody] JsonElement body, CancellationToken ct) => Post("CREATE_PREVIEW", body, ct);

    [HttpPost("create")]
    public Task<IActionResult> Create([FromBody] JsonElement body, CancellationToken ct) => Post("CREATE", body, ct);

    [HttpPost("profile-previews")]
    public Task<IActionResult> ProfilePreview([FromBody] JsonElement body, CancellationToken ct) => Post("PROFILE_PREVIEW", body, ct);

    [HttpPost("profile-apply")]
    public Task<IActionResult> Profile([FromBody] JsonElement body, CancellationToken ct) => Post("PROFILE_APPLY", body, ct);

    [HttpPost("register")]
    public Task<IActionResult> Register([FromBody] JsonElement body, CancellationToken ct) => Post("REGISTER", body, ct);

    [HttpPost("hold")]
    public Task<IActionResult> Hold([FromBody] JsonElement body, CancellationToken ct) => Post("HOLD", body, ct);

    [HttpPost("retire")]
    public Task<IActionResult> Retire([FromBody] JsonElement body, CancellationToken ct) => Post("RETIRE", body, ct);

    [HttpPost("restore")]
    public Task<IActionResult> Restore([FromBody] JsonElement body, CancellationToken ct) => Post("RESTORE", body, ct);

    [HttpPost("account-probes")]
    public Task<IActionResult> AccountProbe([FromBody] JsonElement body, CancellationToken ct) =>
        Run(() => owner.ProbeMappingAccount(Preparation(body), ct));

    [HttpPost("mapping-contexts")]
    public Task<IActionResult> MappingContext([FromBody] JsonElement body, CancellationToken ct) =>
        Run(() => owner.PrepareMappingContext(Preparation(body), ct));

    [HttpPost("mapping-previews")]
    public Task<IActionResult> MappingPreview([FromBody] JsonElement body, CancellationToken ct) =>
        Run(() => owner.PrepareMappingPreview(Preparation(body), FinanceContext(), ct));

    [HttpPost("mapping-apply")]
    public Task<IActionResult> MappingApply([FromBody] JsonElement body, CancellationToken ct) =>
        Run(() => owner.ApplyMapping(body, Header("If-Match"), Header("Idempotency-Key"), FinanceContext(), ct));

    [HttpPost("mapping-decision-bindings")]
    public Task<IActionResult> MappingDecisionBinding([FromBody] JsonElement body, CancellationToken ct) =>
        Run(() => owner.BindMappingDecision(Preparation(body), ct));

    [HttpGet("mapping")]
    public Task<IActionResult> Mapping([FromQuery] string? legalEntityKey, [FromQuery] string? accountId, CancellationToken ct) =>
        Run(() => owner.ReadMapping(legalEntityKey, accountId, null, null, false, ct));

    [HttpGet("mapping-history")]
    public Task<IActionResult> MappingHistory([FromQuery] string? legalEntityKey, [FromQuery] string? accountId,
        [FromQuery] string? beforeRevision, [FromQuery] int? limit, CancellationToken ct) =>
        Run(() => owner.ReadMapping(legalEntityKey, accountId, beforeRevision, limit, true, ct));

    [HttpPost("mapping-reconcile")]
    public Task<IActionResult> MappingReconcile([FromBody] JsonElement body, CancellationToken ct) =>
        Run(() => owner.ReconcileMapping(body, Header("If-Match"), Header("Idempotency-Key"), ct));

    [HttpGet("reconcile-jobs/{id:guid}")]
    public Task<IActionResult> MappingJob(Guid id, CancellationToken ct) => Run(() => owner.ReadMappingJob(id, ct));

    [HttpPost("mapping-correction-contexts")]
    public Task<IActionResult> MappingCorrectionContext([FromBody] JsonElement body, CancellationToken ct) =>
        Run(() => owner.PrepareMappingCorrection(Preparation(body), ct));

    private JsonElement Preparation(JsonElement body)
    {
        if (Header("If-Match") is not null || Header("Idempotency-Key") is not null)
            throw new OrderPriceCorrectionException("PRECONDITION_SYNTAX", 400);
        return body;
    }

    private Task<IActionResult> Get(string operation, Guid? id, string? legal, CancellationToken ct)
        => Run(() => owner.ReadAsync(operation, id, legal, FinanceContext(), ct));

    private Task<IActionResult> Post(string operation, JsonElement body, CancellationToken ct)
        => Run(() => owner.PostAsync(operation, body, Header("If-Match"), Header("Idempotency-Key"), FinanceContext(), ct));

    private Guid? FinanceContext()
    {
        var context = Header("X-Finance-Context");
        if (context is null) return null;
        if (!Guid.TryParseExact(context, "D", out var id) || id == Guid.Empty || id.ToString("D") != context)
            throw new OrderPriceCorrectionException("BP_FINANCE_CONTEXT_INVALID", 400);
        return id;
    }

    private string? Header(string name)
    {
        if (!Request.Headers.TryGetValue(name, out var values)) return null;
        if (values.Count != 1 || string.IsNullOrEmpty(values[0]))
            throw new OrderPriceCorrectionException("PRECONDITION_SYNTAX", 400);
        return values[0];
    }

    private async Task<IActionResult> Run(Func<Task<BusinessPartnerWorkspaceV1.HttpResult>> run)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var result = await run();
            if (result.ETag is not null) Response.Headers.ETag = result.ETag;
            if (result.Body is BusinessPartnerWorkspaceV1.ExportFile csv)
            {
                Response.Headers["X-BP-Policy-Version"] = csv.PolicyVersion;
                Response.Headers["X-BP-As-Of"] = csv.AsOf.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
                HttpContext.Items[AuditEvidenceV1.PermissionVersionItem] = csv.PolicyVersion;
                HttpContext.Items[AuditEvidenceV1.SnapshotAsOfItem] = csv.AsOf;
                return File(csv.Bytes, "text/csv; charset=utf-8", "business-partners.csv");
            }
            return new JsonResult(result.Body, Wire);
        }
        catch (OrderPriceCorrectionException error)
        {
            var fields = (error.InnerException as BusinessPartnerWorkspaceV1.IssuesException)?.Details ?? Array.Empty<object>();
            var retry = error.Status switch
            {
                400 or 422 => "FIX_INPUT", 412 => "REFRESH_CONTEXT",
                409 when error.Message is "MAPPING_CONTEXT_MISMATCH" or "CORRECTION_CONTEXT_STALE" => "REFRESH_CONTEXT",
                503 when error.Message == "OPERATIONS_OUTCOME_UNKNOWN" => "QUERY_ORIGINAL",
                503 => "CONTACT_OWNER", _ => "NONE"
            };
            return Error(error.Status, error.Message, fields, retry);
        }
        catch (Exception error) when (error is JsonException or EncoderFallbackException)
        {
            return Error(400, "FIELD_INVALID", Array.Empty<object>(), "FIX_INPUT");
        }
    }

    private IActionResult Error(int status, string code, object fields, string retry)
        => new JsonResult(new { code, messageKey = code, fieldErrors = fields, commandId = (Guid?)null, retry, correlationId = Guid.NewGuid() }, Wire)
            { StatusCode = status };
}
