using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Actual CLI producer/consumer compatibility, with temporary keys only. Never release evidence.
public sealed class CosignCommandRoundTripTests
{
    [Fact]
    public async Task Native_sign_blob_output_verifies_and_rejects_changed_bytes_with_isolated_ephemeral_keys()
    {
        var executable = Environment.GetEnvironmentVariable("P10_COSIGN_PATH") ??
            throw new InvalidOperationException("The pinned cosign security build is required.");
        using var file = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        var field = OperatingSystem.IsWindows() ? "WindowsSha256" : "LinuxSha256";
        var expected = typeof(CosignBlobVerifier).GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!
            .GetRawConstantValue();
        Assert.Equal(expected, Convert.ToHexString(await SHA256.HashDataAsync(file)).ToLowerInvariant());
        var directory = Directory.CreateTempSubdirectory("cp6-p10-cosign-roundtrip-").FullName;
        try
        {
            var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var prefix = Path.Combine(directory, "fixture");
            await Run(executable, directory, password, "generate-key-pair", "--output-key-prefix", prefix);
            var payload = "{\"fixture\":\"native-cosign-sign-blob\"}\n"u8.ToArray();
            var payloadPath = Path.Combine(directory, "payload.json");
            var bundlePath = Path.Combine(directory, "bundle.json");
            await File.WriteAllBytesAsync(payloadPath, payload);
            await Run(executable, directory, password, "sign-blob", "--yes", "--key", prefix + ".key",
                "--use-signing-config=false", "--tlog-upload=false", "--new-bundle-format=true",
                "--bundle", bundlePath, payloadPath);
            using var key = ECDsa.Create();
            key.ImportFromPem(await File.ReadAllTextAsync(prefix + ".pub"));
            Assert.Equal(256, key.KeySize);
            var spki = key.ExportSubjectPublicKeyInfo();
            var trusted = new Cp6PinnedTrustKey("sha256:" + Cp6DeterministicJson.Sha256Hex(spki), "candidate-locator",
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"), DateTimeOffset.Parse("2028-01-01T00:00:00Z"),
                PemEncoding.WriteString("PUBLIC KEY", spki), null, null);
            var bundle = await File.ReadAllBytesAsync(bundlePath);
            var verifier = new CosignBlobVerifier(executable);
            await verifier.VerifyAsync(payload, bundle, trusted);
            payload[3] ^= 1;
            Assert.Equal("cosign-signature", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
                verifier.VerifyAsync(payload, bundle, trusted))).Code);
        }
        finally
        {
            foreach (var name in new[] { "fixture.key", "fixture.pub", "payload.json", "bundle.json" })
                File.Delete(Path.Combine(directory, name));
            Directory.Delete(directory); // Owns exactly these files; never recursively deletes caller data.
        }
    }

    private static async Task Run(string executable, string directory, string password, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = directory
        };
        start.Environment.Clear();
        foreach (var name in new[] { "SystemRoot", "WINDIR" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        foreach (var name in new[] { "HOME", "USERPROFILE", "TMP", "TEMP", "TMPDIR" })
            start.Environment[name] = directory;
        start.Environment["COSIGN_PASSWORD"] = password;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, error);
            Assert.Equal(0, process.ExitCode); // Do not expose key paths, passwords or raw child output.
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }
}
