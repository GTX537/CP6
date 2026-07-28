using CP6.Client.Core;

namespace CP6.Mobile;

/// <summary>
/// Strongly typed facade for mobile language keys so MAUI can compile XAML bindings.
/// </summary>
public sealed class MobileText(ILanguageService language)
{
    public string Tenant => language["client.tenant"];
    public string UserName => language["login.username"];
    public string Password => language["login.password"];
    public string Login => language["login.button"];
    public string Sso => language["client.sso"];
    public string TwoFactor => language["client.twoFactor"];
    public string Verify => language["client.verify"];
    public string EmailOtp => language["client.emailOtp"];
    public string Language => language["client.language"];
    public string MobileTasksTitle => language["wms.mobile.title"];
    public string Refresh => language["wms.common.refresh"];
    public string Logout => language["layout.logout"];
    public string ClaimStart => language["client.claimStart"];
    public string SimulatedScan => language["client.simulatedScan"];
    public string ScanSource => language["client.scanSource"];
    public string ScanProduct => language["client.scanProduct"];
    public string ScanTarget => language["client.scanTarget"];
    public string ScanPlaceholder => language["wms.mobile.scan.ph"];
    public string SubmitScan => language["client.submitScan"];
    public string ConfirmQuantity => language["client.confirmQuantity"];
    public string Quantity => language["wms.common.qty"];
    public string CompleteMove => language["client.completeMove"];
    public string ReloadTask => language["client.reloadTask"];
    public string RestartScan => language["client.restartScan"];
    public string ReadyComplete => language["client.readyComplete"];
    public string Completed => language["client.completed"];
    public string UpgradeRequired => language["client.upgradeRequired"];
    public string DownloadUpdate => language["client.downloadUpdate"];
    public string BusinessDisabled => language["client.businessDisabled"];
    public string StartupBlocked => language["client.startupBlocked"];
    public string RetryStartup => language["client.retryStartup"];
    public string CurrentVersion => language["client.currentVersion"];
    public string LatestVersion => language["client.latestVersion"];
    public string MinimumVersion => language["client.minimumVersion"];
    public string ReleaseHash => language["client.releaseHash"];
}
