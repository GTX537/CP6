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
    [HttpPost("simulation/planning-inputs")]public Task<SchedRefV1> PlanningInput(SchedPlanningInputV1 input,CancellationToken ct)=>owner.PublishPlanningSimulationAsync(input,ct);
    [HttpGet("arrangement-captures")]public Task<ArrangementCaptureV1> ArrangementCapture([FromQuery]Guid siteId,CancellationToken ct)=>owner.ArrangementCaptureAsync(siteId,ct);
    [HttpPost("arrangement-previews")]public Task<ArrangementPreviewV1> PrepareArrangement(ArrangementRequestV1 input,CancellationToken ct)=>owner.PrepareArrangementAsync(input,ct);
    [HttpGet("arrangement-previews/{id:guid}")]public Task<ArrangementPageV1> Arrangement(Guid id,[FromQuery]int cursor=0,[FromQuery]int pageSize=20,CancellationToken ct=default)=>owner.ArrangementPreviewAsync(id,cursor,pageSize,ct);
    [HttpPost("arrangement-previews/{id:guid}/apply")]public Task<ArrangementBatchResultV1> ApplyArrangement(Guid id,ArrangementApplyV1 input,CancellationToken ct)
    {if(id!=input.PreviewId)throw new CP6.Core.Services.Erp.OrderPriceCorrectionException("SCHED_PREVIEW_ROUTE_MISMATCH",400);return owner.ApplyArrangementAsync(input,ct);}
    [HttpGet("arrangement-batches/{id:guid}")]public Task<ArrangementBatchResultV1> Batch(Guid id,CancellationToken ct)=>owner.ArrangementBatchAsync(id,ct);
    [HttpPost("arrangement-batches/{id:guid}/continue")]public Task<ArrangementBatchResultV1> Continue(Guid id,CancellationToken ct)=>owner.ContinueArrangementAsync(id,ct);
    [HttpPost("arrangement-batches/{id:guid}/stop")]public Task<ArrangementBatchResultV1> Stop(Guid id,CancellationToken ct)=>owner.StopArrangementAsync(id,ct);
}
