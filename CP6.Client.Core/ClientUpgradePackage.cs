using System.Security.Cryptography;
using System.Xml;

namespace CP6.Client.Core;

public interface IClientPackageInstaller
{
    Task OpenAsync(string packagePath, string version, string publisherSha256, CancellationToken ct);
}

internal sealed class UnsupportedClientPackageInstaller : IClientPackageInstaller
{
    public Task OpenAsync(string packagePath, string version, string publisherSha256, CancellationToken ct)
        => throw new InvalidOperationException("E-CLIENT-UPGRADE-INSTALLER");
}

public sealed class ClientUpgradePackage(IHttpClientFactory clients, ClientOptions options, IClientPackageInstaller installer)
{
    internal async Task OpenAsync(ClientUpgradeDecision decision, Func<bool> stillApproved, CancellationToken ct)
    {
        var uri = decision.DownloadUri!;
        var hash = decision.Sha256!;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMinutes(5));
        using var client = clients.CreateClient(ClientServiceCollectionExtensions.UpgradeDownloadClient);

        if (Path.GetExtension(uri.AbsolutePath).Equals(".appinstaller", StringComparison.OrdinalIgnoreCase))
        {
            var descriptor = await DownloadAsync(client, uri, hash, budget.Token);
            try
            {
                uri = ReadMainPackageUri(descriptor, decision.PackageSha256);
                hash = decision.PackageSha256!;
            }
            finally { File.Delete(descriptor); }
        }

        var package = await DownloadAsync(client, uri, hash, budget.Token);
        try
        {
            budget.Token.ThrowIfCancellationRequested();
            if (!stillApproved())
                throw new InvalidOperationException("E-CLIENT-UPGRADE-METADATA");
            await installer.OpenAsync(package, decision.LatestVersion, decision.PublisherSha256!, budget.Token);
        }
        catch
        {
            File.Delete(package);
            throw;
        }
    }

    private async Task<string> DownloadAsync(HttpClient client, Uri uri, string expectedHash, CancellationToken ct)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode != System.Net.HttpStatusCode.OK || !uri.Equals(response.RequestMessage?.RequestUri))
            throw new InvalidOperationException("E-CLIENT-UPGRADE-DOWNLOAD");

        Directory.CreateDirectory(options.UpgradeStagingDirectory);
        var path = Path.Combine(options.UpgradeStagingDirectory, $"{Guid.NewGuid():N}{Path.GetExtension(uri.AbsolutePath)}");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous);
        try
        {
            await using (stream)
            {
                await using var body = await response.Content.ReadAsStreamAsync(ct);
                await body.CopyToAsync(stream, ct);
                stream.Position = 0;
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
                if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("E-CLIENT-UPGRADE-HASH");
            }
            return path;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    private static Uri ReadMainPackageUri(string descriptor, string? expectedHash)
    {
        try
        {
            using var reader = XmlReader.Create(descriptor, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                MaxCharactersInDocument = 128 * 1024
            });
            var root = System.Xml.Linq.XDocument.Load(reader).Root;
            if (root?.Name.LocalName != "AppInstaller")
                throw new InvalidOperationException("E-CLIENT-UPGRADE-METADATA");
            var packages = root.Elements(root.Name.Namespace + "MainPackage").ToArray();
            if (packages.Length != 1 || !ClientUpgradeService.TryValidateDownload(
                    (string?)packages[0].Attribute("Uri"), expectedHash, "windows", out var uri) ||
                !Path.GetExtension(uri!.AbsolutePath).Equals(".msix", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("E-CLIENT-UPGRADE-METADATA");
            return uri;
        }
        catch (XmlException)
        {
            throw new InvalidOperationException("E-CLIENT-UPGRADE-METADATA");
        }
    }
}
