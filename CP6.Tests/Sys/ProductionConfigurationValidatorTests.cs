using CP6.WebApi.Configuration;
using Microsoft.Extensions.Configuration;

namespace CP6.Tests.Sys;

public class ProductionConfigurationValidatorTests
{
    [Fact]
    public void GetErrors_AcceptsCompleteProductionConfiguration()
    {
        var configuration = BuildConfiguration();

        var errors = ProductionConfigurationValidator.GetErrors(configuration);

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("AllowedHosts", "*", "AllowedHosts")]
    [InlineData("AllowedHosts", "localhost", "AllowedHosts")]
    [InlineData("ConnectionStrings:DefaultConnection", "Server=localhost;Database=CP6;Encrypt=False;TrustServerCertificate=True", "ConnectionStrings:DefaultConnection")]
    [InlineData("ConnectionStrings:Redis", "", "ConnectionStrings:Redis")]
    [InlineData("ConnectionStrings:Redis", "cache.cp6.internal:6379,password=a-strong-cache-password", "ssl=true")]
    [InlineData("RabbitMQ:HostName", "localhost", "RabbitMQ:HostName")]
    [InlineData("RabbitMQ:Password", "__SET_VIA_ENV__", "RabbitMQ:Password")]
    [InlineData("Kafka:BootstrapServers", "localhost:9092", "Kafka:BootstrapServers")]
    [InlineData("JWT:Secret", "__SET_VIA_ENV_OR_LOCAL_OVERRIDE_MIN_32_CHARS__", "JWT:Secret")]
    [InlineData("Security:Cookie:Secure", "false", "Security:Cookie:Secure")]
    [InlineData("Security:Csrf:Enabled", "false", "Security:Csrf:Enabled")]
    [InlineData("Cors:AllowedOrigins:0", "http://web.cp6.example", "Cors:AllowedOrigins")]
    [InlineData("Cors:AllowedOrigins:0", "https://user:pass@web.cp6.example", "Cors:AllowedOrigins")]
    [InlineData("Security:NativeClient:SsoGrantMinutes", "5", "Security:NativeClient:SsoGrantMinutes")]
    [InlineData("Security:NativeClient:AllowedRedirectUris:1", "https://attacker.example/callback", "AllowedRedirectUris")]
    [InlineData("Security:NativeClient:Windows:MinimumVersion", "2.0.0", "MinimumVersion")]
    [InlineData("Security:NativeClient:Windows:DownloadUrl", "http://downloads.cp6.example/cp6.msix", "DownloadUrl")]
    [InlineData("Security:NativeClient:Android:Sha256", "", "Sha256")]
    [InlineData("Storage:Provider", "Local", "Storage:Provider")]
    [InlineData("Storage:S3:Endpoint", "http://objects.cp6.example", "Storage:S3:Endpoint")]
    [InlineData("Storage:S3:SecretKey", "__SET_VIA_ENV__", "Storage:S3:SecretKey")]
    [InlineData("Startup:SkipDatabaseInitialization", "false", "SkipDatabaseInitialization")]
    public void GetErrors_RejectsUnsafeProductionValue(
        string key,
        string value,
        string expectedErrorFragment)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            [key] = value
        });

        var errors = ProductionConfigurationValidator.GetErrors(configuration);

        Assert.Contains(errors, error =>
            error.Contains(expectedErrorFragment, StringComparison.Ordinal));
    }

    [Fact]
    public void GetErrors_RequiresSmtpWhenEmailOtpFallbackIsEnabled()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Security:TwoFactor:EmailFallbackEnabled"] = "true",
            ["Email:Smtp:Host"] = ""
        });

        var errors = ProductionConfigurationValidator.GetErrors(configuration);

        Assert.Contains(errors, error => error.Contains("Email:Smtp:Host", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DoesNotEchoSecretValues()
    {
        const string unsafeSecret = "do-not-print-this-value";
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["JWT:Secret"] = unsafeSecret
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration));

        Assert.Contains("JWT:Secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(unsafeSecret, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("database.cp6.internal")]
    [InlineData("database-1.cp6.internal,database-2.cp6.internal")]
    public void GetErrors_AcceptsPostgreSqlWithVerifiedServerIdentity(string hosts)
    {
        var configuration = PostgreSqlConfiguration($"Host={hosts};Database=CP6;Username=cp6;Password=secret-value;SSL Mode=VerifyFull");

        Assert.Empty(ProductionConfigurationValidator.GetErrors(configuration));
    }

    [Theory]
    [InlineData("Disable")]
    [InlineData("Allow")]
    [InlineData("Prefer")]
    [InlineData("Require")]
    [InlineData("VerifyCA")]
    public void GetErrors_RequiresPostgreSqlVerifyFull(string sslMode)
    {
        var configuration = PostgreSqlConfiguration($"Host=database.cp6.internal;Database=CP6;Username=cp6;Password=secret-value;SSL Mode={sslMode}");

        Assert.Contains(ProductionConfigurationValidator.GetErrors(configuration), error => error.Contains("VerifyFull", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]
    [InlineData("::1")]
    [InlineData("[::1]:5432")]
    [InlineData("/var/run/postgresql")]
    [InlineData("database.cp6.internal,localhost")]
    [InlineData("database.cp6.internal,127.0.0.1:5432")]
    [InlineData("localhost,database.cp6.internal")]
    public void GetErrors_RejectsEveryLocalPostgreSqlHost(string hosts)
    {
        var configuration = PostgreSqlConfiguration($"Host={hosts};Database=CP6;Username=cp6;Password=secret-value;SSL Mode=VerifyFull");

        Assert.Contains(ProductionConfigurationValidator.GetErrors(configuration), error => error.Contains("local", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Host=database.cp6.internal;Password=do-not-print;do-not-print=invalid")]
    [InlineData("Host=database.cp6.internal;Password=do-not-print;SSL Mode=do-not-print")]
    [InlineData("Host=database.cp6.internal;Password=do-not-print;Port=do-not-print")]
    public void Validate_InvalidPostgreSqlConnectionDoesNotExposeParserErrorOrSecret(string connection)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.Validate(PostgreSqlConfiguration(connection)));

        Assert.Contains("PostgreSQL connection string", exception.Message);
        Assert.DoesNotContain("do-not-print", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Validate_PostgreSqlRequiresHostWithoutThrowingNullParserError()
    {
        var configuration = PostgreSqlConfiguration("Database=CP6;Username=cp6;Password=do-not-print;SSL Mode=VerifyFull");
        var exception = Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.Validate(configuration));

        Assert.Contains("PostgreSQL connection string", exception.Message);
        Assert.DoesNotContain("do-not-print", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("postgres")]
    [InlineData("Password=do-not-print")]
    public void Validate_InvalidProviderDoesNotFallBackOrExposeInput(string provider)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?> { ["Database:Provider"] = provider });
        var exception = Assert.Throws<InvalidOperationException>(() => ProductionConfigurationValidator.Validate(configuration));

        Assert.Contains("Database:Provider", exception.Message);
        Assert.DoesNotContain("do-not-print", exception.ToString());
        Assert.Null(exception.InnerException);
    }

    private static IConfiguration PostgreSqlConfiguration(string connection) => BuildConfiguration(new Dictionary<string, string?>
    {
        ["Database:Provider"] = "PostgreSql",
        ["ConnectionStrings:DefaultConnection"] = connection
    });

    private static IConfiguration BuildConfiguration(
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "api.cp6.example",
            ["ConnectionStrings:DefaultConnection"] =
                "Server=sql.cp6.internal;Database=CP6;User ID=cp6;Password=a-strong-db-password;Encrypt=True;TrustServerCertificate=False",
            ["ConnectionStrings:Redis"] =
                "cache.cp6.internal:6380,ssl=True,password=a-strong-cache-password",
            ["RabbitMQ:HostName"] = "rabbitmq.cp6.internal",
            ["RabbitMQ:Password"] = "a-strong-rabbit-password",
            ["Kafka:BootstrapServers"] = "kafka-1.cp6.internal:9092,kafka-2.cp6.internal:9092",
            ["JWT:Secret"] = "0123456789abcdef0123456789abcdef",
            ["JWT:Issuer"] = "CP6",
            ["JWT:Audience"] = "CP6.Clients",
            ["Security:Cookie:Secure"] = "true",
            ["Security:Csrf:Enabled"] = "true",
            ["Cors:AllowedOrigins:0"] = "https://web.cp6.example",
            ["Security:Sso:PublicBaseUrl"] = "https://api.cp6.example",
            ["Security:Sso:FrontendBaseUrl"] = "https://web.cp6.example",
            ["Security:TwoFactor:EmailFallbackEnabled"] = "false",
            ["Security:NativeClient:SsoGrantMinutes"] = "2",
            ["Security:NativeClient:AllowedRedirectUris:0"] = "cp6-desktop://auth/callback",
            ["Security:NativeClient:AllowedRedirectUris:1"] = "cp6-mobile://auth/callback",
            ["Security:NativeClient:Windows:LatestVersion"] = "1.2.0",
            ["Security:NativeClient:Windows:MinimumVersion"] = "1.1.0",
            ["Security:NativeClient:Windows:DownloadUrl"] =
                "https://downloads.cp6.example/windows/cp6-desktop.msix",
            ["Security:NativeClient:Windows:Sha256"] =
                "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            ["Security:NativeClient:Android:LatestVersion"] = "1.2.0",
            ["Security:NativeClient:Android:MinimumVersion"] = "1.1.0",
            ["Security:NativeClient:Android:DownloadUrl"] =
                "https://downloads.cp6.example/android/cp6-mobile.apk",
            ["Security:NativeClient:Android:Sha256"] =
                "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789",
            ["Storage:Provider"] = "S3",
            ["Storage:S3:Endpoint"] = "https://objects.cp6.example",
            ["Storage:S3:Bucket"] = "cp6-production",
            ["Storage:S3:AccessKey"] = "cp6-runtime",
            ["Storage:S3:SecretKey"] = "a-strong-object-secret",
            ["Storage:S3:ServerSideEncryption"] = "AES256",
            ["Startup:Mode"] = "Api",
            ["Startup:SkipDatabaseInitialization"] = "true"
        };

        if (overrides is not null)
        {
            foreach (var pair in overrides)
                values[pair.Key] = pair.Value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
