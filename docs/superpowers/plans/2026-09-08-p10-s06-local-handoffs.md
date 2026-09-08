# P10 S06 bounded public job file handoffs implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans task-by-task. The owner selected inline sequential execution; no subagents.

**Goal:** Give validation and publication commands bounded create-new public file handoffs with exact entry sets.

**Architecture:** One local adapter copies and validates all bytes/names before creating a new directory, opens every output with CreateNew and never reuses a prior directory. Exact reads reject links, extra/missing/nested entries and oversized files; they return raw bytes, not authenticity or acceptance.

**Tech Stack:** .NET 8.0.424 file APIs, existing 4-MiB control/8-MiB NuGet/64-MiB aggregate limits, xUnit real temporary filesystem tests.

---

## Scope and self-review

- Public evidence/package bytes only. No signing key, credential, artifact-authentication assertion or candidate-acceptance result.
- Require absolute paths, existing parent, no observed linked ancestor and fresh output directory. Reject cross-platform unsafe names, case collisions and file/directory collisions.
- At most 32 files, one subdirectory level, no empty files; exact reads allow no extra files or empty directories.
- Preserve native bytes without canonicalizing them. Existing contents are never deleted or overwritten.
- Partial new directories are left for the owning workflow's scoped temporary cleanup on I/O failure; this adapter does not delete arbitrary paths.
- Checks do not claim protection from a malicious process sharing the same runner identity; protected ephemeral workflow isolation remains required.
- No remote writes, deployment, global temporary cleanup or existing R2 changes.

## Files

- Create: `tools/p10/ReleaseVerifier/S06LocalFiles.cs`
- Test: `tools/p10/ReleaseVerifier.Tests/S06LocalFilesTests.cs`
- Plan: `docs/superpowers/plans/2026-09-08-p10-s06-local-handoffs.md`

### Task 1: Tests and RED

- [x] Write the complete tests and throwing WriteNew/ReadExact scaffolds.
- [x] Run the focused suite; require missing-behavior failures after compilation, no skips.

