using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Persistence;

/// <summary>Provider-specific logical resource locks with explicit caller-owned lifetimes.</summary>
public static class DatabaseResourceLocks
{
    private static readonly TimeSpan PostgreSqlPollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Acquires an exclusive resource lock until the caller's current transaction ends.
    /// A zero timeout attempts once; a non-negative finite timeout returns false when busy.
    /// The caller owns isolation, rollback and any whole-business-operation retry.
    /// Waiting for a PostgreSQL advisory lock does not refresh a Serializable snapshot;
    /// any necessary fresh-transaction retry belongs to the caller's complete protocol.
    /// </summary>
    public static async Task<bool> TryAcquireTransactionAsync(
        DbContext context,
        string resource,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateResource(resource);
        ValidateTimeout(timeoutMs);
        var provider = Provider(context);
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = context.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A caller transaction is required for transaction resource locks.");
        var connection = context.Database.GetDbConnection();
        ValidateOpenConnection(connection, provider);

        return provider == DatabaseProvider.SqlServer
            ? await TryAcquireSqlServerAsync(connection, transaction.GetDbTransaction(), resource, "Transaction", timeoutMs, cancellationToken)
            : await TryAcquirePostgreSqlAsync(connection, transaction.GetDbTransaction(), PostgreSqlKey(resource), session: false, timeoutMs, cancellationToken);
    }

    /// <summary>
    /// Acquires an exclusive session resource lock on an already-open caller connection.
    /// Returns null when busy. The handle must be released or disposed before that connection
    /// is closed or returned to its pool; neither acquisition nor disposal owns the connection.
    /// </summary>
    public static async Task<DatabaseSessionResourceLock?> TryAcquireSessionAsync(
        DbConnection connection,
        DatabaseProvider provider,
        string resource,
        int timeoutMs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ValidateResource(resource);
        ValidateTimeout(timeoutMs);
        ValidateProvider(provider);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateOpenConnection(connection, provider);
        var acquired = provider == DatabaseProvider.SqlServer
            ? await TryAcquireSqlServerAsync(connection, null, resource, "Session", timeoutMs, cancellationToken)
            : await TryAcquirePostgreSqlAsync(connection, null, PostgreSqlKey(resource), session: true, timeoutMs, cancellationToken);

        return acquired ? new DatabaseSessionResourceLock(connection, provider, resource) : null;
    }

