using CP6.WebApi.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;

namespace CP6.Tests.Infra;

public sealed class LocalJsonConfigurationTests
{
    [Fact]
    public void Local_source_is_inserted_before_unprefixed_environment_variables()
    {
        const string key = "CP6:ConfigurationTests:LocalOverride";
        var configuration = CreateConfiguration(
            key,
            appsettingsValue: "appsettings",
            localValue: "local");

        Assert.Equal("local", configuration[key]);
        Assert.Equal("DOTNET_", Assert.IsType<EnvironmentVariablesConfigurationSource>(
            configuration.Sources[0]).Prefix);
        Assert.IsType<MemoryConfigurationSource>(configuration.Sources[1]);
        Assert.IsType<MemoryConfigurationSource>(configuration.Sources[2]);
        Assert.Null(Assert.IsType<EnvironmentVariablesConfigurationSource>(
            configuration.Sources[3]).Prefix);
    }

    [Fact]
    public void Unprefixed_environment_source_still_overrides_local_source()
    {
        var key = $"CP6_CONFIGURATION_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(key, "environment");

        try
        {
            var configuration = CreateConfiguration(
                key,
                appsettingsValue: "appsettings",
                localValue: "local");

            Assert.Equal("environment", configuration[key]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void Source_after_environment_variables_still_overrides_local_source()
    {
        const string key = "CP6:ConfigurationTests:CommandLineOverride";
        var configuration = CreateConfiguration(
            key,
            appsettingsValue: "appsettings",
            localValue: "local",
            laterValue: "command-line");

        Assert.Equal("command-line", configuration[key]);
        Assert.IsType<MemoryConfigurationSource>(configuration.Sources[^1]);
    }

    [Fact]
    public void Local_source_is_appended_when_unprefixed_environment_source_is_missing()
    {
        const string key = "CP6:ConfigurationTests:Fallback";
        var configuration = new ConfigurationManager();
        configuration.Sources.Clear();
        configuration.Sources.Add(new MemoryConfigurationSource
        {
            InitialData = new[] { new KeyValuePair<string, string?>(key, "appsettings") },
        });
        var localSource = new MemoryConfigurationSource
        {
            InitialData = new[] { new KeyValuePair<string, string?>(key, "local") },
        };

        LocalJsonConfiguration.InsertBeforeUnprefixedEnvironmentVariables(
            configuration,
            localSource);

        Assert.Same(localSource, configuration.Sources[^1]);
        Assert.Equal("local", configuration[key]);
    }

    private static ConfigurationManager CreateConfiguration(
        string key,
        string appsettingsValue,
        string localValue,
        string? laterValue = null)
    {
        var configuration = new ConfigurationManager();
        configuration.Sources.Clear();
        configuration.Sources.Add(new EnvironmentVariablesConfigurationSource
        {
            Prefix = "DOTNET_",
        });
        configuration.Sources.Add(new MemoryConfigurationSource
        {
            InitialData = new[] { new KeyValuePair<string, string?>(key, appsettingsValue) },
        });
        configuration.Sources.Add(new EnvironmentVariablesConfigurationSource());
        if (laterValue is not null)
        {
            configuration.Sources.Add(new MemoryConfigurationSource
            {
                InitialData = new[] { new KeyValuePair<string, string?>(key, laterValue) },
            });
        }

        var localSource = new MemoryConfigurationSource
        {
            InitialData = new[] { new KeyValuePair<string, string?>(key, localValue) },
        };
        LocalJsonConfiguration.InsertBeforeUnprefixedEnvironmentVariables(
            configuration,
            localSource);

        return configuration;
    }
}