```csharp
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

public sealed class S06LocalFilesTests : IDisposable
{
    private readonly string _parent = Path.Combine(Path.GetTempPath(), "cp6-p10-local-test-" + Guid.NewGuid().ToString("N"));
    public S06LocalFilesTests() => Directory.CreateDirectory(_parent);
    private string Stage => Path.Combine(_parent, "stage");

    [Fact]
    public void Public_raw_bytes_roundtrip_without_canonicalization()
    {
        var bytes = "{ \"native\": true }\n"u8.ToArray();
        S06LocalFiles.WriteNew(Stage, new Dictionary<string, ReadOnlyMemory<byte>>
        {
            ["producer.json"] = bytes,
            ["objects/abc"] = new byte[] { 1, 2, 3 }
        });
        var read = S06LocalFiles.ReadExact(Stage, ["producer.json", "objects/abc"]);
        Assert.Equal(bytes, read["producer.json"].ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, read["objects/abc"].ToArray());
    }

    [Fact]
    public void Existing_directory_is_never_reused_or_overwritten()
    {
        S06LocalFiles.WriteNew(Stage, Files());
        Assert.Equal("local-stage-exists", Assert.Throws<Cp6ReleaseContractException>(() =>
            S06LocalFiles.WriteNew(Stage, new Dictionary<string, ReadOnlyMemory<byte>> { ["data.json"] = "{\"new\":true}"u8.ToArray() })).Code);
        Assert.Equal("{}"u8.ToArray(), File.ReadAllBytes(Path.Combine(Stage, "data.json")));
    }

    [Fact]
    public void Existing_file_is_never_replaced_by_a_directory()
    {
        File.WriteAllBytes(Stage, new byte[] { 42 });
        Assert.Equal("local-stage-exists", Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.WriteNew(Stage, Files())).Code);
        Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(Stage));
    }

    [Theory]
    [InlineData("../escape.json")]
    [InlineData("/escape.json")]
    [InlineData("x\\escape.json")]
    [InlineData("x/y/z.json")]
    [InlineData("x//y.json")]
    [InlineData("data.json\n")]
    [InlineData("data..json")]
    [InlineData("data.json.")]
    [InlineData("con.json")]
    [InlineData("x/LPT1")]
    public void Unsafe_names_fail_before_creating_the_directory(string name)
    {
        var files = new Dictionary<string, ReadOnlyMemory<byte>> { [name] = "{}"u8.ToArray() };
        Assert.Equal("local-stage-name", Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.WriteNew(Stage, files)).Code);
        Assert.False(Directory.Exists(Stage));
    }

    [Theory]
    [InlineData("case-collision")]
    [InlineData("file-directory-collision")]
    [InlineData("empty")]
    [InlineData("too-many")]
    public void Inconsistent_entry_sets_fail_before_writing(string mutation)
    {
        var files = Files();
        if (mutation == "case-collision") files.Add("Data.json", "{}"u8.ToArray());
        if (mutation == "file-directory-collision") files.Add("data.json/child", "{}"u8.ToArray());
        if (mutation == "empty") files.Clear();
        if (mutation == "too-many")
            for (var i = 0; i < 32; i++) files.Add("extra" + i + ".json", "{}"u8.ToArray());
        Assert.Equal("local-stage-files", Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.WriteNew(Stage, files)).Code);
        Assert.False(Directory.Exists(Stage));
    }

    [Theory]
    [InlineData("data.json", 0)]
    [InlineData("data.json", 4194305)]
    [InlineData("packages/package.nupkg", 8388609)]
    public void Per_file_byte_limits_fail_before_writing(string name, int length)
    {
        Assert.Equal("local-stage-size", Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.WriteNew(Stage,
            new Dictionary<string, ReadOnlyMemory<byte>> { [name] = new byte[length] })).Code);
        Assert.False(Directory.Exists(Stage));
    }

    [Fact]
    public void Aggregate_byte_limit_is_checked_before_copying_or_writing()
    {
        var payload = new byte[4194304];
        var files = Enumerable.Range(0, 17).ToDictionary(i => "file" + i + ".json",
            _ => (ReadOnlyMemory<byte>)payload);
        Assert.Equal("local-stage-size", Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.WriteNew(Stage, files)).Code);
        Assert.False(Directory.Exists(Stage));
    }

    [Fact]
    public void NuGet_payloads_use_the_existing_eight_MiB_bound()
    {
        var bytes = new byte[4194305];
        S06LocalFiles.WriteNew(Stage, new Dictionary<string, ReadOnlyMemory<byte>> { ["packages/package.nupkg"] = bytes });
        Assert.Equal(bytes, S06LocalFiles.ReadExact(Stage, ["packages/package.nupkg"])["packages/package.nupkg"].ToArray());
    }

    [Theory]
    [InlineData("extra-file")]
    [InlineData("missing-file")]
    [InlineData("empty-directory")]
    [InlineData("nested-directory")]
    [InlineData("oversized-file")]
    public void Read_requires_the_exact_bounded_entry_set(string mutation)
    {
        S06LocalFiles.WriteNew(Stage, Files());
        if (mutation == "extra-file") File.WriteAllText(Path.Combine(Stage, "extra.json"), "{}");
        if (mutation == "missing-file") File.Delete(Path.Combine(Stage, "data.json"));
        if (mutation == "empty-directory") Directory.CreateDirectory(Path.Combine(Stage, "empty"));
        if (mutation == "nested-directory") Directory.CreateDirectory(Path.Combine(Stage, "nested", "deep"));
        if (mutation == "oversized-file") File.WriteAllBytes(Path.Combine(Stage, "data.json"), new byte[4194305]);
        Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.ReadExact(Stage, ["data.json"]));
    }

    [Fact]
    public void Relative_output_paths_are_not_resolved_against_ambient_working_directories() =>
        Assert.Equal("local-stage-path", Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.WriteNew("stage", Files())).Code);

    [Fact]
    public void Missing_input_paths_are_sanitized()
    {
        var error = Assert.Throws<Cp6ReleaseContractException>(() => S06LocalFiles.ReadExact(
            Path.Combine(_parent, "private-marker"), ["data.json"]));
        Assert.Equal("local-stage-missing", error.Code);
        Assert.DoesNotContain("private-marker", error.Message);
        Assert.Null(error.InnerException);
    }

    private static Dictionary<string, ReadOnlyMemory<byte>> Files() => new() { ["data.json"] = "{}"u8.ToArray() };

    public void Dispose()
    {
        var actual = Path.GetFullPath(_parent);
        var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetDirectoryName(actual) != parent || !Path.GetFileName(actual).StartsWith("cp6-p10-local-test-", StringComparison.Ordinal))
            throw new InvalidOperationException("Test cleanup escaped its owned directory.");
        Directory.Delete(actual, recursive: true);
    }
}
```

