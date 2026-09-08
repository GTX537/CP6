using System.Diagnostics;
using System.Globalization;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Pure process/summary profile. It neither launches a process nor returns a verification capability.
internal static class S06CleanProcessProfile
{
    internal static ProcessStartInfo Start(string hostPath, string assemblyPath, string directory,
        IReadOnlyDictionary<string, string?> workflowEnvironment, IReadOnlyDictionary<string, string> readEnvironment,
        string releaseTag, long artifactId)
    {
        _ = R2ObjectTarget.Discovery(releaseTag, R2DiscoveryPart.Locator);
        Require(artifactId > 0 && Path.IsPathFullyQualified(hostPath) &&
            Path.GetFileNameWithoutExtension(hostPath) == "dotnet" && Path.IsPathFullyQualified(assemblyPath) &&
            Path.GetFileName(assemblyPath) == "CP6.P10.ReleaseVerifier.dll" && Path.IsPathFullyQualified(directory),
            "clean-process-path");
        _ = S06CurrentWorkflowChecks.ReadEnvironment(workflowEnvironment, S06ReleaseIdentity.PublicationPath);
        Require(readEnvironment.Keys.Order(StringComparer.Ordinal).SequenceEqual(
            ReadOnlyVerificationCommand.RequiredVariables.Order(StringComparer.Ordinal), StringComparer.Ordinal) &&
            readEnvironment.Values.All(v => v is { Length: > 0 and <= 4096 } && !v.Any(char.IsControl)), "clean-process-environment");
        var start = new ProcessStartInfo(hostPath)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory
        };
        start.Environment.Clear();
        foreach (var name in S06CurrentWorkflowChecks.EnvironmentNames) start.Environment[name] = workflowEnvironment[name]!;
        foreach (var pair in readEnvironment) start.Environment[pair.Key] = pair.Value;
        foreach (var name in new[] { "SystemRoot", "WINDIR" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        foreach (var name in new[] { "HOME", "USERPROFILE", "TMP", "TEMP", "TMPDIR" }) start.Environment[name] = directory;
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        foreach (var argument in new[] { assemblyPath, "confirm-platform-intent", releaseTag,
            artifactId.ToString(CultureInfo.InvariantCulture) }) start.ArgumentList.Add(argument);
        return start;
    }

    internal static void RequireSummary(ReadOnlyMemory<byte> output, string releaseTag, long artifactId,
        string locatorSha256, string candidateSha256, long publicationRunId, DateTimeOffset started, DateTimeOffset completed)
    {
        Require(output.Length is > 1 and <= 65536 && output.Span[^1] == (byte)'\n', "clean-process-output");
        var length = output.Length - 1;
        if (output.Span[length - 1] == (byte)'\r') length--;
        var bytes = output[..length].ToArray();
        Require(bytes.AsSpan().SequenceEqual(Cp6DeterministicJson.Canonicalize(bytes)), "clean-process-output");
        try
        {
            var root = GitHubApiJson.Parse(bytes);
            Exact(root, "state", "releaseTag", "sha256", "locatorSha256", "intentArtifactId", "candidateAccepted", "deployable",
                "publicationWorkflowCompleted", "validationRunId", "publicationRunId", "verifiedAtUtc");
            Require(Text(root, "state") == "PreCommitVerified" && Text(root, "releaseTag") == releaseTag &&
                Text(root, "sha256") == candidateSha256 && Text(root, "locatorSha256") == locatorSha256 &&
                root.GetProperty("intentArtifactId").GetInt64() == artifactId &&
                root.GetProperty("publicationRunId").GetInt64() == publicationRunId &&
                root.GetProperty("validationRunId").GetInt64() > 0 &&
                root.GetProperty("validationRunId").GetInt64() != publicationRunId &&
                root.GetProperty("candidateAccepted").ValueKind == System.Text.Json.JsonValueKind.False &&
                root.GetProperty("deployable").ValueKind == System.Text.Json.JsonValueKind.False &&
                root.GetProperty("publicationWorkflowCompleted").ValueKind == System.Text.Json.JsonValueKind.False, "clean-process-binding");
            var verified = Time(root, "verifiedAtUtc");
            Require(started.Offset == TimeSpan.Zero && completed.Offset == TimeSpan.Zero && started <= completed &&
                verified >= DateTimeOffset.FromUnixTimeMilliseconds(started.ToUnixTimeMilliseconds()) &&
                verified <= completed, "clean-process-time");
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("clean-process-output"); }
    }
}
