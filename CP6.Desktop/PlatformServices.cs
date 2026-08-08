using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Client.Core;
using CP6.Client.Api;
using System.Runtime.InteropServices;
using System.Net.Http;

namespace CP6.Desktop;

public sealed class DpapiRefreshTokenStore : IRefreshTokenStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CP6.Desktop.RefreshToken.v1");
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CP6",
        "desktop.refresh");

    public async Task<string?> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return null;
        var protectedBytes = await File.ReadAllBytesAsync(_path, ct);
        var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    public async Task WriteAsync(string refreshToken, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(refreshToken),
            Entropy,
            DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_path, protectedBytes, ct);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        if (File.Exists(_path)) File.Delete(_path);
        return Task.CompletedTask;
    }
}

public sealed class DpapiPkceVerifierStore : IPkceVerifierStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CP6.Desktop.SsoPkce.v1");
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CP6",
        "desktop.sso-pkce");

    public async Task WriteAsync(string verifier, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(verifier),
            Entropy,
            DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_path, protectedBytes, ct);
    }

    public async Task<string?> TakeAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return null;
        var protectedBytes = await File.ReadAllBytesAsync(_path, ct);
        File.Delete(_path);
        var bytes = ProtectedData.Unprotect(
            protectedBytes,
            Entropy,
            DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}

public sealed class WindowsSystemBrowser : ISystemBrowser
{
    public Task OpenAsync(Uri uri, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            }) is null)
        {
            throw new InvalidOperationException("E-CLIENT-BROWSER-OPEN");
        }
        return Task.CompletedTask;
    }
}

internal static class DeviceIdentity
{
    public static string GetOrCreate()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CP6",
            "desktop.device");
        if (File.Exists(path)) return File.ReadAllText(path).Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var value = $"win-{Guid.NewGuid():N}";
        File.WriteAllText(path, value);
        return value;
    }
}

public sealed class WindowsDeviceRequestSigner : IDeviceRequestSigner
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CP6.Desktop.DeviceKey.v1");
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CP6",
        "desktop.device-key");

    public async Task<string> GetOrCreatePublicKeyAsync(CancellationToken ct = default)
    {
        using var key = await ReadOrCreateAsync(ct);
        return PemEncoding.WriteString("PUBLIC KEY", key.ExportSubjectPublicKeyInfo());
    }

    public async Task<string> SignAsync(byte[] payload, CancellationToken ct = default)
    {
        using var key = await ReadOrCreateAsync(ct);
        return Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256));
    }

    private async Task<ECDsa> ReadOrCreateAsync(CancellationToken ct)
    {
        var key = ECDsa.Create();
        if (File.Exists(_path))
        {
            var protectedBytes = await File.ReadAllBytesAsync(_path, ct);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            key.ImportPkcs8PrivateKey(bytes, out _);
            return key;
        }
        key.GenerateKey(ECCurve.NamedCurves.nistP256);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var encrypted = ProtectedData.Protect(
            key.ExportPkcs8PrivateKey(), Entropy, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_path, encrypted, ct);
        return key;
    }
}

public static class DesktopSettings
{
    private static readonly string ApiPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CP6",
        "desktop.api-url");
    private static readonly string ActivationPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CP6",
        "desktop.device-activated");

    public static string? ReadApiUrl()
        => File.Exists(ApiPath) ? File.ReadAllText(ApiPath).Trim() : null;

    public static void WriteApiUrl(string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ApiPath)!);
        File.WriteAllText(ApiPath, value);
    }

    public static bool IsDeviceActivated()
    {
        if (!File.Exists(ActivationPath))
            return !string.IsNullOrWhiteSpace(ReadApiUrl());

        return bool.TryParse(File.ReadAllText(ActivationPath), out var activated)
               && activated;
    }

    public static void WriteDeviceActivation(bool activated)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ActivationPath)!);
        File.WriteAllText(ActivationPath, activated.ToString());
    }
}

public sealed class DesktopDeviceActivationService
{
    private readonly Cp6ApiClient _api;
    private readonly ClientOptions _options;
    private readonly IDeviceRequestSigner _signer;
    private readonly ClientDeviceHeartbeatLoop _heartbeat;

    public DesktopDeviceActivationService(
        IHttpClientFactory clients,
        ClientOptions options,
        IDeviceRequestSigner signer,
        ClientDeviceHeartbeatLoop heartbeat)
    {
        _api = new Cp6ApiClient(clients.CreateClient(ClientServiceCollectionExtensions.RawClient));
        _options = options;
        _signer = signer;
        _heartbeat = heartbeat;
    }

