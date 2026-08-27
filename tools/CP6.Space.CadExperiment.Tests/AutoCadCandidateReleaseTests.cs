using CP6.Space.CadExperiment;
using CP6.Space.CadWorker.AutoCadCandidate;

namespace CP6.Space.CadExperiment.Tests;

public sealed class AutoCadCandidateReleaseTests
{
    [Fact]
    public async Task Release_manifest_freezes_payload_Core_Console_and_identity()
    {
        using var fixture = new TemporaryDirectory();
        var paths = CreatePayload(fixture.Path);

        var release = await AutoCadCandidateReleaseIdentity.CreateAsync(
            paths.PayloadRoot,
            "1.0.0",
            new string('a', 40),
            "win-x64",
            paths.CoreConsolePath,
            "25.0.58.0.0");

        Assert.Equal(AutoCadCandidateReleaseIdentity.ProviderKey, release.Manifest.ProviderKey);
        Assert.Equal("1.0.0", release.Manifest.ReleaseVersion);
        Assert.Equal("25.0.58.0.0", release.Manifest.AutoCadCoreConsoleVersion);
        Assert.Equal(2, release.Manifest.Files.Length);
        Assert.DoesNotContain("development", release.ProviderVersion, StringComparison.Ordinal);
        Assert.Contains(
            release.WorkerReleaseSha256[..12],
            release.ProviderVersion,
            StringComparison.Ordinal);
        Assert.Contains("autocad.25.0.58.0.0", release.ProviderVersion, StringComparison.Ordinal);
        Assert.Contains("dxf.1.1.0", release.ProviderVersion, StringComparison.Ordinal);

        var loaded = await AutoCadCandidateReleaseIdentity.LoadVerifiedAsync(
            paths.ManifestPath,
            release.WorkerReleaseSha256,
            paths.PayloadRoot,
            paths.CoreConsolePath,
            "25.0.58.0.0",
            "win-x64");

        Assert.Equal(release.ProviderVersion, loaded.ProviderVersion);
        Assert.Equal(release.WorkerReleaseSha256, loaded.WorkerReleaseSha256);
    }

    [Fact]
    public async Task Release_verification_rejects_payload_tampering()
    {
        using var fixture = new TemporaryDirectory();
        var paths = CreatePayload(fixture.Path);
        var release = await CreateReleaseAsync(paths);
        await File.AppendAllTextAsync(paths.EntryAssemblyPath, "tampered");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            AutoCadCandidateReleaseIdentity.LoadVerifiedAsync(
                paths.ManifestPath,
                release.WorkerReleaseSha256,
                paths.PayloadRoot,
                paths.CoreConsolePath,
                "25.0.58.0.0",
                "win-x64"));

