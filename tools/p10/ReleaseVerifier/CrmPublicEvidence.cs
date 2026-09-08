using System.Text.Json;
using CP6.Platform.Release;
using static CP6.P10.ReleaseVerifier.S06InToto;

namespace CP6.P10.ReleaseVerifier;

// The protected collector reads CRM. A normal consumer reads only this authenticated public evidence payload.
// This unsigned codec is not the authentication boundary; the full consumer must verify the Locator/graph/producer first.
internal static class CrmPublicEvidence
{
    private static readonly string[] Names =
        ["crm-index", "crm-main-linux", "crm-main-windows", "crm-pr-linux", "crm-pr-windows"];

    internal static async Task<byte[]> CreateAsync(GitHubWorkflowIdentity producer, string crmReadToken,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireProducer(producer);
        var observed = await CrmConsumerEvidenceSource.ReadAsync(DateTimeOffset.UtcNow, crmReadToken, cancellationToken);
        var details = JsonSerializer.SerializeToElement(new
        {
            archiveRepository = CrmPullRequestSelection.Repository,
            archiveCommitSha = observed.ArchiveSource.SourceGitSha,
            reviewedConsumerTreeSha = CrmConsumerRecordChecks.TreeSha,
            archiveSource = CrmPublicObservations.Source(observed.ArchiveSource),
            documents = observed.Documents.OrderBy(d => d.Key, StringComparer.Ordinal).Select(d => new
            {
                name = d.Key,
                contentBase64 = Convert.ToBase64String(d.Value.CopyBytes())
            }).ToArray(),
            deliveries = new[] { CrmPublicObservations.Delivery(observed.Consumer), CrmPublicObservations.Delivery(observed.Archive) }
        });
        var bytes = Create("CrmConsumer", producer, observed.ObservedAtUtc, details);
        _ = Read(bytes, producer, DateTimeOffset.UtcNow);
        return bytes;
    }

    internal static S06Attestation Read(ReadOnlyMemory<byte> bytes, GitHubWorkflowIdentity producer, DateTimeOffset cutoff)
    {
        var statement = S06InToto.Read(bytes, "CrmConsumer", producer, cutoff);
        try
        {
            var details = statement.Details;
            Exact(details, "archiveRepository", "archiveCommitSha", "reviewedConsumerTreeSha", "archiveSource", "documents", "deliveries");
            Require(Text(details, "archiveRepository") == CrmPullRequestSelection.Repository &&
                Text(details, "archiveCommitSha") == PinnedReleaseDocument.CommitSha &&
                Text(details, "reviewedConsumerTreeSha") == CrmConsumerRecordChecks.TreeSha, "s06-crm-archive");
            var source = CrmPublicObservations.ReadSource(details.GetProperty("archiveSource"), statement.CreatedAtUtc);
            var deliveries = details.GetProperty("deliveries");
            Require(deliveries.ValueKind == JsonValueKind.Array && deliveries.GetArrayLength() == 2, "s06-crm-delivery");
            var consumer = CrmPublicObservations.ReadDelivery(deliveries[0], 46, statement.CreatedAtUtc);
            var archive = CrmPublicObservations.ReadDelivery(deliveries[1], 47, statement.CreatedAtUtc);
            Require(consumer.ObservedAtUtc <= archive.ObservedAtUtc && archive.ObservedAtUtc <= source.ObservedAtUtc,
                "s06-crm-time");
            var documents = Documents(details.GetProperty("documents"));
            CrmConsumerIndexChecks.Index(documents["crm-index"], consumer, archive);
            foreach (var name in Names.Where(n => n != "crm-index"))
                _ = CrmConsumerRecordChecks.Record(name, documents[name], consumer);
            return statement;
        }
        catch (Cp6ReleaseContractException) { throw; }
        catch (Exception) { throw Error("s06-crm-shape"); }
    }

    private static Dictionary<string, JsonElement> Documents(JsonElement values)
    {
        Require(values.ValueKind == JsonValueKind.Array && values.GetArrayLength() == Names.Length, "s06-crm-documents");
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        for (var index = 0; index < Names.Length; index++)
        {
            var value = values[index];
            Exact(value, "name", "contentBase64");
            Require(Text(value, "name") == Names[index], "s06-crm-documents");
            var pinned = PinnedReleaseDocument.Get(Names[index]);
            var encoded = Text(value, "contentBase64");
            Require(encoded.Length == 4 * ((pinned.ByteLength + 2) / 3), "s06-crm-document-bytes");
            var bytes = Convert.FromBase64String(encoded);
            Require(bytes.Length == pinned.ByteLength && Convert.ToBase64String(bytes) == encoded &&
                Cp6DeterministicJson.Sha256Hex(bytes) == pinned.Sha256, "s06-crm-document-bytes");
            result.Add(Names[index], GitHubApiJson.Parse(bytes));
        }
        return result;
    }
}
