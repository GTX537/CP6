using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace CP6.WebApi.Configuration;

/// <summary>
/// Rejects unsafe or incomplete production configuration before the application starts
/// accepting requests. Validation messages deliberately name configuration keys only and
/// never echo configured values because several of those values are secrets.
/// </summary>
internal static partial class ProductionConfigurationValidator
{
    private static readonly string[] RequiredNativeRedirectUris =
    [
        "cp6-desktop://auth/callback",
        "cp6-mobile://auth/callback"
    ];

    public static void Validate(IConfiguration configuration)
    {
        var errors = GetErrors(configuration);
        if (errors.Count == 0)
            return;

        throw new InvalidOperationException(
            "Production configuration validation failed:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, errors.Select(error => $"- {error}")));
    }

    internal static IReadOnlyList<string> GetErrors(IConfiguration configuration)
    {
        var errors = new List<string>();

        ValidateAllowedHosts(configuration, errors);
        ValidateSqlServer(configuration, errors);
        ValidateRedis(configuration, errors);
        ValidateMessaging(configuration, errors);
        ValidateAuthentication(configuration, errors);
        ValidateCors(configuration, errors);
        ValidateNativeClient(configuration, errors);
        ValidateEmail(configuration, errors);

        return errors;
    }

    private static void ValidateAllowedHosts(IConfiguration configuration, ICollection<string> errors)
    {
        var allowedHosts = configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(allowedHosts) ||
            allowedHosts.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(host => host == "*" || IsLocalHost(host)))
        {
            errors.Add("AllowedHosts must contain explicit production host names.");
        }
    }

