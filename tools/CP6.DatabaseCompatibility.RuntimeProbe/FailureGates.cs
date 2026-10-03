using System.Data.Common;
using System.Text.RegularExpressions;
using CP6.Core.Persistence;
using Dapper;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace CP6.DatabaseCompatibility.RuntimeProbe;

internal static class FailureGates
{
    public static async Task<string> ConstraintsAsync(RuntimeFixture fixture)
    {
        await fixture.VerifyOwnerAsync();
        var cases = Cases(fixture);
        var connection = fixture.Connection();
        var languageCases = 1;
        try
        {
            await connection.OpenAsync();
            foreach (var item in cases)
                await NegativeAsync(fixture, connection, item);

            if (!fixture.IsPostgreSql && await connection.QuerySingleAsync<int>("SELECT CONVERT(int,@@LANGID)") != 0)
            {
                var originalLanguage = await connection.QuerySingleAsync<string>("SELECT @@LANGUAGE");
                try
                {
                    await connection.ExecuteAsync("SET LANGUAGE us_english");
                    foreach (var item in cases)
                        await NegativeAsync(fixture, connection, item);
                    languageCases++;
                }
                finally { await connection.ExecuteAsync("SET LANGUAGE @language", new { language = originalLanguage }); }
            }
        }
        finally
        {
            await connection.DisposeAsync();
            await CleanupAsync(fixture, cases.SelectMany(item => item.Tables).Distinct().ToArray());
        }

        return fixture.IsPostgreSql
            ? "Actual PK/explicit unique-index 23505, FK23503 and CHECK23514 matched classifier kind and exact generated constraint name; integrity failures were not retryable. Each failed transaction fully rolled back; same-connection fresh transaction SELECT1 passed four times; all five fixture tables absent."
            : $"Native SQL PK2627, explicit unique-index2601 and FK/CHECK547 matched classifier kind, error-collection code and exact constraint identity across {languageCases} session languages (original, plus English when different). Integrity failures were not retryable; {4 * languageCases} whole rollbacks and fresh-transaction SELECT1 checks passed; all five fixture tables absent. Original language restored.";
    }

    private static async Task NegativeAsync(RuntimeFixture fixture, DbConnection connection, ConstraintCase item)
    {
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            try
            {
                await connection.ExecuteAsync(item.SetupSql, transaction: transaction, commandTimeout: 30);
                foreach (var table in item.Tables)
                {
                    ProbeAssert.Require(await connection.QuerySingleAsync<int>(
                            $"SELECT COUNT(*) FROM {Table(fixture, table)}", transaction: transaction) == 1,
                        $"{item.Label}: the legal native fixture must exist before the negative write.");
                }

                Exception? rejected = null;
                try
                {
                    await connection.ExecuteAsync(item.InvalidSql, transaction: transaction, commandTimeout: 30);
                }
                catch (Exception exception) when (exception is SqlException or PostgresException)
                {
                    rejected = exception;
                }

                ProbeAssert.Require(rejected is not null, $"{item.Label}: the native integrity write must be rejected.");
                if (fixture.IsPostgreSql)
                {
                    ProbeAssert.Require(rejected is PostgresException postgres && postgres.SqlState == item.PostgreSqlState,
                        $"{item.Label}: the original native SQLSTATE must match.");
                }
                else
                {
                    ProbeAssert.Require(rejected is SqlException sql
                            && sql.Errors.Cast<SqlError>().Any(error => error.Number == item.SqlServerCode),
                        $"{item.Label}: the original native error collection must contain the expected SQL code.");
                }

                var failure = DatabaseFailureClassifier.Classify(rejected!);
                ProbeAssert.Require(failure.Kind == item.Kind, $"{item.Label}: classifier kind must match.");
                ProbeAssert.Require(failure.ConstraintName == item.ConstraintName
                        && failure.MatchesConstraint(item.ConstraintName)
                        && !failure.MatchesConstraint(item.ConstraintName + "_other"),
                    $"{item.Label}: the classifier must match only the exact generated constraint identity.");
                ProbeAssert.Require(failure.SqlState == (fixture.IsPostgreSql ? item.PostgreSqlState : null)
                        && failure.DatabaseErrorCode == (fixture.IsPostgreSql ? null : (int?)item.SqlServerCode),
                    $"{item.Label}: classifier native diagnostics must be preserved.");
                ProbeAssert.Require(!failure.CanRetryTransaction,
                    $"{item.Label}: unrelated integrity errors are not automatic whole-transaction retries.");
            }
            finally
            {
                // PostgreSQL rejects further statements after an error until this entire transaction rolls back.
                await transaction.RollbackAsync();
            }
        }

