using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.ErpIntegration;
using CP6.Space.Application;
using CP6.Space.Infrastructure;
using System.Data.Common;

namespace CP6.DatabaseCompatibility.Probe;

internal static class TransactionExperiments
{
    public static async Task RunAsync(ProbeDatabase db, ProbeReport report)
    {
        await report.CheckAsync("Transaction.TwoEfContextsDapperCommit", () => ExecuteAsync(db, true));
        await report.CheckAsync("Transaction.TwoEfContextsDapperRollback", () => ExecuteAsync(db, false));
        await report.CheckAsync("Transaction.SameConnectionStringIsNotSameTransaction", () => NotAtomicAsync(db));
        await report.CheckAsync("Transaction.ActualFourContextDbFacadesCommit", () => FourContextsAsync(db, true));
        await report.CheckAsync("Transaction.ActualFourContextDbFacadesRollback", () => FourContextsAsync(db, false));
    }

    private static async Task<string> ExecuteAsync(ProbeDatabase db, bool commit)
    {
        var id = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
        await using var connection = db.NewConnection();
        await connection.OpenAsync();
        await using (var business = new BusinessContext(connection, db.Provider, db.Schema))
        await using (var outbox = new OutboxContext(connection, db.Provider, db.Schema))
        await using (var transaction = await business.Database.BeginTransactionAsync())
        {
            await outbox.Database.UseTransactionAsync(transaction.GetDbTransaction());
            Expect.True(ReferenceEquals(business.Database.GetDbConnection(), outbox.Database.GetDbConnection()), "Contexts must share the same actual DbConnection object.");
            Expect.True(ReferenceEquals(business.Database.CurrentTransaction!.GetDbTransaction(), outbox.Database.CurrentTransaction!.GetDbTransaction()), "Contexts must share the same actual DbTransaction object.");
            var record = new BusinessRecord { Id = id, TenantId = tenant, Value = "transaction business" };
            business.Records.Add(record);
            await business.SaveChangesAsync();
            Expect.Token(record.RowVersion);
            var message = new OutboxRecord { Id = outboxId, TenantId = tenant, BusinessId = id, Value = "transaction outbox" };
            outbox.Records.Add(message);
            await outbox.SaveChangesAsync();
            Expect.Token(message.RowVersion);
            await connection.ExecuteAsync($"INSERT INTO {db.Table("AuditRecords")} ({db.Column("Id")}, {db.Column("BusinessId")}, {db.Column("RowVersion")}, {db.Column("Value")}) VALUES (@Id,@Business,@Token,@Value)",
                new { Id = auditId, Business = id, Token = record.RowVersion, Value = "transaction Dapper audit" }, transaction.GetDbTransaction());
            var within = await connection.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {db.Table("AuditRecords")} WHERE {db.Column("Id")}=@Id", new { Id = auditId }, transaction.GetDbTransaction());
            Expect.True(within == 1, "Dapper insert must occur in the shared transaction.");
            if (commit) await transaction.CommitAsync();
            else await transaction.RollbackAsync();
        }
        await using var observer = db.NewConnection();
        await observer.OpenAsync();
        var businessCount = await observer.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {db.Table("BusinessRecords")} WHERE {db.Column("Id")}=@Id", new { Id = id });
        var outboxCount = await observer.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {db.Table("OutboxRecords")} WHERE {db.Column("Id")}=@Id", new { Id = outboxId });
        var auditCount = await observer.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {db.Table("AuditRecords")} WHERE {db.Column("Id")}=@Id", new { Id = auditId });
        var expected = commit ? 1 : 0;
        Expect.True(businessCount == expected && outboxCount == expected && auditCount == expected, "Business, outbox and audit must all share commit or rollback outcome as read from another actual connection.");
        return commit
            ? "Two EF contexts plus Dapper shared actual connection/transaction; independent observer saw all three committed records and generated 8-byte tokens."
            : "After real writes in two EF contexts plus Dapper, rollback left all three records absent to independent observer.";
    }

    private static async Task<string> NotAtomicAsync(ProbeDatabase db)
    {
        var businessId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
        await using var a = db.NewConnection();
        await using var b = db.NewConnection();
        await Task.WhenAll(a.OpenAsync(), b.OpenAsync());
        await using var business = new BusinessContext(a, db.Provider, db.Schema);
        await using var transaction = await business.Database.BeginTransactionAsync();
        var row = new BusinessRecord { Id = businessId, TenantId = Guid.NewGuid(), Value = "rollback negative control" };
        business.Records.Add(row);
        await business.SaveChangesAsync();
        await b.ExecuteAsync($"INSERT INTO {db.Table("AuditRecords")} ({db.Column("Id")}, {db.Column("BusinessId")}, {db.Column("RowVersion")}, {db.Column("Value")}) VALUES (@Id,@Business,@Token,@Value)",
            new { Id = auditId, Business = businessId, Token = row.RowVersion, Value = "unshared independent commit" });
        await transaction.RollbackAsync();
        var businessCount = await b.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {db.Table("BusinessRecords")} WHERE {db.Column("Id")}=@Id", new { Id = businessId });
        var auditCount = await b.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {db.Table("AuditRecords")} WHERE {db.Column("Id")}=@Id", new { Id = auditId });
        Expect.True(businessCount == 0 && auditCount == 1, "Negative control must prove same connection string does not enlist a separate Dapper connection.");
        return "Expected negative control: same connection string on a separate connection retained Dapper audit when EF business rolled back; explicit actual transaction participation is required.";
    }

