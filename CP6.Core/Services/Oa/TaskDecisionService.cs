using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed class TaskDecisionService : ITaskDecisionService
{
    private readonly CP6Context _db;
    private readonly IOaInstanceAccessService _access;
    private readonly IFormFieldProjectionService _projection;
    private readonly IFormService _forms;
    private readonly FlowEngine _engine;
    private readonly ICurrentPermissionContext? _permissions;

    public TaskDecisionService(
        CP6Context db, IOaInstanceAccessService access,
        IFormFieldProjectionService projection, IFormService forms, FlowEngine engine,
        ICurrentPermissionContext? permissions = null)
    {
        _db = db;
        _access = access;
        _projection = projection;
        _forms = forms;
        _engine = engine;
        _permissions = permissions;
    }

    public Task<TaskDecisionResult> DecideAsync(TaskDecisionCommand command, CancellationToken ct = default) =>
        DecideCoreAsync(command, null, ct);

    public Task<TaskDecisionResult> DecideAsync(TaskDecisionCommand command,
        Func<CancellationToken, Task<Guid>> currentActor, CancellationToken ct) =>
        DecideCoreAsync(command, currentActor, ct);

    private async Task<TaskDecisionResult> DecideCoreAsync(TaskDecisionCommand command,
        Func<CancellationToken, Task<Guid>>? currentActor, CancellationToken ct)
    {
        var postgres = _db.Database.IsNpgsql();
        if (postgres && (currentActor == null || _permissions == null || _db.Database.CurrentTransaction != null
            || System.Transactions.Transaction.Current != null || _db.ChangeTracker.HasChanges()))
            throw new IamAdministrationExceptionV1(503, "AUTHORITY_FENCE_UNAVAILABLE");

        for (var attempt = 0; ; attempt++)
        {
            var ownsTransaction = _db.Database.IsRelational() && _db.Database.CurrentTransaction == null;
            var committing = false;
            try
            {
                await using var transaction = postgres
                    ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                    : ownsTransaction ? await _db.Database.BeginTransactionAsync(ct) : null;
                try
                {
                    if (postgres)
                    {
                        await AuthenticationCompletionGateV1.AcquireAsync(_db, ct);
                        if (await currentActor!(ct) != command.ActualUserId)
                            throw new IamAdministrationExceptionV1(401, "REAUTH_REQUIRED");
                        var current = await _permissions!.GetAsync();
                        if (current.UserId != command.ActualUserId || !current.ActionKeys.Contains("oa-inbox:approve"))
                            throw new IamAdministrationExceptionV1(403, "ACTION_FORBIDDEN");
                    }

                    var result = await DecideOnceAsync(command, ct);
                    if (postgres && await currentActor!(ct) != command.ActualUserId)
                        throw new IamAdministrationExceptionV1(401, "REAUTH_REQUIRED");
                    if (transaction != null)
                    {
                        ct.ThrowIfCancellationRequested();
                        committing = true;
                        await transaction.CommitAsync(ct);
                    }
                    return result;
                }
                catch (Exception) when (!committing && transaction != null)
                {
                    try { await transaction.RollbackAsync(CancellationToken.None); }
                    catch (DbException) when (postgres)
                    { throw new IamAdministrationExceptionV1(503, "LEGACY_TASK_DECISION_OUTCOME_UNKNOWN"); }
                    throw;
                }
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2 && !committing)
            {
                _db.ChangeTracker.Clear();
                // The retry deliberately restarts at task/instance/FormData reads and repeats
                // authorization, mask projection, patching, computation and engine action.
            }
            catch (Exception error) when (postgres && (committing || error is DbException or DbUpdateException))
            {
                throw new IamAdministrationExceptionV1(503, committing
                    ? "LEGACY_TASK_DECISION_OUTCOME_UNKNOWN" : "AUTHORITY_FENCE_UNAVAILABLE");
            }
            finally
            {
                if (postgres) _db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<TaskDecisionResult> DecideOnceAsync(
        TaskDecisionCommand command, CancellationToken ct)
    {
        var task = await _db.Wf_FlowTasks.SingleOrDefaultAsync(x => x.Id == command.TaskId, ct)
            ?? throw new InvalidOperationException("E-WF-004");
        if (task.Status != FlowTaskStatus.Pending) throw new InvalidOperationException("E-WF-004");
        var instance = await _db.Wf_FlowInstances.SingleAsync(x => x.Id == task.InstanceId, ct);
        FlowEngine.RequireLegacyRuntime(instance);
        if (instance.Status != FlowInstanceStatus.Running || task.AssigneeId != command.EffectiveUserId)
            throw new InvalidOperationException("E-WF-004");
        await _access.GetAsync(command.ActualUserId, command.EffectiveUserId, instance.Id, ct);

        if (instance.FormDataId == null || instance.FormDefVersionId == null)
        {
            if (command.DataPatch.ValueKind == JsonValueKind.Object &&
                command.DataPatch.EnumerateObject().Any())
                throw new InvalidOperationException("E-WF-047");
            await _engine.ActOnceWithoutRetryAsync(
                task.Id, command.ActualUserId,
                command.ActualUserId == command.EffectiveUserId ? null : command.EffectiveUserId,
                IsApprove(command.Decision), command.Comment);
            return new(task.Id, instance.Id, task.Status, null);
        }

        var formData = await _db.Wf_FormDatas.SingleAsync(x => x.Id == instance.FormDataId, ct);
        EnsureRowVersion(formData.RowVersion, command.ExpectedFormDataRowVersion);
        if (command.ExpectedFormDataRowVersion != null)
            _db.Entry(formData).Property(x => x.RowVersion).OriginalValue = command.ExpectedFormDataRowVersion;
        if (command.DataPatch.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("E-WF-047");

        var mask = await _projection.DecisionMaskAsync(instance.Id, task.NodeId, formData.DataJson, ct);
        var data = JsonNode.Parse(formData.DataJson)?.AsObject() ?? new JsonObject();
        foreach (var property in command.DataPatch.EnumerateObject())
        {
            if (!mask.TryGetValue(property.Name, out var permission) || permission != "edit")
                throw new UnauthorizedAccessException("E-WF-042");
            data[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }

        var formVersion = await _db.Wf_FormDefVersions.AsNoTracking()
            .SingleAsync(x => x.Id == instance.FormDefVersionId, ct);
        var (normalized, errors) = _forms.RecomputeAndValidate(formVersion.SchemaJson, data.ToJsonString());
        if (errors.Count != 0) throw new InvalidOperationException("E-WF-047:" + string.Join("|", errors));
        formData.DataJson = normalized;
        formData.Modifier = command.ActualUserId.ToString();
        formData.ModifyDate = DateTime.UtcNow;
        instance.VarsJson = normalized;

        await _engine.ActOnceWithoutRetryAsync(
            task.Id, command.ActualUserId,
            command.ActualUserId == command.EffectiveUserId ? null : command.EffectiveUserId,
            IsApprove(command.Decision), command.Comment);
        return new(task.Id, instance.Id, task.Status, formData.RowVersion);
    }

    private static bool IsApprove(string decision) =>
        decision.ToLowerInvariant() switch
        {
            "approve" => true,
            "reject" => false,
            _ => throw new InvalidOperationException("E-WF-047")
        };

    private static void EnsureRowVersion(byte[]? current, byte[]? expected)
    {
        if (current != null && (expected == null || !current.SequenceEqual(expected)))
            throw new InvalidOperationException("E-WF-049");
    }
}
