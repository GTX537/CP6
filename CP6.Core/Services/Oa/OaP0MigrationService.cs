using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

/// <summary>Expand/backfill companion. Reports counts only; never emits form or workflow payloads.</summary>
public sealed class OaP0MigrationService : IOaP0MigrationService
{
    private readonly CP6Context _db;

    public OaP0MigrationService(CP6Context db) => _db = db;

    public async Task<OaP0PreflightReport> PreflightAsync(CancellationToken ct = default)
    {
        var flows = await _db.Wf_FlowDefs.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        var forms = await _db.Wf_FormDefs.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        var instances = await _db.Wf_FlowInstances.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        var formData = await _db.Wf_FormDatas.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);

        var flowKeys = flows.Select(x => (x.TenantId, x.FlowKey)).ToHashSet();
        var formKeys = forms.Select(x => (x.TenantId, x.FormKey)).ToHashSet();
        var active = instances.Where(x => x.Status is FlowInstanceStatus.Running or FlowInstanceStatus.Suspended).ToList();
        var draftIds = instances.Where(x => x.Status == FlowInstanceStatus.Draft).Select(x => x.Id).ToHashSet();

        var invalidLegacyDrafts =
            await _db.Wf_FlowTokens.IgnoreQueryFilters().CountAsync(x => draftIds.Contains(x.InstanceId), ct) +
            await _db.Wf_FlowTasks.IgnoreQueryFilters().CountAsync(x => draftIds.Contains(x.InstanceId), ct) +
            await _db.Wf_FlowHistories.IgnoreQueryFilters().CountAsync(x => draftIds.Contains(x.InstanceId), ct);

        var invalidSubFlows = 0;
        foreach (var flow in flows)
        {
            foreach (var subKey in ReadSubFlowKeys(flow.SchemaJson))
                if (!flows.Any(x => x.TenantId == flow.TenantId && x.FlowKey == subKey))
                    invalidSubFlows++;
        }

