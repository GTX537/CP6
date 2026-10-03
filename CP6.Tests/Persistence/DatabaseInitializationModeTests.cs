using CP6.Core.Persistence;
using CP6.WebApi.Configuration;
using Microsoft.Extensions.Configuration;

namespace CP6.Tests.Persistence;

public sealed class DatabaseInitializationModeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SqlServerSupportsApplicationAndOneShotInitialization(bool initializeOnly)
        => DatabaseRuntimeSupport.EnsureSupported(Options("SqlServer"), initializeOnly);

    [Fact]
    public void PostgreSqlApplicationRuntimeRemainsRejected()
        => Assert.Throws<InvalidOperationException>(() =>
            DatabaseRuntimeSupport.EnsureSupported(Options("PostgreSql")));

    [Fact]
    public void PostgreSqlOneShotInitializationCanRunWithoutEnablingApplicationRuntime()
        => DatabaseRuntimeSupport.EnsureSupported(Options("PostgreSql"), databaseInitializationOnly: true);

    [Fact]
    public void NullProviderCannotBypassValidationInInitializationMode()
        => Assert.Throws<ArgumentNullException>(() =>
            DatabaseRuntimeSupport.EnsureSupported(null!, databaseInitializationOnly: true));

    private static DatabaseOptions Options(string provider) => DatabaseOptions.FromConfiguration(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["Database:Provider"] = provider
        }).Build());
}