### Task 2: Implement and GREEN

- [x] Replace scaffolds with the adapter below.
- [x] Rerun focused tests and require all passing.

```csharp
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
```

### Task 3: Verify and commit

- [x] Run the full Release suite and format check; inspect actual outcomes.
- [x] Review exact three-file diff and plan/source agreement; scan for secret/debug residue.
- [ ] Stage these three files and commit `feat(p10): bound create-new public workflow file handoffs`.

```powershell
$env:DOTNET_ROOT = 'C:/Users/tt/.dotnet'
$env:DOTNET_HOST_PATH = 'C:/Users/tt/.dotnet/dotnet.exe'
$env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
$env:P10_COSIGN_PATH = 'C:/Users/tt/AppData/Local/Temp/cp6-p10-cosign-c4ab1329239f4721a3f8f5ac741ce3c9/cosign-windows-amd64.exe'
$env:P10_FORMAL_PACKAGE_ROOT = 'D:/CP6.Platform-worktrees/p10-formal-schema-parity/artifacts/p10-0.10.1-publication/windows/feed-readback-packages'
$env:P10_GITHUB_READ_TOKEN = gh auth token
$env:P10_FEED_READ_TOKEN = $env:P10_GITHUB_READ_TOKEN
try {
  & $env:DOTNET_HOST_PATH test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~S06LocalFilesTests
  if ($LASTEXITCODE -ne 0) { throw 'Focused verification failed.' }
  & $env:DOTNET_HOST_PATH test ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj -c Release --no-restore
  if ($LASTEXITCODE -ne 0) { throw 'Full verification failed.' }
  & $env:DOTNET_HOST_PATH format ReleaseVerifier.Tests/CP6.P10.ReleaseVerifier.Tests.csproj --no-restore --verify-no-changes
  if ($LASTEXITCODE -ne 0) { throw 'Format verification failed.' }
} finally {
  Remove-Item Env:P10_GITHUB_READ_TOKEN -ErrorAction SilentlyContinue
  Remove-Item Env:P10_FEED_READ_TOKEN -ErrorAction SilentlyContinue
}
```

```powershell
git diff --check
git add -- docs/superpowers/plans/2026-09-08-p10-s06-local-handoffs.md tools/p10/ReleaseVerifier/S06LocalFiles.cs tools/p10/ReleaseVerifier.Tests/S06LocalFilesTests.cs
git diff --cached --check
git diff --cached --stat
git commit -m "feat(p10): bound create-new public workflow file handoffs"
```

## Verification outcome (2026-09-08)

- RED: 29 expected throwing-scaffold failures, 0 passes/skips after compilation.
- Focused GREEN: 29 passed, 0 failed, 0 skipped.
- Full Release suite: 1,407 passed, 0 failed, 0 skipped in 47 seconds.
- The subsequent formatting gate identified one dictionary-initializer newline in the new test. Changed only that whitespace in the test and plan, then formatting and rebuilt focused 29-case suite both passed.
- Full tests were run before the whitespace-only correction; focused tests and format were rerun after it. No remote writes or candidate-acceptance claims.
