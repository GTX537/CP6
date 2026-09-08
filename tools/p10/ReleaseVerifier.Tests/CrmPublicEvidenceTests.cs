using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier.Tests;

// The producer is an unsigned unit vector. The five records and four workflow observations are actual CRM reads.
public sealed class CrmPublicEvidenceTests
{
    private static GitHubWorkflowIdentity Producer => new("GTX537/CP6", S06ReleaseIdentity.ValidationPath,
        new string('b', 40), 12345, 1, new string('c', 40));
    private static readonly Lazy<Task<byte[]>> Actual = new(() => CrmPublicEvidence.CreateAsync(Producer, Token()));

    [Fact]
    public async Task Actual_private_evidence_becomes_a_bounded_public_summary_read_without_a_private_token()
    {
        var bytes = await Actual.Value;
        var statement = CrmPublicEvidence.Read(bytes, Producer, DateTimeOffset.UtcNow);
        Assert.Equal("CrmConsumer", statement.Kind);
        Assert.Equal(9, JsonNode.Parse(bytes)!["subject"]!.AsArray().Count);
        var documents = statement.Details.GetProperty("documents");
        Assert.Equal(5, documents.GetArrayLength());
        foreach (var document in documents.EnumerateArray())
        {
            var pinned = PinnedReleaseDocument.Get(document.GetProperty("name").GetString()!);
            var raw = Convert.FromBase64String(document.GetProperty("contentBase64").GetString()!);
            Assert.Equal(pinned.ByteLength, raw.Length);
            Assert.Equal(pinned.Sha256, Cp6DeterministicJson.Sha256Hex(raw));
        }
        Assert.Equal(2, statement.Details.GetProperty("deliveries").GetArrayLength());
        var text = Encoding.UTF8.GetString(bytes);
        Assert.False(text.Contains(Token(), StringComparison.Ordinal));
        Assert.False(text.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(Token())), StringComparison.Ordinal));
        Assert.Equal(bytes, Cp6DeterministicJson.Canonicalize(bytes));
    }

    [Theory]
    [InlineData("archive-repository")]
    [InlineData("archive-commit")]
    [InlineData("reviewed-tree")]
    [InlineData("source-repository")]
    [InlineData("source-commit")]
    [InlineData("source-main")]
    [InlineData("source-protection-type")]
    [InlineData("source-relationship")]
    [InlineData("source-observed")]
    [InlineData("source-extra")]
    [InlineData("document-missing")]
    [InlineData("document-order")]
    [InlineData("document-name")]
    [InlineData("document-bytes")]
    [InlineData("document-base64-spacing")]
    [InlineData("document-extra")]
    [InlineData("delivery-missing")]
    [InlineData("delivery-order")]
    [InlineData("delivery-number")]
    [InlineData("merge-after-main")]
    [InlineData("delivery-observed")]
    [InlineData("run-identity")]
    [InlineData("run-file-hash")]
    [InlineData("run-file-length")]
    [InlineData("run-event")]
    [InlineData("run-conclusion")]
    [InlineData("run-status")]
    [InlineData("run-observed")]
    [InlineData("run-time")]
    [InlineData("job-missing")]
    [InlineData("job-name")]
    [InlineData("job-id")]
    [InlineData("job-conclusion")]
    [InlineData("job-time")]
    [InlineData("job-order")]
    [InlineData("job-extra")]
    public async Task Selected_public_fields_cannot_change_the_fixed_private_evidence_bindings(string mutation)
    {
        var root = JsonNode.Parse(await Actual.Value)!;
        var details = root["predicate"]!["details"]!;
        var source = details["archiveSource"]!;
        var documents = details["documents"]!.AsArray();
        var deliveries = details["deliveries"]!.AsArray();
        var delivery = deliveries[0]!;
        var run = delivery["main"]!;
        var jobs = run["jobs"]!.AsArray();
        var job = jobs[0]!;
        if (mutation == "archive-repository") details["archiveRepository"] = "Other/Repo";
        if (mutation == "archive-commit") details["archiveCommitSha"] = new string('a', 40);
        if (mutation == "reviewed-tree") details["reviewedConsumerTreeSha"] = new string('a', 40);
        if (mutation == "source-repository") source["repository"] = "GTX537/CP6.Platform";
        if (mutation == "source-commit") source["sourceGitSha"] = new string('a', 40);
        if (mutation == "source-main") source["observedMainSha"] = "main";
        if (mutation == "source-protection-type") source["mainProtectedAtObservation"] = "false";
        if (mutation == "source-relationship") source["relationship"] = "Unknown";
        if (mutation == "source-observed") source["observedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UtcNow.AddDays(1));
        if (mutation == "source-extra") source["privateLog"] = "not-allowed";
        if (mutation == "document-missing") documents.RemoveAt(4);
        if (mutation == "document-order") Swap(documents, 0, 1);
        if (mutation == "document-name") documents[0]!["name"] = "publication";
        if (mutation == "document-bytes") documents[0]!["contentBase64"] = Convert.ToBase64String("{}"u8);
        if (mutation == "document-base64-spacing")
            documents[0]!["contentBase64"] = documents[0]!["contentBase64"]!.GetValue<string>() + "\n";
        if (mutation == "document-extra") documents[0]!["unreviewed"] = true;
        if (mutation == "delivery-missing") deliveries.RemoveAt(1);
        if (mutation == "delivery-order") Swap(deliveries, 0, 1);
        if (mutation == "delivery-number") delivery["number"] = 48;
        if (mutation == "merge-after-main") delivery["mergedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UtcNow);
        if (mutation == "delivery-observed") delivery["observedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UnixEpoch);
        if (mutation == "run-identity") run["workflow"]!["runId"] = 1;
        if (mutation == "run-file-hash") run["file"]!["sha256"] = new string('a', 64);
        if (mutation == "run-file-length") run["file"]!["byteLength"] = 1;
        if (mutation == "run-event") run["event"] = "workflow_dispatch";
        if (mutation == "run-conclusion") run["conclusion"] = "failure";
        if (mutation == "run-status") run["status"] = "in_progress";
        if (mutation == "run-observed") run["observedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UtcNow.AddDays(1));
        if (mutation == "run-time") run["startedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UtcNow);
        if (mutation == "job-missing") jobs.RemoveAt(4);
        if (mutation == "job-name") job["name"] = "not-a-required-job";
        if (mutation == "job-id") job["id"] = 0;
        if (mutation == "job-conclusion") job["conclusion"] = "skipped";
        if (mutation == "job-time") job["startedAtUtc"] = S06InToto.FormatTime(DateTimeOffset.UnixEpoch);
        if (mutation == "job-order") Swap(jobs, 0, 1);
        if (mutation == "job-extra") job["runnerName"] = "not-allowed";
        var failure = Assert.Throws<Cp6ReleaseContractException>(() =>
            CrmPublicEvidence.Read(Canonical(root), Producer, DateTimeOffset.UtcNow));
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public async Task An_invalid_public_producer_fails_before_private_collection() =>
        Assert.Equal("s06-attestation-producer", (await Assert.ThrowsAsync<Cp6ReleaseContractException>(() =>
            CrmPublicEvidence.CreateAsync(Producer with { WorkflowPath = S06ReleaseIdentity.PublicationPath },
                "not-a-real-token"))).Code);

    [Fact]
    public async Task Precancelled_collection_uses_no_private_credential()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CrmPublicEvidence.CreateAsync(Producer, "not-a-real-token", cancellation.Token));
    }

    private static void Swap(JsonArray array, int left, int right)
    {
        var value = array[left]!.DeepClone();
        array[left] = array[right]!.DeepClone();
        array[right] = value;
    }

    private static byte[] Canonical(JsonNode root) =>
        Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(root));
    private static string Token() => Environment.GetEnvironmentVariable("P10_GITHUB_READ_TOKEN") ??
        throw new InvalidOperationException("Actual CRM read authorization is required; no skip or synthetic CRM evidence.");
}
