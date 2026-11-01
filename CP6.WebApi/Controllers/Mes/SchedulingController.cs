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
    [HttpGet("board-context")]public Task<BoardContextV1> BoardContext(CancellationToken ct)=>owner.BoardContextAsync(ct);
    [HttpPost("simulation/board-sources")]public Task<SchedRefV1> BoardSource(BoardSourceV1 input,CancellationToken ct)=>owner.PublishBoardSimulationAsync(input,ct);
    [HttpGet("operations/{id:guid}")]public Task<BoardDetailV1> Operation(Guid id,CancellationToken ct)=>owner.BoardDetailAsync(id,ct);
    [HttpGet("works/{id:guid}")]public Task<SchedWorkV1> Work(Guid id,CancellationToken ct)=>owner.WorkAsync(id,ct);
    [HttpGet("work-context")]public Task<object> WorkContext(CancellationToken ct)=>owner.WorkContextAsync(ct);
    [HttpGet("preview-context")]public Task<object> PreviewContext(CancellationToken ct)=>owner.PreviewContextAsync(ct);
    [HttpPost("works/{id:guid}/reconcile")]public Task<SchedWorkV1> Reconcile(Guid id,CancellationToken ct)=>owner.ReconcileWorkAsync(id,ct);
    [HttpGet("board")]public Task<BoardPageV1> Board([FromQuery]Guid siteId,[FromQuery]string from,[FromQuery]string to,[FromQuery]string zoneId,[FromQuery]string statuses,
        [FromQuery]string? processRef=null,[FromQuery]string? workgroupRef=null,[FromQuery]string? resourceRef=null,[FromQuery]string sort="START_ASC",
        [FromQuery]Guid? captureId=null,[FromQuery]int cursor=0,[FromQuery]int pageSize=20,CancellationToken ct=default)
    {
        try
        {
            SchedRefV1? Ref(string? text)=>text==null?null:System.Text.Json.JsonSerializer.Deserialize<SchedRefV1>(text,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))??throw new System.Text.Json.JsonException();
            return owner.BoardAsync(new(siteId,new(SchedInstantConverterV1.Parse(from),SchedInstantConverterV1.Parse(to)),zoneId,Ref(processRef),Ref(workgroupRef),Ref(resourceRef),statuses.Split(','),sort),captureId,cursor,pageSize,ct);
        }
        catch(System.Text.Json.JsonException){throw SchedulingError("BOARD_OFFSET_TIME_OR_REFERENCE_INVALID");}
    }
    [HttpGet("context")]public Task<object> Context([FromQuery]Guid? siteId,CancellationToken ct)=>owner.ContextAsync(siteId,ct);
    [HttpPost("simulation/roots"),RequestSizeLimit(2097152)]public Task<SchedRootV1> Open(SchedOpeningV1 input,CancellationToken ct)=>owner.OpenSimulationAsync(input,ct);
    [HttpPost("simulation/observations"),RequestSizeLimit(2097152)]public Task<SchedRefV1> Publish(SchedObservationV1 input,CancellationToken ct)=>owner.PublishSimulationAsync(input,ct);
    [HttpGet("roots/{id:guid}/capture")]public Task<SchedCaptureV1> Capture(Guid id,CancellationToken ct)=>owner.CaptureAsync(id,ct);
    [HttpPost("change-plans")]public Task<SchedPlanV1> Prepare(SchedChangeRequestV1 input,CancellationToken ct)=>owner.PrepareAsync(input,ct);
    [HttpGet("change-plans/{id:guid}")]public Task<SchedPlanV1> Plan(Guid id,CancellationToken ct)=>owner.PlanAsync(id,ct);
    [HttpPost("change-plans/{id:guid}/apply")]public Task<object> Apply(Guid id,SchedApplyV1 input,CancellationToken ct)
    {if(id!=input.PlanId)throw new CP6.Core.Services.Erp.OrderPriceCorrectionException("SCHED_PLAN_ROUTE_MISMATCH",400);return owner.ApplyChangeAsync(input,ct);}
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
    [HttpPost("simulation/queues/{resource:guid}")]public Task<QueueHeadV1> OpenQueue(Guid resource,[FromQuery]Guid siteId,SchedRefV1 input,CancellationToken ct)
    {if(resource!=input.Id)throw SchedulingError("QUEUE_RESOURCE_ROUTE_MISMATCH");return owner.InitializeQueueSimulationAsync(siteId,input,ct);}
    [HttpGet("queue-context")]public Task<object> QueueContext(CancellationToken ct)=>owner.QueueContextAsync(ct);
    [HttpPost("simulation/queue-execution-events")]public Task<QueueExecutionResultV1> QueueExecution(QueueExecutionEventV1 input,CancellationToken ct)=>owner.PublishQueueExecutionSimulationAsync(input,ct);
    [HttpGet("resources/{resource:guid}/queue")]public Task<QueueViewV1> Queue(Guid resource,[FromQuery]Guid siteId,[FromQuery]Guid? captureId=null,[FromQuery]int cursor=0,[FromQuery]int pageSize=20,
        [FromQuery]string? windowFrom=null,[FromQuery]string? windowTo=null,CancellationToken ct=default)
    {
        if((windowFrom==null)!=(windowTo==null))throw SchedulingError("QUEUE_WINDOW_INVALID");
        SchedIntervalV1? window;
        try{window=windowFrom==null?null:new(SchedInstantConverterV1.Parse(windowFrom),SchedInstantConverterV1.Parse(windowTo!));}
        catch(System.Text.Json.JsonException){throw SchedulingError("SCHED_OFFSET_TIME_REQUIRED");}
        return owner.QueueAsync(siteId,resource,captureId,cursor,pageSize,window,ct);
    }
    [HttpPost("resources/{resource:guid}/queue-plans")]public Task<QueuePlanV1> PrepareQueue(Guid resource,QueuePlanRequestV1 input,CancellationToken ct)=>owner.PrepareQueueAsync(resource,input,ct);
    [HttpGet("queue-plans/{id:guid}")]public Task<QueuePlanV1> QueuePlan(Guid id,CancellationToken ct)=>owner.QueuePlanAsync(id,ct);
    [HttpPost("queue-plans/{id:guid}/apply")]public Task<QueueReplyV1> ApplyQueue(Guid id,SchedApplyV1 input,CancellationToken ct)
    {if(id!=input.PlanId)throw SchedulingError("QUEUE_PLAN_ROUTE_MISMATCH");return owner.ApplyQueueAsync(input,ct);}
    [HttpGet("resources/{resource:guid}/queue-history")]public Task<object[]> QueueHistory(Guid resource,[FromQuery]Guid siteId,CancellationToken ct)=>owner.QueueHistoryAsync(siteId,resource,ct);
    [HttpPost("simulation/rebind-sources")]public Task<SchedRefV1> RebindSource(RebindSimulationV1 input,CancellationToken ct)=>owner.OpenRebindSimulationAsync(input,ct);
    [HttpGet("operations/{id:guid}/rebind-context")]public Task<RebindContextV1> RebindContext(Guid id,[FromQuery]Guid rootId,CancellationToken ct)=>owner.RebindContextAsync(rootId,id,ct);
    [HttpGet("rebind-works/{id:guid}")]public Task<RebindWorkV1> RebindWork(Guid id,CancellationToken ct)=>owner.RebindWorkAsync(id,ct);
    [HttpPost("rebind-works/{id:guid}/continue")]public Task<RebindReplyV1> RebindContinue(Guid id,RebindActionV1 input,CancellationToken ct)=>owner.ContinueRebindAsync(id,input,ct);
    [HttpPost("rebind-works/{id:guid}/stop")]public Task<RebindReplyV1> RebindStop(Guid id,RebindActionV1 input,CancellationToken ct)=>owner.StopRebindAsync(id,input,ct);
    [HttpPost("rebind-works/{id:guid}/reconcile")]public Task<RebindWorkV1> RebindReconcile(Guid id,CancellationToken ct)=>owner.ReconcileRebindAsync(id,ct);
    [HttpPost("rebind-works/{id:guid}/migrate-queue")]public Task<RebindMigrationV1> RebindQueue(Guid id,CancellationToken ct)=>owner.MigrateRebindQueueAsync(id,ct);
    [HttpPost("simulation/rebind-works/{id:guid}/deliver")]public Task<RebindPhaseReceiptV1> RebindDeliver(Guid id,CancellationToken ct)=>owner.DeliverRebindSimulationAsync(id,ct);
    [HttpPost("simulation/rebind-works/{id:guid}/conserve")]public Task<RebindWorkV1> RebindConserve(Guid id,CancellationToken ct)=>owner.ConserveRebindSimulationAsync(id,ct);
    [HttpPost("simulation/rebind-works/{id:guid}/execution-resolution")]public Task<RebindExecutionV1> RebindExecutionResolution(Guid id,RebindExecutionV1 input,CancellationToken ct)=>owner.ResolveRebindExecutionSimulationAsync(id,input,ct);
    private static CP6.Core.Services.Erp.OrderPriceCorrectionException SchedulingError(string code)=>new(code,400);
}
