using CP6.Core.Services.Oa;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

public sealed partial class FlowAuthoringOwnerV2
{
    // Internal TxS consumer after registered service + native Scope authority. This is not
    // an alternate public definition reader and does not impersonate a governance user.
    internal async Task<FlowPublicationV2> SubmissionSource(string key,Guid? version,CancellationToken ct)
    {
        fence.RequireTransaction();await ActivationReady(ct);
        var head=await Heads().SingleOrDefaultAsync(x=>x.Key==key,ct)??throw Error("FLOW_VERSION_UNAVAILABLE",422);
        if(!head.Enabled)throw Error("FLOW_VERSION_UNAVAILABLE",422);
        var id=version??await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_workflow_authoring_v2.versions
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head.Id}
            ORDER BY version_number DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if(id==Guid.Empty)throw Error("FLOW_VERSION_UNAVAILABLE",422);
        var source=await ActivationSource(head,id,ct);
        await new DependencyReader(this,null,null,ct).QualifyRoot(source);return source;
    }
    // Called only by the binding Owner inside its authentication/Scope transaction. Never opens or commits one.
    internal async Task<ApprovalBindingFlowV2[]> BindingChoices(UserPermissionContext actor, CancellationToken ct)
    {
        fence.RequireTransaction(); await ReadCapability(actor, ct, AdminMenu);
        var result = new List<ApprovalBindingFlowV2>();
        foreach (var head in await Heads().OrderBy(x => x.Key).ToArrayAsync(ct))
        {
            if (!await Visible(actor, head, ct)) continue;
            var permission = await Permission(actor, head, false, false, ct, AdminMenu);
            if (Fields.Any(f => !permission.Fields[f].CanRead)) continue;
            var rows = await db.Database.SqlQuery<PublicationRow>($"""
                SELECT id AS "Id",snapshot_json AS "Snapshot",version_number AS "Number" FROM cp6_workflow_authoring_v2.versions
                WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head.Id}
                """).OrderByDescending(x => x.Number).ToArrayAsync(ct);
            result.AddRange(rows.Select(p => new ApprovalBindingFlowV2(head.Key, p.Id, p.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), "Published", head.Enabled)));
            if (result.Count > 1000) throw Error("BINDING_OPTIONS_LIMIT", 422);
        }
        return result.ToArray();
    }
    // 控制历史可以在流程停用后读取，但仍须具备当前原文的数据和字段读权。
    internal async Task<bool> CanReadBindingSource(UserPermissionContext actor,Guid definition,Guid version,CancellationToken ct)
    {
        fence.RequireTransaction();
        if(!actor.MenuKeys.Contains(AdminMenu)) return false;
        var access=await FieldAccess(false,ct,actor.UserId);
        if(Fields.Any(f=>!access.Fields[f].CanRead)) return false;
        var forms=new FormAuthoringOwnerV2(db,http,fence,currentActor,catalog);
        var pending=new Stack<(Guid Head,Guid Version)>();
        var checkedVersions=new HashSet<Guid>();
        pending.Push((definition,version));
        while(pending.TryPop(out var target))
        {
            if(!checkedVersions.Add(target.Version)) continue;
            var head=await Heads().SingleOrDefaultAsync(h=>h.Id==target.Head,ct);
            if(head==null || !await Visible(actor,head,ct)) return false;
            var row=await Published(target.Head,target.Version,ct);
            if(row==null) return false;
            var original=System.Text.Json.JsonSerializer.Deserialize<FlowPublicationV2>(row.Snapshot,Json)!;
            foreach(var dependency in original.DependencyRefs)
            {
                if(dependency.Kind=="FORM" && !await forms.CanReadPublishedBinding(actor,dependency.Reference,Pin(dependency),ct)) return false;
                if(dependency.Kind=="FLOW")
                {
                    var child=await Heads().SingleOrDefaultAsync(h=>h.Key==dependency.Reference,ct);
                    if(child==null) return false;
                    pending.Push((child.Id,Pin(dependency)));
                }
            }
        }
        return true;
    }
    internal async Task<FlowPublicationV2> BindingSource(UserPermissionContext actor, string key, Guid? version, CancellationToken ct)
    {
        fence.RequireTransaction(); await ActivationReady(ct);
        var head = await Heads().SingleOrDefaultAsync(x => x.Key == key, ct) ?? throw Error("FLOW_VERSION_UNAVAILABLE", 422);
        var fields = await Permission(actor, head, false, false, ct, AdminMenu);
        if (Fields.Any(f => !fields.Fields[f].CanRead)) throw Error("FIELD_FORBIDDEN", 403);
        if (!head.Enabled) throw Error("FLOW_VERSION_UNAVAILABLE", 422);
        var id = version ?? await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM cp6_workflow_authoring_v2.versions
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head.Id}
            ORDER BY version_number DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);
        if (id == Guid.Empty) throw Error("FLOW_VERSION_UNAVAILABLE", 422);
        var source = await ActivationSource(head, id, ct); await ActivationDependencies(actor, source, ct); return source;
    }
}
