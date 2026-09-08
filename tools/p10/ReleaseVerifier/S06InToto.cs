using System.Globalization;
using System.Text.Json;
using CP6.Platform.Release;

namespace CP6.P10.ReleaseVerifier;

// Release-specific payload envelope, not a replacement for CP6.Platform.Release contracts.
// An unsigned envelope is data, never a candidate-acceptance or workflow-success capability.
internal static class S06InToto
{
    private const string Prefix = "https://schemas.cp6.dev/release/p10-s06/";

    internal static byte[] Create(string kind, GitHubWorkflowIdentity producer, DateTimeOffset createdAtUtc,
        JsonElement details, string? imageDigest = null)
    {
        RequireProducer(producer);
        RequireUtc(createdAtUtc);
        Require(details.ValueKind == JsonValueKind.Object, "s06-attestation-details");
        var document = JsonSerializer.SerializeToUtf8Bytes(new
        {
            _type = "https://in-toto.io/Statement/v1",
            subject = Subjects(kind, imageDigest),
            predicateType = Prefix + kind + ".v1",
            predicate = new
            {
                createdAtUtc = FormatTime(createdAtUtc),
                producer = Workflow(producer),
                policyVersion = 1,
                conclusion = "Success",
                deployable = false,
                details
            }
        });
        return Cp6DeterministicJson.Canonicalize(document);
    }

    internal static S06Attestation Read(ReadOnlyMemory<byte> bytes, string kind, GitHubWorkflowIdentity producer,
        DateTimeOffset cutoff, string? imageDigest = null)
    {
        RequireProducer(producer);
        RequireUtc(cutoff);
        var subjects = Subjects(kind, imageDigest);
        Require(bytes.Length is > 0 and <= Cp6DeterministicJson.MaximumBytes, "s06-attestation-size");
        var owned = bytes.ToArray();
        Require(owned.AsSpan().SequenceEqual(Cp6DeterministicJson.Canonicalize(owned)), "non-canonical-json");
        try
        {
            using var document = JsonDocument.Parse(owned);
            var root = document.RootElement;
            Exact(root, "_type", "subject", "predicateType", "predicate");
            Require(Text(root, "_type") == "https://in-toto.io/Statement/v1" &&
                Text(root, "predicateType") == Prefix + kind + ".v1" &&
                Equal(root.GetProperty("subject"), subjects), "s06-attestation-subject");
            var predicate = root.GetProperty("predicate");
            Exact(predicate, "createdAtUtc", "producer", "policyVersion", "conclusion", "deployable", "details");
            Require(Equal(predicate.GetProperty("producer"), Workflow(producer)) &&
                predicate.GetProperty("policyVersion").GetInt64() == 1 &&
                Text(predicate, "conclusion") == "Success" &&
                predicate.GetProperty("deployable").ValueKind == JsonValueKind.False, "s06-attestation-policy");
            var created = Time(predicate, "createdAtUtc");
            Require(created <= cutoff, "s06-attestation-time");
            var details = predicate.GetProperty("details");
            Require(details.ValueKind == JsonValueKind.Object, "s06-attestation-details");
            return new(kind, created, details.Clone(), Cp6DeterministicJson.Sha256Hex(owned));
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("s06-attestation-shape"); }
    }

    internal static void RequireProducer(GitHubWorkflowIdentity workflow)
    {
        workflow.RequireValid();
        Require(workflow.Repository == "GTX537/CP6" && workflow.WorkflowPath == S06ReleaseIdentity.ValidationPath,
            "s06-attestation-producer");
    }

    internal static JsonElement Workflow(GitHubWorkflowIdentity workflow) => JsonSerializer.SerializeToElement(new
    {
        repository = workflow.Repository,
        workflowPath = workflow.WorkflowPath,
        workflowFileSha = workflow.WorkflowFileSha,
        runId = workflow.RunId,
        runAttempt = workflow.RunAttempt,
        commitSha = workflow.CommitSha,
        environment = OciSignatureProfile.EnvironmentName
    });

    internal static string FormatTime(DateTimeOffset time) =>
        time.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    internal static DateTimeOffset Time(JsonElement value, string name)
    {
        Require(DateTimeOffset.TryParseExact(Text(value, name), "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var time) && time >= DateTimeOffset.UnixEpoch, "s06-attestation-time");
        return time;
    }

    internal static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;

    internal static void Exact(JsonElement value, params string[] names) =>
        Require(value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Select(p => p.Name)
            .Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            "s06-attestation-shape");

    internal static void Require(bool condition, string code)
    {
        if (!condition) throw Error(code);
    }

    internal static Cp6ReleaseContractException Error(string code) =>
        new(code, "S06 public evidence violates the fixed envelope or selected-field profile.");

    private static void RequireUtc(DateTimeOffset time) =>
        Require(time.Offset == TimeSpan.Zero && time >= DateTimeOffset.UnixEpoch, "s06-attestation-time");

    private static bool Equal(JsonElement left, JsonElement right) =>
        Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(new { value = left }))
            .AsSpan().SequenceEqual(Cp6DeterministicJson.Canonicalize(JsonSerializer.SerializeToUtf8Bytes(new { value = right })));

    private static JsonElement Subjects(string kind, string? imageDigest)
    {
        Require(kind is "SourceReference" or "FormalPackageVerification" or "CrmConsumer" or "ImageProvenance" or "OciSignature",
            "s06-attestation-kind");
        var subjects = new List<JsonElement>();
        if (kind is "SourceReference" or "FormalPackageVerification" or "CrmConsumer")
            subjects.Add(Subject("git+https://github.com/GTX537/CP6.Platform", "gitCommit", S06ReleaseIdentity.Source));
        if (kind is "FormalPackageVerification" or "CrmConsumer")
            subjects.AddRange(S06ReleaseIdentity.PackageHashes.Select(p => Package(p.Key, p.Value)));
        if (kind == "CrmConsumer")
            subjects.Add(Subject("GTX537/CP6.CRM/p10-s05/0.10.1", "sha256", S06ReleaseIdentity.CrmIndexHash));
        if (kind is "ImageProvenance" or "OciSignature")
        {
            OciWirePolicy.RequireDigest(imageDigest!);
            subjects.Add(Subject(S06ReleaseIdentity.ImageRepository, "sha256", imageDigest![7..]));
            if (kind == "ImageProvenance")
                subjects.Add(Package(S06ReleaseIdentity.ReleasePackage,
                    S06ReleaseIdentity.PackageHashes[S06ReleaseIdentity.ReleasePackage]));
        }
        else Require(imageDigest is null, "s06-attestation-subject");
        return JsonSerializer.SerializeToElement(subjects.OrderBy(s => Text(s, "name"), StringComparer.Ordinal));
    }

    private static JsonElement Package(string id, string hash) =>
        Subject("https://nuget.pkg.github.com/GTX537/index.json#" + id + "/" + S06ReleaseIdentity.Version, "sha256", hash);

    private static JsonElement Subject(string name, string algorithm, string hash) =>
        JsonSerializer.SerializeToElement(new { name, digest = new Dictionary<string, string> { [algorithm] = hash } });
}

internal sealed record S06Attestation(string Kind, DateTimeOffset CreatedAtUtc, JsonElement Details, string Sha256);
