using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Fixed non-deployable S06 runtime image. Only hosted CI builds authoritative candidate images.
internal static class ImageBuildProfile
{
    internal const string BaseImage = "mcr.microsoft.com/dotnet/runtime@sha256:0f8aaa92bdf4ea871aa0fc4c5903ca81fcae3af26d8fd2d6e918fdd226df3338";
    internal const string DockerfileSha256 = "b2f21cd090f7ecc8d933f48858b6f4584fcc12fe502db4304798714ba1c22474";
    internal const string RuntimeVersion = "8.0.30";
    internal const string SdkVersion = "8.0.424";
    internal const string Platform = "linux/amd64";
    internal const string CosignVersion = "3.1.3-cp6.1";
    internal const string CosignSha256 = "a2bcc99765d97f1b7db0cf22afc0d2dd523e94c250900e9d8e4710b2a4a35740";

    internal static byte[] DockerfileBytes()
    {
        using var stream = typeof(ImageBuildProfile).Assembly.GetManifestResourceStream("CP6.P10.verifier.Dockerfile") ??
            throw S06InToto.Error("image-build-profile");
        var bytes = new byte[2049];
        var length = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        S06InToto.Require(length <= 2048 &&
            Cp6DeterministicJson.Sha256Hex(bytes.AsSpan(0, length)) == DockerfileSha256, "image-build-profile");
        return bytes[..length];
    }
}
