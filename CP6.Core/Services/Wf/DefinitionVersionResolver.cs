using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public sealed class DefinitionVersionResolver : IDefinitionVersionResolver
{
    private readonly CP6Context _db;
    public DefinitionVersionResolver(CP6Context db) => _db = db;

    public async Task<FlowVersionResolution> ResolveLatestFlowAsync(
        string flowKey, bool validateDependencies = true, CancellationToken ct = default)
    {
        var head = await _db.Wf_FlowDefs.AsNoTracking()
            .SingleOrDefaultAsync(x => x.FlowKey == flowKey, ct);
        if (head is null || !head.Enable) throw new InvalidOperationException("E-WF-029");
        var version = await _db.Wf_FlowDefVersions.AsNoTracking()
            .Where(x => x.FlowDefId == head.Id && x.Status == WfDefinitionVersionStatus.Published)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("E-WF-029");

        if (validateDependencies)
        {
            var disabledDependency = await (
                from dependency in _db.Wf_FlowDefVersionDependencies
                join targetVersion in _db.Wf_FlowDefVersions on dependency.TargetFlowDefVersionId equals targetVersion.Id
                join targetHead in _db.Wf_FlowDefs on targetVersion.FlowDefId equals targetHead.Id
                where dependency.FlowDefVersionId == version.Id && !targetHead.Enable
                select dependency.Id).AnyAsync(ct);
            if (disabledDependency) throw new InvalidOperationException("E-WF-029");
        }
        return new(head, version);
    }

    public async Task<FlowVersionResolution> ResolveFlowAsync(Guid versionId, CancellationToken ct = default)
    {
        var version = await _db.Wf_FlowDefVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == versionId, ct)
                      ?? throw new InvalidOperationException("E-WF-046");
        if (version.Status != WfDefinitionVersionStatus.Published) throw new InvalidOperationException("E-WF-046");
        var head = await _db.Wf_FlowDefs.AsNoTracking().SingleAsync(x => x.Id == version.FlowDefId, ct);
        return new(head, version);
    }

    public async Task<FormVersionResolution> ResolveLatestFormAsync(string formKey, CancellationToken ct = default)
    {
        var head = await _db.Wf_FormDefs.AsNoTracking().SingleOrDefaultAsync(x => x.FormKey == formKey, ct);
        if (head is null || !head.Enable) throw new InvalidOperationException("E-WF-036");
        var version = await _db.Wf_FormDefVersions.AsNoTracking()
            .Where(x => x.FormDefId == head.Id && x.Status == WfDefinitionVersionStatus.Published)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("E-WF-036");
        return new(head, version);
    }

    public async Task<FormVersionResolution> ResolveFormAsync(
        Guid? versionId, string? legacyFormKey = null, int? legacyVersion = null, CancellationToken ct = default)
    {
        if (versionId is Guid pinned)
        {
            var version = await _db.Wf_FormDefVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == pinned, ct)
                          ?? throw new InvalidOperationException("E-WF-046");
            if (version.Status != WfDefinitionVersionStatus.Published) throw new InvalidOperationException("E-WF-046");
            var head = await _db.Wf_FormDefs.AsNoTracking().SingleAsync(x => x.Id == version.FormDefId, ct);
            return new(head, version);
        }

        if (string.IsNullOrWhiteSpace(legacyFormKey)) throw new InvalidOperationException("E-WF-046");
        var legacyHead = await _db.Wf_FormDefs.AsNoTracking()
            .SingleOrDefaultAsync(x => x.FormKey == legacyFormKey, ct)
            ?? throw new InvalidOperationException("E-WF-046");
        return new(legacyHead, null, LegacyFallback: true);
    }
}