    public async Task<ActivatedClientDevice> ActivateAsync(
        string payload,
        CancellationToken ct = default)
    {
        var ticket = Parse(payload);
        var previous = _options.ApiBaseAddress;
        _options.ApiBaseAddress = ticket.Server;
        try
        {
            var result = await _api.ActivateDeviceAsync(new ActivateClientDeviceRequest
            {
                TenantCode = ticket.Tenant,
                ActivationToken = ticket.Token,
                DeviceId = _options.Context.DeviceId,
                PublicKey = await _signer.GetOrCreatePublicKeyAsync(ct),
                Platform = "Windows",
                AppVersion = _options.Context.AppVersion,
                PlatformVersion = _options.Context.PlatformVersion,
            }, ct);
            DesktopSettings.WriteApiUrl(ticket.Server.AbsoluteUri);
            DesktopSettings.WriteDeviceActivation(true);
            _heartbeat.RequestImmediate();
            return result;
        }
        catch
        {
            _options.ApiBaseAddress = previous;
            throw;
        }
    }

    private static (Uri Server, string Tenant, string Token) Parse(string payload)
    {
        if (!Uri.TryCreate(payload.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "cp6-activate", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("WM-DEVICE-ACTIVATION-QR-INVALID");
        var values = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2)).Where(x => x.Length == 2)
            .ToDictionary(
                x => Uri.UnescapeDataString(x[0]),
                x => Uri.UnescapeDataString(x[1]),
                StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue("server", out var server)
            || !Uri.TryCreate(server, UriKind.Absolute, out var serverUri)
            || !values.TryGetValue("tenant", out var tenant)
            || !values.TryGetValue("token", out var token))
            throw new InvalidOperationException("WM-DEVICE-ACTIVATION-QR-INVALID");
        return (
            new Uri(serverUri.AbsoluteUri.EndsWith('/') ? serverUri.AbsoluteUri : $"{serverUri}/"),
            tenant.Trim(),
            token.Trim());
    }
}

public sealed class WindowsRawLabelPrinter : ILabelPrinter
{
    public Task PrintAsync(LabelJob job, CancellationToken ct = default)
    {
        var rendered = Render(job.TemplateBody, job.PayloadJson);
        var bytes = job.Format.ToUpperInvariant() switch
        {
            "ZPL" or "TSPL" => Encoding.UTF8.GetBytes(rendered),
            "PDF" => DecodePdf(rendered),
            _ => throw new InvalidOperationException("WM-LABEL-FORMAT-UNSUPPORTED"),
        };
        var printer = string.IsNullOrWhiteSpace(job.PrinterName)
            ? NativePrinter.GetDefaultPrinterName()
            : job.PrinterName;
        NativePrinter.Write(printer, bytes, $"CP6 {job.JobNo}");
        return Task.CompletedTask;
    }

    private static string Render(string template, string payloadJson)
    {
        using var payload = JsonDocument.Parse(payloadJson);
        foreach (var property in payload.RootElement.EnumerateObject())
            template = template.Replace(
                $"{{{{{property.Name}}}}}",
                property.Value.ToString(),
                StringComparison.Ordinal);
        return template;
    }

    private static byte[] DecodePdf(string rendered)
    {
        const string prefix = "data:application/pdf;base64,";
        if (rendered.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            rendered = rendered[prefix.Length..];
        try
        {
            var bytes = Convert.FromBase64String(rendered.Trim());
            if (bytes.Length >= 4 && Encoding.ASCII.GetString(bytes, 0, 4) == "%PDF")
                return bytes;
        }
        catch (FormatException) { }
        throw new InvalidOperationException("WM-LABEL-PDF-BASE64-INVALID");
    }
}

internal static class NativePrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocumentName = string.Empty;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType = "RAW";
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);
    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int StartDocPrinter(IntPtr handle, int level, [In] DocInfo info);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr handle, IntPtr data, int count, out int written);
    [DllImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetDefaultPrinter(StringBuilder name, ref int count);

    public static string GetDefaultPrinterName()
    {
        var count = 0;
        _ = GetDefaultPrinter(new StringBuilder(), ref count);
        var name = new StringBuilder(count);
        if (!GetDefaultPrinter(name, ref count) || name.Length == 0)
            throw new InvalidOperationException("WM-LABEL-DEFAULT-PRINTER-NOT-FOUND");
        return name.ToString();
    }

    public static void Write(string printerName, byte[] bytes, string documentName)
    {
        if (!OpenPrinter(printerName, out var handle, IntPtr.Zero))
            throw new InvalidOperationException($"WM-LABEL-PRINTER-OPEN:{Marshal.GetLastWin32Error()}");
        var data = IntPtr.Zero;
        try
        {
            if (StartDocPrinter(handle, 1, new DocInfo { DocumentName = documentName }) == 0
                || !StartPagePrinter(handle))
                throw new InvalidOperationException($"WM-LABEL-PRINT-START:{Marshal.GetLastWin32Error()}");
            data = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, data, bytes.Length);
            if (!WritePrinter(handle, data, bytes.Length, out var written) || written != bytes.Length)
                throw new InvalidOperationException($"WM-LABEL-PRINT-WRITE:{Marshal.GetLastWin32Error()}");
            _ = EndPagePrinter(handle);
            _ = EndDocPrinter(handle);
        }
        finally
        {
            if (data != IntPtr.Zero) Marshal.FreeCoTaskMem(data);
            _ = ClosePrinter(handle);
        }
    }
}
