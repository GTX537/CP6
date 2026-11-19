using System.Security.Cryptography;
using Android.Content.PM;
using CP6.Client.Core;

namespace CP6.Mobile;

internal sealed class AndroidClientPackageInstaller : IClientPackageInstaller
{
    public async Task OpenAsync(string packagePath, string version, string publisherSha256, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var context = Android.App.Application.Context;
        var manager = context.PackageManager ?? throw new InvalidOperationException("E-CLIENT-UPGRADE-INSTALLER");
        using var package = ReadPackage(manager, packagePath);
        if (package is null || !string.Equals(package.PackageName, context.PackageName, StringComparison.Ordinal) ||
            !string.Equals(package.VersionName, version, StringComparison.Ordinal))
            throw new InvalidOperationException("E-CLIENT-UPGRADE-PACKAGE");

        var signers = ReadSigners(package);
        if (signers is not { Count: 1 } || signers[0].ToByteArray() is not { Length: > 0 } certificate ||
            !Convert.ToHexString(SHA256.HashData(certificate)).Equals(publisherSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("E-CLIENT-UPGRADE-SIGNATURE");

        ct.ThrowIfCancellationRequested();
        if (!await Launcher.Default.OpenAsync(new OpenFileRequest("安装已验证的 CP6 更新",
                new ReadOnlyFile(packagePath, "application/vnd.android.package-archive"))))
            throw new InvalidOperationException("E-CLIENT-UPGRADE-INSTALLER");
    }

    // Android 23–27 的活动签名只能通过旧接口读取。
#pragma warning disable CS0618
    private static PackageInfo? ReadPackage(PackageManager manager, string path) =>
        OperatingSystem.IsAndroidVersionAtLeast(28)
            ? manager.GetPackageArchiveInfo(path, PackageInfoFlags.SigningCertificates)
            : manager.GetPackageArchiveInfo(path, PackageInfoFlags.Signatures);

    private static IList<Signature>? ReadSigners(PackageInfo package) =>
        OperatingSystem.IsAndroidVersionAtLeast(28)
            ? package.SigningInfo?.GetApkContentsSigners()
            : package.Signatures;
#pragma warning restore CS0618
}
