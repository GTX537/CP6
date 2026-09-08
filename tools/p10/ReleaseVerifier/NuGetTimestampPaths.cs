namespace CP6.P10.ReleaseVerifier;

// S04 independently verified both system-selected paths for the same seven immutable packages.
// This allowlist supplements real X509Chain.Build; it cannot establish trust on its own.
internal static class NuGetTimestampPaths
{
    private static readonly string[] Windows =
    [
        "2da09da7f4131f9fe72db6c5e6e9c9656755af043f1ea742cc0d2120e141ebfc",
        "ca0b1554ecd901ea19dcad8749e9f2648c8d6dfcea1add9d2c2109415bb82ccd",
        "33846b545a49c9be4903c60e01713c1bd4e4ef31ea65cd95d69e62794f30b941",
        "3e9099b5015e8f486c00bcea9d111ee721faba355a89bcf1df69561e3dc6325c"
    ];

    private static readonly string[] Linux =
    [
        "2da09da7f4131f9fe72db6c5e6e9c9656755af043f1ea742cc0d2120e141ebfc",
        "ca0b1554ecd901ea19dcad8749e9f2648c8d6dfcea1add9d2c2109415bb82ccd",
        "552f7bdcf1a7af9e6ce672017f4f12abf77240c78e761ac203d1d9d20ac89988"
    ];

    internal static void Require(IReadOnlyList<string> hashes) =>
        PinnedNuGetTrust.Require(hashes.SequenceEqual(Windows, StringComparer.Ordinal) ||
            hashes.SequenceEqual(Linux, StringComparer.Ordinal), "nuget-timestamp-chain-binding");
}
