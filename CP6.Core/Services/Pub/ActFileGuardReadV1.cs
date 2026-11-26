using System.Net;
using Npgsql;

namespace CP6.Core.Services.Pub;

public sealed record ActFileGuardBindingV1(Guid StoreId, string ConnectionString);

public sealed class ActFileGuardReadV1 : IAsyncDisposable
{
    private readonly Dictionary<Guid, (Guid Store, NpgsqlDataSource DataSource)> bindings = [];

    public ActFileGuardReadV1(IReadOnlyDictionary<Guid, ActFileGuardBindingV1> configuration)
    {
        var validated = configuration.Select(pair =>
        {
            var value = pair.Value;
            var connection = new NpgsqlConnectionStringBuilder(value.ConnectionString)
            {
                Enlist = false,
                Timeout = 5,
                CommandTimeout = 9,
                ApplicationName = "cp6-main-act-file-guard-read-v1"
            };
            var loopback = IPAddress.TryParse(connection.Host, out var ip) && IPAddress.IsLoopback(ip);
            if (pair.Key == Guid.Empty || value.StoreId == Guid.Empty || string.IsNullOrWhiteSpace(connection.Database) ||
                string.IsNullOrWhiteSpace(connection.Username) || connection.SslMode != SslMode.VerifyFull && !loopback ||
                connection.Multiplexing || connection.NoResetOnClose)
                throw new InvalidOperationException("ACT 文件撤权门需要登记 PostgreSQL 库、独立读取角色和可信 TLS。");
            return (pair.Key, value.StoreId, connection.ConnectionString);
        }).ToArray();
        foreach (var (tenant, store, connection) in validated)
            bindings.Add(tenant, (store, NpgsqlDataSource.Create(connection)));
    }

    internal async Task<T> HoldAsync<T>(Guid tenant, ActFileGuardStampV1 expected,
        Func<ActFileGuardStampV1, Task<T>> use, CancellationToken ct)
    {
        if (!bindings.TryGetValue(tenant, out var binding) || binding.Store != expected.StoreId)
            throw ActFileReferenceV1.Unproven();
        try
        {
            await using var db = await binding.DataSource.OpenConnectionAsync(ct);
            await using var tx = await db.BeginTransactionAsync(ct);
            await using var command = new NpgsqlCommand(
                "SELECT epoch,receipt FROM crm_act_file_guard_v1.hold_v1(@tenant,@store)", db, tx);
            command.Parameters.AddWithValue("tenant", tenant);
            command.Parameters.AddWithValue("store", binding.Store);
            ActFileGuardStampV1 held;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct)) throw ActFileReferenceV1.Unproven();
                held = new(binding.Store, reader.GetInt64(0), reader.GetGuid(1));
                if (await reader.ReadAsync(ct)) throw ActFileReferenceV1.Unproven();
            }
            if (held != expected) throw ActFileReferenceV1.Unproven();
            var result = await use(held);
            // 只读事务释放 CRM 行锁，没有远端引用预占。
            await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            throw ActFileReferenceV1.Unproven(error);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (_, source) in bindings.Values) await source.DisposeAsync();
    }
}