    /// <summary>Stable SHA256 UTF8 resource identity: first eight digest bytes, signed big endian.</summary>
    internal static long PostgreSqlKey(string resource)
    {
        ValidateResource(resource);
        return BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(resource)));
    }

    private static DatabaseProvider Provider(DbContext context)
    {
        if (context.Database.IsSqlServer()) return DatabaseProvider.SqlServer;
        if (context.Database.IsNpgsql()) return DatabaseProvider.PostgreSql;
        throw new InvalidOperationException("Database resource locks require SQL Server or PostgreSQL.");
    }

    private static void ValidateProvider(DatabaseProvider provider)
    {
        if (provider is not (DatabaseProvider.SqlServer or DatabaseProvider.PostgreSql))
            throw new ArgumentOutOfRangeException(nameof(provider));
    }

    private static void ValidateResource(string resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (string.IsNullOrWhiteSpace(resource) || resource.Length > 255)
            throw new ArgumentException("A resource must contain between 1 and 255 characters.", nameof(resource));
    }

    private static void ValidateTimeout(int timeoutMs)
    {
        if (timeoutMs < 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
    }

    private static void ValidateOpenConnection(DbConnection connection, DatabaseProvider provider)
    {
        var matches = provider == DatabaseProvider.SqlServer ? connection is SqlConnection : connection is NpgsqlConnection;
        if (!matches) throw new InvalidOperationException("The database connection does not match Database:Provider.");
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("Database resource locks require an already-open caller connection.");
        new DatabaseConnectionFactory(new(provider)).ValidateConnection(connection);
    }

    private static async Task<bool> TryAcquireSqlServerAsync(
        DbConnection connection, DbTransaction? transaction, string resource, string owner,
        int timeoutMs, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = (int)Math.Max(30, (timeoutMs + 999L) / 1000 + 5);
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = N'Exclusive',
                @LockOwner = @owner,
                @LockTimeout = @timeoutMilliseconds,
                @DbPrincipal = N'public';
            SELECT @result;
            """;
        AddParameter(command, "@resource", DbType.String, resource, 255);
        AddParameter(command, "@owner", DbType.String, owner, 32);
        AddParameter(command, "@timeoutMilliseconds", DbType.Int32, timeoutMs);
        object? result;
        try
        {
            result = await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (SqlException exception) when (cancellationToken.IsCancellationRequested && exception.Number == 0)
        {
            throw new OperationCanceledException("Database resource lock request was canceled.", exception, cancellationToken);
        }
        return result switch
        {
            0 or 1 => true,
            -1 => false,
            -2 => throw new OperationCanceledException("Database resource lock request was canceled.", cancellationToken),
            -3 => throw new DatabaseResourceLockDeadlockException(DatabaseProvider.SqlServer),
            _ => throw new InvalidOperationException("SQL Server returned an invalid application-lock result."),
        };
    }

    private static async Task<bool> TryAcquirePostgreSqlAsync(
        DbConnection connection, DbTransaction? transaction, long key, bool session,
        int timeoutMs, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 30;
        command.CommandText = session
            ? "SELECT pg_catalog.pg_try_advisory_lock(@key);"
            : "SELECT pg_catalog.pg_try_advisory_xact_lock(@key);";
        AddParameter(command, "@key", DbType.Int64, key);
        var timer = Stopwatch.StartNew();
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is not bool acquired)
                throw new InvalidOperationException("PostgreSQL returned an invalid advisory-lock result.");
            if (acquired) return true;
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = timeout - timer.Elapsed;
            if (remaining <= TimeSpan.Zero) return false;
            await Task.Delay(remaining < PostgreSqlPollInterval ? remaining : PostgreSqlPollInterval, cancellationToken);
            if (timer.Elapsed >= timeout) return false;
        }
    }

    internal static async Task ReleaseSessionAsync(
        DbConnection connection, DatabaseProvider provider, string resource, CancellationToken cancellationToken)
    {
        ValidateOpenConnection(connection, provider);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        if (provider == DatabaseProvider.SqlServer)
        {
            command.CommandText = """
                DECLARE @result int;
                EXEC @result = sys.sp_releaseapplock
                    @Resource = @resource,
                    @LockOwner = N'Session',
                    @DbPrincipal = N'public';
                SELECT @result;
                """;
            AddParameter(command, "@resource", DbType.String, resource, 255);
            if (await command.ExecuteScalarAsync(cancellationToken) is not int result || result != 0)
                throw new InvalidOperationException("SQL Server did not release the session application lock.");
        }
        else
        {
            command.CommandText = "SELECT pg_catalog.pg_advisory_unlock(@key);";
            AddParameter(command, "@key", DbType.Int64, PostgreSqlKey(resource));
            if (await command.ExecuteScalarAsync(cancellationToken) is not true)
                throw new InvalidOperationException("PostgreSQL did not release the session advisory lock.");
        }
    }

    private static void AddParameter(DbCommand command, string name, DbType type, object value, int? size = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        if (size is not null) parameter.Size = size.Value;
        command.Parameters.Add(parameter);
    }
}

/// <summary>The native SQL Server application-lock result selected this request as a deadlock victim.</summary>
public sealed class DatabaseResourceLockDeadlockException(DatabaseProvider provider)
    : Exception("Database resource lock request was selected as a deadlock victim.")
{
    public DatabaseProvider Provider { get; } = provider;
}

/// <summary>A non-owning session lock handle; release is explicit and repeated release is harmless.</summary>
public sealed class DatabaseSessionResourceLock : IAsyncDisposable
{
    private readonly DbConnection connection;
    private readonly DatabaseProvider provider;
    private readonly string resource;
    private readonly SemaphoreSlim releaseGate = new(1, 1);
    private bool released;

    internal DatabaseSessionResourceLock(DbConnection connection, DatabaseProvider provider, string resource)
    {
        this.connection = connection;
        this.provider = provider;
        this.resource = resource;
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref released)) return;
        await releaseGate.WaitAsync(cancellationToken);
        try
        {
            if (released) return;
            await DatabaseResourceLocks.ReleaseSessionAsync(connection, provider, resource, cancellationToken);
            Volatile.Write(ref released, true);
        }
        finally
        {
            releaseGate.Release();
        }
    }

    /// <summary>Releases with no cancellation token so an interrupted caller still attempts cleanup.</summary>
    public ValueTask DisposeAsync()
        => new(ReleaseAsync(CancellationToken.None));
}
