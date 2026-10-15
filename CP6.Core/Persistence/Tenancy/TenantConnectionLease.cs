using System.Data;
using CP6.Platform.Abstractions.Context;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>
/// No raw connection property. Business reads and writes run only in explicit local transactions.
/// Dispose the lease before disposing its manager; manager disposal waits for all borrowed leases.
/// </summary>
internal sealed class TenantConnectionLease : IAsyncDisposable
{
    private readonly TenantConnectionManager _manager;
    private readonly TenantConnectionManager.Pool _pool;
    private readonly TrustedRequestContextV1 _trusted;
    private readonly NpgsqlConnection _connection;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly object _disposeSync = new();
    private Task? _disposal;
    private int _closed;

    internal TenantConnectionLease(TenantConnectionManager manager, TenantConnectionManager.Pool pool,
        TrustedRequestContextV1 trusted, NpgsqlConnection connection)
        => (_manager, _pool, _trusted, _connection) = (manager, pool, trusted, connection);

    internal void CheckCurrent()
    {
        if (Volatile.Read(ref _closed) != 0) throw new TenantStorageException(TenantStorageFailure.LeaseClosed);
        _manager.CheckCurrent(_pool, _trusted);
    }
    internal async Task CheckConnectionAsync(CancellationToken cancellationToken)
    {
        CheckCurrent();
        await TenantConnectionGuard.ValidateAsync(_connection, _pool.Database.Database, _pool.Principal.Username, cancellationToken).ConfigureAwait(false);
        CheckCurrent();
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>The callback must explicitly CommitAsync; otherwise disposal rolls back. Never retried.</summary>
    public async Task<T> InTransactionAsync<T>(Func<TenantStorageTransaction, CancellationToken, Task<T>> action,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        CheckCurrent();
        if (!await _operation.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new TenantStorageException(TenantStorageFailure.OperationInProgress);
        try
        {
            await CheckConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await _connection.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
            CheckCurrent();
            var session = new TenantStorageTransaction(this, _connection, transaction);
            try
            {
                var result = await action(session, cancellationToken).ConfigureAwait(false);
                CheckCurrent();
                cancellationToken.ThrowIfCancellationRequested();
                return result;
            }
            finally { await session.CloseAsync().ConfigureAwait(false); }
        }
        catch (Exception error) { throw TenantConnectionManager.Safe(error); }
        finally { _operation.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            Interlocked.Exchange(ref _closed, 1);
            return new(_disposal ??= DisposeCoreAsync());
        }
    }
    private async Task DisposeCoreAsync()
    {
        await _operation.WaitAsync().ConfigureAwait(false);
        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _manager.Release(_pool);
        }
        catch (Exception error) { throw TenantConnectionManager.Safe(error); }
        finally
        {
            _operation.Release();
        }
    }
}

/// <summary>
/// Trusted host callback surface only: do not retain/return the connection, transaction, reader, command or session,
/// dispatch detached work, change session identity, or commit the raw transaction. Use this wrapper's explicit commit.
/// The Owner must perform its current authorization and final write fence in the same transaction.
/// </summary>
internal sealed class TenantStorageTransaction
{
    private readonly TenantConnectionLease _lease;
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private int _closed;
    private bool _committed;
    internal TenantStorageTransaction(TenantConnectionLease lease, NpgsqlConnection connection, NpgsqlTransaction transaction)
        => (_lease, _connection, _transaction) = (lease, connection, transaction);

    public async Task<T> ExecuteAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _lease.CheckConnectionAsync(cancellationToken).ConfigureAwait(false);
            var result = await action(_connection, _transaction, cancellationToken).ConfigureAwait(false);
            _lease.CheckCurrent();
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception error) { throw TenantConnectionManager.Safe(error); }
        finally { _operation.Release(); }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _lease.CheckConnectionAsync(cancellationToken).ConfigureAwait(false);
            await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _committed = true;
            _lease.CheckCurrent();
        }
        catch (Exception error) { throw TenantConnectionManager.Safe(error); }
        finally { _operation.Release(); }
    }
    private async Task EnterAsync(CancellationToken ct)
    {
        if (!await _operation.WaitAsync(0, ct).ConfigureAwait(false))
            throw new TenantStorageException(TenantStorageFailure.OperationInProgress);
        if (Volatile.Read(ref _closed) != 0 || _committed)
        {
            _operation.Release();
            throw new TenantStorageException(TenantStorageFailure.LeaseClosed);
        }
    }
    internal async Task CloseAsync()
    {
        Interlocked.Exchange(ref _closed, 1);
        // An already-running callback is allowed to finish before the transaction rolls back/disposes.
        await _operation.WaitAsync().ConfigureAwait(false);
        _operation.Release();
    }
}

internal static class TenantConnectionGuard
{
    /// <summary>Minimal runtime identity/privilege guard; not a complete object ACL, membership or RLS audit.</summary>
    internal static async Task ValidateAsync(NpgsqlConnection connection, string database, string username, CancellationToken ct)
    {
        try
        {
            await using var command = new NpgsqlCommand("""
                SELECT pg_catalog.current_database(), session_user::text, current_user::text,
                       pg_catalog.current_setting('search_path'),
                       r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                       OR NOT r.rolcanlogin OR d.datdba = r.oid
                       OR pg_catalog.has_database_privilege(session_user, d.oid, 'CREATE')
                       OR pg_catalog.has_database_privilege(session_user, d.oid, 'TEMP')
                FROM pg_catalog.pg_roles AS r
                JOIN pg_catalog.pg_database AS d ON d.datname = pg_catalog.current_database()
                WHERE r.rolname = session_user
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false) || reader.GetString(0) != database
                || reader.GetString(1) != username || reader.GetString(2) != username
                || reader.GetString(3) != "pg_catalog" || reader.GetBoolean(4))
                throw new TenantStorageException(TenantStorageFailure.IdentityRejected);
        }
        catch (Exception error) { throw TenantConnectionManager.Safe(error); }
    }
}
