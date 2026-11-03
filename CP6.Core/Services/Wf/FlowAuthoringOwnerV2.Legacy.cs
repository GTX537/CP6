using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public sealed partial class FlowAuthoringOwnerV2
{
    public async Task<LegacyDefinitionViewV2[]> LegacyAsync(string? key = null, Guid? version = null, int page = 1, CancellationToken ct = default)
    {
        if (page is < 1 or > 100000 || version != null && key == null) throw Error("LEGACY_QUERY_INVALID", 400);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct); await Root(ct); var actor = await Actor(ct);
        await ReadCapability(actor, ct);
        // Creator is a historical free-text label, not a verified Owner or department assignment.
        if (ResourceScope(actor) != 5) throw Error("LEGACY_OWNER_SCOPE_REQUIRED", 403);
        var fields = await FieldAccess(false, ct);
        var rows = await db.Wf_FlowDefs.AsNoTracking().Where(x => key == null || x.FlowKey == key)
            .OrderBy(x => x.FlowKey).Skip(key == null ? (page - 1) * 100 : 0).Take(100).ToArrayAsync(ct);
        if (key != null && rows.Length == 0) throw Error("DEFINITION_NOT_FOUND", 404);
        var result = new List<LegacyDefinitionViewV2>();
        foreach (var row in rows)
        {
            var head = new Head(row.Id, Guid.Empty, row.FlowKey, Guid.Empty, null, Guid.Empty,
                row.FlowName, row.SchemaJson, row.FormKey, row.FunctionId, row.FlowCode);
            if (key == null)
            {
                var values = Values(head).Where(x => x.Key != "SchemaJson" && fields.Fields[x.Key].CanRead).ToDictionary();
                result.Add(new("FLOW", row.Id, row.FlowKey, null, row.Version, null, "CURRENT_HEAD_RECONSTRUCTION", values, null, null));
                continue;
            }
            var snapshots = await db.Wf_FlowDefVersions.AsNoTracking().Where(x => x.FlowDefId == row.Id && (version == null || x.Id == version))
                .OrderByDescending(x => x.Version).Skip(version == null ? (page - 1) * 100 : 0).Take(100).ToArrayAsync(ct);
            if (version != null && snapshots.Length == 0) throw Error("VERSION_NOT_FOUND", 404);
            if (version == null && page == 1)
                result.Add(new("FLOW", row.Id, row.FlowKey, null, row.Version, null, "CURRENT_HEAD_RECONSTRUCTION",
                    Values(head).Where(x => fields.Fields[x.Key].CanRead).ToDictionary(), null, null));
            foreach (var snapshot in snapshots)
                // Old versions only froze Name/SchemaJson. Do not present today's form/identity fields as old facts.
                result.Add(new("FLOW", row.Id, row.FlowKey, snapshot.Id, snapshot.Version, snapshot.Status,
                    LegacyDefinitionViewV2.Reconstructed(snapshot.Creator) ? "LEGACY_HEAD_BACKFILL" : "LEGACY_VERSION_SNAPSHOT",
                    new Dictionary<string, string?> { ["Name"] = snapshot.FlowNameSnapshot, ["SchemaJson"] = snapshot.SchemaJson }
                        .Where(x => fields.Fields[x.Key].CanRead).ToDictionary(),
                    LegacyDefinitionViewV2.Reconstructed(snapshot.Creator) ? null : snapshot.PublishedAtUtc,
                    LegacyDefinitionViewV2.Reconstructed(snapshot.Creator) ? null : snapshot.PublishedBy));
        }
        if ((await Actor(ct)).UserId != actor.UserId) throw Error("REAUTH_REQUIRED", 401);
        CompletionClock(); return result.ToArray();
    }

    // The retained v1 original reader shares this current actor boundary and completion clock.
    internal Task<CP6.Core.Services.Sys.UserPermissionContext> LegacyActorAsync(CancellationToken ct) => Actor(ct);

    // Called only by the legacy original-result reader inside its authentication transaction.
    internal async Task EnsureLegacyOriginalReadAsync(CP6.Core.Services.Sys.UserPermissionContext actor, CancellationToken ct)
    {
        await ReadCapability(actor, ct);
        if (ResourceScope(actor) != 5) throw Error("LEGACY_OWNER_SCOPE_REQUIRED", 403);
        var fields = await FieldAccess(false, ct);
        if (!fields.Fields["Name"].CanRead || !fields.Fields["SchemaJson"].CanRead) throw Error("LEGACY_RESULT_FIELD_FORBIDDEN", 403);
        CompletionClock();
    }
}
