using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public partial class FlowEngine
{
    // Only new native assignments enter this list. Historical local DueAt values are
    // never guessed into UTC, and changing a published head cannot change a deadline.
    private readonly List<(Wf_FlowInstance Instance, Wf_FlowTask Task)> _deadlineTasks = [];
    private async Task SaveNativeDeadlines(CancellationToken ct)
    {
        if (_deadlineTasks.Count == 0) return;
        var at = await _db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
        string? zoneId = null;
        TimeZoneInfo? zone = null;
        foreach (var (instance, task) in _deadlineTasks)
        {
            var publication = await ApprovalPublication(instance.FlowDefVersionId!.Value, ct);
            var node = FindNode(Deserialize(publication.Fields["SchemaJson"]!), task.NodeId)!;
            if (node.TimeoutHours == null && string.IsNullOrWhiteSpace(node.TimeoutAction)) continue;
            if (zone == null)
            {
                if (!await _db.Database.SqlQueryRaw<bool>("SELECT cp6_native_timeout_v2.ready() AS \"Value\"").SingleAsync(ct))
                    throw new CP6.Core.Services.Sys.ScopeAuthorizationExceptionV1("TIMEOUT_WRITER_UNAVAILABLE");
                zoneId = await _db.Sys_Tenants.Where(t => t.Id == _db.CurrentTenantId).Select(t => t.TimeZoneId).SingleAsync(ct);
                zone = NativeQueryDatesV2.Zone(zoneId);
            }
            var action = node.TimeoutAction?.Trim().ToLowerInvariant();
            if (action == "erroredge") action = "errorEdge";
            var valid = node.TimeoutHours is > 0 && action is "remind" or "approve" or "reject" or "escalate" or "errorEdge";
            DateTimeOffset? due = null;
            if (valid)
            {
                try { due = at.AddHours(node.TimeoutHours!.Value); }
                catch (ArgumentOutOfRangeException) { valid = false; } // Explicit INVALID_POLICY, never an invented due date.
            }
            var policy = new NativeDeadlinePolicyV2(publication.VersionId, node.Id, node.TimeoutHours, action,
                node.EscalateTo, "NONE", "UTC_DURATION", "NOT_APPLICABLE", zoneId!, (int)zone.GetUtcOffset(at).TotalMinutes);
            // The legacy timestamp column is a compatibility projection for these new
            // native rows. The versioned timestamptz fact is the scheduling authority.
            task.DueAt = due == null ? null : DateTime.SpecifyKind(due.Value.UtcDateTime, DateTimeKind.Unspecified);
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_native_timeout_v2.deadlines(task_id,revision,tenant_id,environment_id,instance_id,root_id,context_kind,created_at,due_at,policy_json)
                VALUES({task.Id},1,{_db.CurrentTenantId},{ManagedEnvironment},{instance.Id},{ManagedRoot},{(_personalFormRuntime ? "SFS" : "APP")},
                  {at},{due},{JsonSerializer.Serialize(policy,PersonalFormOwnerV2.Json)})
                """, ct);
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_native_timeout_v2.pending(task_id,revision,occurrence,next_due,state)
                VALUES({task.Id},1,1,{due},{(valid ? "READY" : "INVALID_POLICY")})
                """, ct);
        }
        _deadlineTasks.Clear();
    }
}
