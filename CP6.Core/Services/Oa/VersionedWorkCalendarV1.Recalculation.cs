using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeadlineRecalculationPreviewInputV1(Guid CalendarId, long? ExpectedCalendarRevision, Guid[] TaskIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeadlineRecalculationInputV1(Guid CommandId, Guid PreviewId, Guid TaskId,
    long? ExpectedDeadlineRevision, string ExpectedTaskVersion, string ExpectedInstanceVersion, string Reason);

public sealed record DeadlineRecalculationEvidenceV1(long Revision, long Occurrence, string State, DateTimeOffset CreatedAt,
    DateTimeOffset? DueAt, DateTimeOffset? NextDue, NativeDeadlinePolicyV2 Policy);
public sealed record DeadlineRecalculationItemV1(Guid TaskId, string TaskVersion, string InstanceVersion,
    DeadlineRecalculationEvidenceV1 Before, DeadlineRecalculationEvidenceV1 After);
public sealed record DeadlineRecalculationPreviewV1(Guid PreviewId, Guid CalendarId, long CalendarRevision,
    string TimeZoneRulesVersion, DateTimeOffset ExpiresAt, DeadlineRecalculationItemV1[] Items);
public sealed record DeadlineRecalculationReceiptV1(Guid CommandId, Guid PreviewId, Guid TaskId, Guid ActorId, string Reason,
    DateTimeOffset ConfirmedAt, DeadlineRecalculationEvidenceV1 Before, DeadlineRecalculationEvidenceV1 After);

public sealed partial class VersionedWorkCalendarV1
{
    private sealed record RecalculationTask(Guid Task, Guid Instance, Guid Root, Guid Assignee, string FlowKey,
        string ContextKind, string Policy, long Revision, long Occurrence, string State,
        DateTimeOffset Created, DateTimeOffset? Due, DateTimeOffset? Next, byte[] TaskVersion, byte[] InstanceVersion);

    private sealed record PreviewRow(Guid Actor, DateTimeOffset Expires, string Body);

    public async Task<DeadlineRecalculationPreviewV1> PreviewRecalculationAsync(DeadlineRecalculationPreviewInputV1 input, CancellationToken ct)
    {
        if (input.ExpectedCalendarRevision is not >= 0 || input.TaskIds is not { Length: >= 1 and <= 50 } ||
            input.TaskIds.Contains(Guid.Empty) || input.TaskIds.Distinct().Count() != input.TaskIds.Length)
            throw Error("DEADLINE_RECALCULATION_INPUT_INVALID");
        var (authority, actor) = await WriteAuthorityAsync("Calendar.Recalculate", ct);
        var calendar = await CurrentAsync(input.CalendarId, input.ExpectedCalendarRevision.Value, ct);
        var zone = await RecalculationZoneAsync(ct);
        var items = new List<DeadlineRecalculationItemV1>(input.TaskIds.Length);
        foreach (var id in input.TaskIds.Order())
            items.Add(ProjectRecalculation(await ReadRecalculationTask(id, ct), calendar, zone));
        var now = await DatabaseNow(ct);
        var preview = new DeadlineRecalculationPreviewV1(Guid.NewGuid(), calendar.CalendarId, calendar.Revision,
            zone.Version!, now.AddMinutes(10), items.ToArray());
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_work_calendar_v1.recalculation_previews(tenant_id,environment_id,id,actor_id,expires_at,body)
            VALUES({db.CurrentTenantId},{fence.EnvironmentId},{preview.PreviewId},{actor},{preview.ExpiresAt},
              CAST({JsonSerializer.Serialize(preview, Json)} AS jsonb))
            """, ct);
        await fence.ValidateAsync(authority, ct);
        return preview;
    }

    public async Task<DeadlineRecalculationReceiptV1> RecalculateAsync(DeadlineRecalculationInputV1 command, CancellationToken ct)
    {
        if (command.CommandId == Guid.Empty || command.PreviewId == Guid.Empty || command.TaskId == Guid.Empty ||
            command.ExpectedDeadlineRevision is not > 0 || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 250)
            throw Error("DEADLINE_RECALCULATION_INPUT_INVALID");
        var (authority, actor) = await WriteAuthorityAsync("Calendar.Recalculate", ct);
        var input = Input(command);
        const string contract = "OA_DEADLINE_RECALCULATE/1";
        if (await Original<DeadlineRecalculationReceiptV1>(command.CommandId, contract, actor, input, ct) is { } original)
            return original;
        var row = await db.Database.SqlQuery<PreviewRow>($"""
            SELECT actor_id AS "Actor",expires_at AS "Expires",body::text AS "Body"
            FROM cp6_work_calendar_v1.recalculation_previews WHERE tenant_id={db.CurrentTenantId}
              AND environment_id={fence.EnvironmentId} AND id={command.PreviewId}
            """).SingleOrDefaultAsync(ct);
        if (row == null || row.Actor != actor) throw Error("DEADLINE_PREVIEW_NOT_FOUND", 404);
        var now = await DatabaseNow(ct);
        if (row.Expires <= now) throw Error("DEADLINE_PREVIEW_CHANGED", 412);
        var preview = JsonSerializer.Deserialize<DeadlineRecalculationPreviewV1>(row.Body, Json)!;
        var originalItem = preview.Items.SingleOrDefault(x => x.TaskId == command.TaskId);
        if (originalItem == null || originalItem.Before.Revision != command.ExpectedDeadlineRevision ||
            originalItem.TaskVersion != command.ExpectedTaskVersion || originalItem.InstanceVersion != command.ExpectedInstanceVersion)
            throw Error("DEADLINE_PREVIEW_CHANGED", 412);

        var calendar = await CurrentAsync(preview.CalendarId, preview.CalendarRevision, ct);
        var zone = await RecalculationZoneAsync(ct);
        if (zone.Version != preview.TimeZoneRulesVersion) throw Error("DEADLINE_PREVIEW_CHANGED", 412);
        var task = await ReadRecalculationTask(command.TaskId, ct);
        if (task.Revision != command.ExpectedDeadlineRevision || Convert.ToBase64String(task.TaskVersion) != command.ExpectedTaskVersion
            || Convert.ToBase64String(task.InstanceVersion) != command.ExpectedInstanceVersion)
            throw Error("DEADLINE_PREVIEW_CHANGED", 412);
        var replacement = ProjectRecalculation(task, calendar, zone);
        if (replacement != originalItem) throw Error("DEADLINE_PREVIEW_CHANGED", 412);
        var changed = replacement.After;
        await zone.RetainAsync(db, ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_work_calendar_v1.recalculations(tenant_id,environment_id,id,preview_id,task_id,actor_id,from_revision,to_revision)
            VALUES({db.CurrentTenantId},{fence.EnvironmentId},{command.CommandId},{command.PreviewId},{task.Task},{actor},
              {task.Revision},{changed.Revision});
            INSERT INTO cp6_native_timeout_v2.deadlines(task_id,revision,tenant_id,environment_id,instance_id,root_id,context_kind,created_at,due_at,policy_json)
            VALUES({task.Task},{changed.Revision},{db.CurrentTenantId},{fence.EnvironmentId},{task.Instance},{task.Root},
              {task.ContextKind},{task.Created},{changed.DueAt},{JsonSerializer.Serialize(changed.Policy, PersonalFormOwnerV2.Json)})
            """, ct);
        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_native_timeout_v2.pending SET revision={changed.Revision},next_due={changed.NextDue},state='READY'
            WHERE task_id={task.Task} AND revision={task.Revision} AND occurrence={task.Occurrence} AND state={task.State}
            """, ct);
        if (updated != 1) throw Error("DEADLINE_PREVIEW_CHANGED", 412);

        // 旧任务行的 DueAt 保留原投影。收件箱与 worker 共同读取新版的原生截止事实。
        await notifications.CreateOutboxAsync(task.Assignee, WfNotificationType.TodoCreated, "待办期限已重算",
            "工作日历管理员确认了新的截止时间。请查看原待办。", task.Instance, task.Task, task.FlowKey,
            "OA_DEADLINE_RECALCULATED/" + command.CommandId.ToString("D"), inAppRequested: true, emailRequested: false);
        await db.SaveChangesAsync(ct);
        var result = new DeadlineRecalculationReceiptV1(command.CommandId, command.PreviewId, task.Task, actor,
            command.Reason, now, replacement.Before, changed);
        await Journal(command.CommandId, contract, actor, input, result, [task.Task, task.Instance],
            new { command.PreviewId, command.Reason, command.ExpectedTaskVersion, command.ExpectedInstanceVersion,
                Before = replacement.Before.Revision, After = changed.Revision }, authority, ct);
        return result;
    }

    private async Task<RecalculationTask> ReadRecalculationTask(Guid task, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<RecalculationTask>($"""
            SELECT d.task_id AS "Task",d.instance_id AS "Instance",d.root_id AS "Root",t."AssigneeId" AS "Assignee",
              i."FlowKey" AS "FlowKey",d.context_kind AS "ContextKind",d.policy_json AS "Policy",p.revision AS "Revision",
              p.occurrence AS "Occurrence",p.state AS "State",d.created_at AS "Created",d.due_at AS "Due",p.next_due AS "Next",
              t."ApprovalRowVersion" AS "TaskVersion",i."RowVersion" AS "InstanceVersion"
            FROM cp6_native_timeout_v2.pending p
            JOIN cp6_native_timeout_v2.deadlines d ON (d.task_id,d.revision)=(p.task_id,p.revision)
            JOIN public."Wf_FlowTask" t ON t."Id"=d.task_id AND t."TenantId"=d.tenant_id
            JOIN public."Wf_FlowInstance" i ON i."Id"=d.instance_id AND i."TenantId"=d.tenant_id
            WHERE d.tenant_id={db.CurrentTenantId} AND d.environment_id={fence.EnvironmentId} AND d.task_id={task}
              AND p.state IN ('READY','CLAIMED','INVALID_POLICY') AND t."Status"=0 AND i."Status"=0
              AND EXISTS(SELECT FROM public."Wf_FlowToken" tok WHERE tok."TenantId"=d.tenant_id AND tok."Id"=t."TokenId"
                 AND tok."Status"=0 AND tok."InstanceId"=i."Id" AND tok."NodeId"=t."NodeId")
            """).SingleOrDefaultAsync(ct);
        return row ?? throw Error("DEADLINE_TASK_NOT_AVAILABLE", 404);
    }

    private static DeadlineRecalculationItemV1 ProjectRecalculation(RecalculationTask task,
        WorkCalendarSnapshotV1 calendar, WorkflowDeadlineZoneV1 zone)
    {
        var policy = JsonSerializer.Deserialize<NativeDeadlinePolicyV2>(task.Policy, PersonalFormOwnerV2.Json)!;
        if (policy.FlowVersionId == Guid.Empty || policy.Action is not ("remind" or "approve" or "reject" or "escalate" or "errorEdge")
            || policy.Action == "remind" && policy.Hours is not > 0)
            throw Error("DEADLINE_POLICY_INVALID");
        var instant = zone.Calculate(task.Created, policy.Hours, policy.Workday, calendar);
        if (instant.DueAt == null) throw Error(instant.FailureCode!);
        var nextPolicy = policy with
        {
            CalendarId = policy.Workday == null ? null : calendar.CalendarId,
            CalendarRevision = policy.Workday == null ? "NOT_APPLICABLE" : calendar.Revision.ToString(CultureInfo.InvariantCulture),
            TimeZoneId = zone.Id!,
            TimeZoneRulesVersion = zone.Version,
            TimeZoneSource = "TENANT_SYSTEM_RULES",
            LocalDueAt = instant.LocalDueAt,
            OffsetMinutes = instant.OffsetMinutes!.Value,
            FailureCode = null,
        };
        if (nextPolicy == policy && instant.DueAt == task.Due) throw Error("DEADLINE_RECALCULATION_NO_CHANGE");
        var before = new DeadlineRecalculationEvidenceV1(task.Revision, task.Occurrence, task.State, task.Created, task.Due, task.Next, policy);
        // 续提醒沿用已承诺的间隔和 occurrence，避免把已发提醒重新变成第一轮。
        var after = new DeadlineRecalculationEvidenceV1(task.Revision + 1, task.Occurrence, "READY", task.Created, instant.DueAt,
            task.Occurrence == 1 ? instant.DueAt : task.Next, nextPolicy);
        return new(task.Task, Convert.ToBase64String(task.TaskVersion), Convert.ToBase64String(task.InstanceVersion), before, after);
    }

    private async Task<WorkflowDeadlineZoneV1> RecalculationZoneAsync(CancellationToken ct)
    {
        var zone = await WorkflowDeadlineZoneV1.ResolveAsync(db, ct);
        return zone.Rules != null ? zone : throw Error(zone.FailureCode!);
    }

    private Task<DateTimeOffset> DatabaseNow(CancellationToken ct) =>
        db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
}