        return new OaP0PreflightReport(
            FlowDefs: flows.Count,
            FormDefs: forms.Count,
            Running: instances.Count(x => x.Status == FlowInstanceStatus.Running),
            Suspended: instances.Count(x => x.Status == FlowInstanceStatus.Suspended),
            Terminal: instances.Count(x => x.Status is FlowInstanceStatus.Approved or FlowInstanceStatus.Rejected or FlowInstanceStatus.Withdrawn),
            LegacyDraftInstances: draftIds.Count,
            OrphanFlowKeys: instances.Count(x => !flowKeys.Contains((x.TenantId, x.FlowKey))),
            OrphanFormKeys: formData.Count(x => !formKeys.Contains((x.TenantId, x.FormKey))),
            UnpinnableActiveInstances: active.Count(x => !flowKeys.Contains((x.TenantId, x.FlowKey))),
            UnpinnableFormData: formData.Count(x => !formKeys.Contains((x.TenantId, x.FormKey))),
            InvalidSubFlowRefs: invalidSubFlows,
            DuplicateActiveBusinessKeys: active
                .Where(x => x.BizType != null && x.BizId != null)
                .GroupBy(x => new { x.TenantId, x.BizType, x.BizId })
                .Count(x => x.Count() > 1),
            InvalidLegacyDrafts: invalidLegacyDrafts);
    }

    public async Task<OaP0BackfillReport> BackfillAsync(CancellationToken ct = default)
    {
        var preflight = await PreflightAsync(ct);
        if (!preflight.SafeToBackfill)
            throw new InvalidOperationException("OA P0 preflight failed; backfill was not started.");

        await using var tx = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            var flows = await _db.Wf_FlowDefs.IgnoreQueryFilters().ToListAsync(ct);
            var forms = await _db.Wf_FormDefs.IgnoreQueryFilters().ToListAsync(ct);
            var existingFlowVersions = await _db.Wf_FlowDefVersions.IgnoreQueryFilters().ToListAsync(ct);
            var existingFormVersions = await _db.Wf_FormDefVersions.IgnoreQueryFilters().ToListAsync(ct);

            var insertedFlowVersions = 0;
            foreach (var flow in flows)
            {
                if (existingFlowVersions.Any(x => x.TenantId == flow.TenantId && x.FlowDefId == flow.Id && x.Version == flow.Version))
                    continue;
                var version = new Wf_FlowDefVersion
                {
                    Id = Guid.NewGuid(), TenantId = flow.TenantId, FlowDefId = flow.Id,
                    Version = flow.Version, Status = WfDefinitionVersionStatus.Published,
                    FlowNameSnapshot = flow.FlowName, SchemaJson = flow.SchemaJson,
                    PublishedAtUtc = DateTime.UtcNow, Creator = "oa-p0-backfill"
                };
                _db.Wf_FlowDefVersions.Add(version);
                existingFlowVersions.Add(version);
                insertedFlowVersions++;
            }

            var insertedFormVersions = 0;
            foreach (var form in forms)
            {
                if (existingFormVersions.Any(x => x.TenantId == form.TenantId && x.FormDefId == form.Id && x.Version == form.Version))
                    continue;
                var version = new Wf_FormDefVersion
                {
                    Id = Guid.NewGuid(), TenantId = form.TenantId, FormDefId = form.Id,
                    Version = form.Version, Status = WfDefinitionVersionStatus.Published,
                    FormNameSnapshot = form.FormName, SchemaJson = form.SchemaJson,
                    PublishedAtUtc = DateTime.UtcNow, Creator = "oa-p0-backfill"
                };
                _db.Wf_FormDefVersions.Add(version);
                existingFormVersions.Add(version);
                insertedFormVersions++;
            }
            await _db.SaveChangesAsync(ct);

            var instances = await _db.Wf_FlowInstances.IgnoreQueryFilters().ToListAsync(ct);
            var flowPins = 0;
            foreach (var instance in instances.Where(x => x.FlowDefVersionId == null))
            {
                var def = flows.SingleOrDefault(x => x.TenantId == instance.TenantId && x.FlowKey == instance.FlowKey);
                if (def == null) continue;
                instance.FlowDefVersionId = existingFlowVersions.Single(x =>
                    x.TenantId == def.TenantId && x.FlowDefId == def.Id && x.Version == def.Version).Id;
                flowPins++;
            }

            var dataRows = await _db.Wf_FormDatas.IgnoreQueryFilters().ToListAsync(ct);
            var dataPins = 0;
            foreach (var data in dataRows.Where(x => x.FormDefVersionId == null))
            {
                var def = forms.SingleOrDefault(x =>
                    x.TenantId == data.TenantId && x.FormKey == data.FormKey && x.Version == data.FormVersion);
                if (def == null) continue;
                data.FormDefVersionId = existingFormVersions.Single(x =>
                    x.TenantId == def.TenantId && x.FormDefId == def.Id && x.Version == def.Version).Id;
                dataPins++;
            }

            var existingBindings = await _db.Wf_FormFlowBindings.IgnoreQueryFilters().ToListAsync(ct);
            var bindingExpected = flows.Count(x => !string.IsNullOrWhiteSpace(x.FormKey));
            var bindings = 0;
            foreach (var flow in flows.Where(x => !string.IsNullOrWhiteSpace(x.FormKey)))
            {
                var form = forms.SingleOrDefault(x => x.TenantId == flow.TenantId && x.FormKey == flow.FormKey);
                if (form == null || existingBindings.Any(x => x.TenantId == flow.TenantId && x.FormDefId == form.Id && x.Enable))
                    continue;
                var binding = new Wf_FormFlowBinding
                {
                    Id = Guid.NewGuid(), TenantId = flow.TenantId, FormDefId = form.Id,
                    FlowDefId = flow.Id, Enable = true, Creator = "oa-p0-backfill"
                };
                _db.Wf_FormFlowBindings.Add(binding);
                existingBindings.Add(binding);
                bindings++;
            }

            var existingDependencies = await _db.Wf_FlowDefVersionDependencies.IgnoreQueryFilters().ToListAsync(ct);
            var dependencyExpected = 0;
            var dependencies = 0;
            foreach (var flow in flows)
            {
                var sourceVersion = existingFlowVersions.Single(x =>
                    x.TenantId == flow.TenantId && x.FlowDefId == flow.Id && x.Version == flow.Version);
                foreach (var (nodeId, subKey) in ReadSubFlowRefs(flow.SchemaJson))
                {
                    dependencyExpected++;
                    if (existingDependencies.Any(x => x.TenantId == flow.TenantId &&
                                                      x.FlowDefVersionId == sourceVersion.Id &&
                                                      x.NodeId == nodeId &&
                                                      x.DependencyType == "SubFlow"))
                        continue;
                    var targetDef = flows.Single(x => x.TenantId == flow.TenantId && x.FlowKey == subKey);
                    var targetVersion = existingFlowVersions
                        .Where(x => x.TenantId == flow.TenantId && x.FlowDefId == targetDef.Id &&
                                    x.Status == WfDefinitionVersionStatus.Published)
                        .OrderByDescending(x => x.Version).First();
                    var dependency = new Wf_FlowDefVersionDependency
                    {
                        Id = Guid.NewGuid(), TenantId = flow.TenantId, FlowDefVersionId = sourceVersion.Id,
                        NodeId = nodeId, DependencyType = "SubFlow", TargetFlowDefVersionId = targetVersion.Id,
                        Creator = "oa-p0-backfill"
                    };
                    _db.Wf_FlowDefVersionDependencies.Add(dependency);
                    existingDependencies.Add(dependency);
                    dependencies++;
                }
            }

            var draftSources = instances.Where(x => x.Status == FlowInstanceStatus.Draft).ToList();
            var existingDrafts = await _db.Wf_FormDrafts.IgnoreQueryFilters().ToListAsync(ct);
            var drafts = 0;
            foreach (var source in draftSources)
            {
                if (existingDrafts.Any(x => x.TenantId == source.TenantId && x.LegacyFlowInstanceId == source.Id))
                    continue;
                var flow = flows.SingleOrDefault(x => x.TenantId == source.TenantId && x.FlowKey == source.FlowKey);
                if (flow == null) continue;
                var form = forms.SingleOrDefault(x => x.TenantId == source.TenantId && x.FormKey == flow.FormKey);
                if (form == null) continue;
                var formVersion = existingFormVersions.Single(x =>
                    x.TenantId == form.TenantId && x.FormDefId == form.Id && x.Version == form.Version);
                var draft = new Wf_FormDraft
                {
                    Id = Guid.NewGuid(), TenantId = source.TenantId, OwnerUserId = source.StarterId,
                    FormDefId = form.Id, FormDefVersionId = formVersion.Id, DataJson = source.VarsJson,
                    Status = WfFormDraftStatus.Active, LegacyFlowInstanceId = source.Id,
                    Creator = "oa-p0-backfill"
                };
                _db.Wf_FormDrafts.Add(draft);
                existingDrafts.Add(draft);
                drafts++;
            }

            await _db.SaveChangesAsync(ct);
            if (tx != null) await tx.CommitAsync(ct);

            return new OaP0BackfillReport(
                new(flows.Count, insertedFlowVersions, flows.Count - insertedFlowVersions, 0),
                new(forms.Count, insertedFormVersions, forms.Count - insertedFormVersions, 0),
                new(instances.Count, flowPins, instances.Count - flowPins, 0),
                new(dataRows.Count, dataPins, dataRows.Count - dataPins, 0),
                new(bindingExpected, bindings, bindingExpected - bindings, 0),
                new(dependencyExpected, dependencies, dependencyExpected - dependencies, 0),
                new(draftSources.Count, drafts, draftSources.Count - drafts, 0));
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static IEnumerable<string> ReadSubFlowKeys(string schemaJson) =>
        ReadSubFlowRefs(schemaJson).Select(x => x.SubFlowKey);

    private static IEnumerable<(string NodeId, string SubFlowKey)> ReadSubFlowRefs(string schemaJson)
    {
        FlowSchema? schema;
        try { schema = JsonSerializer.Deserialize<FlowSchema>(schemaJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
        catch (JsonException) { yield break; }
        if (schema == null) yield break;
        foreach (var node in schema.Nodes.Where(x => x.Type == "subFlow" && !string.IsNullOrWhiteSpace(x.SubFlowKey)))
            yield return (node.Id, node.SubFlowKey!);
    }
}
