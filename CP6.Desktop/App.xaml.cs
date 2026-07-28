using System.Runtime.InteropServices;
using System.Windows;
using CP6.Client.Api;
using CP6.Client.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CP6.Desktop;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var apiUrl = Environment.GetEnvironmentVariable("CP6_API_URL")
                     ?? DesktopSettings.ReadApiUrl()
                     ?? "http://localhost:5177/";
        var options = new ClientOptions
        {
            ApiBaseAddress = new Uri(apiUrl.EndsWith('/') ? apiUrl : $"{apiUrl}/"),
            Platform = "windows",
            Context = new ClientContext
            {
                ClientKind = "Windows",
                DeviceId = DeviceIdentity.GetOrCreate(),
                AppVersion = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
                PlatformVersion = RuntimeInformation.OSDescription,
            },
        };

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IRefreshTokenStore, DpapiRefreshTokenStore>();
                services.AddSingleton<IPkceVerifierStore, DpapiPkceVerifierStore>();
                services.AddSingleton<ISystemBrowser, WindowsSystemBrowser>();
                services.AddSingleton<IDeviceRequestSigner, WindowsDeviceRequestSigner>();
                services.AddSingleton<IClientDeviceHeartbeatContext,
                    DesktopDeviceHeartbeatContext>();
                services.AddSingleton<ILabelPrinter, WindowsRawLabelPrinter>();
                services.AddSingleton<DesktopDeviceActivationService>();
                services.AddCp6ClientCore(options);
                services.AddSingleton<ShellViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
        await _host.StartAsync();
        await _host.Services.GetRequiredService<ClientDeviceHeartbeatLoop>()
            .StartAsync();

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();
        await window.ViewModel.InitializeAsync();

        var callback = e.Args.FirstOrDefault(x => x.StartsWith("cp6-desktop://", StringComparison.OrdinalIgnoreCase));
        if (callback != null) await window.ViewModel.CompleteSsoAsync(new Uri(callback));
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.Services.GetRequiredService<ClientDeviceHeartbeatLoop>()
                .StopAsync();
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
