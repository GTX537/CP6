using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Cryptography only: the caller must obtain the key from the pinned current trust policy.
// A successful blob signature is not candidate, workflow, storage or package acceptance.
public sealed class CosignBlobVerifier(string executablePath)
{
    private const int MaximumOutputBytes = 64 * 1024;
    private const string WindowsSha256 = "9fe59be0eca1271873ce019061335eb1ac419b7059202e797828467ddabe33be";
    private const string LinuxSha256 = "4629c757b7618056f8ddd7e2625ae9fdd94c0372a65049520bc7d9df9efc7f71";

    public async Task VerifyAsync(ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> bundle,
        Cp6PinnedTrustKey key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (payload.Length is < 1 or > Cp6DeterministicJson.MaximumBytes ||
            bundle.Length is < 1 or > Cp6DeterministicJson.MaximumBytes) throw Error("cosign-input");
        var payloadBytes = payload.ToArray();
        var bundleBytes = bundle.ToArray();
        var publicKeyBytes = ValidatePublicKey(key);
        using var executable = await OpenPinnedExecutableAsync(cancellationToken);
        string? directory = null;
        try
        {
            directory = Directory.CreateTempSubdirectory("cp6-p10-cosign-verify-").FullName;
            var payloadPath = CreateFile(directory, "payload.json", payloadBytes);
            var bundlePath = CreateFile(directory, "bundle.json", bundleBytes);
            var keyPath = CreateFile(directory, "public.pem", publicKeyBytes);
            var start = new ProcessStartInfo(executable.Name)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                WorkingDirectory = directory
            };
            start.Environment.Clear();
            foreach (var name in new[] { "SystemRoot", "WINDIR" })
                if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
            foreach (var name in new[] { "HOME", "USERPROFILE", "TMP", "TEMP", "TMPDIR" })
                start.Environment[name] = directory;
            foreach (var argument in new[]
            {
                "verify-blob", "--key", keyPath, "--bundle", bundlePath,
                "--offline=true", "--new-bundle-format=true", "--insecure-ignore-tlog=true", payloadPath
            }) start.ArgumentList.Add(argument);

            using var process = Process.Start(start) ?? throw Error("cosign-process");
            process.StandardInput.Close();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try
            {
                await Task.WhenAll(
                    process.WaitForExitAsync(timeout.Token),
                    DrainAsync(process.StandardOutput.BaseStream, process, timeout.Token),
                    DrainAsync(process.StandardError.BaseStream, process, timeout.Token));
                if (process.ExitCode != 0) throw Error("cosign-signature");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw Error("cosign-timeout");
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw Error("cosign-process"); }
        finally
        {
            if (directory is not null)
            {
                try
                {
                    // No recursive removal or path supplied by a document or caller.
                    foreach (var name in new[] { "payload.json", "bundle.json", "public.pem" })
                        File.Delete(Path.Combine(directory, name));
                    Directory.Delete(directory);
                }
                catch (Exception) { throw Error("cosign-cleanup"); }
            }
        }
    }

    private async Task<FileStream> OpenPinnedExecutableAsync(CancellationToken cancellationToken)
    {
        FileStream? stream = null;
        try
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
                !(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) ||
                !Path.IsPathFullyQualified(executablePath) ||
                File.GetAttributes(executablePath).HasFlag(FileAttributes.ReparsePoint))
                throw Error("cosign-tool");
            stream = new FileStream(executablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var expected = OperatingSystem.IsWindows() ? WindowsSha256 : LinuxSha256;
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(expected, actual, StringComparison.Ordinal)) throw Error("cosign-tool");
            return stream; // Keep the validated file handle open until the child exits.
        }
        catch (OperationCanceledException) { stream?.Dispose(); throw; }
        catch (Exception) { stream?.Dispose(); throw Error("cosign-tool"); }
    }

    private static byte[] ValidatePublicKey(Cp6PinnedTrustKey key)
    {
        try
        {
            if (key.Purpose is not ("oci" or "candidate-locator") || key.PublicKey.Length > 4096)
                throw Error("cosign-key");
            using var verifier = ECDsa.Create();
            verifier.ImportFromPem(key.PublicKey);
            var spki = verifier.ExportSubjectPublicKeyInfo();
            if (verifier.KeySize != 256 || verifier.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7" ||
                key.PublicKey != PemEncoding.WriteString("PUBLIC KEY", spki) ||
                key.KeyId != "sha256:" + Cp6DeterministicJson.Sha256Hex(spki)) throw Error("cosign-key");
            return Encoding.UTF8.GetBytes(key.PublicKey);
        }
        catch (Exception) { throw Error("cosign-key"); }
    }

    private static string CreateFile(string directory, string name, byte[] bytes)
    {
        var path = Path.Combine(directory, name);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes);
        return path;
    }

    private static async Task DrainAsync(Stream stream, Process process, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var total = 0;
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0) return;
            total += count;
            if (total > MaximumOutputBytes)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw Error("cosign-output");
            }
        }
    }

    private static Cp6ReleaseContractException Error(string code) =>
        new(code, "Pinned blob signature verification did not complete successfully.");
}