    private static void ValidateSqlServer(IConfiguration configuration, ICollection<string> errors)
    {
        const string key = "ConnectionStrings:DefaultConnection";
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            errors.Add($"{key} is required.");
            return;
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (IsLocalHost(builder.DataSource))
                errors.Add($"{key} must not target a local development SQL Server.");
            if (builder.TrustServerCertificate)
                errors.Add($"{key} must set TrustServerCertificate=false.");
            if (builder.Encrypt == SqlConnectionEncryptOption.Optional)
                errors.Add($"{key} must enable SQL transport encryption.");
        }
        catch (ArgumentException)
        {
            errors.Add($"{key} is not a valid SQL Server connection string.");
        }
    }

    private static void ValidateRedis(IConfiguration configuration, ICollection<string> errors)
    {
        const string key = "ConnectionStrings:Redis";
        var connectionString = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            errors.Add($"{key} is required in production.");
            return;
        }

        var segments = connectionString.Split(
            ',',
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            errors.Add($"{key} is not a valid Redis connection string.");
            return;
        }

        var endpoint = segments[0];
        if (IsLocalHost(endpoint))
            errors.Add($"{key} must not target a local development Redis instance.");
        if (!segments.Skip(1).Any(segment =>
                segment.Equals("ssl=true", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"{key} must enable Redis transport encryption with ssl=true.");
        }
    }

    private static void ValidateMessaging(IConfiguration configuration, ICollection<string> errors)
    {
        if (IsLocalHost(configuration["RabbitMQ:HostName"]))
            errors.Add("RabbitMQ:HostName must not target a local development broker.");
        if (IsUnsafeSecret(configuration["RabbitMQ:Password"], minimumLength: 12))
            errors.Add("RabbitMQ:Password must be supplied through a production secret source.");

        var kafkaServers = configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrWhiteSpace(kafkaServers) ||
            kafkaServers.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(IsLocalHost))
        {
            errors.Add("Kafka:BootstrapServers must contain non-local production brokers.");
        }
    }

    private static void ValidateAuthentication(IConfiguration configuration, ICollection<string> errors)
    {
        if (IsUnsafeSecret(configuration["JWT:Secret"], minimumLength: 32))
            errors.Add("JWT:Secret must be a non-placeholder secret with at least 32 characters.");
        if (string.IsNullOrWhiteSpace(configuration["JWT:Issuer"]))
            errors.Add("JWT:Issuer is required.");
        if (string.IsNullOrWhiteSpace(configuration["JWT:Audience"]))
            errors.Add("JWT:Audience is required.");
        if (!configuration.GetValue<bool>("Security:Cookie:Secure"))
            errors.Add("Security:Cookie:Secure must be true.");
        if (!configuration.GetValue<bool>("Security:Csrf:Enabled"))
            errors.Add("Security:Csrf:Enabled must be true.");

        ValidateOptionalHttpsUrl(configuration, "Security:Sso:PublicBaseUrl", errors);
        ValidateOptionalHttpsUrl(configuration, "Security:Sso:FrontendBaseUrl", errors);
    }

    private static void ValidateCors(IConfiguration configuration, ICollection<string> errors)
    {
        const string key = "Cors:AllowedOrigins";
        var origins = configuration.GetSection(key)
            .GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
        if (origins.Length == 0)
        {
            errors.Add($"{key} must contain at least one HTTPS origin.");
            return;
        }

        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                !string.IsNullOrEmpty(uri.AbsolutePath.Trim('/')))
            {
                errors.Add($"{key} must contain HTTPS origins without paths, queries, or fragments.");
                return;
            }
        }
    }

    private static void ValidateNativeClient(IConfiguration configuration, ICollection<string> errors)
    {
        var grantMinutes = configuration.GetValue<int>("Security:NativeClient:SsoGrantMinutes");
        if (grantMinutes is < 1 or > 2)
            errors.Add("Security:NativeClient:SsoGrantMinutes must be between 1 and 2 minutes.");

        var redirects = configuration
            .GetSection("Security:NativeClient:AllowedRedirectUris")
            .GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
        if (redirects.Length != RequiredNativeRedirectUris.Length ||
            RequiredNativeRedirectUris.Except(redirects, StringComparer.Ordinal).Any() ||
            redirects.Except(RequiredNativeRedirectUris, StringComparer.Ordinal).Any())
        {
            errors.Add(
                "Security:NativeClient:AllowedRedirectUris must contain only the approved desktop and mobile callbacks.");
        }

        ValidateClientRelease(configuration, "Windows", [".msix", ".appinstaller"], errors);
        ValidateClientRelease(configuration, "Android", [".apk"], errors);
    }

    private static void ValidateClientRelease(
        IConfiguration configuration,
        string clientKind,
        string[] allowedExtensions,
        ICollection<string> errors)
    {
        var prefix = $"Security:NativeClient:{clientKind}";
        var latestText = configuration[$"{prefix}:LatestVersion"];
        var minimumText = configuration[$"{prefix}:MinimumVersion"];

        var latestValid = Version.TryParse(latestText, out var latest);
        var minimumValid = Version.TryParse(minimumText, out var minimum);
        if (!latestValid)
            errors.Add($"{prefix}:LatestVersion must be a valid version.");
        if (!minimumValid)
            errors.Add($"{prefix}:MinimumVersion must be a valid version.");
        if (latestValid && minimumValid && minimum! > latest!)
            errors.Add($"{prefix}:MinimumVersion must not exceed LatestVersion.");

        var downloadUrl = configuration[$"{prefix}:DownloadUrl"];
        if (!TryGetHttpsUri(downloadUrl, out var downloadUri) ||
            !allowedExtensions.Any(extension =>
                downloadUri!.AbsolutePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"{prefix}:DownloadUrl must be an HTTPS URL for an approved package type.");
        }

        var sha256 = configuration[$"{prefix}:Sha256"];
        if (string.IsNullOrWhiteSpace(sha256) || !Sha256Regex().IsMatch(sha256))
            errors.Add($"{prefix}:Sha256 must be a 64-character hexadecimal digest.");
    }

    private static void ValidateEmail(IConfiguration configuration, ICollection<string> errors)
    {
        if (configuration.GetValue<bool>("Security:TwoFactor:EmailFallbackEnabled") &&
            string.IsNullOrWhiteSpace(configuration["Email:Smtp:Host"]))
        {
            errors.Add(
                "Email:Smtp:Host is required when Security:TwoFactor:EmailFallbackEnabled is true.");
        }
    }

    private static void ValidateOptionalHttpsUrl(
        IConfiguration configuration,
        string key,
        ICollection<string> errors)
    {
        var value = configuration[key];
        if (!string.IsNullOrWhiteSpace(value) && !TryGetHttpsUri(value, out _))
            errors.Add($"{key} must be an HTTPS URL when configured.");
    }

    private static bool TryGetHttpsUri(string? value, out Uri? uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            !string.IsNullOrWhiteSpace(uri.Host) &&
            string.IsNullOrEmpty(uri.UserInfo))
        {
            return true;
        }

        uri = null;
        return false;
    }

    private static bool IsUnsafeSecret(string? value, int minimumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < minimumLength)
            return true;

        return value.StartsWith("__", StringComparison.Ordinal) ||
               value.Equals("changeme", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("change-me", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("password", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLocalHost(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return true;

        var host = endpoint.Trim();
        if (host.StartsWith('['))
        {
            var closingBracket = host.IndexOf(']');
            if (closingBracket > 0)
                host = host[1..closingBracket];
        }
        else
        {
            var slashIndex = host.IndexOf('\\');
            if (slashIndex >= 0)
                host = host[..slashIndex];

            var colonIndex = host.LastIndexOf(':');
            if (colonIndex > 0)
                host = host[..colonIndex];
        }

        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               host.Equals(".", StringComparison.Ordinal) ||
               host.Equals("(local)", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("(localdb)", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("127.0.0.1", StringComparison.Ordinal) ||
               host.Equals("::1", StringComparison.Ordinal);
    }

    [GeneratedRegex("^[A-Fa-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}
