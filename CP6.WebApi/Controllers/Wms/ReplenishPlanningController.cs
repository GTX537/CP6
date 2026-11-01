using CP6.Core.Services.Wms.Replenish;
using CP6.WebApi.Controllers.Erp;
using CP6.WebApi.Controllers.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Wms;

[ApiController,Authorize,Route("api/wms/replenish/v1"),OrderOwnerResult,DocumentReviewAvailability]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class ReplenishPlanningController(ReplenishPlanningOwnerV1 owner):ControllerBase
{
    [HttpGet("context")]public Task<object> Context(CancellationToken ct)=>owner.ContextAsync(ct);
    [HttpPost("simulation/observations"),RequestSizeLimit(2097152)]public Task<RepRefV1> Simulation(RepSimulationInputV1 c,CancellationToken ct)=>owner.PublishSimulationAsync(c,ct);
    [HttpPost("suggestions"),RequestSizeLimit(2097152)]public async Task<IActionResult> Suggest(RepSuggestInputV1 c,CancellationToken ct)
    {var result=await owner.SuggestAsync(c,ct);return StatusCode(owner.Replayed?200:201,result);}
    [HttpGet("suggestions/{id:guid}")]public Task<RepSuggestionV1> Suggestion(Guid id,CancellationToken ct)=>owner.SuggestionAsync(id,ct);
    [HttpGet("allocation-plans/{id:guid}")]public Task<RepPlanV1> Plan(Guid id,CancellationToken ct)=>owner.PlanAsync(id,ct);
    [HttpGet("cuts/{id:guid}")]public Task<RepCutV1> Cut(Guid id,CancellationToken ct)=>owner.CutAsync(id,ct);
    [HttpPost("batches"),RequestSizeLimit(2097152)]public async Task<IActionResult> Generate(RepBatchInputV1 c,CancellationToken ct)
    {var result=await owner.GenerateAsync(c,ct);return StatusCode(owner.Replayed?200:201,result);}
    [HttpGet("batches/{id:guid}")]public Task<RepBatchV1> Batch(Guid id,CancellationToken ct)=>owner.BatchAsync(id,ct);
    [HttpGet("orders/{id:guid}")]public Task<RepOrderV1> Order(Guid id,CancellationToken ct)=>owner.OrderAsync(id,ct);
    [HttpPost("orders"),RequestSizeLimit(2097152)]public async Task<IActionResult> CreateDraft(RepDraftInputV1 c,CancellationToken ct)
    {var result=await owner.CreateDraftAsync(c,ct);return StatusCode(owner.Replayed?200:201,result);}
    [HttpPatch("orders/{id:guid}"),RequestSizeLimit(2097152)]public Task<RepOrderV1> EditDraft(Guid id,RepDraftInputV1 c,CancellationToken ct)=>owner.EditDraftAsync(id,c,ct);
    [HttpGet("orders/{id:guid}/history")]public Task<object> History(Guid id,[FromQuery]long afterRevision=0,[FromQuery]int limit=50,CancellationToken ct=default)=>owner.OrderHistoryAsync(id,afterRevision,limit,ct);
    [HttpPost("orders/{id:guid}/prepare")]public async Task<IActionResult> Prepare(Guid id,RepCommandV1 c,CancellationToken ct)
    {var result=await owner.PrepareAsync(id,c,ct);return StatusCode(result.CanDispatch?200:202,result);}
    [HttpGet("active-intents/{id:guid}")]public Task<RepIntentV1> Intent(Guid id,CancellationToken ct)=>owner.IntentAsync(id,ct);
    [HttpGet("operations/{key}")]public Task<RepOperationV1> Operation(string key,CancellationToken ct)=>owner.OperationAsync(key,ct);
}
