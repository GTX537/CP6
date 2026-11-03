using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

/// <summary>流程定义服务（OA 章03/04）。FlowDef upsert（schema 变更升版）+ 实例详情聚合查询。</summary>
public class FlowDefService : IFlowDefService
{
    private readonly CP6Context _db;
    public FlowDefService(CP6Context db) => _db = db;

    // Legacy definitions remain readable; publication requires the versioned Owner command.
    public Task<Guid> SaveDefAsync(string flowKey, string flowName, string? formKey, string schemaJson, string? user = null)
        => LegacyDefinitionWrites.Rejected<Guid>();

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

    public async Task<DefinitionDraftDto?> GetDraftAsync(string flowKey, bool createIfMissing = false, string? user = null)
    {
        // Retain the legacy argument for source compatibility, but a read never creates a version.
        var head = await _db.Wf_FlowDefs.AsNoTracking().FirstOrDefaultAsync(x => x.FlowKey == flowKey);
        if (head == null) return null;
        var draft = await _db.Wf_FlowDefVersions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.FlowDefId == head.Id && x.Status == WfDefinitionVersionStatus.Draft);
        return draft == null ? null : ToDraft(head.Id, draft);
    }

    public Task<DefinitionDraftDto> SaveDraftAsync(string flowKey, string flowName, string? formKey,
        string schemaJson, byte[]? rowVersion, string? user = null) => LegacyDefinitionWrites.Rejected<DefinitionDraftDto>();

    public Task<DefinitionPublishResult> PublishAsync(string flowKey, byte[]? rowVersion, Guid publishedBy,
        CancellationToken ct = default) => LegacyDefinitionWrites.Rejected<DefinitionPublishResult>();

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
        if (inst.Creator == FlowEngine.PersonalFormRuntimeV2)
            throw new CP6.Core.Services.Sys.ScopeAuthorizationExceptionV1("FORM_READ_OWNER_REQUIRED",409);
        if (inst.Creator == FlowEngine.ApprovalRuntimeV2)
            throw new CP6.Core.Services.Sys.ScopeAuthorizationExceptionV1("APP_READ_OWNER_REQUIRED",409);

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


}
