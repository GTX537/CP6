using System.Reflection;
using System.Text.Json;

namespace CP6.WebApi.Observability;

// The host deps file declares a package/runtime-asset association. It does not authenticate
// the package. Only the independently supplied expected DLL hash pins the observed bytes.
internal static class PlatformHostIdentityDependencies
{
    private const int MaximumBytes = 16 * 1024 * 1024;

    internal static bool IsAppBaseAsset(string assemblyName, string location) =>
        string.Equals(Path.GetFullPath(location), Path.Combine(Path.GetFullPath(AppContext.BaseDirectory), assemblyName + ".dll"),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static IReadOnlyDictionary<string, string> Read()
    {
        try
        {
            var entry = Assembly.GetEntryAssembly();
            var name = entry?.GetName().Name;
            if (name is null || entry!.IsDynamic || string.IsNullOrEmpty(entry.Location) || !IsAppBaseAsset(name, entry.Location))
                return new Dictionary<string, string>();
            // The path is derived only from the actual entry assembly and application base.
            using var stream = new FileStream(Path.Combine(AppContext.BaseDirectory, name + ".deps.json"),
                FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > MaximumBytes) return new Dictionary<string, string>();
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) return new Dictionary<string, string>();
            return Parse(bytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or
            InvalidOperationException or System.Security.SecurityException or ArgumentException)
        {
            return new Dictionary<string, string>();
        }
    }

    internal static IReadOnlyDictionary<string, string> Parse(ReadOnlyMemory<byte> bytes)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (bytes.Length is <= 0 or > MaximumBytes) return result;
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (HasDuplicateProperties(root)) return result;
            if (!Property(root, "runtimeTarget", out var runtimeTarget) || !Property(runtimeTarget, "name", out var targetName) ||
                targetName.ValueKind != JsonValueKind.String || !Property(root, "targets", out var targets) ||
                !Property(targets, targetName.GetString()!, out var target) || target.ValueKind != JsonValueKind.Object ||
                !Property(root, "libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var id in PlatformHostIdentityManifest.PackageIds)
            {
                var entries = target.EnumerateObject().Where(p => p.Name.StartsWith(id + "/", StringComparison.Ordinal)).ToArray();
                if (entries.Length != 1) continue;
                var entry = entries[0];
                var version = PlatformHostIdentityShape.PackageVersion(entry.Name[(id.Length + 1)..]);
                if (version is null || !Property(libraries, entry.Name, out var library) ||
                    !Property(library, "type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "package" ||
                    !Property(entry.Value, "runtime", out var assets) || assets.ValueKind != JsonValueKind.Object)
                    continue;
                var matchingAssets = assets.EnumerateObject().Where(a => AssetName(a.Name) == id + ".dll").ToArray();
                if (matchingAssets.Length != 1 || matchingAssets[0].Value.ValueKind != JsonValueKind.Object) continue;

                // Ambiguous ownership cannot supply a declaration, even when one entry has
                // the expected package ID. Check every runtime asset in the selected target.
                var owners = 0;
                foreach (var candidate in target.EnumerateObject())
                {
                    if (Property(candidate.Value, "runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Object)
                        owners += runtime.EnumerateObject().Count(a => AssetName(a.Name) == id + ".dll");
                }
                if (owners == 1) result.Add(id, version);
            }
            return result;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    private static string? AssetName(string path)
    {
        if (path.Length > 512 || path.Contains('\\') || path.Contains(':') || path.StartsWith('/')) return null;
        var parts = path.Split('/');
        return parts.Any(p => p is "" or "." or "..") ? null : parts[^1];
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!names.Add(property.Name) || HasDuplicateProperties(property.Value)) return true;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                if (HasDuplicateProperties(item)) return true;
        }
        return false;
    }

    private static bool Property(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;
        var found = false;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name != name) continue;
            if (found) return false;
            value = property.Value;
            found = true;
        }
        return found;
    }
}
