using System.Text.RegularExpressions;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Public job handoffs only: create-new directories/files, bounded reads and exact flat-or-one-level names.
// This is not an authenticity boundary against another process with the same runner identity.
internal static class S06LocalFiles
{
    internal static void WriteNew(string directory, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files)
    {
        var owned = Snapshot(files);
        try
        {
            var root = Root(directory);
            var parent = Path.GetDirectoryName(root);
            Require(parent is not null && Directory.Exists(parent), "local-stage-parent");
            RequireNoLinks(parent!);
            Require(!Path.Exists(root), "local-stage-exists");
            Directory.CreateDirectory(root);
            RequireNoLinks(root);
            foreach (var pair in owned.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var target = Path.Combine(root, pair.Key.Replace('/', Path.DirectorySeparatorChar));
                var folder = Path.GetDirectoryName(target)!;
                if (folder != root) Directory.CreateDirectory(folder);
                RequireNoLinks(folder);
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                output.Write(pair.Value.Span);
                output.Flush(flushToDisk: true);
            }
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { throw Error("local-stage-io"); }
    }

    internal static IReadOnlyDictionary<string, ReadOnlyMemory<byte>> ReadExact(string directory,
        IReadOnlyCollection<string> expectedNames)
    {
        var names = expectedNames.ToArray();
        RequireNames(names);
        try
        {
            var root = Root(directory);
            Require(Directory.Exists(root), "local-stage-missing");
            RequireNoLinks(root);
            var actual = new List<string>();
            foreach (var entry in Directory.EnumerateFileSystemEntries(root))
            {
                Require((File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0, "local-stage-link");
                if (Directory.Exists(entry))
                {
                    var children = Directory.EnumerateFileSystemEntries(entry).Take(33).ToArray();
                    Require(children.Length is > 0 and <= 32, "local-stage-files");
                    foreach (var child in children)
                    {
                        Require((File.GetAttributes(child) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0,
                            "local-stage-files");
                        actual.Add(Path.GetRelativePath(root, child).Replace(Path.DirectorySeparatorChar, '/'));
                    }
                }
                else actual.Add(Path.GetFileName(entry));
                Require(actual.Count <= 32, "local-stage-files");
            }
            Require(actual.Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal),
                StringComparer.Ordinal), "local-stage-files");
            var files = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
            long total = 0;
            foreach (var name in names)
            {
                using var input = new FileStream(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)),
                    FileMode.Open, FileAccess.Read, FileShare.Read);
                Require(input.Length > 0 && input.Length <= Limit(name), "local-stage-size");
                total += input.Length;
                Require(total <= WorkflowArtifactSelection.MaximumArchiveBytes, "local-stage-size");
                var bytes = new byte[(int)input.Length];
                input.ReadExactly(bytes);
                Require(input.ReadByte() == -1, "local-stage-size");
                files.Add(name, bytes);
            }
            return Snapshot(files);
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { throw Error("local-stage-io"); }
    }

    private static Dictionary<string, ReadOnlyMemory<byte>> Snapshot(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files)
    {
        RequireNames(files.Keys.ToArray());
        Require(files.All(p => p.Value.Length > 0 && p.Value.Length <= Limit(p.Key)) &&
            files.Values.Sum(v => (long)v.Length) <= WorkflowArtifactSelection.MaximumArchiveBytes, "local-stage-size");
        return files.ToDictionary(p => p.Key, p => (ReadOnlyMemory<byte>)p.Value.ToArray(), StringComparer.Ordinal);
    }

    private static int Limit(string name) => name.EndsWith(".nupkg", StringComparison.Ordinal) ?
        FormalNuGetVerifier.MaximumPackageBytes : Cp6DeterministicJson.MaximumBytes;

    private static string Root(string directory)
    {
        Require(!string.IsNullOrEmpty(directory) && directory.Length <= 4096 &&
            !directory.Any(char.IsControl) && Path.IsPathFullyQualified(directory), "local-stage-path");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }

    private static void RequireNames(IReadOnlyCollection<string> names)
    {
        Require(names.Count is > 0 and <= 32 && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Count,
            "local-stage-files");
        foreach (var name in names)
        {
            Require(name.Length <= 256 && !name.Contains("..", StringComparison.Ordinal) &&
                Regex.IsMatch(name, "^[A-Za-z0-9][A-Za-z0-9.-]*(/[A-Za-z0-9][A-Za-z0-9.-]*)?\\z",
                    RegexOptions.CultureInvariant), "local-stage-name");
            foreach (var part in name.Split('/'))
                Require(!part.EndsWith('.') && !Regex.IsMatch(part, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\\.|$)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), "local-stage-name");
            Require(!name.Contains('/') || !names.Contains(name[..name.IndexOf('/')], StringComparer.OrdinalIgnoreCase),
                "local-stage-files");
        }
    }

    private static void RequireNoLinks(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            Require(directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0, "local-stage-link");
    }
}
