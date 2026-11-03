using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public sealed partial class FormAuthoringOwnerV2
{
    public async Task<LegacyDefinitionViewV2[]> LegacyAsync(string? key = null, Guid? version = null, int page = 1, CancellationToken ct = default)
    {
        if (page is < 1 or > 100000 || version != null && key == null) throw Error("LEGACY_QUERY_INVALID", 400);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct); var actor = await Actor(ct);
        await ReadCapability(actor, ct);
        if (ResourceScope(actor) != 5) throw Error("LEGACY_OWNER_SCOPE_REQUIRED", 403);
        var fields = await FieldAccess(false, ct);
        var rows = await db.Wf_FormDefs.AsNoTracking().Where(x => key == null || x.FormKey == key)
            .OrderBy(x => x.FormKey).Skip(key == null ? (page - 1) * 100 : 0).Take(100).ToArrayAsync(ct);
        if (key != null && rows.Length == 0) throw Error("DEFINITION_NOT_FOUND", 404);
        var result = new List<LegacyDefinitionViewV2>();
        foreach (var row in rows)
        {
            var head = new Head(row.Id, Guid.Empty, row.FormKey, Guid.Empty, null, Guid.Empty,
                row.FormName, row.SchemaJson, row.Category, row.SubCategory, Guid.Empty);
            if (key == null)
            {
                var values = Values(head).Where(x => x.Key != "SchemaJson" && fields.Fields[x.Key].CanRead).ToDictionary();
                result.Add(new("FORM", row.Id, row.FormKey, null, row.Version, null, "CURRENT_HEAD_RECONSTRUCTION", values, null, null));
                continue;
            }
            var snapshots = await db.Wf_FormDefVersions.AsNoTracking().Where(x => x.FormDefId == row.Id && (version == null || x.Id == version))
                .OrderByDescending(x => x.Version).Skip(version == null ? (page - 1) * 100 : 0).Take(100).ToArrayAsync(ct);
            if (version != null && snapshots.Length == 0) throw Error("VERSION_NOT_FOUND", 404);
            if (version == null && page == 1)
                result.Add(new("FORM", row.Id, row.FormKey, null, row.Version, null, "CURRENT_HEAD_RECONSTRUCTION",
                    Values(head).Where(x => fields.Fields[x.Key].CanRead).ToDictionary(), null, null));
            foreach (var snapshot in snapshots)
                result.Add(new("FORM", row.Id, row.FormKey, snapshot.Id, snapshot.Version, snapshot.Status,
                    LegacyDefinitionViewV2.Reconstructed(snapshot.Creator) ? "LEGACY_HEAD_BACKFILL" : "LEGACY_VERSION_SNAPSHOT",
                    new Dictionary<string, string?> { ["Name"] = snapshot.FormNameSnapshot, ["SchemaJson"] = snapshot.SchemaJson }
                        .Where(x => fields.Fields[x.Key].CanRead).ToDictionary(),
                    LegacyDefinitionViewV2.Reconstructed(snapshot.Creator) ? null : snapshot.PublishedAtUtc,
                    LegacyDefinitionViewV2.Reconstructed(snapshot.Creator) ? null : snapshot.PublishedBy));
        }
        if ((await Actor(ct)).UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401);
        CompletionClock(); return result.ToArray();
    }
}
