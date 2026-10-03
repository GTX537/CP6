using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

public static class Wp2CatalogNegativeControls
{
    // Invoke only on the same open connection that passed the caller's owner preflight.
    // Ownership is checked again here before DDL; no additional connection is created.
    public static async Task<string> VerifyAsync(DbConnection connection, bool pg, IModel coreModel)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(coreModel);
        Require(connection.State == ConnectionState.Open, "Catalog negative control requires the verified open task connection.");
        Require(pg ? connection is NpgsqlConnection : connection is SqlConnection, "Catalog negative control provider must match the task connection.");
        Require(Regex.IsMatch(connection.Database, "\\ACP6Compat_WP2_[0-9]{8}_[a-f0-9]{8}\\z"), "Catalog negative control requires a dedicated WP2 database.");
        Require(pg
            ? ((NpgsqlConnection)connection).Host is "localhost" or "127.0.0.1" or "::1"
            : ((SqlConnection)connection).DataSource == "localhost\\KOUSQLSERVER",
            "Catalog negative control requires the recorded loopback database host.");

        var owner = Environment.GetEnvironmentVariable("CP6_TEST_DATABASE_OWNER");
        Require(owner is not null && Regex.IsMatch(owner, "\\A[0-9a-f]{32}\\z"), "Catalog negative control requires the task owner receipt.");
        var actualOwner = await connection.QuerySingleOrDefaultAsync<string>(pg
            ? "SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database()"
            : "SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2')");
        Require(actualOwner == (pg ? $"DB-COMPAT-01-WP2:{owner}" : owner), "Catalog negative control database ownership must match before DDL.");

        var schema = pg ? "public" : "dbo";
        var table = coreModel.GetRelationalModel().Tables.Single(t => t.Name == "Sys_Users" && (t.Schema ?? schema) == schema);
        var index = table.Indexes.Single(i => i.Name == "IX_Sys_Users_DeptId");
        Require(!index.IsUnique && index.Filter is null && index.Columns.Count == 1 && index.Columns[0].Name == "DeptId",
            "The negative fixture must be the model's nonunique, unfiltered department index.");
        Require(index.Columns[0].IsNullable, "The wrong-filter fixture must use the actual nullable department column.");
        await Wp2CatalogGates.VerifyAsync(connection, pg, coreModel);

        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 30;
            command.CommandText = pg
                ? "DROP INDEX \"public\".\"IX_Sys_Users_DeptId\""
                : "DROP INDEX [IX_Sys_Users_DeptId] ON [dbo].[Sys_Users]";
            await command.ExecuteNonQueryAsync();

            var rejectedMissingIndex = false;
            try
            {
                await Wp2CatalogGates.VerifyAsync(connection, pg, coreModel, transaction);
            }
            catch (InvalidOperationException exception) when
                (exception.Message == $"Missing index {schema}.Sys_Users.IX_Sys_Users_DeptId.")
            {
                rejectedMissingIndex = true;
            }
            Require(rejectedMissingIndex, "Catalog verifier must reject the deliberately missing model department index.");

            command.CommandText = pg
                ? "CREATE INDEX \"IX_Sys_Users_DeptId\" ON \"public\".\"Sys_Users\"(\"DeptId\") WHERE \"DeptId\" IS NOT NULL"
                : "CREATE INDEX [IX_Sys_Users_DeptId] ON [dbo].[Sys_Users]([DeptId]) WHERE [DeptId] IS NOT NULL";
            await command.ExecuteNonQueryAsync();
            var rejectedWrongFilter = false;
            try { await Wp2CatalogGates.VerifyAsync(connection, pg, coreModel, transaction); }
            catch (CatalogAssertionException exception) when
                (exception.Message == $"Index filter differs for {schema}.Sys_Users.IX_Sys_Users_DeptId.")
            { rejectedWrongFilter = true; }
            Require(rejectedWrongFilter, "A NOT NULL predicate on a nullable column must remain a real index-filter mismatch.");
        }
        finally
        {
            // There is no commit path and no business-row DML. A failed assertion also restores the index.
            await transaction.RollbackAsync();
            await Wp2CatalogGates.VerifyAsync(connection, pg, coreModel);
        }

        return "Complete catalog passed; transaction-only missing index and wrong filter on its nullable department column were rejected by exact assertions; rollback restored the complete catalog. No business rows changed.";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
