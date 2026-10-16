using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CP6.Core.Persistence.Tenancy;

internal sealed class TenantEfCommandGuard(TenantStorageTransaction owner, NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid tenant) : DbCommandInterceptor, IDisposable
{
    private bool _closed;
    public void Dispose() => _closed = true;
    private void Check(DbCommand command)
    {
        if (_closed || !ReferenceEquals(command.Connection, connection) || !ReferenceEquals(command.Transaction, transaction))
            throw new TenantStorageException(TenantStorageFailure.LeaseClosed);
        _ = owner.CurrentBinding;
    }
    internal async Task ValidateCurrentAsync(CancellationToken ct)
    {
        if (_closed) throw new TenantStorageException(TenantStorageFailure.LeaseClosed);
        _ = owner.CurrentBinding;
        await owner.CheckConnectionAsync(ct).ConfigureAwait(false);
        await using var identity = new NpgsqlCommand("SELECT cp6_tenancy_v1.current_tenant_id()", connection, transaction);
        if (await identity.ExecuteScalarAsync(ct).ConfigureAwait(false) is not Guid actual || actual != tenant)
            throw new TenantStorageException(TenantStorageFailure.IdentityRejected);
        _ = owner.CurrentBinding;
    }
    private async Task ValidateAsync(DbCommand command, CancellationToken ct)
    { Check(command); await ValidateCurrentAsync(ct).ConfigureAwait(false); Check(command); }
    private void Validate(DbCommand command) => ValidateAsync(command, default).GetAwaiter().GetResult();

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand c, CommandEventData e, InterceptionResult<DbDataReader> r)
    { Validate(c); return r; }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand c, CommandEventData e, InterceptionResult<int> r)
    { Validate(c); return r; }
    public override InterceptionResult<object> ScalarExecuting(DbCommand c, CommandEventData e, InterceptionResult<object> r)
    { Validate(c); return r; }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData e,
        InterceptionResult<DbDataReader> r, CancellationToken ct = default)
    { await ValidateAsync(c, ct).ConfigureAwait(false); return r; }
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData e,
        InterceptionResult<int> r, CancellationToken ct = default)
    { await ValidateAsync(c, ct).ConfigureAwait(false); return r; }
    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand c, CommandEventData e,
        InterceptionResult<object> r, CancellationToken ct = default)
    { await ValidateAsync(c, ct).ConfigureAwait(false); return r; }
}
