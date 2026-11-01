using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;
internal sealed partial class SchedulingStoreV1
{
    internal async Task<RebindSimulationV1?> RebindSource(Guid assignment,CancellationToken ct)
    {var b=await db.Database.SqlQuery<byte[]>($"""SELECT body AS "Value" FROM cp6_scheduling_v1.rebind_sources WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND assignment_id={assignment}""").SingleOrDefaultAsync(ct);return b==null?null:Decode<RebindSimulationV1>(b);}
    internal Task SaveRebindSource(Guid assignment,RebindSimulationV1 input,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.rebind_sources VALUES({db.CurrentTenantId},{environment},{assignment},{Bytes(input)})
        """,ct);
    internal async Task<RebindWorkV1> RebindWork(Guid id,CancellationToken ct)=>Decode<RebindWorkV1>(await db.Database.SqlQuery<byte[]>($"""
        SELECT body AS "Value" FROM cp6_scheduling_v1.rebind_works WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND work_id={id}
        """).SingleOrDefaultAsync(ct)??throw Error("SCHED_NOT_VISIBLE",404));
    internal Task SaveRebindWork(RebindWorkV1 w,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.rebind_works VALUES({db.CurrentTenantId},{environment},{w.WorkId},{w.PlanId},{w.RootId},{Bytes(w)})
        ON CONFLICT(tenant_id,environment_id,work_id) DO UPDATE SET body=EXCLUDED.body
        """,ct);
    internal Task<CommandRow?> RebindCommand(Guid actor,Guid command,CancellationToken ct)=>db.Database.SqlQuery<CommandRow>($"""
        SELECT input_digest AS "InputDigest",plan_id AS "PlanId",body AS "Body" FROM cp6_scheduling_v1.rebind_commands
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND actor_id={actor} AND command_id={command}
        """).SingleOrDefaultAsync(ct);
    internal Task SaveRebindCommand(Guid actor,Guid command,object input,RebindReplyV1 reply,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.rebind_commands VALUES({db.CurrentTenantId},{environment},{actor},{command},{Digest(input)},{reply.Work.PlanId},{Bytes(reply)})
        """,ct);
    internal Task SaveRebindRequest(RebindPhaseRequestV1 r,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.rebind_requests VALUES({db.CurrentTenantId},{environment},{r.WorkId},{r.Phase},{r.Key},{Bytes(r)})
        """,ct);
    internal async Task<RebindPhaseRequestV1> RebindRequest(Guid id,string phase,CancellationToken ct)=>Decode<RebindPhaseRequestV1>(await db.Database.SqlQuery<byte[]>($"""
        SELECT body AS "Value" FROM cp6_scheduling_v1.rebind_requests WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND work_id={id} AND phase={phase}
        """).SingleAsync(ct));
    internal async Task<RebindPhaseReceiptV1?> RebindReceipt(Guid id,string phase,CancellationToken ct)
    {var b=await db.Database.SqlQuery<byte[]>($"""SELECT body AS "Value" FROM cp6_scheduling_v1.rebind_receipts WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND work_id={id} AND phase={phase}""").SingleOrDefaultAsync(ct);return b==null?null:Decode<RebindPhaseReceiptV1>(b);}
    internal Task SaveRebindReceipt(Guid work,RebindPhaseReceiptV1 r,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.rebind_receipts VALUES({db.CurrentTenantId},{environment},{work},{r.Phase},{Bytes(r)})
        """,ct);
    internal async Task<RebindClaimV1?> RebindClaim(Guid id,CancellationToken ct)
    {var b=await db.Database.SqlQuery<byte[]>($"""SELECT body AS "Value" FROM cp6_scheduling_v1.rebind_claims WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND claim_id={id}""").SingleOrDefaultAsync(ct);return b==null?null:Decode<RebindClaimV1>(b);}
    internal Task SaveRebindClaim(RebindClaimV1 c,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.rebind_claims VALUES({db.CurrentTenantId},{environment},{c.Reference.Id},{Bytes(c)})
        ON CONFLICT(tenant_id,environment_id,claim_id) DO UPDATE SET body=EXCLUDED.body
        """,ct);
    internal async Task<RebindExecutionV1> RebindExecution(Guid id,CancellationToken ct)=>Decode<RebindExecutionV1>(await db.Database.SqlQuery<byte[]>($"""
        SELECT body AS "Value" FROM cp6_scheduling_v1.rebind_executions WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND work_ref_id={id}
        """).SingleAsync(ct));
    internal Task SaveRebindExecution(Guid assignment,RebindExecutionV1 e,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.rebind_executions VALUES({db.CurrentTenantId},{environment},{e.WorkRef.Id},{assignment},{Bytes(e)})
        ON CONFLICT(tenant_id,environment_id,work_ref_id) DO UPDATE SET body=EXCLUDED.body
        """,ct);
    internal Task SetRebindFence(RebindWorkV1 w,bool stopped,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.rebind_fences VALUES({db.CurrentTenantId},{environment},{w.RootId},{w.OperationId},{w.WorkId},true,{stopped})
        ON CONFLICT(tenant_id,environment_id,work_id) DO UPDATE SET commit_stopped=cp6_scheduling_v1.rebind_fences.commit_stopped OR EXCLUDED.commit_stopped
        """,ct);
    internal Task<bool> RebindStopped(Guid work,CancellationToken ct)=>db.Database.SqlQuery<bool>($"""
        SELECT commit_stopped AS "Value" FROM cp6_scheduling_v1.rebind_fences WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND work_id={work}
        """).SingleOrDefaultAsync(ct);
    internal async Task<RebindMigrationV1?> RebindMigration(Guid work,CancellationToken ct)
    {var b=await db.Database.SqlQuery<byte[]>($"""SELECT body AS "Value" FROM cp6_scheduling_v1.rebind_migrations WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND work_id={work}""").SingleOrDefaultAsync(ct);return b==null?null:Decode<RebindMigrationV1>(b);}
    internal async Task<RebindMigrationV1[]> RebindMigrations(CancellationToken ct)=>(await db.Database.SqlQuery<byte[]>($"""
        SELECT body AS "Value" FROM cp6_scheduling_v1.rebind_migrations WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment}
        """).ToArrayAsync(ct)).Select(Decode<RebindMigrationV1>).ToArray();
    internal Task SaveRebindMigration(RebindMigrationV1 m,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.rebind_migrations VALUES({db.CurrentTenantId},{environment},{m.WorkId},{m.RebindKey},{m.NewAssignment.Id},{Bytes(m)})
        ON CONFLICT(tenant_id,environment_id,work_id) DO UPDATE SET body=EXCLUDED.body
        """,ct);
}
