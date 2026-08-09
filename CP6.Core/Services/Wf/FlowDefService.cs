using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CP6.Core.Services.Wf;

/// <summary>流程定义服务（OA 章03/04）。FlowDef upsert（schema 变更升版）+ 实例详情聚合查询。</summary>
public class FlowDefService : IFlowDefService
{
    private readonly CP6Context _db;
    public FlowDefService(CP6Context db) => _db = db;

    /// <summary>Legacy internal helper retained for existing service callers. Browser endpoints use SaveDraftAsync.</summary>
    public async Task<Guid> SaveDefAsync(string flowKey, string flowName, string? formKey, string schemaJson, string? user = null)
    {
        var draft = await SaveDraftAsync(flowKey, flowName, formKey, schemaJson, null, user);
        var row = await _db.Wf_FlowDefVersions.SingleAsync(x => x.Id == draft.VersionId);
        row.Status = WfDefinitionVersionStatus.Published;
        row.PublishedAtUtc = DateTime.UtcNow;
        var head = await _db.Wf_FlowDefs.SingleAsync(x => x.Id == draft.DefinitionId);
        head.SchemaJson = row.SchemaJson;
        head.FlowName = row.FlowNameSnapshot;
        head.Version = row.Version;
        await _db.SaveChangesAsync();
        return draft.DefinitionId;
    }

    public async Task<Wf_FlowDef?> GetDefAsync(string flowKey)
    {
        var head = await _db.Wf_FlowDefs.AsNoTracking().FirstOrDefaultAsync(x => x.FlowKey == flowKey);
        if (head == null) return null;
        var published = await _db.Wf_FlowDefVersions.AsNoTracking()
            .Where(x => x.FlowDefId == head.Id && x.Status == WfDefinitionVersionStatus.Published)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync();
        if (published != null)
        {
            head.FlowName = published.FlowNameSnapshot;
            head.SchemaJson = published.SchemaJson;
            head.Version = published.Version;
        }
        return head;
    }

    public async Task<DefinitionDraftDto?> GetDraftAsync(string flowKey, bool createIfMissing = true, string? user = null)
    {
        var head = await _db.Wf_FlowDefs.FirstOrDefaultAsync(x => x.FlowKey == flowKey);
        if (head == null) return null;
        var draft = await _db.Wf_FlowDefVersions
            .SingleOrDefaultAsync(x => x.FlowDefId == head.Id && x.Status == WfDefinitionVersionStatus.Draft);
        if (draft == null && createIfMissing)
        {
            var latest = await _db.Wf_FlowDefVersions.AsNoTracking()
                .Where(x => x.FlowDefId == head.Id && x.Status == WfDefinitionVersionStatus.Published)
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync();
            draft = new Wf_FlowDefVersion
            {
                Id = Guid.NewGuid(), FlowDefId = head.Id,
                Version = (latest?.Version ?? 0) + 1,
                Status = WfDefinitionVersionStatus.Draft,
                FlowNameSnapshot = latest?.FlowNameSnapshot ?? head.FlowName,
                SchemaJson = latest?.SchemaJson ?? head.SchemaJson,
                Creator = user
            };
            _db.Wf_FlowDefVersions.Add(draft);
            await _db.SaveChangesAsync();
        }
        return draft == null ? null : ToDraft(head.Id, draft);
    }

