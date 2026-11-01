using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;
internal sealed partial class SchedulingStoreV1
{
    internal async Task<QueueHeadV1?> Queue(Guid site,Guid resource,CancellationToken ct)
    {var body=await db.Database.SqlQuery<byte[]>($"""
        SELECT body AS "Value" FROM cp6_scheduling_v1.queue_heads WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND site_id={site} AND resource_id={resource}
        """).SingleOrDefaultAsync(ct);return body==null?null:Decode<QueueHeadV1>(body);}
    internal async Task SaveQueue(QueueHeadV1 head,object audit,CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
          INSERT INTO cp6_scheduling_v1.queue_heads VALUES({db.CurrentTenantId},{environment},{head.SiteId},{head.ResourceRef.Id},{head.QueueId},{Bytes(head)})
          ON CONFLICT(tenant_id,environment_id,site_id,resource_id) DO UPDATE SET body=EXCLUDED.body;
          INSERT INTO cp6_scheduling_v1.queue_revisions VALUES({db.CurrentTenantId},{environment},{head.QueueId},{head.Revision},{Bytes(head)},{Bytes(audit)});
          """,ct);
        var reference=Ref("SCHED-QUEUE",head.QueueId,head.Revision,head);
        // Queue versions are part of the already adopted complete Root schedule context, without changing WO domains.
        foreach(var root in await Roots(head.SiteId,ct))
        if(root.QueueHeads.Any(x=>x.Id==head.QueueId)||head.Members.Any(x=>x.RootId==root.RootId))
            await SetRoot(root with{QueueHeads=root.QueueHeads.Where(x=>x.Id!=head.QueueId).Append(reference).OrderBy(x=>x.Id).ToArray()},ct);
    }
    internal Task<CommandRow?> QueueCommand(Guid actor,Guid id,CancellationToken ct)=>db.Database.SqlQuery<CommandRow>($"""
        SELECT input_digest AS "InputDigest",plan_id AS "PlanId",body AS "Body" FROM cp6_scheduling_v1.queue_commands
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND actor_id={actor} AND command_id={id}
        """).SingleOrDefaultAsync(ct);
    internal Task SaveQueueCommand(Guid actor,SchedApplyV1 input,QueueReplyV1 reply,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.queue_commands VALUES({db.CurrentTenantId},{environment},{actor},{input.CommandId},{Digest(input)},{input.PlanId},{Bytes(reply)})
        """,ct);
    internal async Task<QueueReplyV1?> QueueApplied(Guid id,CancellationToken ct)
    {var b=await db.Database.SqlQuery<byte[]>($"""
        SELECT body AS "Value" FROM cp6_scheduling_v1.queue_applications WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND plan_id={id}
        """).SingleOrDefaultAsync(ct);return b==null?null:Decode<QueueReplyV1>(b);}
    internal Task SaveQueueApplied(Guid id,QueueReplyV1 reply,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.queue_applications VALUES({db.CurrentTenantId},{environment},{id},{Bytes(reply)})
        """,ct);
    internal async Task<QueueExecutionEventV1?> Execution(Guid root,Guid op,CancellationToken ct)
    {var b=await db.Database.SqlQuery<byte[]>($"""
        SELECT body AS "Value" FROM cp6_scheduling_v1.queue_execution_heads WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND root_id={root} AND operation_id={op}
        """).SingleOrDefaultAsync(ct);return b==null?null:Decode<QueueExecutionEventV1>(b);}
    internal async Task<string?> ExecutionDigest(SchedRefV1 fact,CancellationToken ct)=>await db.Database.SqlQuery<string>($"""
        SELECT input_digest AS "Value" FROM cp6_scheduling_v1.queue_execution_events WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND owner={fact.Owner} AND fact_id={fact.Id} AND version={fact.Version}
        """).SingleOrDefaultAsync(ct);
    internal async Task<QueueExecutionEventV1?> ExecutionStream(SchedRefV1 fact,CancellationToken ct)
    {var b=await db.Database.SqlQuery<byte[]>($"""
        SELECT body AS "Value" FROM cp6_scheduling_v1.queue_execution_events WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND owner={fact.Owner} AND fact_id={fact.Id} LIMIT 1
        """).SingleOrDefaultAsync(ct);return b==null?null:Decode<QueueExecutionEventV1>(b);}
    internal async Task SaveExecution(QueueExecutionEventV1 e,bool advance,CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.queue_execution_events VALUES({db.CurrentTenantId},{environment},{e.FactRef.Owner},{e.FactRef.Id},{e.FactRef.Version},{Digest(e)},{Bytes(e)});
        """,ct);
        if(advance)await db.Database.ExecuteSqlInterpolatedAsync($"""
              INSERT INTO cp6_scheduling_v1.queue_execution_heads VALUES({db.CurrentTenantId},{environment},{e.RootId},{e.OperationId},{Bytes(e)})
              ON CONFLICT(tenant_id,environment_id,root_id,operation_id) DO UPDATE SET body=EXCLUDED.body;
              """,ct);
    }
}
