using System.Globalization;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Mes.QualitySpecificationValuesV1;
using static CP6.Core.Services.Mes.QualitySpecificationStoreV1;

namespace CP6.Core.Services.Mes;

/// <summary>Independent professional quality source. Engineering can hold this source before
/// a baseline exists; this service never decides inspection outcomes or grants production use.</summary>
public sealed partial class QualitySpecificationAuthorityService(CP6Context db, OperationsOwnerV1 owner,
    ScopeAuthorizationFenceV1 scope, QualitySpecificationRuntimeV1 runtime, ControlledDocumentOwnerV1 documents)
{
    internal bool Owns(CP6Context context)=>ReferenceEquals(db,context) && owner.Owns(db) && documents.Owns(db);
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private Guid Environment => scope.EnvironmentId;
    private async Task<QualitySpecificationStoreV1> Store(OperationsAccessV1 access, bool write, CancellationToken ct)
    {
        if (runtime.EvidenceMode is not ("REAL" or "DEMO")) throw Error("QUALITY_SPEC_ENVIRONMENT_UNAVAILABLE",503);
        await scope.Root(ct); var lease = await scope.AcquireAsync(ct); access.AtCommit(t => scope.ValidateAsync(lease,t));
        var store = new QualitySpecificationStoreV1(db,Environment); await store.Fence(write,ct); return store;
    }
    private async Task<QualitySpecificationViewV1> View(OperationsAccessV1 a,QualitySpecificationStoreV1 s,Guid id,long? version,CancellationToken ct)
    {
        await a.RequireQualitySpecification(Environment,id,ct); a.AtCommit(t => a.RequireQualitySpecification(Environment,id,t));
        var v=await s.Read(id,version,ct);
        if(v.EvidenceMode!=runtime.EvidenceMode)throw Error("QUALITY_SPEC_MODE_MISMATCH",403);
        var control=await s.Control(id,ct);var c=control.Body;
        var policy=await s.Record<QualityReviewPolicyV1>(id,"POLICY",c.PolicyId,ct);
        var packet=await s.Record<QualityReviewPacketV1>(id,"PACKET",c.PacketId,ct);
        var approval=await s.Record<QualitySpecificationApprovalV1>(id,"APPROVAL",c.ApprovalId,ct);
        var votes=await s.Votes(id,c.PacketId,ct);
        var missing=await Sources(a,v.Body,false,ct);
        return new(v,control.Reference,c,"\"qs1-"+control.Reference.Digest+"\"",policy,packet,votes,approval,missing);
    }
    public Task<QualitySpecificationScopeV1> ScopeAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(
        OperationsOwnerV1.QualitySpecification,"query",false,async(a,t)=>{
            a.RequireQualitySpecificationFields(false);_ = await Store(a,false,t);
            return new QualitySpecificationScopeV1(db.CurrentTenantId,a.ActorId,Environment,runtime.EvidenceMode);
        },ct);
    public Task<QualitySpecificationViewV1[]> ListAsync(string? search=null,int page=1,CancellationToken ct=default)
    {
        if(search?.Length>256 || page is <1 or >100000)throw Error("QUALITY_SPEC_QUERY_INVALID",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.QualitySpecification,"query",false,async(a,t)=>{
            var s=await Store(a,false,t);var rows=a.QualitySpecifications(Environment);
            if(!string.IsNullOrEmpty(search))rows=rows.Where(x=>x.Name.Contains(search));
            var ids=await rows.OrderBy(x=>x.Name).ThenBy(x=>x.Id).Skip((page-1)*20).Take(20).Select(x=>x.Id).ToArrayAsync(t);
            var result=new List<QualitySpecificationViewV1>();foreach(var id in ids)result.Add(await View(a,s,id,null,t));return result.ToArray();
        },ct);
    }
    public Task<QualitySpecificationViewV1> ReadAsync(Guid id,string? version=null,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("QUALITY_SPEC_IDENTITY_INVALID",400);var exact=version==null?(long?)null:Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.QualitySpecification,"query",false,async(a,t)=>await View(a,await Store(a,false,t),id,exact,t),ct);
    }
    public sealed record RoleChoice(int RoleId,string RoleName);
    public Task<RoleChoice[]> RolesAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.QualitySpecification,"review-policy",false,async(a,t)=>{
        a.RequireQualitySpecificationFields(false);
        return await db.Sys_Roles.AsNoTracking().Where(x=>x.Enable).OrderBy(x=>x.RoleName).ThenBy(x=>x.RoleId).Select(x=>new RoleChoice(x.RoleId,x.RoleName)).ToArrayAsync(t);
    },ct,readOnlyAuthorityFence:true);
    private async Task Roles(QualityReviewPolicyInputV1 p,CancellationToken ct)
    {
        var expected=p.Roles.Select(x=>x.RoleId).Distinct().Order().ToArray();
        var actual=await db.Sys_Roles.AsNoTracking().Where(x=>x.Enable&&expected.Contains(x.RoleId)).OrderBy(x=>x.RoleId).Select(x=>x.RoleId).ToArrayAsync(ct);
        if(!expected.SequenceEqual(actual))throw Error("QUALITY_REVIEW_ROLE_UNAVAILABLE",422);
    }
    private async Task<QualitySpecificationRecordV1<QualityReviewPolicyV1>> ActivePolicy(OperationsAccessV1 root,QualitySpecificationViewV1 v,CancellationToken ct)
    {
        var p=v.Policy??throw Error("QUALITY_REVIEW_POLICY_REQUIRED",422);var now=Now();
        if(p.Body.Input.State!="ENABLED" || now<p.Body.Input.EffectiveFrom || now>=p.Body.Input.EffectiveTo)throw Error("QUALITY_REVIEW_POLICY_UNAVAILABLE",422);
        await Roles(p.Body.Input,ct);
        var initial=await Authority.QualityPolicyAuthorityAsync(p.Body.ActorId,ct);
        var source=new OperationsAccessV1(db,owner,initial);await source.RequireQualitySpecification(Environment,v.Version.Reference.Id,ct);
        root.AtCommit(async t=>{
            var final=await Authority.QualityPolicyAuthorityAsync(p.Body.ActorId,t);
            if(final.Source.SourcePermissionVersion!=initial.Source.SourcePermissionVersion || final.OrganizationVersion!=initial.OrganizationVersion
                || Now()>=p.Body.Input.EffectiveTo)throw Error("QUALITY_REVIEW_POLICY_AUTHORITY_CHANGED",403);
            await Roles(p.Body.Input,t);
        });
        return p;
    }
    private static void Match(QualitySpecificationViewV1 v,string? expected)
    {if(string.IsNullOrEmpty(expected))throw Error("IF_MATCH_REQUIRED",428);if(v.Etag!=expected)throw Error("QUALITY_SPEC_CHANGED",412);}
    private static string Action(string action)=>action switch
    {"CREATE"=>"add","REVISE"=>"edit","POLICY"=>"review-policy","SUBMIT"=>"review-submit","APPROVE" or "RETURN"=>"review-decide","WITHDRAW"=>"withdraw",_=>throw Error("QUALITY_SPEC_ACTION_INVALID",400)};
    public Task<QualitySpecificationResultV1> CommandAsync(QualitySpecificationCommandV1 input,CancellationToken ct=default)
    {
        if(input==null || input.OperationId==Guid.Empty || input.SpecificationId==Guid.Empty)throw Error("QUALITY_SPEC_COMMAND_INVALID",400);
        var command=OperationsOwnerV1.Freeze(input);var action=Action(command.Action);Text(command.Reason);
        if(command.Action is "CREATE" or "REVISE")Body(command.Body);else if(command.Body!=null)throw Error("QUALITY_SPEC_COMMAND_INVALID",400);
        if(command.Action=="POLICY")Policy(command.Policy);else if(command.Policy!=null)throw Error("QUALITY_SPEC_COMMAND_INVALID",400);
        if(command.Action=="CREATE" && command.ExpectedEtag!=null)throw Error("QUALITY_SPEC_COMMAND_INVALID",400);
        var bytes=MainOwnerOperationJournalV1.Encode(command);var journal=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.QualitySpecification,action,true,async(a,t)=>{
            var s=await Store(a,true,t);
            if(original!=null)return await Disclose(a,s,original,command.OperationId,bytes,t);
            a.RequireQualitySpecificationFields(command.Action is "CREATE" or "REVISE");
            QualitySpecificationViewV1? before=null;QualitySpecificationVersionV1 version;
            var id=command.SpecificationId;
            if(command.Action=="CREATE")
            {
                await a.RequireQualitySpecificationCreation(command.Body!.Product.Id,t);await Sources(a,command.Body,false,t);
                version=await s.Version(id,command.Body,a.ActorId,runtime.EvidenceMode,command.OperationId,true,t);
            }
            else
            {
                before=await View(a,s,id,null,t);Match(before,command.ExpectedEtag);version=before.Version;
                if(command.Action=="REVISE")
                {
                    if(before.Control.State=="IN_REVIEW")throw Error("QUALITY_REVIEW_FROZEN",409);
                    await Sources(a,command.Body!,false,t);version=await s.Version(id,command.Body!,a.ActorId,runtime.EvidenceMode,command.OperationId,false,t);
                }
            }
            var c=new QualitySpecificationControlV1(id,checked((before?.Control.Revision??0)+1),version.Reference.Version,
                before?.Control.State??"DRAFT",before?.Control.PolicyId,before?.Control.PacketId,before?.Control.ApprovalId,a.ActorId,command.Reason,Now());
            switch(command.Action)
            {
                case "CREATE":case "REVISE": c=c with{State="DRAFT",PacketId=null};break;
                case "POLICY":
                    if(c.State=="IN_REVIEW")throw Error("QUALITY_REVIEW_FROZEN",409);
                    await Roles(command.Policy!,t);var policy=new QualityReviewPolicyV1(Guid.NewGuid(),id,a.ActorId,command.Policy!,Now());
                    await s.Record(id,"POLICY",policy.Id,policy,command.OperationId,a.ActorId,t);c=c with{PolicyId=policy.Id};break;
                case "SUBMIT":
                    if(c.State is not ("DRAFT" or "RETURNED"))throw Error("QUALITY_REVIEW_STATE_INVALID",409);
                    var p=await ActivePolicy(a,before!,t);await Sources(a,version.Body,true,t);
                    var packet=new QualityReviewPacketV1(Guid.NewGuid(),version.Reference,p.Reference,version.AuthorId,a.ActorId,Hash(Bytes(version.Body.Requirements)),Now());
                    await s.Record(id,"PACKET",packet.Id,packet,command.OperationId,a.ActorId,t);c=c with{State="IN_REVIEW",PacketId=packet.Id};break;
                case "APPROVE":case "RETURN":
                    if(c.State!="IN_REVIEW" || before!.Packet==null)throw Error("QUALITY_REVIEW_STATE_INVALID",409);
                    var active=await ActivePolicy(a,before,t);var frozen=before.Packet;
                    if(frozen.Body.Specification!=version.Reference || frozen.Body.Policy!=active.Reference || frozen.Body.RequirementsDigest!=Hash(Bytes(version.Body.Requirements)))throw Error("QUALITY_REVIEW_SOURCE_CHANGED",412);
                    if(frozen.Body.AuthorId==a.ActorId)throw Error("QUALITY_REVIEW_AUTHOR_SEPARATION",403);
                    if(before.Votes.Any(x=>x.Body.ActorId==a.ActorId))throw Error("QUALITY_REVIEW_ALREADY_SIGNED",409);
                    var authority=a.Authorities.Single(x=>x.Resource==OperationsOwnerV1.QualitySpecification&&x.Action==action&&x.Actor.UserId==a.ActorId);
                    var roleIds=authority.Actor.RoleIds.Distinct().Order().ToArray();
                    if(!active.Body.Input.Roles.Any(x=>roleIds.Contains(x.RoleId)))throw Error("QUALITY_REVIEW_PROFESSIONAL_ROLE_REQUIRED",403);
                    if(command.Action=="APPROVE")await Sources(a,version.Body,true,t);
                    var vote=new QualityReviewVoteV1(Guid.NewGuid(),frozen.Body.Id,a.ActorId,roleIds,command.Action=="APPROVE",command.Reason,
                        authority.Source.SourcePermissionVersion,authority.OrganizationVersion,Now());
                    var signed=await s.Record(id,"VOTE",vote.Id,vote,command.OperationId,a.ActorId,t);
                    var votes=before.Votes.Append(signed).ToArray();
                    if(!vote.Approved)c=c with{State="RETURNED"};
                    else if(active.Body.Input.Roles.All(r=>votes.Where(v=>v.Body.Approved&&v.Body.RoleIds.Contains(r.RoleId)).Select(v=>v.Body.ActorId).Distinct().Count()>=r.MinimumApprovals))
                    {
                        var approval=new QualitySpecificationApprovalV1(Guid.NewGuid(),version.Reference,frozen.Reference,active.Reference,
                            votes.Where(x=>x.Body.Approved).Select(x=>x.Reference).ToArray(),frozen.Body.RequirementsDigest,version.Body.CompletenessStatement,Now());
                        await s.Record(id,"APPROVAL",approval.Id,approval,command.OperationId,a.ActorId,t);c=c with{State="APPROVED",ApprovalId=approval.Id};
                    }
                    break;
                case "WITHDRAW":
                    if(c.ApprovalId==null)throw Error("QUALITY_SPEC_APPROVAL_REQUIRED",409);
                    c=c with{State="WITHDRAWN",ApprovalId=null,PacketId=null};break;
            }
            var control=await s.SaveControl(c,command.OperationId,t);
            var result=new QualitySpecificationResultV1(command.OperationId,id,command.Action,version.Reference,control,c.State);
            await journal.Store(Journal,Contract,command.OperationId,a.ActorId,bytes,result,[id],new{before=before?.ControlReference,after=control,command.Reason},t);
            return result;
        },ct,actionAtFence:async t=>{original=await journal.Read(Journal,command.OperationId,t);return original==null?action:"query";});
    }
    private async Task<QualitySpecificationResultV1> Disclose(OperationsAccessV1 a,QualitySpecificationStoreV1 s,
        MainOwnerOperationJournalV1.Original original,Guid operation,byte[]? input,CancellationToken ct)
    {
        if(original.Actor!=a.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(original.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(input!=null&&!MainOwnerOperationJournalV1.Matches(original,input))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<QualitySpecificationResultV1>(original.Result);
        if(result==null || result.OperationId!=operation || !original.Objects.SequenceEqual(new[]{result.ObjectId}))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        var saved=await View(a,s,result.ObjectId,Revision(result.Specification.Version),ct);
        if(saved.Version.Reference!=result.Specification)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);return result;
    }
    public Task<QualitySpecificationOperationV1> OperationAsync(Guid id,bool close=false,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.QualitySpecification,"query",close,async(a,t)=>{
            var s=await Store(a,close,t);var journal=new MainOwnerOperationJournalV1(db);var original=await journal.Read(Journal,id,t);
            if(original!=null)
            {
                if(original.Actor!=a.ActorId || original.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                return original.State=="CLOSED"?new(id,"CLOSED"):new QualitySpecificationOperationV1(id,"COMMITTED",await Disclose(a,s,original,id,null,t));
            }
            var result=new QualitySpecificationOperationV1(id,close?"CLOSED":"UNKNOWN");
            if(close)await journal.Store(Journal,Contract,id,a.ActorId,null,result,[],new{action="close-uncommitted-quality-specification"},t);
            return result;
        },ct);
    }
}
