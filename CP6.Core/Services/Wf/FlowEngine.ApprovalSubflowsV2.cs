using System.Text.Json;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public partial class FlowEngine
{
    private readonly Dictionary<Guid, FlowPublicationV2> _approvalPublications = [];
    private readonly List<(Wf_FlowInstance Instance, FlowPublicationV2 Publication, string Node)> _approvalChildren = [];

    // These are immutable sources already qualified at TxS. In-flight execution never consults
    // current activation or substitutes the latest publication for an accepted version.
    private async Task<FlowPublicationV2> ApprovalPublication(Guid version, CancellationToken ct = default)
    {
        if (_approvalPublications.TryGetValue(version, out var known)) return known;
        var environment = Guid.Parse(_approvalOriginal!.Scope.EnvironmentId);
        var json = await _db.Database.SqlQuery<string>($"""
            SELECT snapshot_json AS "Value" FROM cp6_workflow_authoring_v2.versions
            WHERE tenant_id={_db.CurrentTenantId} AND environment_id={environment} AND id={version}
            """).SingleOrDefaultAsync(ct) ?? throw new ScopeAuthorizationExceptionV1("FLOW_VERSION_UNAVAILABLE");
        var publication = JsonSerializer.Deserialize<FlowPublicationV2>(json, ApprovalBindingOwnerV2.Json)!;
        _approvalPublications.Add(version, publication); return publication;
    }

    private static Guid? ApprovalFormPin(FlowPublicationV2 publication)
        => publication.DependencyRefs.SingleOrDefault(x => x.Kind == "FORM" && x.Location == "formKey") is { } form
            ? Guid.Parse(form.Version!) : null;

    // A call owns a distinct parked token beneath the executing branch token. Preserve its
    // node/slot identity while the original branch continues, including later prune/return.
    internal Wf_FlowToken SubFlowCallToken(Wf_FlowInstance instance, FlowNode node, Wf_FlowToken branch)
        => !_approvalStartV2 ? branch : SnapshotTokens(instance.Id).SingleOrDefault(x =>
            x.ParentTokenId == branch.Id && x.NodeId == node.Id && x.Status == FlowTokenStatus.Active)
            ?? SpawnToken(instance, node, branch.Id, branch.ForkId);

    private async Task<Guid> SubmitApprovalChild(Guid version, Guid starter, string vars,
        Guid parentId, Guid parentTokenId, int index)
    {
        var publication = await ApprovalPublication(version);
        var schema = Deserialize(publication.Fields["SchemaJson"]!); var first = FirstNode(schema)!;
        var parentToken = _db.Wf_FlowTokens.Local.Single(x => x.Id == parentTokenId);
        var child = new Wf_FlowInstance
        {
            Id = Guid.NewGuid(), FlowDefVersionId = version, FormDefVersionId = ApprovalFormPin(publication),
            FlowKey = publication.Key, StarterId = starter, VarsJson = vars, Creator = ApprovalRuntimeV2,
            Status = FlowInstanceStatus.Running, CurrentNode = first.Id,
            ParentInstanceId = parentId, ParentTokenId = parentTokenId, SubIndex = index
        };
        _db.Wf_FlowInstances.Add(child); _approvalChildren.Add((child, publication, parentToken.NodeId));
        AddHistory(child.Id, first.Id, starter, "submit", "Pinned subprocess of original Owner submission");
        await EnterNodeAsync(child, schema, first, SpawnToken(child, first));
        return child.Id;
    }

    // Membership is persisted before EF runtime rows. Deferred FKs/graph constraints close it
    // together with starts/commands at the caller's single commit, including new grandchildren.
    internal async Task SaveApprovalRuntime(CancellationToken ct)
    {
        var environment = Guid.Parse(_approvalOriginal!.Scope.EnvironmentId);
        var root = ApprovalInstanceId(_approvalOriginal);
        foreach (var (child, publication, node) in _approvalChildren)
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_approval_runtime_v2.children(tenant_id,environment_id,instance_id,root_instance_id,
                  parent_instance_id,parent_token_id,parent_node,sub_index,flow_head,flow_version)
                VALUES({_db.CurrentTenantId},{environment},{child.Id},{root},{child.ParentInstanceId},
                  {child.ParentTokenId},{node},{child.SubIndex},{publication.DefinitionId},{publication.VersionId})
                """, ct);
        _approvalChildren.Clear();
    }

    // No legacy resume job or second transaction. The native Scope fence serializes decisions;
    // each terminal child can therefore resume its parent and ancestors in this same TxD.
    private async Task DrainApprovalSubflows(CancellationToken ct)
    {
        var root = ApprovalInstanceId(_approvalOriginal!);
        var environment = Guid.Parse(_approvalOriginal!.Scope.EnvironmentId);
        var ids = await _db.Database.SqlQuery<Guid>($"""
            SELECT instance_id AS "Value" FROM cp6_approval_runtime_v2.instances
            WHERE tenant_id={_db.CurrentTenantId} AND environment_id={environment} AND root_instance_id={root}
            """).ToArrayAsync(ct);
        await _db.Wf_FlowInstances.Where(x => ids.Contains(x.Id)).LoadAsync(ct);
        var members = ids.ToHashSet(); members.Add(root);
        foreach (var child in _approvalChildren) members.Add(child.Instance.Id);
        while (true)
        {
            var progressed = false;
            var groups = _db.Wf_FlowInstances.Local.Where(x => members.Contains(x.Id) && x.ParentTokenId != null)
                .GroupBy(x => x.ParentTokenId!.Value).ToArray();
            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();
                var token = _db.Wf_FlowTokens.Local.FirstOrDefault(x => x.Id == group.Key)
                    ?? await _db.Wf_FlowTokens.SingleAsync(x => x.Id == group.Key, ct);
                var parent = _db.Wf_FlowInstances.Local.Single(x => x.Id == token.InstanceId);
                if (token.Status != FlowTokenStatus.Active || parent.Status != FlowInstanceStatus.Running) continue;
                var schema = await LoadSchemaAsync(parent, ct); var node = FindNode(schema, token.NodeId)!;
                var children = group.OrderBy(x => x.SubIndex).ToArray();
                var approved = children.Where(x => x.Status == FlowInstanceStatus.Approved).ToArray();
                var dead = children.Where(x => x.Status is FlowInstanceStatus.Rejected or FlowInstanceStatus.Withdrawn).ToArray();
                var live = children.Where(x => x.Status is FlowInstanceStatus.Running or FlowInstanceStatus.Suspended or FlowInstanceStatus.Draft).ToArray();
                var any = string.Equals(node.SubCompletionPolicy, SubFlowCompletionPolicy.Any, StringComparison.OrdinalIgnoreCase);
                var pass = any ? approved.Length != 0 : dead.Length == 0 && live.Length == 0;
                var fail = any ? approved.Length == 0 && live.Length == 0 : dead.Length != 0;
                if (!pass && !fail) continue;
                foreach (var child in live) SubFlowCascade.CancelInstanceTree(_db, child);
                // Complete the call and resume its original branch. Keeping this lineage lets
                // terminal evidence exclude a successful child if its branch is later pruned.
                var branch = _db.Wf_FlowTokens.Local.FirstOrDefault(x => x.Id == token.ParentTokenId)
                    ?? await _db.Wf_FlowTokens.SingleAsync(x => x.Id == token.ParentTokenId, ct);
                ConsumeToken(token);
                if (pass)
                    await ResumeSubFlowAsync(parent, schema, node, branch,
                        any ? [approved[0]] : approved, aggregate: !any && node.SubCollectionVar != null);
                else
                    await SubFlowErrorDisposeAsync(parent, schema, node, branch,
                        null, dead[0].SubIndex!.Value, dead[0].Id, dead[0].Status);
                parent.ModifyDate = DateTime.Now; progressed = true;
                foreach (var child in _approvalChildren) members.Add(child.Instance.Id);
            }
            if (!progressed) return;
        }
    }
}
