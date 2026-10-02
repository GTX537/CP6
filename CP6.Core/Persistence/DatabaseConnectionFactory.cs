using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace CP6.Core.Persistence;

public sealed class DatabaseConnectionFactory(DatabaseOptions database)
{
    private readonly DatabaseOptions _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>Creates an unopened connection owned by the caller.</summary>
    public DbConnection Create(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("A non-empty database connection string is required.");

        try
        {
            return _database.Provider switch
            {
                DatabaseProvider.SqlServer => new SqlConnection(connectionString),
                DatabaseProvider.PostgreSql => new NpgsqlConnection(connectionString),
                _ => throw new InvalidOperationException("Database:Provider must be SqlServer or PostgreSql.")
            };
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            // Provider parsing exceptions can include connection-string keys or values.
            throw new InvalidOperationException("The database connection string is invalid for Database:Provider.");
        }
    }

    internal void ValidateConnection(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var matchesProvider = _database.Provider switch
        {
            DatabaseProvider.SqlServer => connection is SqlConnection,
            DatabaseProvider.PostgreSql => connection is NpgsqlConnection,
            _ => false
        };

        if (!matchesProvider)
            throw new InvalidOperationException("The database connection does not match Database:Provider.");
    }
}
