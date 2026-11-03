using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Wf;

/// <summary>
/// Explicit historical data arrangement for legacy runtime tests. This is not a publisher or an authorization
/// adapter: it inserts simulated pre-existing records directly. Current authoring/publication uses real PG Owners.
/// </summary>
internal static class LegacyDefinitionFixture
{
    private static readonly DateTime HistoricalAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    internal static async Task<DefinitionDraftDto> FlowDraft(CP6Context db, string key, string name, string? formKey,
        string schema, byte[]? unusedToken = null, string? user = null)
    {
        var head = await db.Wf_FlowDefs.SingleOrDefaultAsync(x => x.FlowKey == key);
        if (head == null) { head = new() { Id = Guid.NewGuid(), FlowKey = key, FlowName = name, FormKey = formKey, SchemaJson = schema, Creator = user }; db.Wf_FlowDefs.Add(head); }
        var draft = await db.Wf_FlowDefVersions.SingleOrDefaultAsync(x => x.FlowDefId == head.Id && x.Status == 0);
        if (draft == null)
        {
            draft = new() { Id = Guid.NewGuid(), FlowDefId = head.Id, Version = (await db.Wf_FlowDefVersions.Where(x => x.FlowDefId == head.Id).Select(x => (int?)x.Version).MaxAsync() ?? 0) + 1 };
            db.Wf_FlowDefVersions.Add(draft);
        }
        draft.FlowNameSnapshot = name; draft.SchemaJson = schema;
        await db.SaveChangesAsync(); return new(head.Id, draft.Id, draft.Version, name, schema, draft.RowVersion, 0);
    }
    internal static async Task<DefinitionDraftDto> FormDraft(CP6Context db, string key, string name, string schema,
        byte[]? unusedToken = null, string? user = null)
    {
        var head = await db.Wf_FormDefs.SingleOrDefaultAsync(x => x.FormKey == key);
        if (head == null) { head = new() { Id = Guid.NewGuid(), FormKey = key, FormName = name, SchemaJson = schema, Creator = user }; db.Wf_FormDefs.Add(head); }
        var draft = await db.Wf_FormDefVersions.SingleOrDefaultAsync(x => x.FormDefId == head.Id && x.Status == 0);
        if (draft == null)
        {
            draft = new() { Id = Guid.NewGuid(), FormDefId = head.Id, Version = (await db.Wf_FormDefVersions.Where(x => x.FormDefId == head.Id).Select(x => (int?)x.Version).MaxAsync() ?? 0) + 1 };
            db.Wf_FormDefVersions.Add(draft);
        }
        draft.FormNameSnapshot = name; draft.SchemaJson = schema;
        await db.SaveChangesAsync(); return new(head.Id, draft.Id, draft.Version, name, schema, draft.RowVersion, 0);
    }
    internal static async Task<DefinitionPublishResult> FlowPublished(CP6Context db, string key, byte[]? unusedToken, Guid publisher)
    {
        var head = await db.Wf_FlowDefs.SingleAsync(x => x.FlowKey == key);
        var draft = await db.Wf_FlowDefVersions.SingleAsync(x => x.FlowDefId == head.Id && x.Status == 0);
        draft.Status = 1; draft.PublishedAtUtc = HistoricalAt; draft.PublishedBy = publisher;
        head.FlowName = draft.FlowNameSnapshot; head.SchemaJson = draft.SchemaJson; head.Version = draft.Version;
        await db.SaveChangesAsync(); await Dependencies(db, draft);
        return new(head.Id, draft.Id, draft.Version, HistoricalAt);
    }
    internal static async Task<DefinitionPublishResult> FormPublished(CP6Context db, string key, byte[]? unusedToken, Guid publisher)
    {
        var head = await db.Wf_FormDefs.SingleAsync(x => x.FormKey == key);
        var draft = await db.Wf_FormDefVersions.SingleAsync(x => x.FormDefId == head.Id && x.Status == 0);
        draft.Status = 1; draft.PublishedAtUtc = HistoricalAt; draft.PublishedBy = publisher;
        head.FormName = draft.FormNameSnapshot; head.SchemaJson = draft.SchemaJson; head.Version = draft.Version;
        await db.SaveChangesAsync(); return new(head.Id, draft.Id, draft.Version, HistoricalAt);
    }
    internal static async Task<Guid> Flow(CP6Context db, string key, string name, string? formKey, string schema, string? user = null)
    {
        var draft = await FlowDraft(db, key, name, formKey, schema, user: user);
        await FlowPublished(db, key, draft.RowVersion, Guid.Empty); return draft.DefinitionId;
    }
    internal static async Task<Guid> Form(CP6Context db, string key, string name, string schema, string? user = null)
    {
        var draft = await FormDraft(db, key, name, schema, user: user);
        await FormPublished(db, key, draft.RowVersion, Guid.Empty); return draft.DefinitionId;
    }
    internal static async Task ArrangePublishedHistory(CP6Context db)
    {
        // Called explicitly by historical runtime fixtures after their head data has been arranged.
        var heads = await db.Wf_FlowDefs.ToArrayAsync(); var added = new List<Wf_FlowDefVersion>();
        foreach (var head in heads)
        {
            if (await db.Wf_FlowDefVersions.AnyAsync(x => x.FlowDefId == head.Id)) continue;
            var version = new Wf_FlowDefVersion { Id = Guid.NewGuid(), FlowDefId = head.Id, Version = head.Version, Status = 1,
                FlowNameSnapshot = head.FlowName, SchemaJson = head.SchemaJson, PublishedAtUtc = HistoricalAt, Creator = "simulated-historical-fixture" };
            db.Wf_FlowDefVersions.Add(version); added.Add(version);
        }
        await db.SaveChangesAsync(); foreach (var version in added) await Dependencies(db, version);
        // Manually parked instances in these fixtures also represent an explicitly recorded historical pin.
        foreach (var instance in await db.Wf_FlowInstances.Where(x => x.FlowDefVersionId == null).ToArrayAsync())
        {
            var version = await (from h in db.Wf_FlowDefs join v in db.Wf_FlowDefVersions on h.Id equals v.FlowDefId
                where h.FlowKey == instance.FlowKey && v.Status == 1 orderby v.Version descending select v).FirstOrDefaultAsync();
            if (version != null) instance.FlowDefVersionId = version.Id;
        }
        await db.SaveChangesAsync();
    }
    private static async Task Dependencies(CP6Context db, Wf_FlowDefVersion version)
    {
        var schema = JsonSerializer.Deserialize<FlowSchema>(version.SchemaJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        foreach (var node in schema.Nodes.Where(x => x.Type == "subFlow"))
        {
            var child = await (from h in db.Wf_FlowDefs join v in db.Wf_FlowDefVersions on h.Id equals v.FlowDefId
                where h.FlowKey == node.SubFlowKey && v.Status == 1 orderby v.Version descending select v).FirstOrDefaultAsync();
            if (child != null) db.Wf_FlowDefVersionDependencies.Add(new() { Id = Guid.NewGuid(), FlowDefVersionId = version.Id,
                NodeId = node.Id, DependencyType = "SubFlow", TargetFlowDefVersionId = child.Id });
        }
        await db.SaveChangesAsync();
    }
}
