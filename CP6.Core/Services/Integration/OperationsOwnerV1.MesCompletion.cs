using System.Data;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Integration;

public sealed partial class OperationsOwnerV1
{
    internal void StageMesCompletion(Guid workOrderId, OperationsDelegationV1 completion)
    {
        var sessions = completionSessions ?? throw new OrderPriceCorrectionException("FINANCE_OWNER_REQUIRED", 503);
        var id = completion.CompletionResultId!.Value;
        var evt = new IntegrationEvent
        {
            Id = id, CorrelationId = id, TenantId = db.CurrentTenantId,
            SourceModule = "MES", TargetModule = "FIN", HookName = MesCompletionFinanceIntentV1.Hook,
            SourceNo = completion.Number, Creator = completion.Actor, CreateDate = DateTime.Now, NextRetryAt = DateTime.UtcNow,
        };
        new MesCompletionFinanceIntentV1(workOrderId, completion.CompletedQuantity!.Value,
            sessions.Capture(completion.Subject, db.CurrentTenantId), completion.PermissionVersion).StoreIn(evt);
        db.IntegrationEvents.Add(evt);
    }

    internal async Task<(IntegrationEvent Event, MesCompletionFinanceIntentV1? Intent)?> ExecuteMesCompletionStepAsync(
        Guid id, DateTime dueBefore, bool finance, CancellationToken ct,
        Func<IntegrationEvent, MesCompletionFinanceIntentV1, OperationsAccessV1, CancellationToken, Task<MesCompletionFinanceIntentV1>> step)
    {
        var sessions = completionSessions ?? throw new OrderPriceCorrectionException("FINANCE_OWNER_REQUIRED", 503);
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null
            || db.ChangeTracker.HasChanges() || db.ChangeTracker.Entries().Any(e => IsBusiness(e.Entity.GetType().Name)
                || e.Entity is CP6.Entity.DomainModels.Fin.CostSheet or CP6.Entity.DomainModels.Fin.CostSheetLine
                    or CP6.Entity.DomainModels.Fin.FinSequence))
            throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);

        var tenant = db.CurrentTenantId;
        var retained = db.ChangeTracker.Entries().Select(e => e.Entity).ToHashSet(ReferenceEqualityComparer.Instance);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            if (!await TenantActive())
                return null;
            await LockGraph(true, ct, finance ? FinCost : Production);

            var evt = await db.IntegrationEvents.SingleOrDefaultAsync(e => e.TenantId == tenant && e.Id == id
                && e.SourceModule == "MES" && e.TargetModule == "FIN" && e.HookName == MesCompletionFinanceIntentV1.Hook
                && e.Status == IntegrationEventStatus.Pending && e.NextRetryAt <= dueBefore, ct);
            if (evt == null)
                return null;

            const string effects = "mes_completion_effects";
            var original = db.ChangeTracker.Entries().ToDictionary(entry => entry.Entity,
                entry => entry.CurrentValues.Clone(), ReferenceEqualityComparer.Instance);
            await tx.CreateSavepointAsync(effects, ct);
            MesCompletionFinanceIntentV1? intent = null;
            try
            {
                try { intent = MesCompletionFinanceIntentV1.Read(evt); }
                catch (JsonException error) { throw new OrderPriceCorrectionException("COMPLETION_INTENT_INVALID", 422, error); }

                var session = intent.Session;
                await sessions.RequireCurrentAsync(session, tenant, ct);
                var parent = await AuthorityCoreAsync(Production, "complete", session.Subject, true, ct);
                if (parent.Source.SourcePermissionVersion != intent.PermissionVersion)
                    throw new OrderPriceCorrectionException("AUTHORITY_CHANGED", 403);

                var completion = new OperationsDelegationV1(db, tenant, session.Subject, evt.Creator!, Production,
                    "complete", evt.SourceNo, intent.PermissionVersion)
                {
                    CompletionResultId = id, CompletedQuantity = intent.Quantity, CompletionWorkOrderId = intent.WorkOrderId,
                };
                var access = new OperationsAccessV1(db, this, parent, storedSubjectSource: ReadFinanceSource);
                await access.RequireDelegationParent(completion, ct);
                access.AtCommit(token => access.RequireDelegationParent(completion, token));

                var updated = await step(evt, intent, access, ct);
                if (!ReferenceEquals(updated, intent))
                    updated.StoreIn(evt);
                intent = updated;
                if (evt.Status != IntegrationEventStatus.Pending)
                {
                    evt.Attempts++;
                    evt.NextRetryAt = null;
                }
                await db.SaveChangesAsync(ct);
                await access.ValidateFinalAsync(ct);
                foreach (var initial in access.Authorities)
                    RequireUnchangedAuthority(initial, await AuthorityCoreAsync(initial.Resource, initial.Action, session.Subject, true, ct));
                await sessions.RequireCurrentAsync(session, tenant, ct);

                Task<Authority> ReadFinanceSource(string resource, string action, CancellationToken token)
                    => finance && (resource == FinCost && action is "collect" or "settle"
                        || resource is Mes or Product or ProcessCostRate && action == "query")
                        ? AuthorityCoreAsync(resource, action, session.Subject, true, token)
                        : throw new OrderPriceCorrectionException("DELEGATION_FORBIDDEN", 403);
            }
            catch (OrderPriceCorrectionException error) when (error.Status is 401 or 403 or 409 or 422)
            {
                await tx.RollbackToSavepointAsync(effects, ct);
                foreach (var entry in db.ChangeTracker.Entries().ToArray())
                {
                    if (!original.TryGetValue(entry.Entity, out var values))
                        entry.State = EntityState.Detached;
                    else
                    {
                        entry.CurrentValues.SetValues(values);
                        entry.OriginalValues.SetValues(values);
                        entry.State = EntityState.Unchanged;
                    }
                }
                evt.Status = IntegrationEventStatus.DeadLetter;
                evt.LastError = error.Message;
                evt.NextRetryAt = null;
                evt.Attempts++;
                await db.SaveChangesAsync(ct);
                if (!await TenantActive())
                    return null;
            }
            await tx.ReleaseSavepointAsync(effects, ct);
            ct.ThrowIfCancellationRequested();
            try { await tx.CommitAsync(ct); }
            catch (Exception error) { throw new OrderPriceCorrectionException("OPERATIONS_OUTCOME_UNKNOWN", 503, error); }
            return (evt, intent);
        }
        finally
        {
            foreach (var entry in db.ChangeTracker.Entries().ToArray())
                if (!retained.Contains(entry.Entity))
                    entry.State = EntityState.Detached;
        }

        Task<bool> TenantActive() => db.Sys_Tenants.AsNoTracking().AnyAsync(t => t.Id == tenant && t.Enable
            && (t.ExpireDate == null || t.ExpireDate > DateTime.Now), ct);
    }
}
