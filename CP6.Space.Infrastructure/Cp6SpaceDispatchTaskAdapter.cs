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
    private const string CompensationCommandName = "space-dispatch-unassign";
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
        if (operationIds.Any(value => value == Guid.Empty))
            throw Invalid("SPACE_DISPATCH_ADAPTER_COMMAND_INVALID");
        var replay = await TryReplayAsync(
            assignments,
            operationIds,
            CommandName,
            "Applied",
            cancellationToken);
        if (replay is not null)
            return new SpaceDispatchTaskAdapterResult(AdapterVersion, replay);

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

    public async Task<SpaceDispatchTaskAdapterResult> StageCompensationAsync(
        SpaceDispatchTaskCompensationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ApprovalRequestId == Guid.Empty ||
            command.ActionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(command.WarehouseCode) ||
            string.IsNullOrWhiteSpace(command.ChangedBy) ||
            command.OccurredAtUtc.Kind != DateTimeKind.Utc ||
            command.Assignments.Count is < 1 or > 100)
        {
            throw Invalid("SPACE_DISPATCH_COMPENSATION_COMMAND_INVALID");
        }

        var assignments = command.Assignments.OrderBy(value => value.Rank).ToArray();
        if (!assignments.Select(value => value.Rank)
                .SequenceEqual(assignments.Select(value => value.Rank).Distinct()) ||
            assignments.Select(value => value.TaskId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != assignments.Length ||
            assignments.Select(value => value.AssignmentOperationId).Distinct().Count() !=
                assignments.Length ||
            assignments.Select(value => value.CompensationOperationId).Distinct().Count() !=
                assignments.Length ||
            assignments.Any(value => value.AssignmentOperationId == Guid.Empty ||
                value.CompensationOperationId == Guid.Empty))
        {
            throw Invalid("SPACE_DISPATCH_COMPENSATION_COMMAND_INVALID");
        }

        var compensationOperationIds = assignments
            .Select(value => value.CompensationOperationId)
            .ToArray();
        var replay = await TryReplayCompensationAsync(
            assignments,
            compensationOperationIds,
            cancellationToken);
        if (replay is not null)
            return new SpaceDispatchTaskAdapterResult(AdapterVersion, replay);

        await EnsureOriginalAssignmentReceiptsAsync(assignments, cancellationToken);

        var taskIds = assignments.Select(value => value.TaskId).ToArray();
        var rows = await db.MobileTasks
            .Where(value => !value.IsDeleted && taskIds.Contains(value.MobileTaskNo))
            .ToArrayAsync(cancellationToken);
        var byTask = rows.ToDictionary(
            value => value.MobileTaskNo,
            StringComparer.OrdinalIgnoreCase);
        if (byTask.Count != assignments.Length)
            throw Stale("SPACE_DISPATCH_COMPENSATION_TASK_MISSING");

        var scope = await accessScopes.GetCurrentAsync(cancellationToken);
        foreach (var assignment in assignments)
        {
            if (!byTask.TryGetValue(assignment.TaskId, out var task) ||
                task.Status != MobileTaskStatus.Pending ||
                !string.Equals(task.AssignedTo, assignment.AssignedTo,
                    StringComparison.OrdinalIgnoreCase) ||
                task.ExecutionVersion != assignment.TaskExecutionVersion ||
                task.StartedAt.HasValue ||
                task.DoneAt.HasValue ||
                !string.Equals(task.WarehouseCd, command.WarehouseCode,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(task.WarehouseCd, assignment.WarehouseCode,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(task.AreaCd, assignment.AreaCode,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(task.TaskType, assignment.TaskType,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw Stale("SPACE_DISPATCH_COMPENSATION_NOT_SAFE");
            }
            if (!scope.Allows(task.WarehouseCd, task.AreaCd))
                throw Invalid("SPACE_DISPATCH_TASK_SCOPE_DENIED");
        }

        var receipts = new List<SpaceDispatchTaskAdapterReceipt>(assignments.Length);
        foreach (var assignment in assignments)
        {
            var task = byTask[assignment.TaskId];
            var previousAssignedTo = task.AssignedTo!;
            task.AssignedTo = null;
            task.Modifier = command.ChangedBy;
            task.ModifyDate = command.OccurredAtUtc;
            db.MobileTaskEvents.Add(new CP6.Entity.DomainModels.Wms.MobileTaskEvent
            {
                TenantId = task.TenantId,
                TaskNo = task.MobileTaskNo,
                EventType = "AssignmentCompensated",
                OperationId = assignment.CompensationOperationId,
                ExecutionVersion = task.ExecutionVersion,
                UserName = command.ChangedBy,
                OccurredAt = command.OccurredAtUtc,
                DataJson = JsonSerializer.Serialize(new
                {
                    source = AdapterVersion,
                    command.ApprovalRequestId,
                    command.ActionId,
                    assignment.Rank,
                    assignment.PersonExternalId,
                    previousAssignedTo,
                }, Json),
            });
            db.TaskCommandReceipts.Add(new TaskCommandReceipt
            {
                TenantId = task.TenantId,
                OperationId = assignment.CompensationOperationId,
                TaskNo = task.MobileTaskNo,
                CommandName = CompensationCommandName,
                ResultJson = JsonSerializer.Serialize(new
                {
                    taskNo = task.MobileTaskNo,
                    assignedTo = (string?)null,
                    previousAssignedTo,
                    outcome = "Compensated",
                    source = AdapterVersion,
                }, Json),
                CompletedAt = command.OccurredAtUtc,
            });
            receipts.Add(new SpaceDispatchTaskAdapterReceipt(
                assignment.Rank,
                assignment.TaskId,
                assignment.PersonExternalId,
                assignment.CompensationOperationId,
                "Compensated"));
        }

        return new SpaceDispatchTaskAdapterResult(AdapterVersion, receipts);
    }

    private async Task<IReadOnlyList<SpaceDispatchTaskAdapterReceipt>?> TryReplayAsync(
        IReadOnlyList<SpaceDispatchTaskAssignmentCommand> assignments,
        Guid[] operationIds,
        string commandName,
        string expectedOutcome,
        CancellationToken cancellationToken)
    {
        var previous = await db.TaskCommandReceipts.AsNoTracking()
            .Where(value => operationIds.Contains(value.OperationId))
            .ToArrayAsync(cancellationToken);
        if (previous.Length == 0) return null;
        if (previous.Length != assignments.Count)
            throw Invalid("SPACE_DISPATCH_ADAPTER_RECEIPT_PARTIAL");

        var byOperation = previous.ToDictionary(value => value.OperationId);
        var replay = new List<SpaceDispatchTaskAdapterReceipt>(assignments.Count);
        foreach (var assignment in assignments)
        {
            if (!byOperation.TryGetValue(assignment.OperationId, out var receipt) ||
                !string.Equals(receipt.TaskNo, assignment.TaskId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(receipt.CommandName, commandName,
                    StringComparison.Ordinal))
            {
                throw Invalid("SPACE_DISPATCH_ADAPTER_RECEIPT_CONFLICT");
            }
            var payload = ParseReceipt(receipt.ResultJson);
            if (!string.Equals(payload.TaskNo, assignment.TaskId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(payload.AssignedTo, assignment.AssignedTo,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(payload.Outcome, expectedOutcome,
                    StringComparison.Ordinal) ||
                !string.Equals(payload.Source, AdapterVersion,
                    StringComparison.Ordinal))
            {
                throw Invalid("SPACE_DISPATCH_ADAPTER_RECEIPT_CONFLICT");
            }
            replay.Add(new SpaceDispatchTaskAdapterReceipt(
                assignment.Rank,
                assignment.TaskId,
                assignment.PersonExternalId,
                assignment.OperationId,
                expectedOutcome));
        }
        return replay;
    }

    private async Task<IReadOnlyList<SpaceDispatchTaskAdapterReceipt>?>
        TryReplayCompensationAsync(
            IReadOnlyList<SpaceDispatchTaskCompensationItem> assignments,
            Guid[] operationIds,
            CancellationToken cancellationToken)
    {
        var previous = await db.TaskCommandReceipts.AsNoTracking()
            .Where(value => operationIds.Contains(value.OperationId))
            .ToArrayAsync(cancellationToken);
        if (previous.Length == 0) return null;
        if (previous.Length != assignments.Count)
            throw Invalid("SPACE_DISPATCH_COMPENSATION_RECEIPT_PARTIAL");

        var byOperation = previous.ToDictionary(value => value.OperationId);
        var replay = new List<SpaceDispatchTaskAdapterReceipt>(assignments.Count);
        foreach (var assignment in assignments)
        {
            if (!byOperation.TryGetValue(assignment.CompensationOperationId,
                    out var receipt) ||
                !string.Equals(receipt.TaskNo, assignment.TaskId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(receipt.CommandName, CompensationCommandName,
                    StringComparison.Ordinal))
            {
                throw Invalid("SPACE_DISPATCH_COMPENSATION_RECEIPT_CONFLICT");
            }
            var payload = ParseReceipt(receipt.ResultJson);
            if (!string.Equals(payload.TaskNo, assignment.TaskId,
                    StringComparison.OrdinalIgnoreCase) ||
                payload.AssignedTo is not null ||
                !string.Equals(payload.PreviousAssignedTo, assignment.AssignedTo,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(payload.Outcome, "Compensated",
                    StringComparison.Ordinal) ||
                !string.Equals(payload.Source, AdapterVersion,
                    StringComparison.Ordinal))
            {
                throw Invalid("SPACE_DISPATCH_COMPENSATION_RECEIPT_CONFLICT");
            }
            replay.Add(new SpaceDispatchTaskAdapterReceipt(
                assignment.Rank,
                assignment.TaskId,
                assignment.PersonExternalId,
                assignment.CompensationOperationId,
                "Compensated"));
        }
        return replay;
    }

    private async Task EnsureOriginalAssignmentReceiptsAsync(
        IReadOnlyList<SpaceDispatchTaskCompensationItem> assignments,
        CancellationToken cancellationToken)
    {
        var operationIds = assignments.Select(value => value.AssignmentOperationId)
            .ToArray();
        var previous = await db.TaskCommandReceipts.AsNoTracking()
            .Where(value => operationIds.Contains(value.OperationId))
            .ToArrayAsync(cancellationToken);
        if (previous.Length != assignments.Count)
            throw Invalid("SPACE_DISPATCH_ASSIGNMENT_RECEIPT_MISSING");
        var byOperation = previous.ToDictionary(value => value.OperationId);
        foreach (var assignment in assignments)
        {
            if (!byOperation.TryGetValue(assignment.AssignmentOperationId,
                    out var receipt) ||
                !string.Equals(receipt.TaskNo, assignment.TaskId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(receipt.CommandName, CommandName,
                    StringComparison.Ordinal))
            {
                throw Invalid("SPACE_DISPATCH_ASSIGNMENT_RECEIPT_CONFLICT");
            }
            var payload = ParseReceipt(receipt.ResultJson);
            if (!string.Equals(payload.TaskNo, assignment.TaskId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(payload.AssignedTo, assignment.AssignedTo,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(payload.Outcome, "Applied",
                    StringComparison.Ordinal) ||
                !string.Equals(payload.Source, AdapterVersion,
                    StringComparison.Ordinal))
            {
                throw Invalid("SPACE_DISPATCH_ASSIGNMENT_RECEIPT_CONFLICT");
            }
        }
    }

    private static DispatchReceiptPayload ParseReceipt(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DispatchReceiptPayload>(json, Json)
                ?? throw new JsonException();
        }
        catch (JsonException)
        {
            throw Invalid("SPACE_DISPATCH_ADAPTER_RECEIPT_INVALID");
        }
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

    private sealed record DispatchReceiptPayload(
        string TaskNo,
        string? AssignedTo,
        string? PreviousAssignedTo,
        string Outcome,
        string Source);
}
