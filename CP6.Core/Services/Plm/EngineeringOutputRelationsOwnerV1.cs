using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringOutputRelationsValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringOutputRelationsOwnerV1(CP6Context db,OperationsOwnerV1 owner,ScopeAuthorizationFenceV1 scope,
    QualitySpecificationRuntimeV1 runtime,ControlledDocumentOwnerV1 documents,QualitySpecificationAuthorityService quality)
{
    internal bool Owns(CP6Context context)=>ReferenceEquals(db,context) && owner.Owns(db) && documents.Owns(db) && quality.Owns(db);
    private OperationsOwnerV1 Authority=>Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private Guid Environment=>scope.EnvironmentId;
    private async Task<EngineeringOutputRelationsStoreV1> Store(OperationsAccessV1 a,bool write,CancellationToken ct)
    {
        if(runtime.EvidenceMode is not ("REAL" or "DEMO"))throw Error("OUTPUT_RELATIONS_ENVIRONMENT_UNAVAILABLE",503);
        await scope.Root(ct);var lease=await scope.AcquireAsync(ct);a.AtCommit(t=>scope.ValidateAsync(lease,t));
        var s=new EngineeringOutputRelationsStoreV1(db,Environment);await s.Fence(write,ct);return s;
    }
    private async Task<OutputRelationsViewV1> View(OperationsAccessV1 a,EngineeringOutputRelationsStoreV1 s,Guid id,long? selected,CancellationToken ct)
    {
        await a.RequireOutputRelations(Environment,id,ct);a.AtCommit(t=>a.RequireOutputRelations(Environment,id,t));
        var v=await s.Read(id,selected,ct);if(v.Original.EvidenceMode!=runtime.EvidenceMode)throw Error("OUTPUT_RELATIONS_MODE_MISMATCH",403);
        var control=await s.Control(id,ct);var c=control.Body;
        var p=await s.Record<OutputRelationsPolicyV1>(id,"POLICY",c.PolicyId,ct);
        var q=await s.Record<OutputRelationsQualificationV1>(id,"QUALIFICATION",c.QualificationId,ct);
        var sources=await Sources(a,v.Original.Body,false,ct);FinalSources(a,v.Original.Body,sources,false);
        var missing=sources.Missing.ToList();
        if(sources.Fingerprint!=v.Original.SourceFingerprint)missing.Add("OUTPUT_RELATIONS_SOURCES_CHANGED");
        if(c.DraftVersion!=v.Reference.Version)missing.Add("OUTPUT_RELATIONS_VERSION_CHANGED");
        if(c.State!="QUALIFIED" || q==null)missing.Add(c.State=="WITHDRAWN"?"OUTPUT_RELATIONS_WITHDRAWN":"OUTPUT_RELATIONS_QUALIFICATION_REQUIRED");
        else
        {
            if(q.Body.Relation!=v.Reference || q.Body.SourceFingerprint!=v.Original.SourceFingerprint || q.Body.RelationDigest!=v.Original.Wire.BodyDigest)
                missing.Add("OUTPUT_RELATIONS_QUALIFICATION_CHANGED");
            try
            {
                await a.EngineeringObservation(async(observation,t)=>{
                    var policy=await ActivePolicy(observation,id,p,t);
                    if(q.Body.Policy!=policy.Reference)throw Error("OUTPUT_RELATIONS_POLICY_CHANGED",412);
                    await Qualifier(observation,id,q.Body,t);return true;
                },ct);
            }
            catch(OrderPriceCorrectionException ex) when(ex.Status is 403 or 412 or 422){missing.Add(ex.Message);}
        }
        return new(v,control.Reference,c,"\"or1-"+control.Reference.Digest+"\"",p,q,missing.Distinct(StringComparer.Ordinal).ToArray(),sources.Downstream);
    }
    private async Task Role(OutputRelationsPolicyInputV1 p,CancellationToken ct)
    {if(!await db.Sys_Roles.AsNoTracking().AnyAsync(x=>x.Enable && x.RoleId==p.RoleId,ct))throw Error("OUTPUT_RELATIONS_PROFESSIONAL_ROLE_UNAVAILABLE",422);}
    private async Task<OutputRelationsRecordV1<OutputRelationsPolicyV1>> ActivePolicy(OperationsAccessV1 root,Guid id,
        OutputRelationsRecordV1<OutputRelationsPolicyV1>? policy,CancellationToken ct)
    {
        var p=policy??throw Error("OUTPUT_RELATIONS_POLICY_REQUIRED",422);Policy(p.Body.Input);await Role(p.Body.Input,ct);
        if(DateTimeOffset.UtcNow<p.Body.Input.EffectiveFrom || DateTimeOffset.UtcNow>=p.Body.Input.EffectiveTo)throw Error("OUTPUT_RELATIONS_POLICY_UNAVAILABLE",422);
        var initial=await Authority.OutputRelationsAuthorityAsync("review-policy",p.Body.ActorId,ct);
        var access=new OperationsAccessV1(db,owner,initial);await access.RequireOutputRelations(Environment,id,ct);
        root.AtCommit(async t=>{
            var final=await Authority.OutputRelationsAuthorityAsync("review-policy",p.Body.ActorId,t);
            if(final.Source.SourcePermissionVersion!=initial.Source.SourcePermissionVersion || final.OrganizationVersion!=initial.OrganizationVersion
                || DateTimeOffset.UtcNow>=p.Body.Input.EffectiveTo)throw Error("OUTPUT_RELATIONS_POLICY_AUTHORITY_CHANGED",403);
            await new OperationsAccessV1(db,owner,final).RequireOutputRelations(Environment,id,t);await Role(p.Body.Input,t);
        });return p;
    }
    private async Task Qualifier(OperationsAccessV1 root,Guid id,OutputRelationsQualificationV1 q,CancellationToken ct)
    {
        async Task Check(CancellationToken t)
        {
            var a=await Authority.OutputRelationsAuthorityAsync("qualify",q.ActorId,t);
            if(!a.Actor.RoleIds.Contains(q.RoleId) || a.Source.SourcePermissionVersion!=q.PermissionVersion || a.OrganizationVersion!=q.OrganizationVersion)
                throw Error("OUTPUT_RELATIONS_QUALIFIER_AUTHORITY_CHANGED",403);
            await new OperationsAccessV1(db,owner,a).RequireOutputRelations(Environment,id,t);
        }
        await Check(ct);root.AtCommit(Check);
    }
    public Task<QualitySpecificationScopeV1> ScopeAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringOutputRelations,"query",false,
        async(a,t)=>{_ = await Store(a,false,t);return new QualitySpecificationScopeV1(db.CurrentTenantId,a.ActorId,Environment,runtime.EvidenceMode);},ct);
    public Task<OutputRelationsViewV1[]> ListAsync(string? search=null,int page=1,CancellationToken ct=default)
    {
        if(search?.Length>256 || page is <1 or >100000)throw Error("OUTPUT_RELATIONS_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringOutputRelations,"query",false,async(a,t)=>{
            var s=await Store(a,false,t);var rows=a.OutputRelations(Environment);if(!string.IsNullOrEmpty(search))rows=rows.Where(x=>x.Name.Contains(search));
            var ids=await rows.OrderBy(x=>x.Name).ThenBy(x=>x.Id).Skip((page-1)*20).Take(20).Select(x=>x.Id).ToArrayAsync(t);
            var result=new List<OutputRelationsViewV1>();foreach(var id in ids)result.Add(await View(a,s,id,null,t));return result.ToArray();
        },ct);
    }
    public Task<OutputRelationsViewV1> ReadAsync(Guid id,string? version=null,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("OUTPUT_RELATIONS_IDENTITY_INVALID",400);var selected=version==null?(long?)null:Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringOutputRelations,"query",false,async(a,t)=>await View(a,await Store(a,false,t),id,selected,t),ct);
    }
    public Task<EngineeringWorkingChangeV1[]> DiffAsync(Guid id,string before,string after,CancellationToken ct=default)
    {
        var left=Revision(before);var right=Revision(after);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringOutputRelations,"query",false,async(a,t)=>{
            var s=await Store(a,false,t);var x=await View(a,s,id,left,t);var y=await View(a,s,id,right,t);
            var result=new List<EngineeringWorkingChangeV1>();
            void Compare(string path,object first,object last)
            {if(Hash(Bytes(first))!=Hash(Bytes(last)))result.Add(new(path,JsonSerializer.SerializeToElement(first,EngineeringProductStoreV1.Json),JsonSerializer.SerializeToElement(last,EngineeringProductStoreV1.Json)));}
            Compare("definition",x.Version.Original.Body,y.Version.Original.Body);Compare("relations",x.Version.Original.Wire,y.Version.Original.Wire);
            Compare("sourceProofs",x.Version.Original.Sources,y.Version.Original.Sources);return result.ToArray();
        },ct);
    }
    public sealed record RoleChoice(int RoleId,string RoleName);
    public Task<RoleChoice[]> RolesAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringOutputRelations,"review-policy",false,
        async(a,t)=>await db.Sys_Roles.AsNoTracking().Where(x=>x.Enable).OrderBy(x=>x.RoleName).ThenBy(x=>x.RoleId).Select(x=>new RoleChoice(x.RoleId,x.RoleName)).ToArrayAsync(t),ct,readOnlyAuthorityFence:true);
    private static string Action(string action)=>action switch{"CREATE"=>"add","REVISE"=>"edit","POLICY"=>"review-policy","QUALIFY"=>"qualify","WITHDRAW"=>"withdraw",_=>throw Error("OUTPUT_RELATIONS_ACTION_INVALID",400)};
    public Task<OutputRelationsResultV1> CommandAsync(EngineeringOutputRelationsCommandV1 input,CancellationToken ct=default)
    {
        if(input==null || input.OperationId==Guid.Empty || input.RelationId==Guid.Empty)throw Error("OUTPUT_RELATIONS_COMMAND_INVALID",400);
        var command=OperationsOwnerV1.Freeze(input);var action=Action(command.Action);EngineeringRevisionValuesV1.Text(command.Reason);
        if(command.Action is "CREATE" or "REVISE")Body(command.Body);else if(command.Body!=null)throw Error("OUTPUT_RELATIONS_COMMAND_INVALID",400);
        if(command.Action=="POLICY")Policy(command.Policy);else if(command.Policy!=null)throw Error("OUTPUT_RELATIONS_COMMAND_INVALID",400);
        if(command.Action=="CREATE" && command.ExpectedEtag!=null)throw Error("OUTPUT_RELATIONS_COMMAND_INVALID",400);
        var bytes=MainOwnerOperationJournalV1.Encode(command);var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringOutputRelations,action,true,async(a,t)=>{
            var s=await Store(a,true,t);if(original!=null)return await Disclose(a,s,original,command.OperationId,bytes,t);
            OutputRelationsViewV1? before=null;OutputRelationsVersionV1 version;var id=command.RelationId;
            if(command.Action=="CREATE")
            {
                var sources=await Sources(a,command.Body!,false,t);await a.RequireOutputRelationsCreation(sources.Manufacturing.Body.RevisionOrigin.Id,t);
                FinalSources(a,command.Body!,sources,false);
                version=await s.Version(id,command.Body!,sources.Manufacturing,sources.Mbom,sources.Routing,sources.Fingerprint,sources.Pins,a.ActorId,runtime.EvidenceMode,command.OperationId,true,t);
            }
            else
            {
                before=await View(a,s,id,null,t);if(string.IsNullOrEmpty(command.ExpectedEtag))throw Error("IF_MATCH_REQUIRED",428);
                if(before.Etag!=command.ExpectedEtag)throw Error("OUTPUT_RELATIONS_CHANGED",412);version=before.Version;
                if(command.Action=="REVISE")
                {
                    var sources=await Sources(a,command.Body!,false,t);FinalSources(a,command.Body!,sources,false);
                    version=await s.Version(id,command.Body!,sources.Manufacturing,sources.Mbom,sources.Routing,sources.Fingerprint,sources.Pins,a.ActorId,runtime.EvidenceMode,command.OperationId,false,t);
                }
            }
            var c=new OutputRelationsControlV1(id,checked((before?.Control.Revision??0)+1),version.Reference.Version,before?.Control.State??"DRAFT",
                before?.Control.PolicyId,before?.Control.QualificationId,a.ActorId,command.Reason,DateTimeOffset.UtcNow);
            switch(command.Action)
            {
                case "CREATE":case "REVISE":c=c with{State="DRAFT",QualificationId=null};break;
                case "POLICY":
                    await Role(command.Policy!,t);var p=new OutputRelationsPolicyV1(Guid.NewGuid(),id,a.ActorId,command.Policy!,DateTimeOffset.UtcNow);
                    await s.SaveRecord(id,"POLICY",p.Id,p,command.OperationId,a.ActorId,t);c=c with{PolicyId=p.Id,State="DRAFT",QualificationId=null};break;
                case "QUALIFY":
                    if(c.State=="QUALIFIED")throw Error("OUTPUT_RELATIONS_ALREADY_QUALIFIED",409);
                    var policy=await ActivePolicy(a,id,before!.Policy,t);var current=await Sources(a,version.Original.Body,true,t);
                    if(current.Fingerprint!=version.Original.SourceFingerprint)throw Error("OUTPUT_RELATIONS_SOURCES_CHANGED",412);
                    if(a.ActorId==version.Original.AuthorId || a.ActorId==version.Original.CreatorId || current.Authors.Contains(a.ActorId))throw Error("OUTPUT_RELATIONS_AUTHOR_SEPARATION",403);
                    var authority=a.Authorities.Single(x=>x.Resource==OperationsOwnerV1.EngineeringOutputRelations && x.Action==action && x.Actor.UserId==a.ActorId);
                    if(!authority.Actor.RoleIds.Contains(policy.Body.Input.RoleId))throw Error("OUTPUT_RELATIONS_PROFESSIONAL_ROLE_REQUIRED",403);
                    var q=new OutputRelationsQualificationV1(Guid.NewGuid(),id,a.ActorId,version.Reference,policy.Reference,current.Fingerprint,version.Original.Wire.BodyDigest,
                        policy.Body.Input.RoleId,authority.Source.SourcePermissionVersion,authority.OrganizationVersion,command.Reason,DateTimeOffset.UtcNow);
                    await s.SaveRecord(id,"QUALIFICATION",q.Id,q,command.OperationId,a.ActorId,t);FinalSources(a,version.Original.Body,current,true);
                    await Qualifier(a,id,q,t);c=c with{State="QUALIFIED",QualificationId=q.Id};break;
                case "WITHDRAW":
                    if(c.State!="QUALIFIED" || c.QualificationId==null)throw Error("OUTPUT_RELATIONS_QUALIFICATION_REQUIRED",409);
                    c=c with{State="WITHDRAWN",QualificationId=null};break;
            }
            var control=await s.SaveControl(c,command.OperationId,t);var result=new OutputRelationsResultV1(command.OperationId,id,command.Action,version.Reference,control,c.State);
            await journal.Store(Journal,Contract,command.OperationId,a.ActorId,bytes,result,[id],new{before=before?.ControlReference,after=control,command.Reason},t);return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(Journal,command.OperationId,t);return original==null?action:"query";});
    }
    private async Task<OutputRelationsResultV1> Disclose(OperationsAccessV1 a,EngineeringOutputRelationsStoreV1 s,MainOwnerOperationJournalV1.Original original,Guid operation,byte[]? input,CancellationToken ct)
    {
        if(original.Actor!=a.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null && !MainOwnerOperationJournalV1.Matches(original,input))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<OutputRelationsResultV1>(original.Result);
        if(result==null || result.OperationId!=operation || !original.Objects.SequenceEqual(new[]{result.ObjectId}))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        if((await View(a,s,result.ObjectId,Revision(result.Relation.Version),ct)).Version.Reference!=result.Relation)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        return result;
    }
    public Task<OutputRelationsOperationResultV1> OperationAsync(Guid id,bool close=false,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringOutputRelations,"query",close,async(a,t)=>{
            var s=await Store(a,close,t);var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(Journal,id,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                return original.State=="CLOSED"?new(id,"CLOSED"):new OutputRelationsOperationResultV1(id,"COMMITTED",await Disclose(a,s,original,id,null,t));
            }
            var result=new OutputRelationsOperationResultV1(id,close?"CLOSED":"UNKNOWN");
            if(close)await journal.Store(Journal,Contract,id,a.ActorId,null,result,[],new{action="close-uncommitted-output-relations"},t);return result;
        },ct);
    }
}
