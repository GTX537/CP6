using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.RequirementEvaluationValuesV1;
using static CP6.Core.Services.Plm.RequirementEvaluationStoreV1;
namespace CP6.Core.Services.Plm;

/// <summary>Professional approval of evaluation definitions, not evidence of fulfillment or set approval.</summary>
public sealed class RequirementEvaluationService(CP6Context db,OperationsOwnerV1 owner,ScopeAuthorizationFenceV1 scope,
    RequirementEvaluationRuntimeV1 runtime,EngineeringRequirementService requirements)
{
    private OperationsOwnerV1 Authority=>owner.Owns(db)?owner:throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
    private Guid Environment=>scope.EnvironmentId;
    private async Task<RequirementEvaluationStoreV1> Store(OperationsAccessV1 a,bool write,CancellationToken ct)
    {
        if(runtime.EvidenceMode is not("REAL" or "DEMO"))throw Error("REQUIREMENT_REVIEW_ENVIRONMENT_UNAVAILABLE",503);
        await scope.Root(ct);var lease=await scope.AcquireAsync(ct);a.AtCommit(t=>scope.ValidateAsync(lease,t));
        await new EngineeringRequirementStoreV1(db).Fence(write,ct);
        var s=new RequirementEvaluationStoreV1(db,Environment,runtime.EvidenceMode);await s.Fence(write,ct);return s;
    }
    private async Task<RequirementEvaluationInputsV1> Inputs(OperationsAccessV1 a,EngineeringSourceRefV1 reference,bool qualified,CancellationToken ct)
    {
        var v=await requirements.EvaluationInputs(a,reference,ct);
        if(qualified)
        {
            if(v.Missing.Length>0)throw Error("REQUIREMENT_REVIEW_SOURCE_INCOMPLETE",422);
            var digest=InputDigest(v);
            a.AtCommit(async t=>{
                var current=await a.EngineeringFinalObservation((root,token)=>requirements.EvaluationInputs(root,reference,token),t);
                if(current.Missing.Length>0 || InputDigest(current)!=digest)throw Error("REQUIREMENT_REVIEW_SOURCE_CHANGED",412);
            });
        }
        return v;
    }
    private async Task<RequirementEvaluationViewV1> View(OperationsAccessV1 a,RequirementEvaluationStoreV1 s,Guid id,long? version,CancellationToken ct,long? controlVersion=null)
    {
        await a.RequireEngineeringRequirement(id,ct);a.AtCommit(t=>a.RequireEngineeringRequirement(id,t));
        var originals=new EngineeringRequirementStoreV1(db);await originals.Fence(false,ct);var v=await originals.Read(id,version,ct);
        if(v.Body.Kind!="REQUIREMENT")throw Error("REQUIREMENT_DEFINITION_REQUIRED",422);
        var inputs=await Inputs(a,v.Reference,false,ct);v=inputs.Requirement;
        var current=await s.Control(id,ct);var control=controlVersion==null?current:await s.Control(id,ct,controlVersion);var c=control?.Body;
        if(controlVersion!=null && control==null)throw Error("REQUIREMENT_REVIEW_CONTROL_NOT_FOUND",404);
        var policy=await s.Record<RequirementEvaluationPolicyV1>(id,"POLICY",c?.PolicyId,ct);
        var packet=await s.Record<RequirementEvaluationPacketV1>(id,"PACKET",c?.PacketId,ct);
        var approval=await s.Record<RequirementEvaluationApprovalV1>(id,"APPROVAL",c?.ApprovalId,ct);
        var votes=await s.Votes(id,c?.PacketId,ct,controlVersion);
        // Historical packages carry their own dependency permissions; a new draft cannot launder an old source.
        foreach(var reference in new[]{packet?.Body.Requirement,approval?.Body.Requirement}.OfType<EngineeringSourceRefV1>().Distinct())
            if(reference!=v.Reference)await Inputs(a,reference,false,ct);
        if(packet!=null && InputDigest(packet.Body.Inputs)!=packet.Body.InputDigest)throw Error("REQUIREMENT_REVIEW_PACKET_UNAVAILABLE",503);
        return new(Environment,db.CurrentTenantId,runtime.EvidenceMode,v,control?.Reference,c,
            "\"re1-"+Hash(Bytes(new{environment=Environment,tenant=db.CurrentTenantId,mode=runtime.EvidenceMode,requirement=v.Reference,control=control?.Reference}))+"\"",
            policy,packet,votes,approval,inputs.Missing,control?.Reference==current?.Reference);
    }
    public Task<RequirementEvaluationScopeV1> ScopeAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,"query",false,
        async(a,t)=>{a.RequireRequirementFields(false);await Store(a,false,t);return new RequirementEvaluationScopeV1(Environment,db.CurrentTenantId,a.ActorId,runtime.EvidenceMode);},ct);
    public Task<RequirementEvaluationViewV1> ReadAsync(Guid id,string? version=null,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("REQUIREMENT_IDENTITY_INVALID",400);var selected=version==null?(long?)null:Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,"query",false,async(a,t)=>await View(a,await Store(a,false,t),id,selected,t),ct);
    }
    public sealed record RoleChoice(int RoleId,string RoleName);
    public Task<RequirementEvaluationViewV1> ReadControlAsync(Guid id,string revision,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("REQUIREMENT_IDENTITY_INVALID",400);var selected=Revision(revision);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,"query",false,async(a,t)=>await View(a,await Store(a,false,t),id,null,t,selected),ct);
    }
    public Task<RoleChoice[]> RolesAsync(CancellationToken ct=default)=>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,"review-policy",false,async(a,t)=>{
        a.RequireRequirementFields(false);await Store(a,false,t);
        return await db.Sys_Roles.AsNoTracking().Where(x=>x.Enable).OrderBy(x=>x.RoleName).ThenBy(x=>x.RoleId).Select(x=>new RoleChoice(x.RoleId,x.RoleName)).ToArrayAsync(t);
    },ct);
    private async Task Roles(RequirementEvaluationPolicyInputV1 p,CancellationToken ct)
    {
        var ids=p.Roles.Select(x=>x.RoleId).Distinct().Order().ToArray();
        var actual=await db.Sys_Roles.AsNoTracking().Where(x=>x.Enable&&ids.Contains(x.RoleId)).OrderBy(x=>x.RoleId).Select(x=>x.RoleId).ToArrayAsync(ct);
        if(!ids.SequenceEqual(actual))throw Error("REQUIREMENT_REVIEW_ROLE_UNAVAILABLE",422);
    }
    private async Task<RequirementEvaluationRecordV1<RequirementEvaluationPolicyV1>> ActivePolicy(OperationsAccessV1 root,RequirementEvaluationViewV1 v,CancellationToken ct)
    {
        var p=v.Policy??throw Error("REQUIREMENT_REVIEW_POLICY_REQUIRED",422);var now=Now();
        if(p.Body.Input.State!="ENABLED" || now<p.Body.Input.EffectiveFrom || now>=p.Body.Input.EffectiveTo)throw Error("REQUIREMENT_REVIEW_POLICY_UNAVAILABLE",422);
        await Roles(p.Body.Input,ct);var initial=await Authority.RequirementPolicyAuthorityAsync(p.Body.ActorId,ct);
        await new OperationsAccessV1(db,owner,initial).RequireEngineeringRequirement(v.Requirement.Reference.Id,ct);
        root.AtCommit(async t=>{
            var final=await Authority.RequirementPolicyAuthorityAsync(p.Body.ActorId,t);
            if(final.Source.SourcePermissionVersion!=initial.Source.SourcePermissionVersion || final.OrganizationVersion!=initial.OrganizationVersion
                || Now()>=p.Body.Input.EffectiveTo)throw Error("REQUIREMENT_REVIEW_POLICY_AUTHORITY_CHANGED",403);
            await new OperationsAccessV1(db,owner,final).RequireEngineeringRequirement(v.Requirement.Reference.Id,t);await Roles(p.Body.Input,t);
        });return p;
    }
    private static string Action(string action)=>action switch{
        "POLICY"=>"review-policy","SUBMIT"=>"review-submit","APPROVE" or "RETURN"=>"review-decide","WITHDRAW"=>"withdraw",_=>throw Error("REQUIREMENT_REVIEW_ACTION_INVALID",400)};
    public Task<RequirementEvaluationResultV1> CommandAsync(RequirementEvaluationCommandV1 input,CancellationToken ct=default)
    {
        if(input==null || input.OperationId==Guid.Empty || input.RequirementId==Guid.Empty)throw Error("REQUIREMENT_REVIEW_COMMAND_INVALID",400);
        var c=OperationsOwnerV1.Freeze(input);var action=Action(c.Action);EngineeringRequirementValuesV1.Text(c.Reason,true);
        if(c.Action=="POLICY")Policy(c.Policy);else if(c.Policy!=null)throw Error("REQUIREMENT_REVIEW_COMMAND_INVALID",400);
        if(c.Action=="SUBMIT"){EngineeringRequirementValuesV1.Ref(c.Requirement);if(c.Requirement!.Id!=c.RequirementId)throw Error("REQUIREMENT_REFERENCE_INVALID",400);}
        else if(c.Requirement!=null)throw Error("REQUIREMENT_REVIEW_COMMAND_INVALID",400);
        var bytes=MainOwnerOperationJournalV1.Encode(c);var j=new MainOwnerOperationJournalV1(db);MainOwnerOperationJournalV1.Original? original=null;
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,action,true,async(a,t)=>{
            var s=await Store(a,true,t);if(original!=null)return await Disclose(a,s,original,c.OperationId,bytes,t);
            var v=await View(a,s,c.RequirementId,null,t);
            if(string.IsNullOrEmpty(c.ExpectedEtag))throw Error("IF_MATCH_REQUIRED",428);
            if(c.ExpectedEtag!=v.Etag)throw Error("REQUIREMENT_REVIEW_CHANGED",412);
            var control=new RequirementEvaluationControlV1(c.RequirementId,checked((v.Control?.Revision??0)+1),v.Control?.State??"DRAFT",
                v.Control?.PolicyId,v.Control?.PacketId,v.Control?.ApprovalId,a.ActorId,c.Reason,Now());
            await s.EnsureHead(c.RequirementId,t);
            switch(c.Action)
            {
                case "POLICY":
                    // Explicit policy replacement returns a pending package; no hidden frozen-policy dead end.
                    await Roles(c.Policy!,t);var policy=new RequirementEvaluationPolicyV1(Guid.NewGuid(),c.RequirementId,a.ActorId,c.Policy!,Now());
                    await s.Record(c.RequirementId,"POLICY",policy.Id,policy,c.OperationId,a.ActorId,t);
                    control=control with{PolicyId=policy.Id,State=control.State=="IN_REVIEW"?"RETURNED":control.State};break;
                case "SUBMIT":
                    if(control.State=="IN_REVIEW")throw Error("REQUIREMENT_REVIEW_FROZEN",409);
                    if(c.Requirement!=v.Requirement.Reference)throw Error("REQUIREMENT_REVIEW_SOURCE_CHANGED",412);
                    var active=await ActivePolicy(a,v,t);var inputs=await Inputs(a,c.Requirement!,true,t);
                    var packet=new RequirementEvaluationPacketV1(Guid.NewGuid(),c.RequirementId,c.Requirement!,active.Reference,v.Requirement.ActorId,a.ActorId,InputDigest(inputs),inputs,Now());
                    await s.Record(c.RequirementId,"PACKET",packet.Id,packet,c.OperationId,a.ActorId,t);
                    control=control with{State="IN_REVIEW",PacketId=packet.Id};break;
                case "APPROVE":case "RETURN":
                    if(control.State!="IN_REVIEW" || v.Packet==null)throw Error("REQUIREMENT_REVIEW_STATE_INVALID",409);
                    // RETURN can release a package after policy expiry/revocation; current action, role and object remain mandatory.
                    var p=c.Action=="APPROVE"?await ActivePolicy(a,v,t):v.Policy??throw Error("REQUIREMENT_REVIEW_POLICY_REQUIRED",422);
                    var frozen=v.Packet;
                    if(frozen.Body.Requirement!=v.Requirement.Reference || frozen.Body.Policy!=p.Reference)throw Error("REQUIREMENT_REVIEW_SOURCE_CHANGED",412);
                    if(frozen.Body.AuthorId==a.ActorId || frozen.Body.SubmitterId==a.ActorId)throw Error("REQUIREMENT_REVIEW_INDEPENDENT_SIGNER_REQUIRED",403);
                    if(v.Votes.Any(x=>x.Body.ActorId==a.ActorId))throw Error("REQUIREMENT_REVIEW_ALREADY_SIGNED",409);
                    var authority=a.Authorities.Single(x=>x.Resource==OperationsOwnerV1.EngineeringRequirement&&x.Action==action&&x.Actor.UserId==a.ActorId);
                    var roleIds=authority.Actor.RoleIds.Distinct().Order().ToArray();
                    if(!p.Body.Input.Roles.Any(x=>roleIds.Contains(x.RoleId)))throw Error("REQUIREMENT_REVIEW_PROFESSIONAL_ROLE_REQUIRED",403);
                    if(c.Action=="APPROVE" && InputDigest(await Inputs(a,frozen.Body.Requirement,true,t))!=frozen.Body.InputDigest)throw Error("REQUIREMENT_REVIEW_SOURCE_CHANGED",412);
                    var vote=new RequirementEvaluationVoteV1(Guid.NewGuid(),frozen.Body.Id,a.ActorId,roleIds,c.Action=="APPROVE",c.Reason,authority.Source.SourcePermissionVersion,authority.OrganizationVersion,Now());
                    var signed=await s.Record(c.RequirementId,"VOTE",vote.Id,vote,c.OperationId,a.ActorId,t);var votes=v.Votes.Append(signed).ToArray();
                    if(!vote.Approved)control=control with{State="RETURNED"};
                    else if(p.Body.Input.Roles.All(r=>votes.Where(x=>x.Body.Approved&&x.Body.RoleIds.Contains(r.RoleId)).Select(x=>x.Body.ActorId).Distinct().Count()>=r.MinimumApprovals))
                    {
                        var approval=new RequirementEvaluationApprovalV1(Guid.NewGuid(),frozen.Body.Requirement,frozen.Reference,p.Reference,
                            votes.Where(x=>x.Body.Approved).Select(x=>x.Reference).ToArray(),frozen.Body.InputDigest,Now());
                        await s.Record(c.RequirementId,"APPROVAL",approval.Id,approval,c.OperationId,a.ActorId,t);control=control with{State="APPROVED",ApprovalId=approval.Id};
                    }break;
                case "WITHDRAW":
                    if(control.ApprovalId==null)throw Error("REQUIREMENT_APPROVAL_REQUIRED",409);
                    control=control with{State="WITHDRAWN",ApprovalId=null,PacketId=null};break;
            }
            var reference=await s.SaveControl(control,c.OperationId,t);var result=new RequirementEvaluationResultV1(c.OperationId,c.RequirementId,c.Action,v.Requirement.Reference,reference,control.State,Environment,runtime.EvidenceMode);
            await j.Store(Journal,Contract,c.OperationId,a.ActorId,bytes,result,[c.RequirementId],new{before=v.ControlReference,after=reference,c.Reason},t);return result;
        },ct,actionAtFence:async t=>{original=await j.Read(Journal,c.OperationId,t);return original==null?action:"query";});
    }
    private async Task<RequirementEvaluationResultV1> Disclose(OperationsAccessV1 a,RequirementEvaluationStoreV1 s,MainOwnerOperationJournalV1.Original o,Guid id,byte[]? bytes,CancellationToken ct)
    {
        if(o.Actor!=a.ActorId || o.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        if(o.State=="CLOSED")throw Error("ORIGINAL_OPERATION_CLOSED",409);
        if(bytes!=null&&!MainOwnerOperationJournalV1.Matches(o,bytes))throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT",409);
        var result=JsonSerializer.Deserialize<RequirementEvaluationResultV1>(o.Result);
        if(result==null || result.OperationId!=id || !o.Objects.SequenceEqual(new[]{result.ObjectId}))throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
        if(result.EnvironmentId!=Environment || result.EvidenceMode!=runtime.EvidenceMode)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
        var v=await View(a,s,result.ObjectId,Revision(result.Requirement.Version),ct);
        if(v.Requirement.Reference!=result.Requirement)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);return result;
    }
    public Task<RequirementEvaluationOperationV1> OperationAsync(Guid id,bool close=false,CancellationToken ct=default)
    {
        if(id==Guid.Empty)throw Error("ORIGINAL_OPERATION_REQUIRED",400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,"query",close,async(a,t)=>{
            a.RequireRequirementFields(false);var s=await Store(a,close,t);var j=new MainOwnerOperationJournalV1(db);var o=await j.Read(Journal,id,t);
            if(o!=null){if(o.Actor!=a.ActorId || o.Contract!=Contract)throw Error("ORIGINAL_OPERATION_FORBIDDEN",403);
                return o.State=="CLOSED"?new(id,"CLOSED"):new RequirementEvaluationOperationV1(id,"COMMITTED",await Disclose(a,s,o,id,null,t));}
            var result=new RequirementEvaluationOperationV1(id,close?"CLOSED":"UNKNOWN");
            if(close)
            {
                await j.Store(Journal,Contract,id,a.ActorId,null,result,[],new{action="close-uncommitted-requirement-evaluation"},t);
                var saved=await j.Read(Journal,id,t);
                if(saved==null || saved.State!="CLOSED" || saved.Actor!=a.ActorId || saved.Contract!=Contract)throw Error("ORIGINAL_OPERATION_UNAVAILABLE",503);
            }
            return result;
        },ct);
    }
    internal async Task<RequirementEvaluationSourceProofV1> Source(OperationsAccessV1 root,EngineeringSourceRefV1 reference,bool hold,CancellationToken ct)
    {
        EngineeringRequirementValuesV1.Ref(reference);var a=await root.SourceAsync(OperationsOwnerV1.EngineeringRequirement,ct);
        var s=await Store(a,false,ct);var v=await View(a,s,reference.Id,Revision(reference.Version),ct);
        if(v.Requirement.Reference!=reference)throw Error("REQUIREMENT_REFERENCE_CHANGED",412);
        var inputs=await Inputs(a,reference,hold,ct);var digest=InputDigest(inputs);var missing=inputs.Missing.ToList();
        var approval=v.CurrentApproval?.Body.Requirement==reference?v.CurrentApproval:null;
        if(approval==null)missing.Add(v.Control?.State=="WITHDRAWN"?"REQUIREMENT_APPROVAL_WITHDRAWN":"REQUIREMENT_APPROVAL_REQUIRED");
        else if(approval.Body.InputDigest!=digest)throw Error("REQUIREMENT_APPROVAL_INPUT_CHANGED",412);
        if(hold)
        {
            if(missing.Count>0)throw Error("REQUIREMENT_APPROVAL_SOURCE_UNAVAILABLE",422);
            a.AtCommit(async t=>{var c=await s.Control(reference.Id,t);if(c?.Reference!=v.ControlReference)throw Error("REQUIREMENT_REVIEW_CHANGED",412);});
        }
        return new(Environment,db.CurrentTenantId,runtime.EvidenceMode,reference,v.ControlReference,approval?.Reference,missing.Count==0?"AVAILABLE":"UNAVAILABLE",
            missing.Distinct(StringComparer.Ordinal).ToArray(),digest,hold?"HELD_IN_CURRENT_UOW":"READ_ONLY_OBSERVATION");
    }
    internal async Task<RequirementEvaluationSourceProofV1> HoldExact(OperationsAccessV1 root,EngineeringSourceRefV1 reference,EngineeringSourceRefV1 expectedControl,CancellationToken ct)
    {
        if(expectedControl==null || expectedControl.Owner!=ControlOwner || expectedControl.Id!=reference.Id || !ValidDigest(expectedControl.Digest))throw Error("REQUIREMENT_REVIEW_REFERENCE_INVALID",400);
        Revision(expectedControl.Version);var v=await Source(root,reference,true,ct);
        if(v.Control!=expectedControl)throw Error("REQUIREMENT_REVIEW_CHANGED",412);return v;
    }
    public Task<RequirementEvaluationSourceProofV1> SourceAsync(EngineeringSourceRefV1 reference,CancellationToken ct=default)
        =>Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRequirement,"query",false,(a,t)=>Source(a,reference,false,t),ct);
}
