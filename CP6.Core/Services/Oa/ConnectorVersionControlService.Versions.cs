using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using CP6.Entity.Contracts.Wire;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class ConnectorVersionControlService
{
    private sealed record Content(ConnSelectionV1 Selection,ConnConfigV1? Config,Guid? DefinitionId,
        string? SchemaDigest,FlowDependencyRefV2[] Dependencies);
    private sealed record DependencyHeads(Guid ProfileRevision,Guid? FlowActivation,long AuthorityEpoch,Guid AuthorityRevision);
    private static string Fingerprint(object value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(WorkflowCommandJsonV1.Canonical(Value(value))))).ToLowerInvariant();
    private static void Natural(string? name,int length)
    {
        if(string.IsNullOrWhiteSpace(name) || name!=name.Trim() || name.Length>length || name.Any(char.IsControl))
            throw Error("METADATA_INVALID");
    }
    private string EffectiveAdoption(Profile p,ConnProfileV1 spec,ConnectorPinV1? pin=null)
    {
        if(p.Adoption=="SIMULATED") return "SIMULATED";
        if(p.Adoption!="ADOPTED" || string.IsNullOrWhiteSpace(p.Evidence)) return "UNPROVEN";
        if(p.Kind!="CONNECTOR") return triggerAuthorities.Count(a=>a.Supports(Tenant,Environment,p.Id,p.Kind,spec,p.Evidence))==1 ? "ADOPTED":"UNPROVEN";
        if(pin==null || spec.MaxCallDurationSec is not >0) return "UNPROVEN";
        var registered=ConnectorControlRuntimeV1.Describe(pin,spec,false,Tenant,Environment,p.Id,p.Evidence);
        return providers.Count(a=>!a.IsSimulation && a.Matches(registered))==1?"ADOPTED":"UNPROVEN";
    }

    private object PublicProfile(Profile p,ConnectorPinV1? pin=null)
    {
        var spec=Definition(p);
        return new {p.Id,p.Kind,p.Label,p.Enabled,adoption=EffectiveAdoption(p,spec,pin),spec.SourceKind,spec.Implementation,spec.ImplementationVersion,
            spec.Operation,spec.DestinationPolicy,spec.EndpointProfile,spec.InputSchema,spec.OutputSchema,spec.EffectClass,spec.MaxCallDurationSec,
            spec.ParameterMapping,spec.ResponseMapping,spec.LeasePolicy,spec.RetryPolicy,spec.DisclosurePolicy,spec.IdempotencyContract,spec.ResultQueryContract,
            spec.Registration,spec.Schema,spec.VarsSchema,spec.RateLimitPolicy,spec.SourceModule,spec.HookName,spec.TimeZone,spec.TimeRules,spec.Calendar,
            misfirePolicy=p.Kind=="TIMER"?"LATEST_ONE_SKIP_MISSED":null,spec.StarterAuthority,
            hasAuth=spec.CredentialLocator!=null,credentialGeneration=spec.CredentialGeneration,spec.StarterId};
    }
    public Task<JsonElement> OptionsAsync(string kind,CancellationToken ct) => Read(async(actor,token)=>
    {
        CheckKind(kind);
        var rows=await Profiles().Where(p=>kind=="CONNECTOR" ? p.Kind=="CONNECTOR" : p.Kind!="CONNECTOR")
            .OrderBy(p=>p.Label).ThenBy(p=>p.Id).ToArrayAsync(token);
        var definitions=kind=="TRIGGER" ? await flows.BindingChoices(actor,token) : [];
        var legacySources=actor.ActionKeys.Contains(Menu+":"+Permission(kind,"write")) ? await LegacyOptions(kind,definitions,token) : [];
        return Value(new {profiles=rows.Select(row=>PublicProfile(row)),definitions,legacySources,
            actions=actor.ActionKeys.Where(k=>k.StartsWith(Menu+":conn.",StringComparison.Ordinal)).Select(k=>k[(Menu.Length+1)..]).Order()});
    },ct);
    private sealed record LegacyOption(Guid Id,string Name,string SourceKind,string? FlowKey);
    private async Task<LegacyOption[]> LegacyOptions(string kind,IReadOnlyCollection<ApprovalBindingFlowV2> definitions,CancellationToken ct)
    {
        var adopted=Roots().Where(r=>r.Kind==kind).Select(r=>r.Id);
        if(kind=="CONNECTOR")
            return await db.Wf_Connectors.Where(x=>!x.Enabled && !adopted.Contains(x.Id)).OrderBy(x=>x.Name)
                .Select(x=>new LegacyOption(x.Id,x.Name,"TENANT",null)).ToArrayAsync(ct);
        var keys=definitions.Select(d=>d.FlowKey).Distinct().ToArray();
        return await db.Wf_FlowTriggers.Where(x=>!x.Enabled && keys.Contains(x.FlowKey) && !adopted.Contains(x.Id))
            .OrderBy(x=>x.FlowKey).ThenBy(x=>x.Id).Select(x=>new LegacyOption(x.Id,x.FlowKey,
                x.TriggerType==WfTriggerType.Timer?"TIMER":x.TriggerType==WfTriggerType.Event?"EVENT":"MESSAGE",x.FlowKey)).ToArrayAsync(ct);
    }
    private async Task<(Profile Profile,FlowPublicationV2? Flow)> Selected(string kind,ConnSelectionV1 selection,UserPermissionContext actor,CancellationToken ct)
    {
        Natural(selection.Name,100);
        var p=await ProfileFor(selection.ProfileId,ct);
        if(kind=="CONNECTOR")
        {
            if(p.Kind!="CONNECTOR" || selection.SourceKind is not ("TENANT" or "APP") || selection.SourceKind!=Definition(p).SourceKind ||
                selection.FlowKey!=null || selection.FlowVersionId!=null) throw Error("PIN_MISMATCH",409);
            return(p,null);
        }
        if(p.Kind!=selection.SourceKind || p.Kind is not ("TIMER" or "EVENT" or "MESSAGE") ||
            string.IsNullOrWhiteSpace(selection.FlowKey) || selection.FlowVersionId is not Guid id || id==Guid.Empty)
            throw Error("PIN_MISMATCH",409);
        return(p,await flows.BindingSource(actor,selection.FlowKey,id,ct));
    }
    private async Task<DependencyHeads> Heads(Profile p,Guid? flow,ScopeAuthorizationSnapshotV1 auth,CancellationToken ct)
    {
        Guid? revision=null;
        if(flow is Guid id)
            revision=await db.Database.SqlQuery<Guid?>($"""
                SELECT revision AS "Value" FROM cp6_workflow_authoring_v2.activation_current
                WHERE tenant_id={Tenant} AND environment_id={Environment} AND head_id={id}
                """).SingleOrDefaultAsync(ct) ?? throw Error("FLOW_VERSION_UNAVAILABLE",503);
        return new(p.Revision,revision,auth.Epoch,auth.RowVersion);
    }
    private async Task<string?> LegacyRevision(string kind,ConnSelectionV1 selection,Profile profile,CancellationToken ct)
    {
        if(kind=="CONNECTOR")
        {
            if(selection.SourceKind=="APP")
            {
                if(selection.LegacyRootId.HasValue) throw Error("PIN_MISMATCH",409);
                return null;
            }
            var row=await db.Wf_Connectors.AsNoTracking().Where(x=>x.Name.ToLower()==selection.Name.ToLower())
                .Select(x=>new {x.Id,x.Enabled,x.RowVersion,hasAuth=x.AuthJsonEncrypted!=null}).SingleOrDefaultAsync(ct);
            if(row==null && selection.LegacyRootId==null) return null;
            if(row==null || selection.LegacyRootId!=row.Id) throw Error("LEGACY_ROOT_SELECTION_REQUIRED",409);
            if(row.Enabled) throw Error("LEGACY_CONTROL_ACTIVE",409);
            var spec=Definition(profile);
            if(row.hasAuth && (spec.LegacyCredentialOwnerRef!=row.Id || spec.CredentialLocator==null))
                throw Error("CREDENTIAL_OWNER_REQUIRED",403);
            return Fingerprint(new {row.Id,row.RowVersion});
        }
        if(selection.LegacyRootId is not Guid legacyId) return null;
        var trigger=await db.Wf_FlowTriggers.AsNoTracking().Where(x=>x.Id==legacyId)
            .Select(x=>new {x.FlowKey,x.TriggerType,x.Enabled,x.RowVersion}).SingleOrDefaultAsync(ct);
        int type=selection.SourceKind switch {"TIMER"=>WfTriggerType.Timer,"EVENT"=>WfTriggerType.Event,_=>WfTriggerType.Message};
        if(trigger==null || trigger.FlowKey!=selection.FlowKey || trigger.TriggerType!=type) throw Error("PIN_MISMATCH",409);
        if(trigger.Enabled) throw Error("LEGACY_CONTROL_ACTIVE",409);
        return Fingerprint(new {legacyId,trigger.RowVersion});
    }

    private async Task<string> CreationVersion(string kind,ConnSelectionV1 selection,Profile profile,Guid? flow,
        ScopeAuthorizationSnapshotV1 auth,CancellationToken ct)
    {
        if(await Roots().AnyAsync(r=>r.Kind==kind && r.SourceKind==selection.SourceKind && r.Name.ToLower()==selection.Name.ToLower(),ct))
            throw Error("NATURAL_KEY_EXISTS",409);
        var legacy=await LegacyRevision(kind,selection,profile,ct);
        return Fingerprint(new {Tenant,Environment,kind,selection,legacy,heads=await Heads(profile,flow,auth,ct),absence="ABSENT"});
    }
    public Task<JsonElement> CreationAsync(string kind,ConnSelectionV1 selection,CancellationToken ct) => Read(async(actor,token)=>
    {
        CheckKind(kind);
        await Require(actor,Permission(kind,"write"),token);
        var scope=await fence.AcquireAsync(token);
        var (profile,flow)=await Selected(kind,selection,actor,token);
        return Value(new {expectedVersion=await CreationVersion(kind,selection,profile,flow?.DefinitionId,scope.Snapshot,token),absence="ABSENT",profile=PublicProfile(profile)});
    },ct);
    private static readonly HashSet<string> ReservedVariables=new(StringComparer.OrdinalIgnoreCase)
    {
        "scope","tenantId","environmentId","starter","starterId","starterUserId","actorId","permissions",
        "credential","credentialLocator","credentialGeneration","authJson","secret","authorization","accessToken"
    };
    private static bool ContainsReserved(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object=>value.EnumerateObject().Any(p=>ReservedVariables.Contains(p.Name) || ContainsReserved(p.Value)),
        JsonValueKind.Array=>value.EnumerateArray().Any(ContainsReserved),
        _=>false
    };
    private static void CheckConfig(string source,ConnConfigV1? config)
    {
        if(source is "TENANT" or "APP" or "MESSAGE")
        {
            if(config!=null) throw Error("CONFIG_INVALID");
            return;
        }
        if(source=="TIMER")
        {
            if(config?.Cron is not { Length: >0 and <=200 } cron || !WfCronHelper.IsValid(cron) ||
                WfCronHelper.NextUtc(cron,DateTime.UtcNow)==null || config.VarsMap!=null ||
                config.Vars is not { ValueKind:JsonValueKind.Object } vars || ContainsReserved(vars)) throw Error("TIMER_CONFIG_INVALID");
        }
        else if(config?.Cron!=null || config?.Vars!=null || config?.VarsMap?.Count>100 ||
            config?.VarsMap?.Any(p=>ReservedVariables.Contains(p.Key) || string.IsNullOrWhiteSpace(p.Key) ||
                p.Key.Length>100 || string.IsNullOrWhiteSpace(p.Value) || p.Value.Length>500)==true) throw Error("EVENT_CONFIG_INVALID");
    }
    private async Task<Applied> Draft(string kind,Head? root,ConnWireV1 wire,UserPermissionContext actor,ScopeAuthorizationSnapshotV1 auth,CancellationToken ct)
    {
        var draft=wire.Read<ConnDraftV1>();
        var selected=draft.Selection ?? throw Error("SELECTION_REQUIRED");
        Natural(draft.DisplayName,200);
        var (profile,flow)=await Selected(kind,selected,actor,ct);
        CheckConfig(selected.SourceKind,draft.Config);
        var id=root?.Id ?? selected.LegacyRootId ?? Guid.NewGuid();
        var version=Guid.NewGuid();
        var revision=Guid.NewGuid();
        int number=1;
        if(root==null)
        {
            if(kind=="CONNECTOR" && !await db.Database.SqlQuery<bool>($"""
                SELECT pg_try_advisory_xact_lock(cp6_oa_conn_v1.name_lock({Tenant},{selected.Name})) AS "Value"
                """).SingleAsync(ct)) throw Error("LEGACY_CALL_IN_FLIGHT",409);
            if(wire.Expected!=await CreationVersion(kind,selected,profile,flow?.DefinitionId,auth,ct)) throw Error("CONFIG_CHANGED",412);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_oa_conn_v1.roots(tenant_id,environment_id,id,kind,source_kind,name,owner_id,revision,latest_version)
                VALUES({Tenant},{Environment},{id},{kind},{selected.SourceKind},{selected.Name},{actor.UserId},{revision},{version});
                INSERT INTO cp6_oa_conn_v1.frontiers(tenant_id,environment_id,root_id) VALUES({Tenant},{Environment},{id})
                """,ct);
        }
        else
        {
            Expected(wire.Expected,root.Revision);
            if(root.Name!=selected.Name || root.SourceKind!=selected.SourceKind) throw Error("PIN_MISMATCH",409);
            var previous=await Exact(root,root.Latest,ct);
            var previousSource=VersionContent(previous).Selection;
            if(previousSource.LegacyRootId!=selected.LegacyRootId || previousSource.FlowKey!=selected.FlowKey)
                throw Error("PIN_MISMATCH",409);
            number=previous.Number+1;
            var oldProfile=Definition(await ProfileFor(previous.ProfileId,ct));
            if(kind=="CONNECTOR" && (oldProfile.CredentialLocator!=Definition(profile).CredentialLocator ||
                oldProfile.CredentialGeneration!=Definition(profile).CredentialGeneration))
                throw Error("CREDENTIAL_OWNER_REQUIRED",403);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE cp6_oa_conn_v1.roots SET latest_version={version},revision={revision}
                WHERE tenant_id={Tenant} AND environment_id={Environment} AND id={id}
                """,ct);
        }
        var content=Value(new Content(selected,draft.Config,flow?.DefinitionId,flow?.SchemaDigest,flow?.DependencyRefs??[]));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_oa_conn_v1.versions(tenant_id,environment_id,root_id,id,number,maker,profile_id,display_name,content)
            VALUES({Tenant},{Environment},{id},{version},{number},{actor.UserId},{profile.Id},{draft.DisplayName},CAST({content.GetRawText()} AS jsonb));
            INSERT INTO cp6_oa_conn_v1.version_control(tenant_id,environment_id,root_id,version_id,state)
            VALUES({Tenant},{Environment},{id},{version},'DRAFT')
            """,ct);
        return new(id,version,new {revision,number,state="DRAFT"});
    }
    private static Content VersionContent(Version version) => JsonSerializer.Deserialize<Content>(version.Content,Json)!;
    private Task<bool> CanReadSource(Version version,UserPermissionContext actor,CancellationToken ct)
    {
        var content=VersionContent(version);
        return content.DefinitionId is Guid id && content.Selection.FlowVersionId is Guid pin
            ? flows.CanReadBindingSource(actor,id,pin,ct) : Task.FromResult(content.DefinitionId==null);
    }

    private async Task<object> Context(Head root,Version v,CancellationToken ct)
    {
        var profile=await ProfileFor(v.ProfileId,ct);
        var approvals=await (from s in Subjects() join vote in Decisions() on s.Id equals vote.SubjectId into votes
            from vote in votes.DefaultIfEmpty() where s.RootId==root.Id && s.VersionId==v.Id
            orderby s.Id select new {subjectRef=s.Id,s.Kind,s.ImpactRef,s.Maker,decisionRef=(Guid?)vote.Id,
                decision=vote.Vote??"PENDING"}).ToArrayAsync(ct);
        var versions=await Versions(root.Id).OrderByDescending(item=>item.Number)
            .Select(item=>new {item.Id,item.Number}).ToArrayAsync(ct);
        return new {rootId=root.Id,root.Kind,root.SourceKind,root.Name,root.Revision,root.Latest,root.Active,versionId=v.Id,versions,
            v.Number,v.DisplayName,v.State,v.Published,content=VersionContent(v),
            profile=PublicProfile(profile,new(root.SourceKind,root.Id,v.Id,Definition(profile).Operation??"")),approvals};
    }
    public Task<JsonElement> ContextAsync(string kind,Guid id,Guid? version,CancellationToken ct) => Read(async(actor,token)=>
    {
        var root=await Root(kind,id,token);
        var item=await Exact(root,version??root.Latest,token);
        if(!await CanReadSource(item,actor,token)) throw Error("RESOURCE_NOT_FOUND",404);
        return Value(await Context(root,item,token));
    },ct);
    public Task<JsonElement> ListAsync(string kind,int page,CancellationToken ct) => Read(async(actor,token)=>
    {
        CheckKind(kind);
        if(page is <1 or >100000) throw Error("PAGE_INVALID",400);
        var roots=await Roots().Where(r=>r.Kind==kind).OrderBy(r=>r.Name).ThenBy(r=>r.Id).Skip((page-1)*100).Take(100).ToArrayAsync(token);
        var rows=new List<object>();
        foreach(var r in roots)
        {
            var item=await Exact(r,r.Latest,token);
            if(await CanReadSource(item,actor,token)) rows.Add(await Context(r,item,token));
        }
        return Value(rows);
    },ct);
    private async Task CheckAdmission(Head root,Version version,Profile p,UserPermissionContext actor,CancellationToken ct)
    {
        var detail=Definition(p);
        var status=EffectiveAdoption(p,detail,new(root.SourceKind,root.Id,version.Id,detail.Operation??""));
        if(!p.Enabled || string.IsNullOrWhiteSpace(p.Evidence) ||
            !(status=="ADOPTED" || status=="SIMULATED" && options.Value.AllowSimulatedParticipants))
            throw Error("EXTERNAL_CONTRACT_UNPROVEN",503);
        if(root.Kind=="TRIGGER")
        {
            var content=VersionContent(version);
            var (_,publication)=await Selected("TRIGGER",content.Selection,actor,ct);
            if(publication?.DefinitionId!=content.DefinitionId || publication?.SchemaDigest!=content.SchemaDigest ||
                !publication!.DependencyRefs.SequenceEqual(content.Dependencies)) throw Error("PIN_MISMATCH",409);
            var active=await db.Database.SqlQuery<Guid?>($"""
                SELECT published_version_id AS "Value" FROM cp6_workflow_authoring_v2.activation_current
                WHERE tenant_id={Tenant} AND environment_id={Environment} AND head_id={publication.DefinitionId} AND enabled
                """).SingleOrDefaultAsync(ct);
            if(active!=publication.VersionId) throw Error("FLOW_ACTIVATION_CHANGED",412);
            if(detail.StarterId is not Guid starter || string.IsNullOrWhiteSpace(detail.StarterAuthority) ||
                !await db.Sys_Users.AnyAsync(u=>u.Id==starter && u.Enable && (u.LockedUntil==null || u.LockedUntil<=DateTime.Now),ct))
                throw Error("STARTER_AUTHORITY_UNPROVEN",503);
            bool qualified=root.SourceKind switch {
                "TIMER"=>detail.TimeRules!=null && detail.TimeZone!=null,
                "EVENT"=>detail.Registration!=null && detail.Schema!=null && detail.SourceModule!=null && detail.HookName!=null,
                "MESSAGE"=>detail.Registration!=null && detail.Schema!=null && detail.VarsSchema!=null && detail.RateLimitPolicy!=null,
                _=>false
            };
            if(!qualified) throw Error("TRIGGER_CONTRACT_UNPROVEN",503);
            return;
        }
        if(detail.MaxCallDurationSec is not >0 || detail.MaxCallDurationSec>=WfServiceJobService.LeaseDuration.TotalSeconds)
            throw Error("CALL_DURATION_UNPROVEN");
        string?[] required=[detail.Implementation,detail.ImplementationVersion,detail.DestinationPolicy,detail.EndpointProfile,
            detail.Operation,detail.InputSchema,detail.OutputSchema,detail.ParameterMapping,detail.ResponseMapping,detail.LeasePolicy,
            detail.RetryPolicy,detail.DisclosurePolicy];
        if(required.Any(string.IsNullOrWhiteSpace) || detail.EffectClass is not ("READ_ONLY" or "EXTERNAL_MUTATION" or "OWNER_COMMAND")
            || (detail.CredentialLocator==null)!=(detail.CredentialGeneration==null)
            || detail.EffectClass!="READ_ONLY" && (detail.IdempotencyContract==null || detail.ResultQueryContract==null || detail.AdmissionBasis==null))
            throw Error("EXTERNAL_CONTRACT_UNPROVEN",503);

    }
}
