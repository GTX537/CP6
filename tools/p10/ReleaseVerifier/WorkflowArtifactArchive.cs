using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text.RegularExpressions;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.GitHubEvidenceChecks;

namespace CP6.P10.ReleaseVerifier;

// Hash before opening ZIP. Files remain in memory; no path extraction or executable launch.
internal static class WorkflowArtifactArchive
{
    internal static IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Read(ReadOnlyMemory<byte> bytes,
        WorkflowArtifactMetadata metadata)
    {
        Require(bytes.Length is > 0 and <= WorkflowArtifactSelection.MaximumArchiveBytes &&
            bytes.Length == metadata.ByteLength, "artifact-size");
        var owned = bytes.ToArray();
        Require("sha256:" + Cp6DeterministicJson.Sha256Hex(owned) == metadata.Digest, "artifact-digest");
        try
        {
            using var input = new MemoryStream(owned, writable: false);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read);
            Require(archive.Entries.Count is > 0 and <= 32, "artifact-entry-set");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                var fileType = (entry.ExternalAttributes >> 16) & 0xf000;
                Require(Regex.IsMatch(entry.FullName, "^(artifact-index\\.json|locator\\.json|locator\\.sigstore\\.json|objects/[0-9a-f]{64})\\z",
                    RegexOptions.CultureInvariant) && names.Add(entry.FullName) &&
                    (fileType is 0 or 0x8000) && (entry.ExternalAttributes & 0x10) == 0, "artifact-entry-name");
                Require(entry.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes, "artifact-entry-size");
                total += entry.Length;
                Require(total <= WorkflowArtifactSelection.MaximumArchiveBytes, "artifact-entry-size");
            }
            var files = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                using var stream = entry.Open();
                var payload = new byte[(int)entry.Length + 1];
                var count = stream.ReadAtLeast(payload, payload.Length, throwOnEndOfStream: false);
                Require(count == entry.Length, "artifact-entry-size");
                files.Add(entry.FullName, payload.AsMemory(0, count));
            }
            return new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(files);
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw GitHubWirePolicy.Error("artifact-zip");
        }
    }
}
