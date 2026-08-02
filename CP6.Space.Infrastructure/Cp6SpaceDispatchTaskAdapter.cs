using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Wms;
using CP6.Space.Application;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class Cp6SpaceDispatchTaskAdapter(
    CP6Context db,
    IWmsAccessScopeProvider accessScopes) : ISpaceDispatchTaskAdapter
{
    public const string AdapterVersion = "cp6-mobile-task-assignment-v1";
    private const string CommandName = "space-dispatch-assign";
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    public string AdapterId => AdapterVersion;

    public async Task<SpaceDispatchTaskAdapterResult> StageAssignmentsAsync(
        SpaceDispatchTaskAdapterCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ApprovalRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.WarehouseCode) ||
            string.IsNullOrWhiteSpace(command.ChangedBy) ||
            command.OccurredAtUtc.Kind != DateTimeKind.Utc ||
            command.Assignments.Count is < 1 or > 100)
        {
            throw Invalid("SPACE_DISPATCH_ADAPTER_COMMAND_INVALID");
        }

        var assignments = command.Assignments.OrderBy(value => value.Rank).ToArray();
        if (!assignments.Select(value => value.Rank)
                .SequenceEqual(assignments.Select(value => value.Rank).Distinct()) ||
            assignments.Select(value => value.TaskId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != assignments.Length ||
            assignments.Select(value => value.OperationId).Distinct().Count() != assignments.Length)
        {
            throw Invalid("SPACE_DISPATCH_ADAPTER_COMMAND_INVALID");
        }

        var operationIds = assignments.Select(value => value.OperationId).ToArray();
        if (operationIds.Any(value => value == Guid.Empty) ||
            await db.TaskCommandReceipts.AsNoTracking()
                .AnyAsync(value => operationIds.Contains(value.OperationId), cancellationToken))
        {
            throw Invalid("SPACE_DISPATCH_ADAPTER_OPERATION_USED");
        }

        var taskIds = assignments.Select(value => value.TaskId).ToArray();
        var rows = await db.MobileTasks
            .Where(value => !value.IsDeleted && taskIds.Contains(value.MobileTaskNo))
            .ToArrayAsync(cancellationToken);
        var byTask = rows.ToDictionary(
            value => value.MobileTaskNo,
            StringComparer.OrdinalIgnoreCase);
        if (byTask.Count != assignments.Length)
            throw Stale("SPACE_DISPATCH_TASK_STALE");

        var scope = await accessScopes.GetCurrentAsync(cancellationToken);
        foreach (var assignment in assignments)
        {
            if (!byTask.TryGetValue(assignment.TaskId, out var task) ||
                task.Status != MobileTaskStatus.Pending ||
                !string.IsNullOrWhiteSpace(task.AssignedTo) ||
                !string.Equals(task.WarehouseCd, command.WarehouseCode,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(task.WarehouseCd, assignment.WarehouseCode,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(task.AreaCd, assignment.AreaCode,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(task.TaskType, assignment.TaskType,
                    StringComparison.OrdinalIgnoreCase) ||
                task.ContractVersion != assignment.TaskContractVersion ||
                task.ExecutionVersion != assignment.TaskExecutionVersion ||
                !RowVersionEquals(task.RowVersion, assignment.TaskRowVersion))
            {
                throw Stale("SPACE_DISPATCH_TASK_STALE");
            }
            if (!scope.Allows(task.WarehouseCd, task.AreaCd))
                throw Invalid("SPACE_DISPATCH_TASK_SCOPE_DENIED");
            if (string.IsNullOrWhiteSpace(assignment.AssignedTo) ||
                assignment.AssignedTo.Trim().Length > 20)
            {
                throw Stale("SPACE_DISPATCH_ASSIGNEE_INVALID");
            }
        }

        var receipts = new List<SpaceDispatchTaskAdapterReceipt>(assignments.Length);
        foreach (var assignment in assignments)
        {
            var task = byTask[assignment.TaskId];
            task.AssignedTo = assignment.AssignedTo.Trim();
            task.Modifier = command.ChangedBy;
            task.ModifyDate = command.OccurredAtUtc;
            db.MobileTaskEvents.Add(new CP6.Entity.DomainModels.Wms.MobileTaskEvent
            {
                TenantId = task.TenantId,
                TaskNo = task.MobileTaskNo,
                EventType = "Assigned",
                OperationId = assignment.OperationId,
                ExecutionVersion = task.ExecutionVersion,
                UserName = command.ChangedBy,
                OccurredAt = command.OccurredAtUtc,
                DataJson = JsonSerializer.Serialize(new
                {
                    source = AdapterVersion,
                    command.ApprovalRequestId,
                    assignment.Rank,
                    assignment.PersonExternalId,
                    assignedTo = task.AssignedTo,
                }, Json),
            });
            db.TaskCommandReceipts.Add(new TaskCommandReceipt
            {
                TenantId = task.TenantId,
                OperationId = assignment.OperationId,
                TaskNo = task.MobileTaskNo,
                CommandName = CommandName,
                ResultJson = JsonSerializer.Serialize(new
                {
                    taskNo = task.MobileTaskNo,
                    assignedTo = task.AssignedTo,
                    outcome = "Applied",
                    source = AdapterVersion,
                }, Json),
                CompletedAt = command.OccurredAtUtc,
            });
            receipts.Add(new SpaceDispatchTaskAdapterReceipt(
                assignment.Rank,
                assignment.TaskId,
                assignment.PersonExternalId,
                assignment.OperationId,
                "Applied"));
        }

        return new SpaceDispatchTaskAdapterResult(AdapterVersion, receipts);
    }

    private static bool RowVersionEquals(byte[]? current, string supplied)
    {
        try
        {
            return current is { Length: > 0 } &&
                current.AsSpan().SequenceEqual(Convert.FromBase64String(supplied));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static SpaceDispatchTaskAdapterException Invalid(string code) =>
        new(code);

    private static SpaceDispatchTaskAdapterException Stale(string code) =>
        new(code, stale: true);
}
