using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CP6.Space.Application;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Space.Infrastructure;

public static class SpaceCadRemoteWorkerRegistration
{
    public static IServiceCollection AddSpaceCadRemoteWorkerProvider(
        this IServiceCollection services,
        SpaceCadRemoteWorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var endpoint = options.ValidateRuntime();
        _ = options.LoadApprovalManifest(DateTime.UtcNow);
        using var startupCertificate = LoadClientCertificate(options);

        services.AddSingleton(options);
        services.AddSingleton<ISpaceCadRemoteWorkerClient>(_ =>
            CreateClient(options, endpoint));
        services.AddScoped<SpaceCadRemoteWorkerProvider>();
        services.AddScoped(provider =>
        {
            var implementation =
                provider.GetRequiredService<SpaceCadRemoteWorkerProvider>();
            return new SpaceCadProviderRegistration(
                options.ProviderKey,
                options.ProviderVersion,
                options.DisplayName,
                options.DeploymentMode,
                options.DataBoundary,
                options.SupportsDwg,
                options.SupportsDxf,
                implementation,
                implementation);
        });
        return services;
    }

    private static HttpSpaceCadRemoteWorkerClient CreateClient(
        SpaceCadRemoteWorkerOptions options,
        Uri endpoint)
    {
        var certificate = LoadClientCertificate(options);
        var handler = new HttpClientHandler
        {
            CheckCertificateRevocationList = true,
            ServerCertificateCustomValidationCallback = (_, server, _, errors) =>
                ValidateServerCertificate(
                    server,
                    errors,
                    options.ServerCertificateSha256),
        };
        handler.ClientCertificates.Add(certificate);
        var client = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = endpoint,
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return new HttpSpaceCadRemoteWorkerClient(client, options, certificate);
    }

    private static X509Certificate2 LoadClientCertificate(
        SpaceCadRemoteWorkerOptions options)
    {
        var location = Enum.Parse<StoreLocation>(
            options.ClientCertificateStoreLocation,
            ignoreCase: true);
        var name = Enum.Parse<StoreName>(
            options.ClientCertificateStoreName,
            ignoreCase: true);
        using var store = new X509Store(name, location);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var matches = store.Certificates.Find(
            X509FindType.FindByThumbprint,
            options.ClientCertificateThumbprint,
            validOnly: true);
        if (matches.Count != 1 || !matches[0].HasPrivateKey)
        {
            throw new InvalidOperationException(
                "Exactly one valid remote CAD Worker client certificate with a private key is required.");
        }
        return new X509Certificate2(matches[0]);
    }

    private static bool ValidateServerCertificate(
        X509Certificate2? certificate,
        SslPolicyErrors errors,
        string expectedSha256)
    {
        if (certificate is null || errors != SslPolicyErrors.None)
            return false;
        var actual = Convert.ToHexString(SHA256.HashData(certificate.RawData))
            .ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actual),
            Convert.FromHexString(expectedSha256));
    }
}
