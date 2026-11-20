using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CP6.Core.Services.ErpIntegration.CommercialProtectionV1;

namespace CP6.Core.Services.ErpIntegration;

internal sealed class CommercialProtectionStoreV1(IDbContextFactory<ErpIntegrationContext> database, IDataProtectionProvider protection,
    TimeProvider? clock)
{
    internal Task RunAsync(Func<NpgsqlConnection, NpgsqlTransaction, Task> run, CancellationToken ct) =>
        RunAsync(async (db, tx) =>
        {
            await run(db, tx);
            return true;
        }, ct);

    internal async Task<T> RunAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> run, CancellationToken ct)
    {
        if (System.Transactions.Transaction.Current is not null) throw Failure("IC_NATIVE_TRANSACTION_REQUIRED");
        await using var context = await database.CreateDbContextAsync(ct);
        if (context.Database.GetDbConnection() is not NpgsqlConnection db) throw Failure("IC_NATIVE_TRANSACTION_REQUIRED");
        await db.OpenAsync(ct);
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var result = await run(db, tx);
        await tx.CommitAsync(ct);
        return result;
    }

    internal static async Task<T> WithinSavepointAsync<T>(NpgsqlTransaction tx, Func<Task<T>> run, CancellationToken ct)
    {
        var savepoint = "ic_" + Guid.NewGuid().ToString("N");
        await tx.SaveAsync(savepoint, ct);
        T result;
        try { result = await run(); }
        catch
        {
            await tx.RollbackAsync(savepoint, CancellationToken.None);
            await tx.ReleaseAsync(savepoint, CancellationToken.None);
            throw;
        }
        await tx.ReleaseAsync(savepoint, ct);
        return result;
    }

    internal async Task RequireNativeAsync(NpgsqlConnection db, NpgsqlTransaction tx, CancellationToken ct)
    {
        if (System.Transactions.Transaction.Current is not null || !ReferenceEquals(tx.Connection, db) ||
            tx.IsolationLevel is not (IsolationLevel.ReadCommitted or IsolationLevel.Serializable))
            throw Failure("IC_NATIVE_TRANSACTION_REQUIRED");
        await using var configured = await database.CreateDbContextAsync(ct);
        if (configured.Database.GetDbConnection() is not NpgsqlConnection expected || !SameDatabase(expected, db))
            throw Failure("IC_NATIVE_TRANSACTION_REQUIRED");
    }

    private static bool SameDatabase(NpgsqlConnection expected, NpgsqlConnection actual)
    {
        var target = new NpgsqlConnectionStringBuilder(expected.ConnectionString);
        return target.Host == actual.Host && target.Port == actual.Port && target.Database == actual.Database;
    }

    internal async Task<DateTimeOffset> NowAsync(NpgsqlConnection db, NpgsqlTransaction tx, CancellationToken ct)
    {
        if (clock is not null) return Milliseconds(clock.GetUtcNow());
        await using var sql = Command(db, tx, "SELECT clock_timestamp()");
        return Milliseconds(new DateTimeOffset((DateTime)(await sql.ExecuteScalarAsync(ct))!));
    }

    internal static NpgsqlCommand Command(NpgsqlConnection db, NpgsqlTransaction tx, string sql,
        params (string Name, object Value)[] coordinates)
    {
        var command = new NpgsqlCommand(sql, db, tx);
        foreach (var (name, value) in coordinates) command.Parameters.AddWithValue(name, value);
        return command;
    }

    internal static async Task LockKeyAsync(NpgsqlConnection db, NpgsqlTransaction tx, string domain, Guid tenant, string key, CancellationToken ct)
    {
        await using var command = Command(db, tx,
            "SELECT pg_advisory_xact_lock(hashtextextended(@address,28647837))",
            ("address", domain + "/" + tenant.ToString("D") + "/" + key));
        await command.ExecuteNonQueryAsync(ct);
    }

    private IDataProtector For(Guid tenant, string domain, string key) =>
        protection.CreateProtector("CP6.Main.IC/1", tenant.ToString("D"), domain, key);

    internal byte[] Seal<T>(Guid tenant, string domain, string key, T value)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(value, CommercialCanonicalV1.Json);
        try { return For(tenant, domain, key).Protect(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    internal T Open<T>(Guid tenant, string domain, string key, byte[] encrypted) where T : class
    {
        byte[]? clear = null;
        try
        {
            clear = For(tenant, domain, key).Unprotect(encrypted);
            return JsonSerializer.Deserialize<T>(clear, CommercialCanonicalV1.Json) ?? throw Failure("ORIGINAL_UNPROVEN");
        }
        catch (Exception e) when (e is CryptographicException or JsonException) { throw Failure("ORIGINAL_UNPROVEN"); }
        finally { if (clear is not null) CryptographicOperations.ZeroMemory(clear); }
    }

    internal static bool Same<T>(T left, T right) =>
        CommercialCanonicalV1.Data(left).AsSpan().SequenceEqual(CommercialCanonicalV1.Data(right));

    internal static CommercialProtocolException Failure(string code) => new(code);
}
