using System.Diagnostics;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Every commit must await this actual new read-only process against the immutable current-run intent.
// No serialized success flag, process factory or caller-selected executable is accepted here.
internal static class S06CleanPrecommit
{
    internal static async Task VerifyAsync(S06PublicationIntent intent, string readAccessKeyId, string readSecret,
        string cosignPath, string githubReadToken, string feedReadToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workflow = S06CurrentWorkflowChecks.EnvironmentNames.ToDictionary(n => n,
            Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        var readers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["P10_COSIGN_PATH"] = cosignPath,
            ["P10_R2_CONSUMER_ACCESS_KEY_ID"] = readAccessKeyId,
            ["P10_R2_CONSUMER_SECRET_ACCESS_KEY"] = readSecret,
            ["P10_GITHUB_READ_TOKEN"] = githubReadToken,
            ["P10_FEED_READ_TOKEN"] = feedReadToken
        };
        string? directory = null;
        try
        {
            directory = Directory.CreateTempSubdirectory("cp6-p10-clean-precommit-").FullName;
            var start = S06CleanProcessProfile.Start(Environment.ProcessPath ?? "",
                typeof(S06CleanPrecommit).Assembly.Location, directory, workflow, readers,
                intent.Locator.ReleaseTag, intent.Metadata.Id);
            var started = DateTimeOffset.UtcNow;
            using var process = Process.Start(start) ?? throw Error("clean-process-start");
            process.StandardInput.Close();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(25));
            try
            {
                var output = ReadAsync(process.StandardOutput.BaseStream, process, deadline.Token);
                var error = ReadAsync(process.StandardError.BaseStream, process, deadline.Token);
                await Task.WhenAll(process.WaitForExitAsync(deadline.Token), output, error);
                Require(process.ExitCode == 0 && (await error).Length == 0, "clean-process-failed");
                S06CleanProcessProfile.RequireSummary(await output, intent.Locator.ReleaseTag, intent.Metadata.Id,
                    intent.Locator.Sha256, intent.Locator.Subject.Sha256, intent.Publication.Workflow.RunId,
                    started, DateTimeOffset.UtcNow);
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw Error("clean-process-timeout");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { throw Error("clean-process-failed"); }
        finally
        {
            if (directory is not null) Cleanup(directory);
        }
    }

    private static async Task<byte[]> ReadAsync(Stream stream, Process process, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0) return output.ToArray();
            if (output.Length + count > 65536)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw Error("clean-process-output");
            }
            output.Write(buffer, 0, count);
        }
    }

    private static void Cleanup(string directory)
    {
        try
        {
            var actual = Path.GetFullPath(directory);
            var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            Require(Path.GetDirectoryName(actual) == parent &&
                Path.GetFileName(actual).StartsWith("cp6-p10-clean-precommit-", StringComparison.Ordinal) &&
                (File.GetAttributes(actual) & FileAttributes.ReparsePoint) == 0, "clean-process-cleanup");
            // Only this process-created directory; any caches contain public certificates/evidence, never credentials.
            Directory.Delete(actual, recursive: true);
        }
        catch (Exception) { throw Error("clean-process-cleanup"); }
    }
}
