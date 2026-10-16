using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace CP6.WebApi.Observability;

internal sealed record PlatformHostAssemblyObservation(string AssemblyName, bool DefaultLoadContext,
    string? AssemblyVersion, string? InformationalVersion, string? SourceRevision, Guid? LoadedMvid,
    Guid? BackingMvid, string? AssemblySha256, string? ReadErrorCode, string? DeclaredPackageVersion = null);

internal static class PlatformHostIdentityAssemblyReader
{
    internal static IReadOnlyList<PlatformHostAssemblyObservation> Capture()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("CP6.Platform.", StringComparison.OrdinalIgnoreCase) == true).ToArray();
        var declarations = PlatformHostIdentityDependencies.Read();
        return assemblies.Select(a => Read(a, declarations)).ToArray();
    }

    private static PlatformHostAssemblyObservation Read(Assembly assembly, IReadOnlyDictionary<string, string> declarations)
    {
        var name = assembly.GetName();
        var defaultContext = AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default;
        string? information = null, revision = null, hash = null;
        Guid? loadedMvid = null, backingMvid = null;
        try
        {
            loadedMvid = assembly.ManifestModule.ModuleVersionId;
            // CustomAttributeData avoids invoking code from the observed assembly.
            var attributes = assembly.GetCustomAttributesData();
            var versions = attributes.Where(a => a.AttributeType == typeof(AssemblyInformationalVersionAttribute)).ToArray();
            if (versions.Length == 1) information = versions[0].ConstructorArguments[0].Value as string;
            var revisions = attributes.Where(a => a.AttributeType == typeof(AssemblyMetadataAttribute) &&
                a.ConstructorArguments.Count == 2 && a.ConstructorArguments[0].Value as string == "SourceRevisionId").ToArray();
            if (revisions.Length == 1) revision = revisions[0].ConstructorArguments[1].Value as string;
            if (revisions.Length > 1) revision = "invalid";
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location))
                return new(name.Name!, defaultContext, name.Version?.ToString(), information, revision, loadedMvid, null, null, "BACKING_IMAGE_UNAVAILABLE");

            // Hash and PE identity come from the same byte snapshot. Never load a path or copy
            // a DLL into a probe ALC. The MVID ties the backing image to the loaded module;
            // this is not a hash of runtime memory or a signature trust decision.
            using var stream = new FileStream(assembly.Location, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > 64 * 1024 * 1024) throw new BadImageFormatException();
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new IOException();
            using var buffer = new MemoryStream(bytes, writable: false);
            using var pe = new PEReader(buffer);
            var metadata = pe.GetMetadataReader();
            backingMvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
            hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            // A deps entry is only a declaration about a runtime asset copied beside this host.
            // Never borrow that declaration for an assembly loaded from a different directory.
            var declaredVersion = PlatformHostIdentityDependencies.IsAppBaseAsset(name.Name!, assembly.Location) &&
                declarations.TryGetValue(name.Name!, out var declared) ? declared : null;
            return new(name.Name!, defaultContext, name.Version?.ToString(), information, revision, loadedMvid, backingMvid, hash, null, declaredVersion);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or
            NotSupportedException or InvalidOperationException or System.Security.SecurityException or ArgumentException)
        {
            // No exception message or private path enters the diagnostic response.
            return new(name.Name!, defaultContext, name.Version?.ToString(), information, revision, loadedMvid, backingMvid, hash, "BACKING_IMAGE_UNAVAILABLE");
        }
    }
}
