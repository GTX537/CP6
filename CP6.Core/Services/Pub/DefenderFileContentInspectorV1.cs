using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32;

namespace CP6.Core.Services.Pub;

// Explicit server configuration only. A missing scanner is a recorded pending inspection, never CLEAN.
public sealed class DefenderFileContentInspectorV1(IConfiguration configuration) : IFileContentInspectorV1
{
    public async Task<FileInspectionV1> InspectAsync(byte[] bytes, CancellationToken ct)
    {
        FileInspectionV1 Pending(string reason) => new("PENDING", "MicrosoftDefender", null, null, DateTimeOffset.UtcNow, null, reason);
        if (!OperatingSystem.IsWindows() || !configuration.GetValue<bool>("FileContent:Defender:Enabled")) return Pending("SCANNER_NOT_CONFIGURED");
        var executable = configuration["FileContent:Defender:Executable"];
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable) || !File.Exists(executable)) return Pending("SCANNER_UNAVAILABLE");
        string? path = null;
        try
        {
            var signature = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Defender\Signature Updates", "AVSignatureVersion", null) as string;
            if (string.IsNullOrWhiteSpace(signature)) return Pending("SCANNER_SIGNATURE_UNAVAILABLE");
            var engine = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Defender\Signature Updates", "EngineVersion", null) as string;
            if (string.IsNullOrWhiteSpace(engine)) return Pending("SCANNER_ENGINE_UNAVAILABLE");
            var root = configuration["FileContent:Defender:TemporaryDirectory"] ?? Path.Combine(Path.GetTempPath(), "cp6-file-inspection");
            if (!Path.IsPathFullyQualified(root)) return Pending("SCANNER_TEMP_UNAVAILABLE");
            // MpCmdRun rejects mixed/forward slashes even when .NET file IO accepts them.
            root = Path.GetFullPath(root); Directory.CreateDirectory(root); path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".scan");
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await file.WriteAsync(bytes, ct);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-Scan", "-ScanType", "3", "-File", path, "-DisableRemediation" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start); if (process == null) return Pending("SCANNER_UNAVAILABLE");
            // Drain output concurrently; retain only its digest in the immutable scan result.
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync(CancellationToken.None); await Task.WhenAll(stdout, stderr);
                ct.ThrowIfCancellationRequested(); return Pending("SCANNER_TIMEOUT");
            }
            var output = (await stdout) + "\n" + (await stderr);
            if (signature != Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Defender\Signature Updates", "AVSignatureVersion", null) as string
                || engine != Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Defender\Signature Updates", "EngineVersion", null) as string)
                return Pending("SCANNER_VERSION_CHANGED");
            if (!File.Exists(path)) return new("NOT_CLEAN", "MicrosoftDefender", engine, signature, DateTimeOffset.UtcNow, null, "SCANNED_CONTENT_UNAVAILABLE");
            await using var remaining = File.OpenRead(path);
            var actual = await FileContentBytesV1.Read(remaining, bytes.Length, ct);
            var unchanged = FileContentBytesV1.Digest(actual) == FileContentBytesV1.Digest(bytes);
            var clean = process.ExitCode == 0 && unchanged;
            return new(clean ? "CLEAN" : "NOT_CLEAN", "MicrosoftDefender", engine, signature, DateTimeOffset.UtcNow,
                FileContentBytesV1.Digest(System.Text.Encoding.UTF8.GetBytes(output)),
                clean ? "CUSTOM_SCAN_COMPLETED" : "SCANNER_REJECTED_OR_FAILED");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
        { return Pending("SCANNER_UNAVAILABLE"); }
        finally
        {
            // This exact random file belongs to this invocation. Never recursively remove the shared directory.
            if (path != null) try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