    public async Task<DefinitionDraftDto> SaveDraftAsync(
        string flowKey, string flowName, string? formKey, string schemaJson, byte[]? rowVersion, string? user = null)
    {
        if (string.IsNullOrWhiteSpace(flowKey)) throw new InvalidOperationException("FlowKey 不能为空");
        var head = await _db.Wf_FlowDefs.FirstOrDefaultAsync(x => x.FlowKey == flowKey);
        if (head == null)
        {
            head = new Wf_FlowDef
            {
                Id = Guid.NewGuid(), FlowKey = flowKey, FlowName = flowName,
                FormKey = string.IsNullOrWhiteSpace(formKey) ? null : formKey,
                SchemaJson = schemaJson, Version = 1, Creator = user
            };
            _db.Wf_FlowDefs.Add(head);
        }
        else
        {
            head.FlowName = flowName;
            head.FormKey = string.IsNullOrWhiteSpace(formKey) ? null : formKey;
            head.Modifier = user;
            head.ModifyDate = DateTime.UtcNow;
        }

        var draft = await _db.Wf_FlowDefVersions
            .SingleOrDefaultAsync(x => x.FlowDefId == head.Id && x.Status == WfDefinitionVersionStatus.Draft);
        if (draft == null)
        {
            var maxVersion = await _db.Wf_FlowDefVersions
                .Where(x => x.FlowDefId == head.Id).Select(x => (int?)x.Version).MaxAsync() ?? 0;
            draft = new Wf_FlowDefVersion
            {
                Id = Guid.NewGuid(), FlowDefId = head.Id, Version = maxVersion + 1,
                Status = WfDefinitionVersionStatus.Draft, FlowNameSnapshot = flowName,
                SchemaJson = schemaJson, Creator = user
            };
            _db.Wf_FlowDefVersions.Add(draft);
        }
        else
        {
            EnsureRowVersion(draft.RowVersion, rowVersion);
            if (rowVersion != null)
                _db.Entry(draft).Property(x => x.RowVersion).OriginalValue = rowVersion;
            draft.FlowNameSnapshot = flowName;
            draft.SchemaJson = schemaJson;
            draft.Modifier = user;
            draft.ModifyDate = DateTime.UtcNow;
        }

        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("E-WF-045"); }
        return ToDraft(head.Id, draft);
    }

    public async Task<DefinitionPublishResult> PublishAsync(
        string flowKey, byte[]? rowVersion, Guid publishedBy, CancellationToken ct = default)
    {
        var head = await _db.Wf_FlowDefs.SingleOrDefaultAsync(x => x.FlowKey == flowKey, ct)
                   ?? throw new InvalidOperationException("E-WF-030");
        var draft = await _db.Wf_FlowDefVersions
            .SingleOrDefaultAsync(x => x.FlowDefId == head.Id && x.Status == WfDefinitionVersionStatus.Draft, ct)
                    ?? throw new InvalidOperationException("E-WF-030");
        EnsureRowVersion(draft.RowVersion, rowVersion);
        if (rowVersion != null) _db.Entry(draft).Property(x => x.RowVersion).OriginalValue = rowVersion;

        FlowSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<FlowSchema>(draft.SchemaJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new FlowSchema();
        }
        catch (JsonException) { throw new InvalidOperationException("E-WF-030"); }
        if (FlowSchemaValidator.Validate(schema).Count > 0) throw new InvalidOperationException("E-WF-030");
        await new FlowFormCompatibilityValidator(_db).ValidateFlowPublishAsync(head.Id, draft.SchemaJson, ct);

        foreach (var node in schema.Nodes.Where(x =>
                     string.Equals(x.Type, "subFlow", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(x.SubFlowKey)))
        {
            var targetHead = await _db.Wf_FlowDefs.SingleOrDefaultAsync(
                x => x.FlowKey == node.SubFlowKey && x.Enable, ct)
                ?? throw new InvalidOperationException("E-WF-030");
            var targetVersion = await _db.Wf_FlowDefVersions
                .Where(x => x.FlowDefId == targetHead.Id && x.Status == WfDefinitionVersionStatus.Published)
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException("E-WF-030");
            _db.Wf_FlowDefVersionDependencies.Add(new Wf_FlowDefVersionDependency
            {
                Id = Guid.NewGuid(), FlowDefVersionId = draft.Id, NodeId = node.Id,
                DependencyType = "SubFlow", TargetFlowDefVersionId = targetVersion.Id,
                Creator = publishedBy.ToString()
            });
        }

        var publishedAt = DateTime.UtcNow;
        draft.Status = WfDefinitionVersionStatus.Published;
        draft.PublishedAtUtc = publishedAt;
        draft.PublishedBy = publishedBy;
        head.FlowName = draft.FlowNameSnapshot;
        head.SchemaJson = draft.SchemaJson;
        head.Version = draft.Version;
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new InvalidOperationException("E-WF-045"); }
        return new(head.Id, draft.Id, draft.Version, publishedAt);
    }

    public async Task<IReadOnlyList<DefinitionVersionItem>> ListVersionsAsync(string flowKey, CancellationToken ct = default)
    {
        var headId = await _db.Wf_FlowDefs.Where(x => x.FlowKey == flowKey)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (headId == null) return Array.Empty<DefinitionVersionItem>();
        return await _db.Wf_FlowDefVersions.AsNoTracking().Where(x => x.FlowDefId == headId)
            .OrderByDescending(x => x.Version)
            .Select(x => new DefinitionVersionItem(x.Id, x.Version, x.Status, x.FlowNameSnapshot, x.PublishedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<DefinitionVersionDto?> GetVersionAsync(string flowKey, int version, CancellationToken ct = default)
    {
        var row = await (from head in _db.Wf_FlowDefs.AsNoTracking()
                         join item in _db.Wf_FlowDefVersions.AsNoTracking() on head.Id equals item.FlowDefId
                         where head.FlowKey == flowKey && item.Version == version
                         select new { head.Id, Item = item }).SingleOrDefaultAsync(ct);
        return row == null ? null : new(row.Id, row.Item.Id, row.Item.Version, row.Item.Status,
            row.Item.FlowNameSnapshot, row.Item.SchemaJson, row.Item.PublishedAtUtc);
    }

    public async Task<FlowInstanceDetail?> GetInstanceDetailAsync(Guid instanceId)
    {
        var inst = await _db.Wf_FlowInstances.FirstOrDefaultAsync(i => i.Id == instanceId);
        if (inst == null) return null;

        return new FlowInstanceDetail
        {
            Instance = inst,
            History = await _db.Wf_FlowHistories
                .Where(h => h.InstanceId == instanceId)
                .OrderBy(h => h.CreateDate)
                .ToListAsync(),
            Tasks = await _db.Wf_FlowTasks
                .Where(t => t.InstanceId == instanceId)
                .ToListAsync(),
        };
    }

    private static DefinitionDraftDto ToDraft(Guid defId, Wf_FlowDefVersion draft) =>
        new(defId, draft.Id, draft.Version, draft.FlowNameSnapshot, draft.SchemaJson, draft.RowVersion, draft.Status);

    private static void EnsureRowVersion(byte[]? current, byte[]? expected)
    {
        if (expected != null && current != null && !current.SequenceEqual(expected))
            throw new InvalidOperationException("E-WF-045");
    }
}
