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
        var effect=await db.Database.SqlQuery<DateTimeOffset?>($"""
            SELECT max((r.body::jsonb->>'occurredAt')::timestamptz) AS "Value"
            FROM cp6_native_timeout_v2.results r JOIN cp6_native_timeout_v2.claims c ON c.execution_id=r.execution_id
            JOIN cp6_native_timeout_v2.deadlines d ON d.task_id=c.task_id AND d.revision=c.revision
            WHERE d.tenant_id={db.CurrentTenantId} AND d.environment_id={fence.EnvironmentId} AND d.instance_id=ANY({ids})
              AND r.command_id IS NOT NULL AND r.body::jsonb->>'state'='COMPLETED'
            """).SingleAsync(ct);
        return new(scan==null?"UNPROVEN":"OBSERVED_RUNNING",scan,effect,pending.Length,pending.Select(x=>x.NextDue).Min(),deadlines,missing,
            await TimeoutStages(scan,effect,ct),await TimeoutRuns(ids,ct));
    }

    private sealed record RunRead(Guid RunId, Guid Version, DateTimeOffset? Slot, DateTimeOffset Evaluated,
        string Calendar, string Zone, string ZoneVersion, string MissedRun, bool IsRecovery);
    private sealed record AttemptRead(Guid RunId, Guid Id, Guid OperationId, Guid TaskId, long Epoch,
        DateTimeOffset? LeaseUntil, string? Body, Guid? CommandId, Guid[] Messages);
    private sealed record ObservationRead(long Sequence, Guid AttemptId, string State, string? Reason, DateTimeOffset At);

    private async Task<NativeTimeoutStageV2[]> TimeoutStages(DateTimeOffset? scan, DateTimeOffset? effect, CancellationToken ct)
    {
        var observedHost = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT FROM cp6_native_timeout_v2.runs WHERE tenant_id={db.CurrentTenantId}
                AND environment_id={fence.EnvironmentId} AND host_observed) AS "Value"
            """).SingleAsync(ct);
        bool? configured = null;
        if (timeoutOptions != null)
        {
            configured = timeoutOptions.Enabled && timeoutOptions.ServiceId != Guid.Empty && await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT FROM cp6_native_timeout_v2.registrations r
                  LEFT JOIN LATERAL (SELECT s.settings FROM cp6_native_timeout_v2.schedules s
                    WHERE s.tenant_id=r.tenant_id AND s.environment_id=r.environment_id AND s.service_id=r.service_id
                    ORDER BY s.sequence DESC LIMIT 1) schedule ON true
                  WHERE r.tenant_id={db.CurrentTenantId} AND r.environment_id={fence.EnvironmentId}
                    AND r.service_id={timeoutOptions.ServiceId} AND r.host_key={timeoutOptions.HostKey} AND r.enabled
                    AND COALESCE((schedule.settings->'definition'->>'enabled')::boolean,true)) AS "Value"
                """).SingleAsync(ct);
        }
        return [
            new("CODE_PRESENT", true),
            new("HOST_REGISTERED", timeoutHost != null || observedHost ? true : null),
            new("CONFIG_ENABLED", configured),
            new("OBSERVED_RUNNING", scan.HasValue, scan),
            new("LAST_EFFECT_VERIFIED", effect.HasValue, effect)
        ];
    }

    private async Task<NativeTimeoutRunV2[]> TimeoutRuns(Guid[] ids, CancellationToken ct)
    {
        var runs = await db.Database.SqlQuery<RunRead>($"""
            SELECT r.id AS "RunId",r.schedule_version AS "Version",r.slot_at AS "Slot",r.evaluated_at AS "Evaluated",
              s.calendar_version AS "Calendar",s.zone_id AS "Zone",s.zone_version AS "ZoneVersion",
              s.settings->'definition'->>'missedRun' AS "MissedRun",r.recovery AS "IsRecovery"
            FROM cp6_native_timeout_v2.runs r JOIN cp6_native_timeout_v2.schedules s
              ON (s.tenant_id,s.environment_id,s.service_id,s.version)=(r.tenant_id,r.environment_id,r.service_id,r.schedule_version)
            WHERE r.tenant_id={db.CurrentTenantId} AND r.environment_id={fence.EnvironmentId}
              AND (r.confirmed_zero OR EXISTS (
                SELECT FROM cp6_native_timeout_v2.attempts a JOIN cp6_native_timeout_v2.claims c ON c.execution_id=a.execution_id
                JOIN cp6_native_timeout_v2.deadlines d ON d.task_id=c.task_id AND d.revision=c.revision
                WHERE a.run_id=r.id AND d.tenant_id=r.tenant_id AND d.environment_id=r.environment_id AND d.instance_id=ANY({ids})))
            ORDER BY r.evaluated_at DESC,r.id DESC LIMIT 20
            """).ToArrayAsync(ct);
        var runIds = runs.Select(r => r.RunId).ToArray();
        if (runIds.Length == 0) return [];
        var attempts = await db.Database.SqlQuery<AttemptRead>($"""
            SELECT a.run_id AS "RunId",a.id AS "Id",a.execution_id AS "OperationId",c.task_id AS "TaskId",
              a.epoch AS "Epoch",CASE WHEN c.epoch=a.epoch THEN c.lease_until END AS "LeaseUntil",
              result.body AS "Body",result.command_id AS "CommandId",
              ARRAY(SELECT o.id FROM cp6_personal_form_v2.outbox o WHERE d.context_kind='SFS' AND o.command_id=result.command_id
                UNION SELECT o.id FROM cp6_native_tasks_v2.notification_outbox o WHERE d.context_kind='APP' AND o.command_id=result.command_id ORDER BY id) AS "Messages"
            FROM cp6_native_timeout_v2.attempts a JOIN cp6_native_timeout_v2.claims c ON c.execution_id=a.execution_id
            JOIN cp6_native_timeout_v2.deadlines d ON d.task_id=c.task_id AND d.revision=c.revision
            LEFT JOIN cp6_native_timeout_v2.results result ON result.execution_id=a.execution_id AND (result.body::jsonb->>'claimEpoch')::bigint=a.epoch
            WHERE a.run_id=ANY({runIds}) AND d.tenant_id={db.CurrentTenantId} AND d.environment_id={fence.EnvironmentId} AND d.instance_id=ANY({ids})
            ORDER BY a.run_id,c.task_id,a.id
            """).ToArrayAsync(ct);
        var attemptIds = attempts.Select(a => a.Id).ToArray();
        var observations = await db.Database.SqlQuery<ObservationRead>($"""
            SELECT sequence AS "Sequence",attempt_id AS "AttemptId",state AS "State",reason AS "Reason",observed_at AS "At"
            FROM cp6_native_timeout_v2.attempt_observations WHERE attempt_id=ANY({attemptIds}) ORDER BY sequence
            """).ToArrayAsync(ct);
        var byRun = attempts.ToLookup(a => a.RunId);
        var byAttempt = observations.ToLookup(o => o.AttemptId);
        var now = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        return runs.Select(r => {
            var units = byRun[r.RunId].Select(a => Unit(a, byAttempt[a.Id], now)).ToArray();
            var applied = units.Count(u => u.State == "APPLIED");
            var terminal = units.Count(u => u.State == "ALREADY_TERMINAL");
            var blocked = units.Count(u => u.State == "BLOCKED");
            var unknown = units.Count(u => u.State == "UNKNOWN");
            var failed = units.Count(u => u.State == "FAILED_BEFORE_EFFECT");
            var active = units.Count(u => u.State == "RUNNING");
            var state = applied + terminal == units.Length
                ? applied > 0 ? "COMPLETED_WITH_VERIFIED_RESULTS" : "COMPLETED_NO_CHANGES"
                : applied + terminal > 0 ? "PARTIALLY_COMPLETED"
                : unknown > 0 ? "UNKNOWN"
                : active > 0 ? "RUNNING"
                : blocked > 0 ? "BLOCKED"
                : "FAILED_BEFORE_EFFECT";
            var mode = r.MissedRun switch { "0" => "SKIP", "1" => "CATCH_UP_BOUNDED", _ => "CONTINUE_ORIGINAL" };
            return new NativeTimeoutRunV2(r.RunId, r.Version, r.Slot, r.Evaluated, r.Calendar, r.Zone, r.ZoneVersion, mode, r.IsRecovery,
                state, units.Length, applied, terminal, blocked, unknown, failed, active, units);
        }).ToArray();
    }

    private static NativeTimeoutUnitV2 Unit(AttemptRead attempt, IEnumerable<ObservationRead> events, DateTimeOffset now)
    {
        var observations = events.Select(o => new NativeTimeoutObservationV2(o.Sequence, o.State, o.Reason, o.At)).ToArray();
        // 原业务终态一旦核实，后续技术阻断保留于时间线。
        var last = observations.Any(o => o.State == "OWNER_VERIFIED") ? "OWNER_VERIFIED" : observations.LastOrDefault()?.State;
        var receipt = attempt.Body == null ? null : Parse<NativeTimeoutResultV2>(attempt.Body);
        var state = last switch
        {
            "UNKNOWN" => "UNKNOWN",
            "ISOLATED" or "FAILED_BEFORE_EFFECT" => "FAILED_BEFORE_EFFECT",
            "BLOCKED" => "BLOCKED",
            _ => receipt?.State switch
            {
                "COMPLETED" when attempt.CommandId.HasValue => "APPLIED",
                "SKIPPED_ALREADY_HANDLED" => "ALREADY_TERMINAL",
                "BLOCKED_POLICY" or "BLOCKED_TARGET" or "INVALID_POLICY" => "BLOCKED",
                _ => attempt.LeaseUntil > now ? "RUNNING" : "UNKNOWN"
            }
        };
        var showReceipt = receipt != null && last != "UNKNOWN";
        return new(attempt.Id, attempt.OperationId, attempt.TaskId, attempt.Epoch,
            showReceipt ? attempt.CommandId : null, showReceipt ? attempt.Messages : [], state, observations);
    }
}
