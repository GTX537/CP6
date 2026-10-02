using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace CP6.DatabaseCompatibility.Probe;

internal sealed class ProbeDatabase(ProbeProvider provider, string connectionString, string? existingSchema = null) : IAsyncDisposable
{
    private DbConnection? _connection;
    private bool _schemaAttempted;
    private string? _receiptOwner;
    public ProbeProvider Provider { get; } = provider;
    public string Schema { get; } = existingSchema ?? ProbeSafety.NewSchema();
    public Guid OwnershipId { get; } = Guid.NewGuid();
    public string ConnectionEnvironment { get; init; } = provider == ProbeProvider.PostgreSql ? "CP6_TEST_POSTGRES" : "CP6_TEST_SQLSERVER";
    public bool InjectSetupFailure { get; init; }
    public string? ServerVersion { get; private set; }
    public DbConnection Connection => _connection ?? throw new InvalidOperationException();
    public string Table(string name) => $"{ProbeSafety.Quote(Schema, Provider)}.{ProbeSafety.Quote(name, Provider)}";
    public string Column(string name) => Provider == ProbeProvider.PostgreSql ? $"\"{name}\"" : $"[{name}]";
    public string Bool(bool value) => Provider == ProbeProvider.PostgreSql ? (value ? "TRUE" : "FALSE") : (value ? "1" : "0");
    public DbConnection NewConnection() => ProbeSafety.CreateConnection(Provider, connectionString);

    public async Task InitializeAsync()
    {
        var receiptOwner = ProbeSafety.ValidateDatabaseOwner(Environment.GetEnvironmentVariable("CP6_TEST_DATABASE_OWNER"));
        _receiptOwner = receiptOwner;
        _connection = NewConnection();
        await _connection.OpenAsync();
        await ProbeSafety.VerifyDatabaseOwnerAsync(Provider, _connection, receiptOwner);
        ServerVersion = _connection.ServerVersion;
        // SQL Server allows CREATE SCHEMA only as its own batch; execute DDL in one real transaction.
        await using var transaction = await _connection.BeginTransactionAsync();
        _schemaAttempted = true;
        await _connection.ExecuteAsync($"CREATE SCHEMA {ProbeSafety.Quote(Schema, Provider)}", transaction: transaction);
        if (InjectSetupFailure) throw new ProbeAssertionException("Expected setup negative-control failure after CREATE SCHEMA and before COMMIT; transaction must roll back its schema.");
        foreach (var statement in ProbeDdl.Create(this))
            await _connection.ExecuteAsync(statement, transaction: transaction);
        await using (var platform = new PlatformContext(_connection, Provider, Schema))
        {
            // Create only the actual package's four-table model, not any CP6 migration chain.
            var script = platform.Database.GenerateCreateScript();
            foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(batch => !string.IsNullOrWhiteSpace(batch)))
                await _connection.ExecuteAsync(batch, transaction: transaction);
        }
        foreach (var statement in ProbeDdl.PlatformTriggers(this))
            await _connection.ExecuteAsync(statement, transaction: transaction);
        await _connection.ExecuteAsync($"INSERT INTO {Table("__cp6_compat_owner")} ({Column("OwnerId")}, {Column("Task")}) VALUES (@Owner, @Task)",
            new { Owner = OwnershipId, Task = "DB-COMPAT-01/WP1" }, transaction);
        await transaction.CommitAsync();
    }

    public async Task<string> CleanupAsync()
    {
        if (!_schemaAttempted) return "No schema creation was attempted.";
        await using var cleanup = NewConnection();
        await cleanup.OpenAsync();
        await ProbeSafety.VerifyDatabaseOwnerAsync(Provider, cleanup, _receiptOwner!);
        var existsSql = Provider == ProbeProvider.PostgreSql
            ? "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name=@Schema"
            : "SELECT COUNT(*) FROM sys.schemas WHERE name=@Schema";
        var exists = await cleanup.QuerySingleAsync<int>(existsSql, new { Schema });
        if (exists == 0) return "Verified no temporary schema remained after rolled-back or unsuccessful setup.";
        var matches = await cleanup.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {Table("__cp6_compat_owner")} WHERE {Column("OwnerId")}=@Owner AND {Column("Task")}=@Task",
            new { Owner = OwnershipId, Task = "DB-COMPAT-01/WP1" });
        Expect.True(matches == 1, "Cleanup denied: probe schema ownership identity did not match.");
        await using var transaction = await cleanup.BeginTransactionAsync();
        // No CASCADE: a dependency or unknown object keeps the schema for inspection instead of deleting an outside object.
        foreach (var statement in ProbeDdl.Drop(this))
            await cleanup.ExecuteAsync(statement, transaction: transaction);
        await transaction.CommitAsync();
        _schemaAttempted = false;
        return "Owner identity checked; only explicit owned tables, functions, sequence and schema removed.";
    }

    public Task<byte[]> ReadTokenAsync(string table, Guid id, DbConnection? connection = null, DbTransaction? transaction = null) =>
        (connection ?? Connection).QuerySingleAsync<byte[]>($"SELECT {Column("RowVersion")} FROM {Table(table)} WHERE {Column("Id")}=@Id", new { Id = id }, transaction);

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync();
    }
}
