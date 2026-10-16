using System.Text.RegularExpressions;

namespace CP6.WebApi.Observability;

internal sealed class PlatformHostIdentityManifest
{
    internal static IReadOnlyList<string> PackageIds { get; } = Array.AsReadOnly(new[]
    {
        "CP6.Platform.Abstractions", "CP6.Platform.AspNetCore", "CP6.Platform.Contracts",
        "CP6.Platform.EntityFramework", "CP6.Platform.Messaging"
    });

    internal IReadOnlyDictionary<string, PlatformHostIdentityValue?> Expected { get; }
    internal IReadOnlyList<string> ReasonCodes { get; }

    private PlatformHostIdentityManifest(Dictionary<string, PlatformHostIdentityValue?> expected, List<string> reasons)
    {
        Expected = expected;
        ReasonCodes = reasons.AsReadOnly();
    }

    internal static PlatformHostIdentityManifest Read(IConfiguration configuration)
    {
        var expected = PackageIds.ToDictionary(id => id, _ => (PlatformHostIdentityValue?)null, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var reasons = new List<string>();
        foreach (var section in configuration.GetSection("PlatformHostIdentity:ExpectedModules").GetChildren())
        {
            var id = section["PackageId"];
            if (string.IsNullOrWhiteSpace(id))
            {
                reasons.Add("EXPECTED_IDENTITY_INVALID");
                continue;
            }
            if (!expected.ContainsKey(id))
            {
                reasons.Add("UNEXPECTED_EXPECTED_PACKAGE");
                continue;
            }
            if (!seen.Add(id))
            {
                reasons.Add("DUPLICATE_EXPECTED_PACKAGE");
                expected[id] = null;
                continue;
            }
            var version = PlatformHostIdentityShape.PackageVersion(section["PackageVersion"]);
            var source = PlatformHostIdentityShape.Hex(section["SourceSha"], 40);
            var hash = PlatformHostIdentityShape.Hex(section["AssemblySha256"], 64);
            if (version is not null && source is not null && hash is not null)
                expected[id] = new(version, source, hash);
        }
        return new(expected, reasons.Distinct().ToList());
    }
}

internal static class PlatformHostIdentityShape
{
    internal static string? PackageVersion(string? value) => value is { Length: > 0 and <= 64 } &&
        Regex.IsMatch(value, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?\z", RegexOptions.CultureInvariant)
        ? value : null;

    internal static string? AssemblyVersion(string? value) => value is { Length: > 0 and <= 23 } &&
        Version.TryParse(value, out var version) && version.Revision >= 0 && version.ToString() == value ? value : null;

    internal static string? Hex(string? value, int length) => value?.Length == length && value.All(Uri.IsHexDigit)
        ? value.ToLowerInvariant() : null;
}
