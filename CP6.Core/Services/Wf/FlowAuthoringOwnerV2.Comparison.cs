using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public sealed partial class FlowAuthoringOwnerV2
{
    public async Task<DefinitionComparisonV2> CompareAsync(string key, Guid leftVersionId, Guid rightVersionId, int page = 1, CancellationToken ct = default)
    {
        if (leftVersionId == Guid.Empty || rightVersionId == Guid.Empty || page < 1 || page > 100000) throw Error("COMPARISON_INPUT_INVALID", 400);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct); var actor = await Actor(ct);
        var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
        // Check resource authority before disclosing existence of either named version.
        var fields = await Permission(actor, head, false, false, ct);
        var a = await Published(head.Id, leftVersionId, ct) ?? throw Error("VERSION_NOT_FOUND", 404);
        var b = await Published(head.Id, rightVersionId, ct) ?? throw Error("VERSION_NOT_FOUND", 404);
        var left = ProjectPublication(JsonSerializer.Deserialize<FlowPublicationV2>(a.Snapshot, Json)!, fields);
        var right = ProjectPublication(JsonSerializer.Deserialize<FlowPublicationV2>(b.Snapshot, Json)!, fields);
        var changes = DefinitionComparerV2.Compare("FLOW", left.Fields, right.Fields, left.DependencyRefs, right.DependencyRefs);
        if ((await Actor(ct)).UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401); CompletionClock();
        static DefinitionComparisonVersionV2 Version(FlowPublicationV2 p) => new(p.VersionId, p.Version, p.Fields.GetValueOrDefault("Name"),
            p.PublishedAtUtc, p.PublisherRef, p.SchemaDigest, p.EvidenceState, p.DependencyRefs);
        var omitted = Fields.Where(x => !fields.Fields[x].CanRead).ToArray();
        return new("FLOW", key, Version(left), Version(right), omitted.Length == 0 ? "ALL_FIELDS" : "VISIBLE_FIELDS_ONLY", omitted,
            fields.Fields["SchemaJson"].CanRead && fields.Fields["FormKey"].CanRead, page, DefinitionComparerV2.PageSize, changes.Length,
            page * DefinitionComparerV2.PageSize < changes.Length, changes.Skip((page - 1) * DefinitionComparerV2.PageSize).Take(DefinitionComparerV2.PageSize).ToArray());
    }
}
