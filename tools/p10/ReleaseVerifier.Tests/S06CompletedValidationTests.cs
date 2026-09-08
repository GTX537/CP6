using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// Timing vectors are not acceptance evidence. Positive S06 handoff needs a real completed protected producer.
public sealed class S06CompletedValidationTests
{
    private static DateTimeOffset Time => new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static GitHubWorkflowIdentity Producer => new("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
        new string('5', 40), 999991, 1, new string('a', 40));

    [Fact]
    public void Same_second_content_is_bounded_by_the_reported_artifact_second() =>
        Assert.Equal(Time.AddSeconds(1).AddTicks(-1),
            S06ArtifactTiming.ContentCutoff(Time, Time.AddSeconds(3), Time.AddSeconds(4)));

    [Fact]
    public void A_completed_job_in_the_same_second_keeps_the_full_measured_interval() =>
        Assert.Equal(Time.AddSeconds(1).AddTicks(-1),
            S06ArtifactTiming.ContentCutoff(Time, Time, Time.AddSeconds(4)));

    [Fact]
    public void Actual_observation_clips_the_measurement_interval() =>
        Assert.Equal(Time.AddMilliseconds(350),
            S06ArtifactTiming.ContentCutoff(Time, Time, Time.AddMilliseconds(350)));

    [Theory]
    [InlineData("created-after-job")]
    [InlineData("observed-before-job")]
    [InlineData("created-offset")]
    [InlineData("job-offset")]
    [InlineData("observed-offset")]
    [InlineData("created-fraction")]
    [InlineData("job-fraction")]
    [InlineData("before-epoch")]
    [InlineData("future-observation")]
    public void Inconsistent_or_non_API_timing_is_rejected(string mutation)
    {
        var created = Time;
        var completed = Time.AddSeconds(2);
        var observed = Time.AddSeconds(3);
        if (mutation == "created-after-job") created = Time.AddSeconds(4);
        if (mutation == "observed-before-job") observed = Time.AddSeconds(1);
        if (mutation == "created-offset") created = created.ToOffset(TimeSpan.FromHours(1));
        if (mutation == "job-offset") completed = completed.ToOffset(TimeSpan.FromHours(1));
        if (mutation == "observed-offset") observed = observed.ToOffset(TimeSpan.FromHours(1));
        if (mutation == "created-fraction") created = created.AddMilliseconds(1);
        if (mutation == "job-fraction") completed = completed.AddMilliseconds(1);
        if (mutation == "before-epoch") created = DateTimeOffset.UnixEpoch.AddSeconds(-1);
        if (mutation == "future-observation") observed = DateTimeOffset.UtcNow.AddDays(1);
        Assert.Throws<Cp6ReleaseContractException>(() => S06ArtifactTiming.ContentCutoff(created, completed, observed));
    }

    [Fact]
    public async Task Publication_cannot_be_selected_as_a_validation_producer() =>
        Assert.Equal("s06-attestation-producer", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Read(Producer with { WorkflowPath = S06ReleaseIdentity.PublicationPath }, 1, Time))).Code);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task An_invalid_artifact_identity_fails_before_external_reads(long id) =>
        Assert.Equal("artifact-selection", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Read(Producer, id, Time))).Code);

    [Fact]
    public async Task A_future_completion_cutoff_is_refused() =>
        Assert.Equal("s06-validation-cutoff", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Read(Producer, 1, DateTimeOffset.UtcNow.AddDays(1)))).Code);

    [Fact]
    public async Task Missing_GitHub_credentials_never_fall_back_to_ambient_authentication() =>
        Assert.Equal("github-credential", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            Read(Producer, 1, Time))).Code);

    [Fact]
    public async Task Precancelled_handoff_performs_no_external_read()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => S06CompletedValidation.ReadAsync(
            Producer, 1, Time, new("missing-cosign"), "", "", cancellation.Token));
    }

    private static Task<S06CompletedValidation> Read(GitHubWorkflowIdentity producer, long artifactId, DateTimeOffset cutoff) =>
        S06CompletedValidation.ReadAsync(producer, artifactId, cutoff, new("missing-cosign"), "", "");
}
