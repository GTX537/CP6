using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;
internal sealed partial class SchedulingStoreV1
{
    internal async Task<SchedRootV1[]> Roots(Guid site,CancellationToken ct)=>(await db.Database.SqlQuery<byte[]>($"""
        SELECT body AS "Value" FROM cp6_scheduling_v1.wo_roots WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND site_id={site}
        """).ToArrayAsync(ct)).Select(Decode<SchedRootV1>).ToArray();
    internal async Task<SchedPlanningInputV1?> Planning(Guid root,CancellationToken ct)
    {var body=await db.Database.SqlQuery<byte[]>($"""
        SELECT body AS "Value" FROM cp6_scheduling_v1.planning_inputs WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND root_id={root}
        """).SingleOrDefaultAsync(ct);return body==null?null:Decode<SchedPlanningInputV1>(body);}
    internal Task SetPlanning(SchedPlanningInputV1 input,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.planning_inputs VALUES({db.CurrentTenantId},{environment},{input.RootId},{Bytes(input)})
        ON CONFLICT(tenant_id,environment_id,root_id) DO UPDATE SET body=EXCLUDED.body
        """,ct);
    internal Task SaveArrangement(Guid id,Guid site,string kind,Guid actor,object value,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.arrangement_documents VALUES({db.CurrentTenantId},{environment},{id},{site},{kind},{actor},{Bytes(value)})
        """,ct);
    internal async Task<Document> Arrangement(Guid id,string kind,CancellationToken ct)=>await db.Database.SqlQuery<Document>($"""
        SELECT id AS "Id",site_id AS "RootId",kind AS "Kind",actor_id AS "ActorId",body AS "Body" FROM cp6_scheduling_v1.arrangement_documents
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND id={id} AND kind={kind}
        """).SingleOrDefaultAsync(ct)??throw Error("SCHED_NOT_VISIBLE",404);
    internal async Task<ArrangementBatchV1?> Batch(Guid actor,Guid command,CancellationToken ct)
    {var body=await db.Database.SqlQuery<byte[]>($"""
        SELECT body AS "Value" FROM cp6_scheduling_v1.arrangement_batches WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND actor_id={actor} AND command_id={command}
        """).SingleOrDefaultAsync(ct);return body==null?null:Decode<ArrangementBatchV1>(body);}
    internal async Task<ArrangementBatchV1> BatchById(Guid id,CancellationToken ct)=>Decode<ArrangementBatchV1>(await db.Database.SqlQuery<byte[]>($"""
        SELECT body AS "Value" FROM cp6_scheduling_v1.arrangement_batches WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND batch_id={id}
        """).SingleOrDefaultAsync(ct)??throw Error("SCHED_NOT_VISIBLE",404));
    internal Task SaveBatch(ArrangementBatchV1 batch,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.arrangement_batches VALUES({db.CurrentTenantId},{environment},{batch.ActorId},{batch.Input.CommandId},{batch.BatchId},{batch.Input.PreviewId},{Bytes(batch)})
        """,ct);
    internal Task RegisterGroup(Guid preview,ArrangementGroupV1 group,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.arrangement_group_work(tenant_id,environment_id,preview_id,group_id,root_id,state)
        VALUES({db.CurrentTenantId},{environment},{preview},{group.Group.GroupId},{group.Group.RootWoId},'WAITING')
        ON CONFLICT(tenant_id,environment_id,group_id) DO NOTHING
        """,ct);
    internal sealed record GroupRow(Guid GroupId,Guid RootId,string State,string? ErrorCode,byte[]? Body);
    internal Task<GroupRow?> GroupWork(Guid group,CancellationToken ct)=>db.Database.SqlQuery<GroupRow>($"""
        SELECT group_id AS "GroupId",root_id AS "RootId",state AS "State",error_code AS "ErrorCode",body AS "Body"
        FROM cp6_scheduling_v1.arrangement_group_work WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND group_id={group}
        """).SingleOrDefaultAsync(ct);
    internal Task GroupState(Guid group,string state,string? error,RootScheduleGroupResultV1? result,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE cp6_scheduling_v1.arrangement_group_work SET state={state},error_code={error},body={(result==null?null:Bytes(result))}
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND group_id={group}
        """,ct);
}
