using Microsoft.Extensions.Configuration;

namespace CP6.Core.Persistence;

public enum DatabaseProvider
{
    SqlServer,
    PostgreSql
}

/// <summary>A deployment's fixed database selection. Connection strings never select the provider.</summary>
public sealed class DatabaseOptions
{
    public const string ProviderConfigurationKey = "Database:Provider";

    private const string InvalidProviderMessage = "Database:Provider must be SqlServer or PostgreSql.";

    public DatabaseOptions(DatabaseProvider provider)
    {
        if (provider is not (DatabaseProvider.SqlServer or DatabaseProvider.PostgreSql))
            throw new InvalidOperationException(InvalidProviderMessage);

        Provider = provider;
    }

    public DatabaseProvider Provider { get; }

    public static DatabaseOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var value = configuration[ProviderConfigurationKey];
        var hasProviderKey = configuration.GetSection("Database").GetChildren()
            .Any(section => string.Equals(section.Key, "Provider", StringComparison.OrdinalIgnoreCase));

        if (value is null && !hasProviderKey)
            return new(DatabaseProvider.SqlServer);

        return value switch
        {
            "SqlServer" => new(DatabaseProvider.SqlServer),
            "PostgreSql" => new(DatabaseProvider.PostgreSql),
            _ => throw new InvalidOperationException(InvalidProviderMessage)
        };
    }
}
