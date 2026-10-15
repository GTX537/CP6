using CP6.Platform.Abstractions.Context;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

/// <summary>
/// One bounded manager per host process. Reservations cover pool capacity, including initialization and draining.
/// It is deliberately not registered by Program; current host authentication and Owner write fencing are separate.
/// </summary>
internal sealed class TenantConnectionManager : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly string _host;
    private readonly ITrustedContextAccessorV1 _accessor;
    private readonly ITenantCredentialResolver _credentials;
    private readonly TenantPoolLimits _limits;
    private readonly List<Pool> _pools = [];
    private TenantStorageCatalog _catalog;
    private long _generation, _sequence;
    private bool _disposed;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TenantConnectionManager(string host, ITrustedContextAccessorV1 accessor, TenantStorageCatalog catalog,
        ITenantCredentialResolver credentials, TenantPoolLimits limits)
    {
        ArgumentNullException.ThrowIfNull(accessor); ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(credentials); ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        _host = TenantStorageBindingV1.Text(host);
        if (catalog.HostId != host) throw TenantStorageBindingV1.Rejected();
        _accessor = accessor; _catalog = catalog; _credentials = credentials; _limits = limits;
    }

    public async Task<TenantConnectionLease> OpenCurrentAsync(CancellationToken cancellationToken = default)
    {
        var trusted = _accessor.CurrentTrusted ?? throw new TenantStorageException(TenantStorageFailure.TrustedContextRequired);
        cancellationToken.ThrowIfCancellationRequested();
        Pool pool;
        bool initialize;
        lock (_sync)
        {
            CheckTrusted(trusted);
            var binding = _catalog.Resolve(trusted.Context.TenantId);
            var existing = _pools.FirstOrDefault(p => !p.Retired && p.Generation == _generation && ReferenceEquals(p.Binding, binding));
            initialize = existing is null;
            if (existing is null)
            {
                MakeRoom();
                pool = new(binding, _catalog.Database(binding), _catalog.Principal(binding), _generation, ++_sequence);
                _pools.Add(pool);
            }
            else pool = existing;
            pool.Borrowers++;
            pool.LastUse = ++_sequence;
        }
        NpgsqlConnection? connection = null;
        try
        {
            if (initialize) await InitializeAsync(pool, trusted, cancellationToken).ConfigureAwait(false);
            var source = await pool.Source.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            CheckCurrent(pool, trusted);
            connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            CheckCurrent(pool, trusted);
            await TenantConnectionGuard.ValidateAsync(connection, pool.Database.Database, pool.Principal.Username, cancellationToken).ConfigureAwait(false);
            CheckCurrent(pool, trusted);
            cancellationToken.ThrowIfCancellationRequested();
            return new(this, pool, trusted, connection);
        }
        catch (Exception error)
        {
            try { if (connection is not null) await connection.DisposeAsync().ConfigureAwait(false); }
            catch (Exception disposalError) { throw Safe(disposalError); } // Unknown disposal retains its reservation.
            Release(pool);
            throw Safe(error);
        }
    }

    /// <summary>Host configuration only. Any change invalidates old leases; borrowed connections drain on disposal.</summary>
    public void ReplaceCatalog(TenantStorageCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.HostId != _host) throw TenantStorageBindingV1.Rejected();
        lock (_sync)
        {
            ThrowIfDisposed();
            _catalog = catalog;
            _generation++;
            foreach (var pool in _pools.ToArray()) Retire(pool);
        }
    }

    // Non-sensitive diagnostics expose reservations, not connection strings or tenant identities.
    internal (int Pools, int ReservedConnections, int Borrowers, int DrainingPools) Snapshot()
    {
        lock (_sync) return (_pools.Count, _pools.Count * _limits.ConnectionsPerPool,
            _pools.Sum(p => p.Borrowers), _pools.Count(p => p.Retired));
    }

    private async Task InitializeAsync(Pool pool, TrustedRequestContextV1 trusted, CancellationToken ct)
    {
        NpgsqlDataSource? source = null;
        try
        {
            string password;
            try { password = await _credentials.ResolvePasswordAsync(pool.Binding, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { throw new TenantStorageException(TenantStorageFailure.CredentialsUnavailable); }
            ct.ThrowIfCancellationRequested();
            CheckCurrent(pool, trusted);
            if (string.IsNullOrEmpty(password)) throw new TenantStorageException(TenantStorageFailure.CredentialsUnavailable);
            var settings = new NpgsqlConnectionStringBuilder
            {
                Host = pool.Database.Server, Port = pool.Database.Port, Database = pool.Database.Database,
                Username = pool.Principal.Username, Password = password, SslMode = pool.Database.SslMode,
                SearchPath = "pg_catalog", Pooling = true, MinPoolSize = 0, MaxPoolSize = _limits.ConnectionsPerPool,
                Timeout = _limits.ConnectionTimeoutSeconds, CommandTimeout = _limits.CommandTimeoutSeconds,
                CancellationTimeout = 2000, ConnectionIdleLifetime = 60, ConnectionPruningInterval = 10,
                Multiplexing = false, NoResetOnClose = false, Enlist = false, IncludeErrorDetail = false,
                PersistSecurityInfo = false, LogParameters = false, ApplicationName = "cp6-tenant-storage-v1",
                // Npgsql 8 uses PGOPTIONS only when Options is null. Nonempty whitespace survives builder
                // serialization, sends no PostgreSQL switches, and blocks that ambient session override.
                Options = " ", ClientEncoding = "UTF8", Timezone = "UTC"
            };
            source = new NpgsqlDataSourceBuilder(settings.ConnectionString).Build();
            lock (_sync)
            {
                CheckCurrentLocked(pool, trusted);
                pool.DataSource = source;
                pool.Source.TrySetResult(source);
                source = null;
            }
        }
        catch (Exception error)
        {
            try { source?.Dispose(); }
            catch { /* The failed unpublished source has never opened a connection. */ }
            var safe = Safe(error);
            lock (_sync) pool.Retired = true;
            pool.Source.TrySetException(safe);
            // The initiating borrower also awaits Source, observing this shared failure.
        }
    }

    internal void CheckCurrent(Pool pool, TrustedRequestContextV1 trusted)
    {
        lock (_sync) CheckCurrentLocked(pool, trusted);
    }
    private void CheckCurrentLocked(Pool pool, TrustedRequestContextV1 trusted)
    {
        CheckTrusted(trusted);
        if (pool.Retired || pool.Generation != _generation || !ReferenceEquals(_catalog.Resolve(trusted.Context.TenantId), pool.Binding))
            throw new TenantStorageException(TenantStorageFailure.BindingChanged);
    }
    private void CheckTrusted(TrustedRequestContextV1 trusted)
    {
        ThrowIfDisposed();
        if (!ReferenceEquals(_accessor.CurrentTrusted, trusted)) throw new TenantStorageException(TenantStorageFailure.ContextChanged);
        if (trusted.Context.TenantId == Guid.Empty || trusted.Context.TenantId != trusted.Evidence.TenantId
            || trusted.Evidence.HostId != _host || trusted.Evidence.ContextContractVersion != "ctx-v1"
            || trusted.Evidence.CapabilityVersion != "ctx-binding-v1") throw new TenantStorageException(TenantStorageFailure.BindingRejected);
    }
    private void ThrowIfDisposed()
    {
        if (_disposed) throw new TenantStorageException(TenantStorageFailure.LeaseClosed);
    }
    private void MakeRoom()
    {
        while (_pools.Count >= _limits.MaximumPools || (_pools.Count + 1) * _limits.ConnectionsPerPool > _limits.MaximumConnections)
        {
            // Oldest completed idle pool first; a monotonic tie-breaker makes eviction deterministic.
            var idle = _pools.Where(p => p.Borrowers == 0 && p.Source.Task.IsCompleted).OrderBy(p => p.LastUse).FirstOrDefault();
            if (idle is null) throw new TenantStorageException(TenantStorageFailure.CapacityExceeded);
            Retire(idle);
        }
    }
    private void Retire(Pool pool)
    {
        pool.Retired = true;
        if (pool.Borrowers != 0) return;
        // Dispose under the same lock; the reservation is released only after physical idle connections close.
        try { pool.DataSource?.Dispose(); }
        catch (Exception error) { throw Safe(error); } // Keep the reservation if physical disposal is unconfirmed.
        _pools.Remove(pool);
        if (_disposed && _pools.Count == 0) _drained.TrySetResult();
    }
    internal void Release(Pool pool)
    {
        lock (_sync)
        {
            pool.Borrowers--;
            pool.LastUse = ++_sequence;
            if (pool.Retired && pool.Borrowers == 0) Retire(pool);
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposed = true;
            foreach (var pool in _pools.ToArray()) Retire(pool);
            if (_pools.Count == 0) _drained.TrySetResult();
            return new(_drained.Task);
        }
    }
    internal static Exception Safe(Exception error) => error switch
    {
        TenantStorageException => error,
        OperationCanceledException canceled => new OperationCanceledException(
            "Tenant storage operation was canceled.", canceled.CancellationToken),
        PostgresException pg => new TenantStorageException(TenantStorageFailure.DatabaseRejected, pg.SqlState),
        _ => new TenantStorageException(TenantStorageFailure.ConnectionUnavailable)
    };
    internal sealed class Pool(TenantStorageBindingV1 binding, TenantDatabase database, TenantPrincipal principal, long generation, long sequence)
    {
        internal readonly TenantStorageBindingV1 Binding = binding;
        internal readonly TenantDatabase Database = database;
        internal readonly TenantPrincipal Principal = principal;
        internal readonly long Generation = generation;
        internal readonly TaskCompletionSource<NpgsqlDataSource> Source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal NpgsqlDataSource? DataSource;
        internal int Borrowers;
        internal bool Retired;
        internal long LastUse = sequence;
    }
}
