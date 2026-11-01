using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;

internal sealed class SchedulingStoreV1(CP6Context db,Guid environment)
{
    internal sealed record Document(Guid Id,Guid RootId,string Kind,Guid ActorId,byte[] Body);
    internal sealed record CommandRow(string InputDigest,Guid PlanId,byte[] Body);
    internal sealed record Application(Guid ActorId,byte[] Body);
    internal Task Fence(bool write,CancellationToken ct)=>db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_scheduling_v1.wo_roots,cp6_scheduling_v1.source_heads IN "+(write?"SHARE ROW EXCLUSIVE":"SHARE")+" MODE",ct);
    internal IQueryable<Guid> RootIds(Guid? site=null)=>site==null?db.Database.SqlQuery<Guid>($"""
        SELECT root_id AS "Value" FROM cp6_scheduling_v1.wo_roots WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment}
        """):db.Database.SqlQuery<Guid>($"""
        SELECT root_id AS "Value" FROM cp6_scheduling_v1.wo_roots WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND site_id={site}
        """);
    internal async Task<SchedRootV1> Root(Guid id,CancellationToken ct)=>Decode<SchedRootV1>(await db.Database.SqlQuery<byte[]>($"""
        SELECT body AS "Value" FROM cp6_scheduling_v1.wo_roots WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND root_id={id}
        """).SingleOrDefaultAsync(ct)??throw Error("SCHED_NOT_VISIBLE",404));
    internal Task SetRoot(SchedRootV1 root,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE cp6_scheduling_v1.wo_roots SET body={Bytes(root)} WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND root_id={root.RootId}
        """,ct);
    internal async Task<SchedObservationV1> Observation(Guid id,CancellationToken ct)=>Decode<SchedObservationV1>(await db.Database.SqlQuery<byte[]>($"""
        SELECT o.body AS "Value" FROM cp6_scheduling_v1.source_heads h JOIN cp6_scheduling_v1.observations o USING(tenant_id,environment_id,root_id,cut)
        WHERE h.tenant_id={db.CurrentTenantId} AND h.environment_id={environment} AND h.root_id={id}
        """).SingleAsync(ct));
    internal async Task Publish(SchedObservationV1 input,CancellationToken ct)
    {
        var cut=Digest(input.Reference);var bytes=Bytes(input);
        var old=await db.Database.SqlQuery<byte[]>($"""
            SELECT body AS "Value" FROM cp6_scheduling_v1.observations WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND root_id={input.RootId} AND cut={cut}
            """).SingleOrDefaultAsync(ct);
        if(old!=null){if(Digest(Decode<SchedObservationV1>(old))!=Digest(input))throw Error("SCHED_SOURCE_IDENTITY_CONFLICT",409);return;}
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_scheduling_v1.observations VALUES({db.CurrentTenantId},{environment},{input.RootId},{cut},{bytes});
            INSERT INTO cp6_scheduling_v1.source_heads VALUES({db.CurrentTenantId},{environment},{input.RootId},{cut})
            ON CONFLICT(tenant_id,environment_id,root_id) DO UPDATE SET cut=EXCLUDED.cut;
            """,ct);
    }
    internal Task Save(Guid id,Guid root,string kind,Guid actor,object body,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.documents VALUES({db.CurrentTenantId},{environment},{id},{root},{kind},{actor},{Bytes(body)})
        """,ct);
    internal async Task<Document> Read(Guid id,string kind,CancellationToken ct)=>await db.Database.SqlQuery<Document>($"""
        SELECT id AS "Id",root_id AS "RootId",kind AS "Kind",actor_id AS "ActorId",body AS "Body" FROM cp6_scheduling_v1.documents
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND id={id} AND kind={kind}
        """).SingleOrDefaultAsync(ct)??throw Error("SCHED_NOT_VISIBLE",404);
    internal Task<CommandRow?> Command(Guid actor,Guid command,CancellationToken ct)=>db.Database.SqlQuery<CommandRow>($"""
        SELECT input_digest AS "InputDigest",plan_id AS "PlanId",body AS "Body" FROM cp6_scheduling_v1.commands
        WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND actor_id={actor} AND command_id={command}
        """).SingleOrDefaultAsync(ct);
    internal Task<Application?> Applied(Guid plan,CancellationToken ct)=>db.Database.SqlQuery<Application>($"""
        SELECT actor_id AS "ActorId",body AS "Body" FROM cp6_scheduling_v1.applications WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND plan_id={plan}
        """).SingleOrDefaultAsync(ct);
    internal Task SaveCommand(Guid actor,SchedApplyV1 input,SchedReplyV1 reply,CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO cp6_scheduling_v1.commands VALUES({db.CurrentTenantId},{environment},{actor},{input.CommandId},{Digest(input)},{input.PlanId},{Bytes(reply)})
        """,ct);
    internal async Task SaveApplication(SchedPlanV1 plan,Guid actor,RootScheduleGroupResultV1 result,CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_scheduling_v1.applications VALUES({db.CurrentTenantId},{environment},{plan.PlanId},{plan.RootApplyGroup.RootWoId},{result.ApplyKey},{actor},{Bytes(result)});
            INSERT INTO cp6_scheduling_v1.audit_outbox VALUES({db.CurrentTenantId},{environment},{plan.PlanId},{result.AuditRef.Id},{Bytes(new{actor,acceptedAt=Now(),plan=plan.PlanId,result,reason=plan.Input.Reason})});
            """,ct);
    }
    internal async Task<SchedRootV1> Open(SchedOpeningV1 input,string number,CancellationToken ct)
    {
        var digest=Digest(input);var old=await db.Database.SqlQuery<string>($"""
            SELECT input_digest AS "Value" FROM cp6_scheduling_v1.openings WHERE tenant_id={db.CurrentTenantId} AND environment_id={environment} AND root_id={input.RootId}
            """).SingleOrDefaultAsync(ct);
        if(old!=null){if(old!=digest)throw Error("SCHED_OPENING_CONFLICT",409);return await Root(input.RootId,ct);}
        // A freshly adopted unused scope starts with no SCHED queue memberships. No legacy queue is inferred/imported.
        var root=new SchedRootV1(input.RootId,number,input.SiteRef,input.ZoneId,input.SnapshotRef,input.Expected,input.RootSchedule,input.Operations.OrderBy(x=>x.OperationId).ToArray(),[]);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_scheduling_v1.wo_roots VALUES({db.CurrentTenantId},{environment},{root.RootId},{root.SiteRef.Id},{Bytes(root)});
            INSERT INTO cp6_scheduling_v1.openings VALUES({db.CurrentTenantId},{environment},{root.RootId},{number},{digest},{Bytes(input)});
            """,ct);
        await Publish(input.Observation,ct);return root;
    }
}
