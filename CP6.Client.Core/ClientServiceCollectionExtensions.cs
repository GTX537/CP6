using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CP6.Client.Core;

public static class ClientServiceCollectionExtensions
{
    public const string RawClient = "CP6.Raw";
    public const string AuthenticatedClient = "CP6.Authenticated";

    public static IServiceCollection AddCp6ClientCore(
        this IServiceCollection services,
        ClientOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IClientSessionService, ClientSessionService>();
        services.AddSingleton<ILanguageService, LanguageService>();
        services.AddSingleton<ClientBootstrapService>();
        services.AddSingleton<ClientAccessGate>();
        services.AddSingleton<ClientUpgradeService>();
        services.AddSingleton<WmsTaskService>();
        services.AddSingleton<WmsRealtimeService>();
        services.AddSingleton<NativeSsoService>();
        services.TryAddSingleton<IDeviceRequestSigner, UnsupportedDeviceRequestSigner>();
        services.AddSingleton<ClientDeviceHeartbeatService>();
        services.TryAddSingleton<ILabelPrinter, UnsupportedLabelPrinter>();
        services.AddSingleton<LabelGatewayService>();
        services.TryAddSingleton<IPkceVerifierStore, MemoryPkceVerifierStore>();
        services.AddTransient<BearerTokenHandler>();
        services.AddTransient<ClientBusinessAccessHandler>();
        services.AddTransient<DynamicApiEndpointHandler>();

        services.AddHttpClient(RawClient, client =>
        {
            client.BaseAddress = options.ApiBaseAddress;
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddHttpMessageHandler<DynamicApiEndpointHandler>();
        services.AddHttpClient(AuthenticatedClient, client =>
        {
            client.BaseAddress = options.ApiBaseAddress;
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddHttpMessageHandler<DynamicApiEndpointHandler>()
            .AddHttpMessageHandler<ClientBusinessAccessHandler>()
            .AddHttpMessageHandler<BearerTokenHandler>();
        services.AddSingleton(sp => new CP6.Client.Api.Cp6ApiClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(AuthenticatedClient)));
        return services;
    }
}

internal sealed class UnsupportedLabelPrinter : ILabelPrinter
{
    public Task PrintAsync(CP6.Client.Api.LabelJob job, CancellationToken ct = default)
        => throw new PlatformNotSupportedException("Label printing is not configured.");
}

internal sealed class UnsupportedDeviceRequestSigner : IDeviceRequestSigner
{
    public Task<string> GetOrCreatePublicKeyAsync(CancellationToken ct = default)
        => throw new PlatformNotSupportedException("Device signing is not configured.");

    public Task<string> SignAsync(byte[] payload, CancellationToken ct = default)
        => throw new PlatformNotSupportedException("Device signing is not configured.");
}
