using CP6.Client.Core;
using System.Security.Cryptography;
using System.Text.Json;

namespace CP6.Mobile;

public sealed class SecureStorageRefreshTokenStore : IRefreshTokenStore
{
    private const string Key = "cp6.refresh-token";
    public Task<string?> ReadAsync(CancellationToken ct = default)
        => SecureStorage.Default.GetAsync(Key);

    public Task WriteAsync(string refreshToken, CancellationToken ct = default)
        => SecureStorage.Default.SetAsync(Key, refreshToken);

    public Task ClearAsync(CancellationToken ct = default)
    {
        SecureStorage.Default.Remove(Key);
        return Task.CompletedTask;
    }
}

public sealed class SecureStoragePkceVerifierStore : IPkceVerifierStore
{
    private const string Key = "cp6.sso-pkce-verifier";

    public Task WriteAsync(string verifier, CancellationToken ct = default)
        => SecureStorage.Default.SetAsync(Key, verifier);

    public async Task<string?> TakeAsync(CancellationToken ct = default)
    {
        var verifier = await SecureStorage.Default.GetAsync(Key);
        SecureStorage.Default.Remove(Key);
        return verifier;
    }
}

public sealed class MauiSystemBrowser : ISystemBrowser
{
    public async Task OpenAsync(Uri uri, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred))
            throw new InvalidOperationException("E-CLIENT-BROWSER-OPEN");
    }
}

internal static class DeviceIdentity
{
    private const string Key = "cp6.device-id";

    public static string GetOrCreate()
    {
        var value = Preferences.Default.Get(Key, string.Empty);
        if (!string.IsNullOrWhiteSpace(value)) return value;
        value = $"android-{Guid.NewGuid():N}";
        Preferences.Default.Set(Key, value);
        return value;
    }
}

public sealed class FileOfflineMoveProgressStore : IOfflineMoveProgressStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string PathName => Path.Combine(FileSystem.AppDataDirectory, "active-move-progress.json");

    public async Task<OfflineMoveProgress?> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(PathName)) return null;
        await using var stream = File.OpenRead(PathName);
        return await JsonSerializer.DeserializeAsync<OfflineMoveProgress>(stream, Json, ct);
    }

    public async Task WriteAsync(OfflineMoveProgress progress, CancellationToken ct = default)
    {
        var temp = $"{PathName}.tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, progress, Json, ct);
        File.Move(temp, PathName, true);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        if (File.Exists(PathName)) File.Delete(PathName);
        return Task.CompletedTask;
    }
}

public sealed class SecureStorageDeviceRequestSigner : IDeviceRequestSigner
{
    private const string PrivateKeyName = "cp6.device-signing-key";

    public async Task<string> GetOrCreatePublicKeyAsync(CancellationToken ct = default)
    {
        using var key = await ReadOrCreateAsync();
        return PemEncoding.WriteString("PUBLIC KEY", key.ExportSubjectPublicKeyInfo());
    }

    public async Task<string> SignAsync(byte[] payload, CancellationToken ct = default)
    {
        using var key = await ReadOrCreateAsync();
        return Convert.ToBase64String(
            key.SignData(payload, HashAlgorithmName.SHA256));
    }

    private static async Task<ECDsa> ReadOrCreateAsync()
    {
        var encoded = await SecureStorage.Default.GetAsync(PrivateKeyName);
        var key = ECDsa.Create();
        if (!string.IsNullOrWhiteSpace(encoded))
        {
            key.ImportPkcs8PrivateKey(Convert.FromBase64String(encoded), out _);
            return key;
        }

        key.GenerateKey(ECCurve.NamedCurves.nistP256);
        await SecureStorage.Default.SetAsync(
            PrivateKeyName,
            Convert.ToBase64String(key.ExportPkcs8PrivateKey()));
        return key;
    }
}
