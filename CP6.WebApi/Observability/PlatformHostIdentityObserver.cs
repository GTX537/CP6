using System.Diagnostics;

namespace CP6.WebApi.Observability;

public sealed class PlatformHostIdentityObserver
{
    private readonly PlatformHostIdentityManifest _manifest;
    private readonly Func<IReadOnlyList<PlatformHostAssemblyObservation>> _read;
    private readonly DateTimeOffset _startedAt;

    internal PlatformHostIdentityObserver(PlatformHostIdentityManifest manifest)
        : this(manifest, PlatformHostIdentityAssemblyReader.Capture) { }

    // Only tests can inject facts; production registration always uses the live process reader.
    internal PlatformHostIdentityObserver(PlatformHostIdentityManifest manifest,
        Func<IReadOnlyList<PlatformHostAssemblyObservation>> read)
    {
        _manifest = manifest;
        _read = read;
        using var process = Process.GetCurrentProcess();
        _startedAt = process.StartTime.ToUniversalTime();
    }

    public PlatformHostIdentityReport Capture()
    {
        var facts = _read();
        var reasons = _manifest.ReasonCodes.ToList();
        if (facts.Any(f => f.AssemblyName.StartsWith("CP6.Platform.", StringComparison.OrdinalIgnoreCase) &&
            !PlatformHostIdentityManifest.PackageIds.Contains(f.AssemblyName, StringComparer.Ordinal)))
            reasons.Add("UNEXPECTED_PLATFORM_ASSEMBLY");
        var modules = PlatformHostIdentityManifest.PackageIds.Select(id => Compare(id,
            facts.Where(f => f.AssemblyName == id).ToArray())).ToArray();
        var mismatch = reasons.Any(r => r is "DUPLICATE_EXPECTED_PACKAGE" or "UNEXPECTED_EXPECTED_PACKAGE" or "UNEXPECTED_PLATFORM_ASSEMBLY") ||
            modules.Any(m => m.Status == "Mismatch");
        var status = mismatch ? "Mismatch" : reasons.Count > 0 || modules.Any(m => m.Status == "Unknown") ? "Unknown" : "Matched";
        return new(Environment.ProcessId, _startedAt, DateTimeOffset.UtcNow, status, modules, reasons,
            new(false, mismatch ? "Mismatch" : "Unknown", mismatch ? "IDENTITY_MISMATCH" : "COMPATIBILITY_EVIDENCE_MISSING"));
    }

    private PlatformHostModuleIdentity Compare(string id, PlatformHostAssemblyObservation[] observations)
    {
        var expected = _manifest.Expected[id];
        var reasons = new List<string>();
        var mismatch = false;
        if (expected is null) reasons.Add("EXPECTED_IDENTITY_INVALID");
        if (observations.Length == 0)
        {
            reasons.Add("ASSEMBLY_NOT_LOADED");
            return new(id, "Unknown", expected, null, reasons);
        }
        if (observations.Length > 1)
        {
            reasons.Add("DUPLICATE_LOADED_ASSEMBLY");
            return new(id, "Mismatch", expected, null, reasons);
        }
        var fact = observations[0];
        void Conflict(string reason) { mismatch = true; reasons.Add(reason); }
        if (!fact.DefaultLoadContext) Conflict("NON_DEFAULT_LOAD_CONTEXT");
        if (fact.LoadedMvid is null || fact.LoadedMvid == Guid.Empty ||
            fact.BackingMvid is null || fact.BackingMvid == Guid.Empty)
            reasons.Add("MVID_UNAVAILABLE");
        else if (fact.LoadedMvid != fact.BackingMvid) Conflict("BACKING_MVID_MISMATCH");

        var info = fact.InformationalVersion;
        var separator = info is { Length: <= 105 } ? info.LastIndexOf('+') : -1;
        var version = separator > 0 ? PlatformHostIdentityShape.PackageVersion(info![..separator]) : PlatformHostIdentityShape.PackageVersion(info);
        var infoSource = separator > 0 ? PlatformHostIdentityShape.Hex(info![(separator + 1)..], 40) : null;
        var revision = PlatformHostIdentityShape.Hex(fact.SourceRevision, 40);
        var source = infoSource ?? revision;
        var hash = PlatformHostIdentityShape.Hex(fact.AssemblySha256, 64);
        var declaredPackageVersion = PlatformHostIdentityShape.PackageVersion(fact.DeclaredPackageVersion);
        var assemblyVersion = PlatformHostIdentityShape.AssemblyVersion(fact.AssemblyVersion);
        if (declaredPackageVersion is null) reasons.Add("PACKAGE_DECLARATION_UNAVAILABLE");
        if (version is null || source is null || assemblyVersion is null || (separator >= 0 && infoSource is null) ||
            (fact.SourceRevision is not null && revision is null)) reasons.Add("ASSEMBLY_METADATA_INVALID");
        if (infoSource is not null && revision is not null && infoSource != revision) Conflict("SOURCE_METADATA_CONFLICT");
        if (hash is null || fact.ReadErrorCode is not null) reasons.Add("BACKING_IMAGE_UNAVAILABLE");
        if (expected is not null)
        {
            if (declaredPackageVersion is not null && expected.PackageVersion != declaredPackageVersion) Conflict("PACKAGE_VERSION_MISMATCH");
            if (source is not null && expected.SourceSha != source) Conflict("SOURCE_SHA_MISMATCH");
            if (hash is not null && expected.AssemblySha256 != hash) Conflict("ASSEMBLY_SHA256_MISMATCH");
        }
        return new(id, mismatch ? "Mismatch" : reasons.Count > 0 ? "Unknown" : "Matched", expected,
            new(assemblyVersion, version, declaredPackageVersion, source, hash, fact.LoadedMvid, fact.BackingMvid, fact.DefaultLoadContext), reasons);
    }
}
