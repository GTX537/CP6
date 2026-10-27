using CP6.Core.Services.Wms;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Wms;
[ApiController, Authorize, OrderOwnerResult, ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ToolActualUsageController(ToolActualUsageOwnerV1 owner):ControllerBase
{
    private IActionResult Result<T>(T value){Response.Headers.CacheControl="no-store";return Ok(new{code=0,data=value});}
    private string Match=>Request.Headers.IfMatch.ToString();
    [HttpGet("api/wms/specials/v1/tools/{id:guid}/actual-usage-context")]
    public async Task<IActionResult> Context(Guid id,[FromQuery]int page=1,CancellationToken ct=default)
    {var value=await owner.ContextAsync(id,page,ct);Response.Headers.ETag=value.Etag;return Result(value);}
    [HttpGet("api/wms/specials/v1/tools/{id:guid}/actual-evidence-options")]
    public async Task<IActionResult> EvidenceOptions(Guid id,[FromQuery]string kind="USE",[FromQuery]int page=1,CancellationToken ct=default)
    {if(kind!="USE")return BadRequest(new{code="TOOL_ACTUAL_KIND_UNAVAILABLE"});return Result((await owner.ContextAsync(id,page,ct)).Evidence);}
    [HttpGet("api/wms/specials/v1/tools/{id:guid}/use-occurrence-options")]
    public async Task<IActionResult> OccurrenceOptions(Guid id,[FromQuery]string? workRef=null,[FromQuery]int page=1,CancellationToken ct=default)
    {if(workRef!=null)return StatusCode(503,new{code="WORK_TOOL_USE_OWNER_UNAVAILABLE"});return Result((await owner.ContextAsync(id,page,ct)).Occurrences);}
    [HttpGet("api/wms/specials/v1/tools/use-occurrences/{id:guid}")]
    public async Task<IActionResult> Occurrence(Guid id,[FromQuery]string? recordVersion=null,CancellationToken ct=default)=>Result(await owner.LookupAsync(id,"OCCURRENCE",recordVersion,ct));
    [HttpGet("api/wms/specials/v1/tools/observations/{id:guid}")]
    public async Task<IActionResult> Observation(Guid id,CancellationToken ct)=>Result(await owner.LookupAsync(id,"OBSERVATION",ct:ct));
    [HttpGet("api/wms/specials/v1/tools/recognition-decisions/{id:guid}")]
    public async Task<IActionResult> Decision(Guid id,CancellationToken ct)=>Result(await owner.LookupAsync(id,"RECOGNITION",ct:ct));
    [HttpGet("api/wms/specials/v1/tools/{id:guid}/actual-facts/{fact:guid}")]
    public async Task<IActionResult> Fact(Guid id,Guid fact,CancellationToken ct)=>Result(await owner.FactAsync(id,fact,ct));
    [HttpGet("api/wms/specials/v1/tools/{id:guid}/observations/{observation:guid}/application-context")]
    public async Task<IActionResult> ApplicationContext(Guid id,Guid observation,CancellationToken ct)=>Result(await owner.ApplicationContextAsync(id,observation,ct));
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/actual-evidence"),RequestSizeLimit(65536)]
    public async Task<IActionResult> Evidence(Guid id,ToolActualEvidenceCommandV1 command,CancellationToken ct)=>Result(await owner.EvidenceAsync(id,Match,command,ct));
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/use-occurrences/register"),RequestSizeLimit(16384)]
    public async Task<IActionResult> Register(Guid id,ToolActualRegisterV1 command,CancellationToken ct)=>Result(await owner.RegisterAsync(id,Match,command,ct));
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/observations"),RequestSizeLimit(16384)]
    public async Task<IActionResult> Observe(Guid id,ToolActualObservationV1 command,CancellationToken ct)=>Result(await owner.ObserveAsync(id,Match,command,ct));
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/actual-recognition-policies"),RequestSizeLimit(32768)]
    public async Task<IActionResult> Policy(Guid id,ToolActualPolicyCommandV1 command,CancellationToken ct)=>Result(await owner.PolicyAsync(id,Match,command,ct));
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/observations/{observation:guid}/recognition-decisions"),RequestSizeLimit(32768)]
    public async Task<IActionResult> Recognize(Guid id,Guid observation,ToolActualRecognitionV1 command,CancellationToken ct)=>Result(await owner.RecognizeAsync(id,observation,Match,command,ct));
    [HttpPost("api/wms/specials/v1/tools/{id:guid}/observations/{observation:guid}/apply"),RequestSizeLimit(16384)]
    public async Task<IActionResult> Apply(Guid id,Guid observation,ToolActualApplyV1 command,CancellationToken ct)=>Result(await owner.ApplyAsync(id,observation,Match,command,ct));
    [HttpGet("api/wms/tool-actual-usage/v1/operations/{id:guid}")]
    public async Task<IActionResult> Operation(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,false,ct));
    [HttpPost("api/wms/tool-actual-usage/v1/operations/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id,CancellationToken ct)=>Result(await owner.OperationAsync(id,true,ct));
}
