using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Selected upstream tool releases, separate from the unchanged existing R2 workflow versions.
internal static class ImageReportProfile
{
    internal const string SyftVersion = "1.51.1";
    internal const string SyftLinuxArchiveSha256 = "8fcb33017a0dc1058298c923c436d19dfa68ae93968e0b423248542e3afb9fc3";
    internal const string TrivyVersion = "0.74.0";
    internal const string TrivyLinuxArchiveSha256 = "2ae6fe3ee734b7fdf11335663e18c75ea12dccc76062f09f164a3b0f8be4371a";

    internal static JsonElement Read(ReadOnlyMemory<byte> bytes)
    {
        try { return GitHubApiJson.Parse(bytes); }
        catch (Exception) { throw Error("image-report-json"); }
    }

    internal static void Require(bool condition, string code)
    {
        if (!condition) throw Error(code);
    }

    internal static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;

    internal static Cp6ReleaseContractException Error(string code) =>
        new(code, "Image report violates the selected S06 tool, identity or finding profile.");
}
