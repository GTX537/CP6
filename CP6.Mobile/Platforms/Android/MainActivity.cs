using Android.App;
using Android.Content;
using Android.Content.PM;
using CommunityToolkit.Mvvm.Messaging;

namespace CP6.Mobile;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.ScreenSize
                           | ConfigChanges.Orientation
                           | ConfigChanges.UiMode
                           | ConfigChanges.ScreenLayout
                           | ConfigChanges.SmallestScreenSize
                           | ConfigChanges.Density)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "cp6-mobile",
    DataHost = "auth",
    DataPathPrefix = "/callback")]
public class MainActivity : MauiAppCompatActivity
{
    private ScannerBroadcastReceiver? _scannerReceiver;

    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _scannerReceiver = new ScannerBroadcastReceiver();
        var filter = new IntentFilter();
        filter.AddAction("com.cp6.scanner.DATA");
        filter.AddAction("com.symbol.datawedge.DATA");
        filter.AddAction("com.honeywell.decode.intent.action.EDIT_DATA");
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            RegisterReceiver(_scannerReceiver, filter, ReceiverFlags.Exported);
        else
#pragma warning disable CA1422
            RegisterReceiver(_scannerReceiver, filter);
#pragma warning restore CA1422
        DispatchCallback(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        DispatchCallback(intent);
    }

    private static void DispatchCallback(Intent? intent)
    {
        var raw = intent?.DataString;
        if (raw != null && Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            WeakReferenceMessenger.Default.Send(new SsoCallbackMessage(uri));
    }

    protected override void OnDestroy()
    {
        if (_scannerReceiver != null) UnregisterReceiver(_scannerReceiver);
        _scannerReceiver = null;
        base.OnDestroy();
    }
}

public sealed class ScannerBroadcastReceiver : BroadcastReceiver
{
    private static readonly string[] ExtraKeys =
    [
        "com.symbol.datawedge.data_string",
        "data",
        "barcode",
        "scanData",
        "com.honeywell.decode.intent.extra.DATA",
    ];

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent == null) return;
        var value = ExtraKeys
            .Select(intent.GetStringExtra)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (!string.IsNullOrWhiteSpace(value))
            WeakReferenceMessenger.Default.Send(new ScanBroadcastMessage(value));
    }
}
