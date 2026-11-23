using System.Text.Json;
using System.Globalization;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public partial class FlowEngine
{
    // 只为原生新任务固定期限。旧时间及已发布原政策均保留。
    private readonly List<(Wf_FlowInstance Instance, Wf_FlowTask Task)> _deadlineTasks = [];
    private async Task SaveNativeDeadlines(CancellationToken ct)
    {
        if (_deadlineTasks.Count == 0) return;
        var at = await _db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        WorkflowDeadlineZoneV1? zone = null;
        WorkCalendarSnapshotV1? calendar = null;
        foreach (var (instance, task) in _deadlineTasks)
        {
            var publication = await ApprovalPublication(instance.FlowDefVersionId!.Value, ct);
            var node = FindNode(Deserialize(publication.Fields["SchemaJson"]!), task.NodeId)!;
            if (node.TimeoutHours == null && node.TimeoutWorkday == null && string.IsNullOrWhiteSpace(node.TimeoutAction)) continue;
            if (zone == null)
            {
                if (!await _db.Database.SqlQueryRaw<bool>("SELECT cp6_native_timeout_v2.ready() AS \"Value\"").SingleAsync(ct))
                    throw new CP6.Core.Services.Sys.ScopeAuthorizationExceptionV1("TIMEOUT_WRITER_UNAVAILABLE");
                await VersionedWorkCalendarV1.RequireCoverageAsync(_db, ct);
                zone = await WorkflowDeadlineZoneV1.ResolveAsync(_db, ct);
                await zone.RetainAsync(_db, ct);
            }
            if (node.TimeoutWorkday != null && calendar == null)
                calendar = await VersionedWorkCalendarV1.SnapshotAsync(_db, null, null, ct);
            var action = string.IsNullOrWhiteSpace(node.TimeoutAction) ? null : node.TimeoutAction.Trim().ToLowerInvariant();
            if (action == "erroredge") action = "errorEdge";
            var valid = action is "remind" or "approve" or "reject" or "escalate" or "errorEdge"
                && (action != "remind" || node.TimeoutHours is > 0);
            var instant = valid ? zone.Calculate(at, node.TimeoutHours, node.TimeoutWorkday, calendar)
                : new WorkflowDeadlineInstantV1(null, null, null, "DEADLINE_POLICY_INVALID");
            var due = instant.DueAt;
            var pinnedCalendar = node.TimeoutWorkday != null ? calendar : null;
            var policy = new NativeDeadlinePolicyV2(publication.VersionId, node.Id, node.TimeoutHours, action,
                node.EscalateTo, "NONE", pinnedCalendar == null ? "UTC_DURATION" : "WORKDAY_LOCAL",
                pinnedCalendar?.Revision.ToString(CultureInfo.InvariantCulture) ?? "NOT_APPLICABLE", zone.Id ?? "",
                instant.OffsetMinutes ?? 0, CalendarId: pinnedCalendar?.CalendarId, LocalDueAt: instant.LocalDueAt,
                TimeZoneRulesVersion: zone.Version, TimeZoneSource: zone.Rules == null ? "UNPROVEN" : "TENANT_SYSTEM_RULES",
                FailureCode: instant.FailureCode, Workday: node.TimeoutWorkday);
            // 旧时间列仅为兼容投影，扫描以版本化 UTC 事实为准。
            task.DueAt = due == null ? null : DateTime.SpecifyKind(due.Value.UtcDateTime, DateTimeKind.Unspecified);
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_native_timeout_v2.deadlines(task_id,revision,tenant_id,environment_id,instance_id,root_id,context_kind,created_at,due_at,policy_json)
                VALUES({task.Id},1,{_db.CurrentTenantId},{ManagedEnvironment},{instance.Id},{ManagedRoot},{(_personalFormRuntime ? "SFS" : "APP")},
                  {at},{due},{JsonSerializer.Serialize(policy,PersonalFormOwnerV2.Json)})
                """, ct);
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_native_timeout_v2.pending(task_id,revision,occurrence,next_due,state)
                VALUES({task.Id},1,1,{due},{(due != null ? "READY" : "INVALID_POLICY")})
                """, ct);
        }
        _deadlineTasks.Clear();
    }
}