        Assert.Contains("length changed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Release_verification_rejects_Core_Console_tampering()
    {
        using var fixture = new TemporaryDirectory();
        var paths = CreatePayload(fixture.Path);
        var release = await CreateReleaseAsync(paths);
        await File.AppendAllTextAsync(paths.CoreConsolePath, "tampered");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            AutoCadCandidateReleaseIdentity.LoadVerifiedAsync(
                paths.ManifestPath,
                release.WorkerReleaseSha256,
                paths.PayloadRoot,
                paths.CoreConsolePath,
                "25.0.58.0.0",
                "win-x64"));

        Assert.Contains("Core Console hash", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Release_verification_rejects_manifest_hash_mismatch()
    {
        using var fixture = new TemporaryDirectory();
        var paths = CreatePayload(fixture.Path);
        _ = await CreateReleaseAsync(paths);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            AutoCadCandidateReleaseIdentity.LoadVerifiedAsync(
                paths.ManifestPath,
                new string('b', 64),
                paths.PayloadRoot,
                paths.CoreConsolePath,
                "25.0.58.0.0",
                "win-x64"));

        Assert.Contains("Manifest hash", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Worker_uses_the_verified_non_development_release_identity()
    {
        using var fixture = new TemporaryDirectory();
        var paths = CreatePayload(fixture.Path);
        var release = await CreateReleaseAsync(paths);

        var exporter = new ReleaseBoundAutoCadDwgExporter(
            new VersionOnlyExporter("25.0.58.0.0"),
            paths.CoreConsolePath,
            release.Manifest.AutoCadCoreConsoleVersion,
            release.Manifest.AutoCadCoreConsoleSha256);
        var service = new AutoCadCandidateConversionService(
            exporter,
            Path.Combine(fixture.Path, "work"),
            TimeSpan.FromMinutes(1),
            maximumConcurrency: 1,
            release);

        Assert.Equal(AutoCadCandidateReleaseIdentity.ProviderKey, service.ProviderKey);
        Assert.Equal(release.ProviderVersion, service.ProviderVersion);
        Assert.DoesNotContain("development", service.ProviderKey, StringComparison.Ordinal);
        Assert.DoesNotContain("development", service.ProviderVersion, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Release_bound_exporter_rechecks_Core_Console_before_each_DWG()
    {
        using var fixture = new TemporaryDirectory();
        var paths = CreatePayload(fixture.Path);
        var release = await CreateReleaseAsync(paths);
        var inner = new VersionOnlyExporter("25.0.58.0.0");
        var exporter = new ReleaseBoundAutoCadDwgExporter(
            inner,
            paths.CoreConsolePath,
            release.Manifest.AutoCadCoreConsoleVersion,
            release.Manifest.AutoCadCoreConsoleSha256);

        await exporter.ExportDxfAsync("input.dwg", "output.dxf");
        Assert.Equal(1, inner.CallCount);
        await File.AppendAllTextAsync(paths.CoreConsolePath, "tampered-after-startup");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            exporter.ExportDxfAsync("input.dwg", "output.dxf"));

        Assert.Contains("changed after", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task Release_Worker_rejects_an_unbound_exporter()
    {
        using var fixture = new TemporaryDirectory();
        var paths = CreatePayload(fixture.Path);
        var release = await CreateReleaseAsync(paths);

        var exception = Assert.Throws<ArgumentException>(() =>
            new AutoCadCandidateConversionService(
                new VersionOnlyExporter("25.0.58.0.0"),
                Path.Combine(fixture.Path, "work"),
                TimeSpan.FromMinutes(1),
                maximumConcurrency: 1,
                release));

        Assert.Contains("release-bound", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Release_Worker_rejects_an_exporter_bound_to_another_Core_hash()
    {
        using var fixture = new TemporaryDirectory();
        var paths = CreatePayload(fixture.Path);
        var release = await CreateReleaseAsync(paths);
        var exporter = new ReleaseBoundAutoCadDwgExporter(
            new VersionOnlyExporter("25.0.58.0.0"),
            paths.CoreConsolePath,
            release.Manifest.AutoCadCoreConsoleVersion,
            new string('d', 64));

        var exception = Assert.Throws<InvalidDataException>(() =>
            new AutoCadCandidateConversionService(
                exporter,
                Path.Combine(fixture.Path, "work"),
                TimeSpan.FromMinutes(1),
                maximumConcurrency: 1,
                release));

        Assert.Contains("exporter hash", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_rejects_a_root_that_would_exceed_Core_Console_path_limits()
    {
        using var fixture = new TemporaryDirectory();
        var segmentLength = AutoCadCandidateConversionService.MaximumWorkRootPathLength
                            - fixture.Path.Length;
        Assert.True(segmentLength > 0);
        var longRoot = Path.Combine(
            fixture.Path,
            new string('x', segmentLength));
        Assert.True(
            longRoot.Length > AutoCadCandidateConversionService.MaximumWorkRootPathLength);

        var exception = Assert.Throws<ArgumentException>(() =>
            new AutoCadCandidateConversionService(
                new VersionOnlyExporter("25.0.58.0.0"),
                longRoot,
                TimeSpan.FromMinutes(1),
                maximumConcurrency: 1));

        Assert.Contains("120 characters", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(longRoot));
    }

    private static Task<AutoCadCandidateReleaseIdentity> CreateReleaseAsync(
        ReleasePaths paths) =>
        AutoCadCandidateReleaseIdentity.CreateAsync(
            paths.PayloadRoot,
            "1.0.0-rc.1",
            new string('c', 40),
            "win-x64",
            paths.CoreConsolePath,
            "25.0.58.0.0");

    private static ReleasePaths CreatePayload(string fixtureRoot)
    {
        var payloadRoot = Path.Combine(fixtureRoot, "payload");
        var dependencyRoot = Path.Combine(payloadRoot, "dependencies");
        var coreRoot = Path.Combine(fixtureRoot, "autocad");
        Directory.CreateDirectory(dependencyRoot);
        Directory.CreateDirectory(coreRoot);
        var entryAssemblyPath = Path.Combine(
            payloadRoot,
            "CP6.Space.CadWorker.AutoCadCandidate.dll");
        File.WriteAllText(entryAssemblyPath, "worker-entry");
        File.WriteAllText(Path.Combine(dependencyRoot, "dependency.dll"), "dependency");
        var coreConsolePath = Path.Combine(coreRoot, "accoreconsole.exe");
        File.WriteAllText(coreConsolePath, "autocad-core-console");
        return new ReleasePaths(
            payloadRoot,
            entryAssemblyPath,
            coreConsolePath,
            Path.Combine(
                payloadRoot,
                AutoCadCandidateReleaseIdentity.ManifestFileName));
    }

    private sealed record ReleasePaths(
        string PayloadRoot,
        string EntryAssemblyPath,
        string CoreConsolePath,
        string ManifestPath);

    private sealed class VersionOnlyExporter(string providerVersion) : IAutoCadDwgExporter
    {
        public string ProviderVersion => providerVersion;
        public int CallCount { get; private set; }

        public Task ExportDxfAsync(
            string inputDwgPath,
            string outputDxfPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