        await using var recovery = await connection.BeginTransactionAsync();
        ProbeAssert.Require(await connection.QuerySingleAsync<int>("SELECT 1", transaction: recovery) == 1,
            $"{item.Label}: a fresh transaction must work on the same connection after rollback.");
        await recovery.RollbackAsync();
    }

    private static IReadOnlyList<ConstraintCase> Cases(RuntimeFixture fixture)
    {
        var suffix = Guid.NewGuid().ToString("N")[..16];
        var primary = Identifier("w3f_pk_" + suffix);
        var unique = Identifier("w3f_unique_" + suffix);
        var parent = Identifier("w3f_parent_" + suffix);
        var child = Identifier("w3f_child_" + suffix);
        var check = Identifier("w3f_check_" + suffix);
        var primaryName = Identifier("pk_" + primary);
        var uniquePrimaryName = Identifier("pk_" + unique);
        var uniqueName = Identifier("ux_" + unique);
        var parentPrimaryName = Identifier("pk_" + parent);
        var childPrimaryName = Identifier("pk_" + child);
        var foreignName = Identifier("fk_" + child);
        var checkPrimaryName = Identifier("pk_" + check);
        var checkName = Identifier("ck_" + check);
        var id = fixture.Column("Id");
        var value = fixture.Column("Value");
        var parentId = fixture.Column("ParentId");

        return
        [
            new("PrimaryKey", [primary], primaryName, DatabaseFailureKind.UniqueConstraint, 2627, "23505",
                $"CREATE TABLE {Table(fixture, primary)} ({id} INTEGER NOT NULL, {value} INTEGER NOT NULL, CONSTRAINT {fixture.Column(primaryName)} PRIMARY KEY ({id})); INSERT INTO {Table(fixture, primary)} ({id},{value}) VALUES (1,1);",
                $"INSERT INTO {Table(fixture, primary)} ({id},{value}) VALUES (1,2)"),
            new("UniqueIndex", [unique], uniqueName, DatabaseFailureKind.UniqueConstraint, 2601, "23505",
                $"CREATE TABLE {Table(fixture, unique)} ({id} INTEGER NOT NULL, {value} INTEGER NOT NULL, CONSTRAINT {fixture.Column(uniquePrimaryName)} PRIMARY KEY ({id})); CREATE UNIQUE INDEX {fixture.Column(uniqueName)} ON {Table(fixture, unique)} ({value}); INSERT INTO {Table(fixture, unique)} ({id},{value}) VALUES (1,1);",
                $"INSERT INTO {Table(fixture, unique)} ({id},{value}) VALUES (2,1)"),
            new("ForeignKey", [child, parent], foreignName, DatabaseFailureKind.ForeignKey, 547, "23503",
                $"CREATE TABLE {Table(fixture, parent)} ({id} INTEGER NOT NULL, CONSTRAINT {fixture.Column(parentPrimaryName)} PRIMARY KEY ({id})); CREATE TABLE {Table(fixture, child)} ({id} INTEGER NOT NULL, {parentId} INTEGER NOT NULL, CONSTRAINT {fixture.Column(childPrimaryName)} PRIMARY KEY ({id}), CONSTRAINT {fixture.Column(foreignName)} FOREIGN KEY ({parentId}) REFERENCES {Table(fixture, parent)} ({id})); INSERT INTO {Table(fixture, parent)} ({id}) VALUES (1); INSERT INTO {Table(fixture, child)} ({id},{parentId}) VALUES (1,1);",
                $"INSERT INTO {Table(fixture, child)} ({id},{parentId}) VALUES (2,99)"),
            new("CheckConstraint", [check], checkName, DatabaseFailureKind.CheckConstraint, 547, "23514",
                $"CREATE TABLE {Table(fixture, check)} ({id} INTEGER NOT NULL, {value} INTEGER NOT NULL, CONSTRAINT {fixture.Column(checkPrimaryName)} PRIMARY KEY ({id}), CONSTRAINT {fixture.Column(checkName)} CHECK ({value} >= 0)); INSERT INTO {Table(fixture, check)} ({id},{value}) VALUES (1,1);",
                $"INSERT INTO {Table(fixture, check)} ({id},{value}) VALUES (2,-1)")
        ];
    }

    private static async Task CleanupAsync(RuntimeFixture fixture, IEnumerable<string> tables)
    {
        await using var connection = fixture.Connection();
        await connection.OpenAsync();
        foreach (var table in tables)
        {
            if (await ExistsAsync(fixture, connection, table))
                await connection.ExecuteAsync($"DROP TABLE {Table(fixture, table)}", commandTimeout: 30);
            ProbeAssert.Require(!await ExistsAsync(fixture, connection, table),
                "Each exact generated constraint fixture table must be absent after cleanup.");
        }
    }

    private static Task<bool> ExistsAsync(RuntimeFixture fixture, DbConnection connection, string name) =>
        connection.QuerySingleAsync<bool>(fixture.IsPostgreSql
            ? "SELECT EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relname=@name AND c.relkind IN ('r','p'))"
            : "SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo') AND name=@name) THEN 1 ELSE 0 END AS bit)",
            new { name });

    private static string Table(RuntimeFixture fixture, string name) =>
        (fixture.IsPostgreSql ? "public." : "dbo.") + fixture.Column(Identifier(name));

    private static string Identifier(string value)
    {
        ProbeAssert.Require(Regex.IsMatch(value, "\\A[a-z][a-z0-9_]{0,62}\\z", RegexOptions.CultureInvariant),
            "Only internally generated bounded ASCII fixture identifiers are allowed.");
        return value;
    }

    private sealed record ConstraintCase(string Label, string[] Tables, string ConstraintName,
        DatabaseFailureKind Kind, int SqlServerCode, string PostgreSqlState, string SetupSql, string InvalidSql);
}
