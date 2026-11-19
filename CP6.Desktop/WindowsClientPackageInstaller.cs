using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Linq;
using CP6.Client.Core;

namespace CP6.Desktop;

internal sealed class WindowsClientPackageInstaller : IClientPackageInstaller
{
    public Task OpenAsync(string packagePath, string version, string publisherSha256, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!HasExpectedIdentity(packagePath, version))
            throw new InvalidOperationException("E-CLIENT-UPGRADE-PACKAGE");
        if (!HasTrustedSignature(packagePath, publisherSha256))
            throw new InvalidOperationException("E-CLIENT-UPGRADE-SIGNATURE");

        ct.ThrowIfCancellationRequested();
        using var installer = Process.Start(new ProcessStartInfo(packagePath) { UseShellExecute = true });
        if (installer is null)
            throw new InvalidOperationException("E-CLIENT-UPGRADE-INSTALLER");
        return Task.CompletedTask;
    }

    private static bool HasExpectedIdentity(string path, string version)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var manifest = zip.GetEntry("AppxManifest.xml");
            if (manifest is null) return false;
            using var input = manifest.Open();
            using var xml = XmlReader.Create(input, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                MaxCharactersInDocument = 256 * 1024
            });
            var root = XDocument.Load(xml).Root;
            var identity = root?.Element(root.Name.Namespace + "Identity");
            return (string?)identity?.Attribute("Name") == "CP6.Desktop" &&
                   Version.TryParse((string?)identity.Attribute("Version"), out var value) &&
                   value.Revision >= 0 &&
                   version.Split('+', 2)[0] == value.ToString(3);
        }
        catch (InvalidDataException) { return false; }
        catch (XmlException) { return false; }
    }

    private static bool HasTrustedSignature(string path, string expectedPublisher)
    {
        if (!VerifyWindowsTrust(path)) return false;
        try
        {
            using var certificate = X509Certificate.CreateFromSignedFile(path);
            return certificate.GetCertHashString(HashAlgorithmName.SHA256)
                .Equals(expectedPublisher, StringComparison.OrdinalIgnoreCase);
        }
        catch (CryptographicException) { return false; }
    }

    private static bool VerifyWindowsTrust(string path)
    {
        var nativePath = Marshal.StringToCoTaskMemUni(path);
        var filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        try
        {
            var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = nativePath };
            Marshal.StructureToPtr(file, filePointer, false);
            var data = new TrustData
            {
                Size = (uint)Marshal.SizeOf<TrustData>(),
                UiChoice = 2,     // WTD_UI_NONE
                ObjectChoice = 1, // WTD_CHOICE_FILE
                File = filePointer
            };
            var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");
            return WinVerifyTrust(-1, in action, ref data) == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(filePointer);
            Marshal.FreeCoTaskMem(nativePath);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern int WinVerifyTrust(nint window, in Guid action, ref TrustData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustFile
    {
        public uint Size;
        public nint Path;
        public nint Handle;
        public nint KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public nint PolicyCallback;
        public nint SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint ObjectChoice;
        public nint File;
        public uint StateAction;
        public nint StateData;
        public nint Url;
        public uint ProviderFlags;
        public uint UiContext;
        public nint SignatureSettings;
    }
}
