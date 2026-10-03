using System.Data;
using System.Data.Common;
using CP6.Core.Persistence;
using CP6.DatabaseCompatibility.Testing;
using Microsoft.Data.SqlClient;
using Npgsql;

internal static class Wp6MigrationOwnership
{
    internal static bool IsRequested => OwnedTestDatabase.IsRequested();

    // Parse the existing connection's configuration, then verify its actual physical database.
    // Never call OwnedTestDatabase.VerifyAsync here: it would open a different connection.
    internal static async Task VerifyAsync(DbConnection connection, bool pg,
        params DatabaseFixtureRole[] allowedRoles)
    {
        Require(connection.State == ConnectionState.Open, "CP6_COMPAT_MIGRATION_OPEN_CONNECTION_REQUIRED");
        Require(pg ? connection is NpgsqlConnection : connection is SqlConnection,
            "CP6_COMPAT_MIGRATION_PROVIDER_MISMATCH");
        var owned = OwnedTestDatabase.FromEnvironment(
            new(pg ? DatabaseProvider.PostgreSql : DatabaseProvider.SqlServer), connection.ConnectionString,
            allowedRoles, "CP6.WP6.MigrationProbe");
        Require(owned is not null, "CP6_COMPAT_MIGRATION_WP6_CONFIGURATION_REQUIRED");
        // FromEnvironment is pure parsing: loopback, exact name, owner suffix/32 hex digits and role.
        // Credentials removed from an open connection's ConnectionString are neither needed nor reused.
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        if (pg)
        {
            command.CommandText = """
                SELECT current_database(),pg_catalog.shobj_description(d.oid,'pg_database')
                FROM pg_catalog.pg_database d
                WHERE d.datname=current_database() AND d.datname::text COLLATE "C"=@ExpectedDatabase
                """;
            Parameter(command, "ExpectedDatabase", owned!.DatabaseName);
        }
        else
        {
            command.CommandText = """
                SELECT DB_NAME(),
                  (SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=@TaskProperty),
                  (SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=@OwnerProperty)
                """;
            Parameter(command, "TaskProperty", "CP6CompatTask");
            Parameter(command, "OwnerProperty", "CP6CompatOwner");
        }
        await using var reader = await command.ExecuteReaderAsync();
        Require(await reader.ReadAsync(), "CP6_COMPAT_ACTUAL_DATABASE_MISMATCH");
        Require(!reader.IsDBNull(0) && reader.GetString(0) == owned!.DatabaseName
            && connection.Database == owned.DatabaseName, "CP6_COMPAT_ACTUAL_DATABASE_MISMATCH");
        if (pg)
            Require(!reader.IsDBNull(1) && reader.GetString(1) == $"{OwnedTestDatabase.TaskName}:{owned!.Owner}",
                "CP6_COMPAT_ACTUAL_OWNER_MISMATCH");
        else
            Require(!reader.IsDBNull(1) && reader.GetString(1) == OwnedTestDatabase.TaskName
                && !reader.IsDBNull(2) && reader.GetString(2) == owned!.Owner,
                "CP6_COMPAT_ACTUAL_OWNER_MISMATCH");
        Require(!await reader.ReadAsync(), "CP6_COMPAT_MIGRATION_OWNER_RESULT_AMBIGUOUS");
    }

    private static void Parameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.String;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
