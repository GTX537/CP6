using Microsoft.Extensions.Configuration;

namespace CP6.Core.Persistence;

/// <summary>Scaffolding configuration without starting the application or loading its secret files.</summary>
public sealed class DatabaseDesignTimeConfiguration
{
    private DatabaseDesignTimeConfiguration(DatabaseOptions database, string connectionString)
    {
        Database = database;
        ConnectionString = connectionString;
    }

    public DatabaseOptions Database { get; }
    public string ConnectionString { get; }

    public static DatabaseDesignTimeConfiguration FromArguments(string[] args, string? explicitConnectionString = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        var environmentValues = new Dictionary<string, string?>();
        var provider = Environment.GetEnvironmentVariable("Database__Provider");
        if (provider is not null)
            environmentValues[DatabaseOptions.ProviderConfigurationKey] = provider;

        IConfiguration configuration;
        try
        {
            configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(environmentValues)
                .AddCommandLine(args)
                .Build();
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new InvalidOperationException("Database design-time arguments require configuration keys and values.");
        }

        var database = DatabaseOptions.FromConfiguration(configuration);
        var connection = configuration.GetConnectionString("DefaultConnection")
            ?? explicitConnectionString
            ?? (database.Provider == DatabaseProvider.PostgreSql
                ? "Host=localhost;Database=CP6_Design;Username=cp6_design"
                : "Server=localhost;Database=CP6_Design;Integrated Security=true;TrustServerCertificate=true");
        return new(database, connection);
    }
}
