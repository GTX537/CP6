using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;
public sealed record SchedWorkV1(Guid WorkId,Guid PlanId,string State,Guid? OriginalCommandId,string? PayloadDigest,
    RootApplyGroupV1 RootApplyGroup,RootScheduleGroupResultV1? Result,bool CanInferNoEffect,DateTimeOffset AsOf);
public sealed partial class SchedulingOwnerV1
{
    public Task<object> WorkContextAsync(CancellationToken ct=default)=>ViewContext("work.read",ct);
    public Task<object> PreviewContextAsync(CancellationToken ct=default)=>ViewContext("preview.read",ct);
    private Task<object> ViewContext(string action,CancellationToken ct)=>Authority.ExecuteAsync<object>(OperationsOwnerV1.Planning,action,false,
        (a,t)=>Task.FromResult<object>(new{tenantId=db.CurrentTenantId,environmentId=scope.EnvironmentId,actorId=a.ActorId}),ct);
    public Task<SchedWorkV1> WorkAsync(Guid id,CancellationToken ct=default)=>ReadScheduleWork(id,"work.read",ct);
    // TIME_ONLY has one actual atomic Owner leg. Reconciliation reads its original application; it never submits a plan.
    public Task<SchedWorkV1> ReconcileWorkAsync(Guid id,CancellationToken ct=default)=>ReadScheduleWork(id,"work.reconcile",ct);
    private Task<SchedWorkV1> ReadScheduleWork(Guid id,string action,CancellationToken ct)=>Authority.ExecuteAsync(OperationsOwnerV1.Planning,action,false,async(a,t)=>
    {
        await Lease(a,false,t);var document=await db.Database.SqlQuery<SchedulingStoreV1.Document>($"""
            SELECT id AS "Id",root_id AS "RootId",kind AS "Kind",actor_id AS "ActorId",body AS "Body" FROM cp6_scheduling_v1.documents
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND actor_id={a.ActorId} AND kind='plan'
              AND convert_from(body,'UTF8')::jsonb->'rootApplyGroup'->>'groupId'={id.ToString()}
            """).SingleOrDefaultAsync(t)??throw Error("SCHED_NOT_VISIBLE",404);
        await Visible(a,document.RootId,t);var plan=Decode<SchedPlanV1>(document.Body);if(plan.Rebind!=null)throw Error("REBIND_WORK_ROUTE_REQUIRED",409);
        var applied=await Store.Applied(plan.PlanId,t);var original=await db.Database.SqlQuery<SchedulingStoreV1.CommandRow>($"""
            SELECT input_digest AS "InputDigest",plan_id AS "PlanId",body AS "Body" FROM cp6_scheduling_v1.commands
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={scope.EnvironmentId} AND actor_id={a.ActorId} AND plan_id={plan.PlanId}
              AND convert_from(body,'UTF8')::jsonb->>'replayed'='false'
            """).SingleOrDefaultAsync(t);
        return new SchedWorkV1(id,plan.PlanId,applied==null?"NOT_OBSERVED":"COMMITTED",original==null?null:Decode<SchedReplyV1>(original.Body).CommandId,
            original?.InputDigest,plan.RootApplyGroup,applied==null?null:Decode<RootScheduleGroupResultV1>(applied.Body),false,Now());
    },ct);
    private static async Task<string[]> BoardCapabilities(OperationsAccessV1 access,CancellationToken ct)
    {
        var result=new List<string>{"query"};
        foreach(var action in new[]{"plan.prepare","change.apply","work.read","work.reconcile","history.read","queue.read"})
        {
            try{await access.ActionAsync(OperationsOwnerV1.Planning,action,ct);result.Add(action);}
            catch(OrderPriceCorrectionException e)when(e.Message=="ACTION_FORBIDDEN"){}
        }
        if(result.Contains("change.apply"))
        {try{await access.ActionAsync(OperationsOwnerV1.Mes,"change.schedule",ct);}catch(OrderPriceCorrectionException e)when(e.Message=="ACTION_FORBIDDEN"){result.Remove("change.apply");}}
        return result.ToArray();
    }
}
