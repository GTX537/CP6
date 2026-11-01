using CP6.Core.Services.Mes.Scheduling;
using CP6.WebApi.Controllers.Erp;
using CP6.WebApi.Controllers.Plm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CP6.WebApi.Controllers.Mes;
[ApiController,Authorize,Route("api/mes/scheduling"),OrderOwnerResult,DocumentReviewAvailability]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class SchedulingController(SchedulingOwnerV1 owner):ControllerBase
{
    [HttpGet("context")]public Task<object> Context([FromQuery]Guid? siteId,CancellationToken ct)=>owner.ContextAsync(siteId,ct);
    [HttpPost("simulation/roots"),RequestSizeLimit(2097152)]public Task<SchedRootV1> Open(SchedOpeningV1 input,CancellationToken ct)=>owner.OpenSimulationAsync(input,ct);
    [HttpPost("simulation/observations"),RequestSizeLimit(2097152)]public Task<SchedRefV1> Publish(SchedObservationV1 input,CancellationToken ct)=>owner.PublishSimulationAsync(input,ct);
    [HttpGet("roots/{id:guid}/capture")]public Task<SchedCaptureV1> Capture(Guid id,CancellationToken ct)=>owner.CaptureAsync(id,ct);
    [HttpPost("change-plans")]public Task<SchedPlanV1> Prepare(SchedChangeRequestV1 input,CancellationToken ct)=>owner.PrepareAsync(input,ct);
    [HttpGet("change-plans/{id:guid}")]public Task<SchedPlanV1> Plan(Guid id,CancellationToken ct)=>owner.PlanAsync(id,ct);
    [HttpPost("change-plans/{id:guid}/apply")]public Task<SchedReplyV1> Apply(Guid id,SchedApplyV1 input,CancellationToken ct)
    {if(id!=input.PlanId)throw new CP6.Core.Services.Erp.OrderPriceCorrectionException("SCHED_PLAN_ROUTE_MISMATCH",400);return owner.ApplyAsync(input,ct);}
    [HttpGet("commands/{id:guid}")]public Task<object> Command(Guid id,CancellationToken ct)=>owner.CommandAsync(id,ct);
    [HttpGet("roots/{id:guid}/history")]public Task<object[]> History(Guid id,CancellationToken ct)=>owner.HistoryAsync(id,ct);
}
