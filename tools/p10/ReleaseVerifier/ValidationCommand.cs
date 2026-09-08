using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Hosted collector commands write only fresh local public handoff directories.
// Signing/build/push remain explicit protected workflow steps; these commands have no remote-write API.
internal static class ValidationCommand
{
    internal static bool Matches(string[] arguments) => arguments is ["prepare-validation", _] or
        ["finalize-validation", _, _, _];

    internal static async Task<byte[]> ExecuteAsync(string[] arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = arguments.ToArray();
        Require(Matches(args), "validation-command");
        var prepare = args[0] == "prepare-validation";
        var forbidden = ReadOnlyVerificationCommand.ForbiddenVariables
            .Where(n => !prepare || n != "P10_CRM_READ_TOKEN");
        Require(forbidden.All(n => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(n))), "validation-secret-scope");
        var github = Required("P10_GITHUB_READ_TOKEN");
        var feed = Required("P10_FEED_READ_TOKEN");
        if (prepare)
        {
            var crm = Required("P10_CRM_READ_TOKEN");
            var workflow = await S06ValidationCollector.PrepareAsync(args[1], github, crm, feed, cancellationToken);
            return S06ArtifactAssembly.Canonical(new
            {
                state = "ValidationPreparationCreated", producer = Workflow(workflow),
                fileCount = S06ValidationInputs.PreparationNames.Count, candidateAccepted = false, deployable = false
            });
        }
        var cosign = new CosignBlobVerifier(Required("P10_COSIGN_PATH"));
        var artifact = await S06ValidationCollector.FinalizeAsync(args[1], args[2], args[3], cosign, github, feed, cancellationToken);
        return S06ArtifactAssembly.Canonical(new
        {
            state = "ValidationArtifactCreated", producer = Workflow(artifact.Producer),
            indexSha256 = Cp6DeterministicJson.Sha256Hex(artifact.CopyIndexBytes()),
            fileCount = artifact.CopyFiles().Count, candidateAccepted = false, deployable = false,
            validationWorkflowCompleted = false
        });
    }

    private static string Required(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Require(value is { Length: > 0 and <= 4096 } && !value.Any(char.IsControl), "validation-credential");
        return value!;
    }
}
