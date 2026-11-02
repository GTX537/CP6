using System.Text.Json;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public sealed partial class FormAuthoringOwnerV2
{
    public const string CloneOperation = "FORM_CLONE";
    private sealed record CloneMaterial(Head Head, FormAuthoringBodyV2 Body);
    public Task<FormAuthoringResultV2> CloneAsync(string sourceKey, byte[] raw, CancellationToken ct = default)
    {
        var clone = DefinitionCloneCommandV2.Parse(sourceKey, raw);
        var command = new Command(clone.Id, null, new("", "", null, null), clone.Digest, clone.Raw, clone.Correlation);
        return WriteDraft(clone.Body.NewKey, command, clone, ct);
    }
    public Task<FormAuthoringResultV2> CloneOriginalAsync(string newKey, string command, CancellationToken ct = default)
        => OriginalCore(newKey, command, CloneOperation, ct);
    private async Task CloneReady(CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("SELECT cp6_form_authoring_v2.clone_ready() AS \"Value\"").SingleAsync(ct))
            throw Error("CLONE_WRITER_COVERAGE_UNAVAILABLE");
    }
    private async Task AuthorizeCloneSource(UserPermissionContext actor, Head source, CancellationToken ct)
    {
        var fields = await Permission(actor, source, false, false, ct);
        if (Fields.Any(f => !fields.Fields[f].CanRead)) throw Error("FIELD_READ_FORBIDDEN", 403);
    }
    private async Task<CloneMaterial> CloneSource(UserPermissionContext actor, DefinitionCloneCommandV2 clone, CancellationToken ct)
    {
        await CloneReady(ct);
        var head = await Heads().SingleOrDefaultAsync(h => h.Key == clone.SourceKey, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
        await AuthorizeCloneSource(actor, head, ct);
        var row = await Published(head.Id, clone.Body.SourceVersionId, ct) ?? throw Error("SOURCE_VERSION_NOT_FOUND", 404);
        // Published snapshots are internal immutable contracts. Copy exact content without revalidating a new latest version.
        var p = JsonSerializer.Deserialize<FormPublicationV2>(row.Snapshot, Json)!;
        return new(head, new(clone.Body.NewName, p.Fields["SchemaJson"]!, p.Fields["Category"], p.Fields["SubCategory"]));
    }
}
