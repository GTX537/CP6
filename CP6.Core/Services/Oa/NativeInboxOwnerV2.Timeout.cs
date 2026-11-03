using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;
public sealed partial class NativeInboxOwnerV2
{
    private sealed record DeadlineReadRow(Guid Task,long Revision,DateTimeOffset Created,DateTimeOffset? Due,string Policy,
        string State,long Occurrence,DateTimeOffset? Next,string? Result,long? Epoch,DateTimeOffset? Lease);
    // Called only after current resource authorization, scoped to the visible graph.
    private async Task<NativeTimeoutDiagnosticsV2> TimeoutDetail(Guid[] ids,CancellationToken ct)
    {
        var rows=await db.Database.SqlQuery<DeadlineReadRow>($"""
            SELECT d.task_id AS "Task",d.revision AS "Revision",d.created_at AS "Created",d.due_at AS "Due",d.policy_json AS "Policy",
              CASE WHEN p.state IN('READY','CLAIMED') AND (t."Status"=4 OR t."Status"=0 AND i."Status"=4) THEN 'SUSPENDED'
                WHEN p.state IN('READY','CLAIMED') AND t."Status" IN(1,2,3) THEN 'ALREADY_HANDLED' ELSE p.state END AS "State",
              p.occurrence AS "Occurrence",p.next_due AS "Next",r.body AS "Result",c.epoch AS "Epoch",c.lease_until AS "Lease"
            FROM cp6_native_timeout_v2.deadlines d JOIN cp6_native_timeout_v2.pending p ON p.task_id=d.task_id AND p.revision=d.revision
            JOIN public."Wf_FlowTask" t ON t."Id"=d.task_id JOIN public."Wf_FlowInstance" i ON i."Id"=t."InstanceId"
            LEFT JOIN cp6_native_timeout_v2.claims c ON c.task_id=p.task_id AND c.revision=p.revision AND c.occurrence=p.occurrence AND p.state='CLAIMED'
            LEFT JOIN LATERAL(SELECT x.body FROM cp6_native_timeout_v2.results x JOIN cp6_native_timeout_v2.claims y ON y.execution_id=x.execution_id
              WHERE y.task_id=d.task_id AND y.revision<=d.revision ORDER BY y.revision DESC,y.occurrence DESC LIMIT 1) r ON true
            WHERE d.tenant_id={db.CurrentTenantId} AND d.environment_id={fence.EnvironmentId} AND d.instance_id=ANY({ids}) ORDER BY d.created_at,d.task_id
            """).ToArrayAsync(ct);
        var scan=await db.Database.SqlQuery<DateTimeOffset?>($"SELECT max(last_scan) AS \"Value\" FROM cp6_native_timeout_v2.observations WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId}").SingleAsync(ct);
        var deadlines=rows.Select(x=>new NativeDeadlineReadV2(x.Task,x.Revision,x.Created,x.Due,
            JsonSerializer.Deserialize<NativeDeadlinePolicyV2>(x.Policy,PersonalFormOwnerV2.Json)!,x.State,x.Occurrence,x.Next,
            x.Result==null?null:JsonSerializer.Deserialize<NativeTimeoutResultV2>(x.Result,PersonalFormOwnerV2.Json),x.Epoch,x.Lease)).ToArray();
        var pending=deadlines.Where(x=>x.State is "READY" or "CLAIMED"&&x.NextDue<=DateTimeOffset.UtcNow).ToArray();
        var known=deadlines.Select(x=>x.TaskId).ToArray();
        var missing=await db.Wf_FlowTasks.AsNoTracking().Where(t=>ids.Contains(t.InstanceId)&&t.DueAt!=null&&!known.Contains(t.Id)).Select(t=>t.Id).ToArrayAsync(ct);
        return new(scan==null?"UNPROVEN":"OBSERVED_RUNNING",scan,deadlines.Where(x=>x.LastResult?.State=="COMPLETED").Select(x=>(DateTimeOffset?)x.LastResult!.OccurredAt).Max(),
            pending.Length,pending.Select(x=>x.NextDue).Min(),deadlines,missing);
    }
}
