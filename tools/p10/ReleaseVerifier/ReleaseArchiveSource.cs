using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

internal static class ReleaseArchiveSource
{
    internal static async Task<DownloadedReleaseDocument> ReadAsync(string name, string token,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pinned = PinnedReleaseDocument.Get(name);
        using var client = new GitHubReadClient(token);
        var contents = await client.ReadAsync(GitHubReadTarget.Archive(pinned), cancellationToken);
        var bytes = Decode(pinned, contents);
        return new(pinned, bytes, DateTimeOffset.UtcNow);
    }

    internal static byte[] Decode(PinnedReleaseDocument pinned, JsonElement contents)
    {
        Require(Text(contents, "type") == "file" && Text(contents, "path") == pinned.Path &&
            Text(contents, "sha") == pinned.GitBlobSha && Number(contents, "size") == pinned.ByteLength &&
            Text(contents, "encoding") == "base64", "github-archive-identity");
        var encoded = Text(contents, "content");
        Require(encoded.Length <= 65536, "github-archive-size");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(encoded); }
        catch (FormatException) { throw GitHubWirePolicy.Error("github-archive-content"); }
        Require(bytes.Length == pinned.ByteLength, "github-archive-size");
        Require(Cp6DeterministicJson.Sha256Hex(bytes) == pinned.Sha256, "github-archive-hash");
        var header = Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\0");
        Require(Convert.ToHexString(SHA1.HashData(header.Concat(bytes).ToArray())).ToLowerInvariant() == pinned.GitBlobSha,
            "github-archive-hash");
        // Return original bytes, never canonicalized or rewritten. Semantic acceptance is a separate layer.
        return bytes;
    }
}

internal sealed class DownloadedReleaseDocument
{
    private readonly byte[] _bytes;
    internal DownloadedReleaseDocument(PinnedReleaseDocument document, byte[] bytes, DateTimeOffset retrievedAtUtc)
    {
        Document = document;
        _bytes = bytes.ToArray();
        RetrievedAtUtc = retrievedAtUtc;
    }

    internal PinnedReleaseDocument Document { get; }
    internal DateTimeOffset RetrievedAtUtc { get; }
    internal byte[] CopyBytes() => _bytes.ToArray();
}
