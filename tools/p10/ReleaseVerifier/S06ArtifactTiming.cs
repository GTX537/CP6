namespace CP6.P10.ReleaseVerifier;

// GitHub metadata is measured in whole seconds; CP6 control objects retain millisecond precision.
// This bounds content time only and never proves a workflow or artifact authentic.
internal static class S06ArtifactTiming
{
    internal static DateTimeOffset ContentCutoff(DateTimeOffset artifactCreated, DateTimeOffset jobCompleted,
        DateTimeOffset observed)
    {
        GitHubEvidenceChecks.RequireCutoff(artifactCreated);
        GitHubEvidenceChecks.RequireCutoff(jobCompleted);
        GitHubEvidenceChecks.RequireCutoff(observed);
        GitHubEvidenceChecks.Require(artifactCreated <= jobCompleted && jobCompleted <= observed &&
            observed <= DateTimeOffset.UtcNow && artifactCreated.Ticks % TimeSpan.TicksPerSecond == 0 &&
            jobCompleted.Ticks % TimeSpan.TicksPerSecond == 0, "s06-artifact-time");
        var artifactEnd = artifactCreated.AddSeconds(1).AddTicks(-1);
        var jobEnd = jobCompleted.AddSeconds(1).AddTicks(-1);
        return new[] { artifactEnd, jobEnd, observed }.Min();
    }
}
