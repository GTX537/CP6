using System.Runtime.ExceptionServices;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal enum RmaWriteEntry { Draft, Submit }

internal sealed partial class TenantRmaReadStore
{
    internal async Task<T> WriteAsync<T>(CP6Context authority, Guid actorId, RmaWriteEntry entry,
        Func<RmaTenantReadContext, CancellationToken, Task<T>> write, CancellationToken ct)
    {
        var (purpose, route) = entry switch
        {
            RmaWriteEntry.Draft => (DraftPurpose, "api/wms/rma/native-applications-v1"),
            RmaWriteEntry.Submit => (SubmitPurpose, "api/wms/rma/native-applications-v1/{id}/submit"),
            _ => throw new TenantStorageException(TenantStorageFailure.BindingRejected)
        };
        var committing = false;
        var rollbackUnknown = false;
        try
        {
            var tenant = authority.CurrentTenantId;
            var configured = _catalog.Resolve(tenant);
            var authorityConnection = (NpgsqlConnection)authority.Database.GetDbConnection();
            var authorityTx = (NpgsqlTransaction)authority.Database.CurrentTransaction!.GetDbTransaction();
            var primary = await RmaTenantStorageV1.CoreReadPrimaryAsync(authorityConnection, authorityTx, ct);
            var outcome = await InTenantScopeAsync(authority, actorId, route, purpose,
                async (transaction, cancellation) =>
                {
                    try
                    {
                        var attempt = await transaction.ExecuteAsync<(T Value, ExceptionDispatchInfo? Failure)>(
                            async (connection, dbTransaction, token) =>
                            {
                                if (primary != await RmaTenantStorageV1.CoreReadPrimaryAsync(connection, dbTransaction, token))
                                    throw new TenantStorageException(TenantStorageFailure.IdentityRejected);

                                // Acquire the original root on this transaction before the directory or Ready row locks.
                                await using (var acquire = new NpgsqlCommand($"""
                                    SET LOCAL lock_timeout='10s';
                                    SELECT {RmaTenantStorageV1.DraftSchema}.lock_draft(@tenant)
                                    """, connection, dbTransaction))
                                {
                                    acquire.Parameters.AddWithValue("tenant", tenant);
                                    await acquire.ExecuteNonQueryAsync(token);
                                }

                                var bound = await RmaTenantStorageV1.RequireCoreReadBindingAsync(authorityConnection, authorityTx,
                                    configured, _catalog.Principal(configured).Username, _installation, token);
                                if (bound != primary) throw new TenantStorageException(TenantStorageFailure.IdentityRejected);
                                await RmaTenantStorageV1.RequireCoreDraftBindingAsync(authorityConnection, authorityTx, _installation, token);
                                if (entry == RmaWriteEntry.Submit)
                                    await RmaTenantStorageV1.RequireCoreSubmitBindingAsync(authorityConnection, authorityTx, _installation, token);

                                using var guard = new TenantEfCommandGuard(transaction, connection, dbTransaction, tenant);
                                var options = RmaTenantReadContext.Options(connection).AddInterceptors(guard);
                                await using var storage = new RmaTenantReadContext(options.Options, transaction.CurrentBinding);
                                await storage.Database.UseTransactionAsync(dbTransaction, token);
                                try { return (await write(storage, token), null); }
                                catch (Exception error) when (OperationsOwnerV1.IsExpectedOwnerFailure(error, token))
                                { return (default!, ExceptionDispatchInfo.Capture(error)); }
                            }, cancellation);
                        if (attempt.Failure is null)
                        {
                            transaction.SetCommitFence(token =>
                            {
                                token.ThrowIfCancellationRequested();
                                committing = true;
                                return Task.CompletedTask;
                            });
                            await transaction.CommitAsync(cancellation);
                        }
                        return attempt;
                    }
                    finally
                    {
                        if (!committing)
                        {
                            try { await transaction.RollbackAsync(CancellationToken.None); }
                            catch (TenantStorageException) { rollbackUnknown = true; throw; }
                        }
                    }
                }, ct);
            outcome.Failure?.Throw();
            return outcome.Value;
        }
        catch (Exception error) when (committing || rollbackUnknown)
        {
            throw new OrderPriceCorrectionException("OPERATIONS_OUTCOME_UNKNOWN", 503, error);
        }
        catch (TenantStorageException error)
        {
            throw new OrderPriceCorrectionException("RMA_TENANT_BINDING_UNAVAILABLE", 503, error);
        }
    }
}
