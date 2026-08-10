using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;

namespace CP6.WebApi.Configuration;

internal static class LocalJsonConfiguration
{
    internal static void Add(
        ConfigurationManager configuration,
        string path = "appsettings.Local.json",
        bool reloadOnChange = true)
    {
        var localJsonSource = new JsonConfigurationSource
        {
            Path = path,
            Optional = true,
            ReloadOnChange = reloadOnChange,
        };
        localJsonSource.ResolveFileProvider();

        InsertBeforeUnprefixedEnvironmentVariables(configuration, localJsonSource);
    }

    internal static void InsertBeforeUnprefixedEnvironmentVariables(
        ConfigurationManager configuration,
        IConfigurationSource localSource)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(localSource);

        var environmentVariablesIndex = -1;
        for (var i = 0; i < configuration.Sources.Count; i++)
        {
            if (configuration.Sources[i] is EnvironmentVariablesConfigurationSource { Prefix: null })
            {
                environmentVariablesIndex = i;
                break;
            }
        }

        if (environmentVariablesIndex >= 0)
            configuration.Sources.Insert(environmentVariablesIndex, localSource);
        else
            configuration.Sources.Add(localSource);
    }
}
