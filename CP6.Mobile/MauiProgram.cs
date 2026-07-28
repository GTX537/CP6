using System.Reflection;
using CP6.Client.Api;
using CP6.Client.Core;
using Microsoft.Extensions.Logging;
using ZXing.Net.Maui.Controls;

namespace CP6.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var apiUrl = Environment.GetEnvironmentVariable("CP6_API_URL")
                     ?? Preferences.Default.Get("cp6.api-url", "http://10.0.2.2:5177/");
        var options = new ClientOptions
        {
            ApiBaseAddress = new Uri(apiUrl.EndsWith('/') ? apiUrl : $"{apiUrl}/"),
            Platform = "android",
            Context = new ClientContext
            {
                ClientKind = "Android",
                DeviceId = DeviceIdentity.GetOrCreate(),
                AppVersion = AppInfo.Current.VersionString,
                PlatformVersion = DeviceInfo.Current.VersionString,
            },
        };

        var builder = MauiApp.CreateBuilder()
            .UseMauiApp<App>()
            .UseBarcodeReader();
        builder.Logging.AddDebug();
        builder.Services.AddSingleton<IRefreshTokenStore, SecureStorageRefreshTokenStore>();
        builder.Services.AddSingleton<IPkceVerifierStore, SecureStoragePkceVerifierStore>();
        builder.Services.AddSingleton<IDeviceRequestSigner, SecureStorageDeviceRequestSigner>();
        builder.Services.AddSingleton<IClientDeviceHeartbeatContext,
            MobileDeviceHeartbeatContext>();
        builder.Services.AddSingleton<ISystemBrowser, MauiSystemBrowser>();
        builder.Services.AddSingleton<IOfflineMoveProgressStore, FileOfflineMoveProgressStore>();
        builder.Services.AddCp6ClientCore(options);
        builder.Services.AddSingleton<MobileClientState>();
        builder.Services.AddSingleton<DeviceActivationService>();
        builder.Services.AddTransient(
            _ => new ScannerInputProcessor(MobileScannerSettings.Read()));
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<TaskListViewModel>();
        builder.Services.AddTransient<TaskDetailViewModel>();
        builder.Services.AddTransient<MoveScanViewModel>();
        builder.Services.AddTransient<UpgradeViewModel>();
        builder.Services.AddTransient<DeviceActivationViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<TaskListPage>();
        builder.Services.AddTransient<TaskDetailPage>();
        builder.Services.AddTransient<MoveScanPage>();
        builder.Services.AddTransient<UpgradePage>();
        builder.Services.AddTransient<DeviceActivationPage>();
        return builder.Build();
    }
}
