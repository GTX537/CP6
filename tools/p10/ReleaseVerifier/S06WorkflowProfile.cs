using System.Text.Json;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// Fixed job profiles shared by live reads and temporal checks. No workflow success is inferred here.
internal static class S06WorkflowProfile
{
    internal static GitHubWorkflowIdentity FormalPublication => new("GTX537/CP6.Platform",
        ".github/workflows/p10-formal-packages.yml", "01c94e11213231ef6f73d8f9a9cccd9f1563e185",
        34126521193, 1, S06ReleaseIdentity.Source);
    internal static IReadOnlyList<string> FormalJobs { get; } = Array.AsReadOnly(new[] { "sign-publish", "verify-linux" });
    internal static IReadOnlyList<string> ValidationJobs { get; } = Array.AsReadOnly(new[] { "validate" });
    internal static IReadOnlyList<string> PublicationJobs { get; } = Array.AsReadOnly(new[] { "publish" });

    internal static GitHubWorkflowIdentity Read(JsonElement value, string expectedPath)
    {
        try
        {
            Require(expectedPath is S06ReleaseIdentity.ValidationPath or S06ReleaseIdentity.PublicationPath,
                "s06-workflow-profile");
            Exact(value, "repository", "workflowPath", "workflowFileSha", "runId", "runAttempt", "commitSha", "environment");
            var result = new GitHubWorkflowIdentity(Text(value, "repository"), Text(value, "workflowPath"),
                Text(value, "workflowFileSha"), value.GetProperty("runId").GetInt64(),
                value.GetProperty("runAttempt").GetInt64(), Text(value, "commitSha"));
            result.RequireValid();
            Require(result.Repository == "GTX537/CP6" && result.WorkflowPath == expectedPath &&
                Text(value, "environment") == OciSignatureProfile.EnvironmentName, "s06-workflow-profile");
            return result;
        }
        catch (CP6.Platform.Release.Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("s06-workflow-profile"); }
    }
}
