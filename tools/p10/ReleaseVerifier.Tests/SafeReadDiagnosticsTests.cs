using System.Net;
using System.Text.RegularExpressions;
using CP6.P10.ReleaseVerifier;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

[CollectionDefinition("Exclusive stderr", DisableParallelization = true)]
public sealed class ExclusiveStderrCollection;

[Collection("Exclusive stderr")]
public sealed class SafeReadDiagnosticsTests
{
    [Theory]
    [InlineData("GTX537/CP6", "cp6", S06ReleaseIdentity.ValidationPath)]
    [InlineData("GTX537/CP6", "cp6", S06ReleaseIdentity.PublicationPath)]
    [InlineData("GTX537/CP6.Platform", "platform", ".github/workflows/p10-formal-packages.yml")]
    [InlineData("GTX537/CP6.CRM", "crm", ".github/workflows/crm-validation.yml")]
    public async Task Every_repository_target_uses_only_finite_labels(string repository, string label, string workflow)
    {
        var sha = new string('a', 40);
        var targets = new[]
        {
            (GitHubReadTarget.Run(repository, 12345, 2), "run"),
            (GitHubReadTarget.Jobs(repository, 12345, 2), "jobs"),
            (GitHubReadTarget.MainBranch(repository), "main"),
            (GitHubReadTarget.Compare(repository, sha, sha), "compare"),
            (GitHubReadTarget.Workflow(repository, workflow, sha), "workflow")
        };
        foreach (var (target, operation) in targets)
            await AssertTarget(target, label + "." + operation);
    }

    [Fact]
    public async Task Pinned_archives_pull_requests_commits_and_artifacts_have_fixed_categories()
    {
        foreach (var name in new[] { "crm-index", "crm-pr-linux", "crm-pr-windows", "crm-main-linux", "crm-main-windows", "publication", "package-provenance" })
            await AssertTarget(GitHubReadTarget.Archive(PinnedReleaseDocument.Get(name)), "crm.archive");
        foreach (var number in new[] { 46, 47 })
            await AssertTarget(GitHubReadTarget.CrmPullRequest(CrmPullRequestSelection.Get(number)), "crm.pull-request");
        foreach (var checkout in new[] { false, true })
            await AssertTarget(GitHubReadTarget.CrmConsumerCommit(checkout), "crm.commit");
        await AssertTarget(GitHubReadTarget.Artifact(12345), "cp6.artifact");
        await AssertTarget(GitHubReadTarget.ArtifactZip(12345), "cp6.artifact");
    }

    private static async Task AssertTarget(GitHubReadTarget target, string expected)
    {
        Assert.Equal(expected, target.DiagnosticCategory);
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        using var output = new StringWriter();
        var original = Console.Error;
        try
        {
            Console.SetError(output);
            var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
                GitHubWirePolicy.ReadResponseAsync(response, default, target));
            Assert.Equal("github-http-status", error.Code);
        }
        finally { Console.SetError(original); }
        Assert.Equal("p10-github-read target=" + expected + " status=404" + Environment.NewLine, output.ToString());
        Assert.DoesNotContain(target.Path, output.ToString());
    }

    [Fact]
    public async Task Successful_HTTP_read_preserves_bytes_and_emits_no_error_diagnostic()
    {
        var bytes = "{}"u8.ToArray();
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new("application/json");
        using var output = new StringWriter();
        var original = Console.Error;
        try
        {
            Console.SetError(output);
            Assert.Equal(bytes, await GitHubWirePolicy.ReadResponseAsync(response, default, GitHubReadTarget.MainBranch("GTX537/CP6")));
        }
        finally { Console.SetError(original); }
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData(201)]
    [InlineData(206)]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Non_200_diagnostic_is_numeric_only_and_preserves_failure(int status)
    {
        const string secret = "private-response-marker";
        using var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(secret),
            ReasonPhrase = secret,
            RequestMessage = new(HttpMethod.Get, "https://example.invalid/" + secret)
        };
        response.Headers.Add("X-Private", secret);
        response.Headers.Location = new Uri("https://example.invalid/" + secret);
        using var output = new StringWriter();
        var original = Console.Error;
        try
        {
            Console.SetError(output);
            var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
                GitHubWirePolicy.ReadResponseAsync(response, default));
            Assert.Equal("github-http-status", error.Code);
            Assert.Null(error.InnerException);
            Assert.DoesNotContain(secret, error.ToString());
        }
        finally { Console.SetError(original); }
        Assert.Equal($"p10-github-read target=unspecified status={status}" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Cancellation_does_not_emit_a_misleading_HTTP_diagnostic()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        using var output = new StringWriter();
        var original = Console.Error;
        try
        {
            Console.SetError(output);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                GitHubWirePolicy.ReadResponseAsync(response, cancel.Token));
        }
        finally { Console.SetError(original); }
        Assert.Empty(output.ToString());
    }

    [Fact]
    public async Task Actual_transport_carries_fixed_target_without_exposing_invalid_credential()
    {
        // Intentionally invalid, non-secret credential; this cannot accept any release evidence.
        const string token = "invalid-p10-diagnostic-test-credential";
        using var client = new GitHubReadClient(token);
        using var output = new StringWriter();
        var original = Console.Error;
        try
        {
            Console.SetError(output);
            var error = await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
                client.ReadAsync(GitHubReadTarget.MainBranch("GTX537/CP6")));
            Assert.Equal("github-http-status", error.Code);
            Assert.DoesNotContain(token, error.ToString());
        }
        finally { Console.SetError(original); }
        Assert.Matches(new Regex(@"\Ap10-github-read target=cp6.main status=(401|403|429)\r?\n\z"), output.ToString());
    }

    [Fact]
    public void Preparation_stages_are_ordered_and_prepared_marker_follows_output_creation()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "tools/p10/ReleaseVerifier/S06ValidationCollector.cs")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "tools/p10/ReleaseVerifier/S06ValidationCollector.cs"));
        var expected = new[]
        {
            "p10-validation-stage current-workflow", "var current = await S06CurrentWorkflow.CaptureAsync",
            "p10-validation-stage platform-source", "var source = await SourceReferenceEvidence.CreateAsync",
            "p10-validation-stage formal-packages", "var packages = await FormalVerificationEvidence.CollectAsync",
            "p10-validation-stage crm-consumer", "var crm = await CrmPublicEvidence.CreateAsync",
            "p10-validation-stage publication-archive", "var publication = await ReleaseArchiveSource.ReadAsync",
            "p10-validation-stage package-provenance", "var provenance = await ReleaseArchiveSource.ReadAsync",
            "p10-validation-stage final-current-workflow", "var final = await S06CurrentWorkflow.CaptureAsync",
            "S06LocalFiles.WriteNew(outputDirectory, files);", "p10-validation-stage prepared", "return producer;"
        };
        var offset = 0;
        foreach (var item in expected)
        {
            var position = source.IndexOf(item, offset, StringComparison.Ordinal);
            Assert.True(position >= offset, "Missing or unordered: " + item);
            offset = position + item.Length;
        }
    }
}
