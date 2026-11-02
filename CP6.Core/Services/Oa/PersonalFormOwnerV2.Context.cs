using System.Text.Json;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class PersonalFormOwnerV2
{
    private sealed record ActivationRow(Guid Revision, bool Enabled, Guid? Version);
    private sealed record BindingRow(Guid Revision, Guid Head, Guid Version, Guid FormVersion, string Key);
    private sealed record Admission(Head Head, FormPublicationV2 Form, PersonalFormPolicyV2 Policy, ActivationRow Activation,
        BindingRow? Binding, FlowPublicationV2? Flow, Dictionary<string,string> Mask);
    private async Task<Admission> Resolve(UserPermissionContext actor, string key, bool submit, CancellationToken ct)
    {
        var head = await Heads().SingleOrDefaultAsync(x=>x.Key==key,ct) ?? throw Error("FORM_NOT_FOUND",404);
        var policy = await Policy(head.Id,ct) ?? throw Error("FORM_USE_POLICY_REQUIRED",403);
        var activation = await db.Database.SqlQuery<ActivationRow>($"""
            SELECT revision AS "Revision",enabled AS "Enabled",published_version_id AS "Version" FROM cp6_form_authoring_v2.activation_current
            WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head.Id}
            """).SingleAsync(ct);
        if (activation.Version == null) throw Error("FORM_NOT_PUBLISHED",422);
        var publication = await Publication(head.Id,activation.Version.Value,ct);
        var mask = await Mask(actor,head.Id,publication,ct);
        if (!policy.Enabled || !activation.Enabled) throw Error("FORM_DISABLED",422);
        var binding = await db.Database.SqlQuery<BindingRow>($"""
            SELECT a.revision AS "Revision",a.head_id AS "Head",a.published_version_id AS "Version",a.form_version_id AS "FormVersion",h.flow_key AS "Key"
            FROM cp6_workflow_authoring_v2.activation_current a JOIN cp6_workflow_authoring_v2.heads h
              ON h.tenant_id=a.tenant_id AND h.environment_id=a.environment_id AND h.id=a.head_id
            WHERE a.tenant_id={db.CurrentTenantId} AND a.environment_id={fence.EnvironmentId} AND a.form_head_id={head.Id} AND a.enabled
            """).SingleOrDefaultAsync(ct);
        FlowPublicationV2? flow = null;
        if (binding != null)
        {
            if (binding.FormVersion != publication.VersionId) throw Error("FORM_BINDING_VERSION_CHANGED",409);
            flow = await flows.SubmissionSource(binding.Key,binding.Version,ct);
            // The selected immutable graph is internally qualified. Service effects still
            // require an adopted ordinary-FORM Owner, so reject that capability at admission.
            if (submit) await RuntimeCapability(flow,[],ct);
        }
        else if (submit && !policy.AllowsUnbound) throw Error("FORM_BINDING_REQUIRED",422);
        return new(head,publication,policy,activation,binding,flow,mask);
    }
    private async Task RuntimeCapability(FlowPublicationV2 source, HashSet<Guid> visited, CancellationToken ct)
    {
        if(!visited.Add(source.VersionId)) return;
        if(source.DependencyRefs.Any(x=>x.Kind=="SERVICE")) throw Error("FORM_SERVICE_OWNER_REQUIRED",422);
        foreach(var reference in source.DependencyRefs.Where(x=>x.Kind=="FLOW"))
        {
            var id=Guid.Parse(reference.Version!);
            var json=await db.Database.SqlQuery<string>($"""
                SELECT snapshot_json AS "Value" FROM cp6_workflow_authoring_v2.versions
                WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND id={id}
                """).SingleAsync(ct);
            await RuntimeCapability(Parse<FlowPublicationV2>(json),visited,ct);
        }
    }
    private PersonalFormContextV2 Context(Admission value, UserPermissionContext actor)
    {
        var capabilities = new List<string>();
        if(actor.ActionKeys.Contains("oa-form-catalog:add")) capabilities.Add("CREATE_DRAFT");
        if(actor.ActionKeys.Contains("oa-form-catalog:submit") && (value.Binding!=null || value.Policy.AllowsUnbound)) capabilities.Add("SUBMIT");
        return new(value.Head.Key,value.Form.VersionId,value.Form.Version,value.Form.Fields["Name"]!,
            ProjectSchema(value.Form.Fields["SchemaJson"]!,value.Mask),value.Activation.Revision.ToString("D"),value.Binding?.Revision.ToString("D"),
            value.Flow?.VersionId,value.Flow?.Version,value.Binding?.Revision.ToString("D"),value.Policy.AllowsUnbound,capabilities.ToArray(),
            value.Policy.Version+":"+value.Activation.Revision+":"+value.Binding?.Revision,value.Mask,TitleAccess(actor));
    }
    public async Task<PersonalFormContextV2> ContextAsync(string key, CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await fence.AcquireAsync(ct);await Ready(ct);
        var actor=await Actor(ct);await Action(actor,"query",ct);var value=await Resolve(actor,key,false,ct);
        // A visible submit capability always means its exact graph can participate in this transaction.
        if(value.Flow!=null) await RuntimeCapability(value.Flow,[],ct);
        await Final(actor,lease,ct);return Context(value,actor);
    }
    private const string PolicyOwner="Main.Workflow.PersonalFormPolicy.v2",PolicyContract="Main.Workflow.PersonalFormPolicy.configure.v2";
    public async Task<PersonalFormPolicyV2?> PolicyAsync(string key,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await fence.AcquireAsync(ct);var actor=await Actor(ct);
        await Action(actor,"edit",ct,"pub-role-perm");var head=await Heads().SingleOrDefaultAsync(x=>x.Key==key,ct)??throw Error("FORM_NOT_FOUND",404);
        var policy=await Policy(head.Id,ct);await Final(actor,lease,ct);return policy;
    }
    public async Task<PersonalFormPolicyV2> ConfigureAsync(string key,PersonalFormPolicyInputV2 input,CancellationToken ct=default)
    {
        if(input.OperationId==Guid.Empty || input.Roles==null || input.Roles.Length>100 || input.Roles.Any(x=>x==null) || input.Roles.Select(x=>x.RoleId).Distinct().Count()!=input.Roles.Length
            || input.Roles.Any(x=>x.Fields==null || x.Fields.Any(f=>string.IsNullOrWhiteSpace(f.Key)||f.Value is not ("edit" or "readonly" or "hidden")))
            || input.RetentionDays is < 1 or > 36500 || string.IsNullOrWhiteSpace(input.RetentionPolicyRef) || input.RetentionPolicyRef.Length>200
            || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length>500
            || input.ExpectedVersion!="absent" && !Guid.TryParseExact(input.ExpectedVersion,"D",out _)) throw Error("POLICY_INPUT_INVALID",400);
        var raw=MainOwnerOperationJournalV1.Encode(new{Key=key,Input=input,Environment=fence.EnvironmentId});
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var committing=false;
        try
        {
            var initialLease=await fence.AcquireAsync(ct);await Ready(ct);var actor=await Actor(ct);await Action(actor,"edit",ct,"pub-role-perm");
            var journal=new MainOwnerOperationJournalV1(db);var old=await journal.Read(PolicyOwner,input.OperationId,ct);
            if(old!=null)
            {
                if(old.Actor!=actor.UserId || old.Contract!=PolicyContract)throw Error("OBJECT_FORBIDDEN",403);
                if(!MainOwnerOperationJournalV1.Matches(old,raw))throw Error("COMMAND_BODY_CONFLICT",409);
                await Final(actor,initialLease,ct);return JsonSerializer.Deserialize<PersonalFormPolicyV2>(old.Result,Json)!;
            }
            var head=await Heads().SingleOrDefaultAsync(x=>x.Key==key,ct)??throw Error("FORM_NOT_FOUND",404);
            var previous=await Policy(head.Id,ct);
            if(input.ExpectedVersion!=(previous?.Version.ToString("D")??"absent"))throw Error("POLICY_CHANGED",409);
            var roles=input.Roles.Select(x=>x.RoleId).ToArray();
            if(await db.Sys_Roles.CountAsync(r=>roles.Contains(r.RoleId),ct)!=roles.Length)throw Error("ROLE_REFERENCE_INVALID",422);
            var schemas=await db.Database.SqlQuery<string>($"""
                SELECT snapshot_json AS "Value" FROM cp6_form_authoring_v2.versions
                WHERE tenant_id={db.CurrentTenantId} AND environment_id={fence.EnvironmentId} AND head_id={head.Id}
                """).ToArrayAsync(ct);
            var names=schemas.SelectMany(s=>PersonalFormValuesV2.Schema(Parse<FormPublicationV2>(s).Fields["SchemaJson"]!).Fields.Select(f=>f.Name)).ToHashSet(StringComparer.Ordinal);
            if(input.Roles.Any(r=>r.Fields.Keys.Any(f=>!names.Contains(f))))throw Error("POLICY_FIELD_UNKNOWN",422);
            var body=Encode(new PersonalFormPolicyV2(Guid.Empty,input.Enabled,input.AllowsUnbound,input.Roles,input.RetentionPolicyRef,input.RetentionDays,input.AllowRestore));
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_personal_form_v2.policy(tenant_id,environment_id,head_id,body) VALUES({db.CurrentTenantId},{fence.EnvironmentId},{head.Id},{body})
                ON CONFLICT(tenant_id,environment_id,head_id) DO UPDATE SET body=EXCLUDED.body
                """,ct);
            var saved=(await Policy(head.Id,ct))!;var lease=await fence.AcquireAsync(ct);
            await journal.Store(PolicyOwner,PolicyContract,input.OperationId,actor.UserId,raw,saved,[lease.Snapshot.PermissionDomain],new{input.Reason,Previous=previous,Current=saved,Scope=lease.Snapshot},ct);
            await Action(await Actor(ct),"edit",ct,"pub-role-perm");await Final(actor,lease,ct);committing=true;await tx.CommitAsync(ct);return saved;
        }
        catch(Exception ex){await Rollback(tx,committing,ex);throw;}
    }
}