    private static DbContextOptions<T> Options<T>(DatabaseOptions database, DbConnection connection, DatabaseContextKind kind) where T : DbContext
    {
        var profile = DatabaseMigrationProfile.For(database, kind);
        return DatabaseContextOptions.Configure(new DbContextOptionsBuilder<T>(), database, connection,
            profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema).Options;
    }

    private static async Task<string> FourContextsAsync(ProbeDatabase db, bool commit)
    {
        var database = new DatabaseOptions(db.Provider == ProbeProvider.SqlServer ? DatabaseProvider.SqlServer : DatabaseProvider.PostgreSql);
        await using var connection = db.NewConnection();
        await connection.OpenAsync();
        var tenant = Guid.NewGuid();
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var auditId = Guid.NewGuid();
        await using var core = new CP6Context(Options<CP6Context>(database, connection, DatabaseContextKind.Core));
        await using var space = new SpaceContext(Options<SpaceContext>(database, connection, DatabaseContextKind.Space), new ProbeSpaceExecution(tenant), new SystemSpaceClock());
        await using var identity = new IdentityMessagingContext(Options<IdentityMessagingContext>(database, connection, DatabaseContextKind.IdentityPriority));
        await using var erp = new ErpIntegrationContext(Options<ErpIntegrationContext>(database, connection, DatabaseContextKind.ErpIntegration));
        DbContext[] contexts = [core, space, identity, erp];
        await using (var transaction = await core.Database.BeginTransactionAsync())
        {
            foreach (var context in contexts.Skip(1)) await context.Database.UseTransactionAsync(transaction.GetDbTransaction());
            for (var index = 0; index < contexts.Length; index++)
            {
                var context = contexts[index];
                Expect.True(ReferenceEquals(connection, context.Database.GetDbConnection()), "Actual context registration must retain the exact supplied connection object.");
                Expect.True(ReferenceEquals(transaction.GetDbTransaction(), context.Database.CurrentTransaction!.GetDbTransaction()), "Actual context must enlist the exact shared DbTransaction object.");
                var sql = $"INSERT INTO {db.Table("BusinessRecords")} ({db.Column("Id")},{db.Column("TenantId")},{db.Column("Value")}) VALUES ({{0}},{{1}},{{2}})";
                var count = await context.Database.ExecuteSqlRawAsync(sql, ids[index], tenant, context.GetType().Name);
                Expect.True(count == 1, "Each actual context DatabaseFacade must perform its limited-table write inside the shared transaction.");
            }
            var token = await db.ReadTokenAsync("BusinessRecords", ids[0], connection, transaction.GetDbTransaction());
            await connection.ExecuteAsync($"INSERT INTO {db.Table("AuditRecords")} ({db.Column("Id")},{db.Column("BusinessId")},{db.Column("RowVersion")},{db.Column("Value")}) VALUES (@Id,@Business,@Token,@Value)",
                new { Id = auditId, Business = ids[0], Token = token, Value = "actual-four-context Dapper" }, transaction.GetDbTransaction());
            if (commit) await transaction.CommitAsync();
            else await transaction.RollbackAsync();
        }
        await using var observer = db.NewConnection();
        await observer.OpenAsync();
        foreach (var id in ids)
        {
            var count = await observer.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {db.Table("BusinessRecords")} WHERE {db.Column("Id")}=@Id", new { Id = id });
            Expect.True(count == (commit ? 1 : 0), "All four actual context writes must share commit/rollback outcome.");
        }
        var auditCount = await observer.QuerySingleAsync<int>($"SELECT COUNT(*) FROM {db.Table("AuditRecords")} WHERE {db.Column("Id")}=@Id", new { Id = auditId });
        Expect.True(auditCount == (commit ? 1 : 0), "Dapper must share actual four-context transaction outcome.");
        return $"Actual CP6Context, SpaceContext, IdentityMessagingContext, ErpIntegrationContext plus Dapper shared connection/transaction and {(commit ? "committed" : "rolled back")} five limited-table writes; no model replacement, entity SaveChanges or full migration acceptance is claimed.";
    }

    private sealed class ProbeSpaceExecution(Guid tenant) : ISpaceExecutionContext
    {
        public Guid TenantId => tenant;
        public Guid ActorId => Guid.Parse("00000000-0000-0000-0000-000000000001");
    }
}
