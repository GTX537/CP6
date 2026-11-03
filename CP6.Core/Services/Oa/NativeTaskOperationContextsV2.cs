using CP6.Core.Services.Sys;
using CP6.Core.Services.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

public sealed partial class PersonalFormOwnerV2
{
    public async Task<NativeTaskOperationContextV2> OperationContextAsync(Guid task,Guid? behalf=null,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);var lease=await ReadLease(ct);await WriteReady(lease,ct);
        var actor=await Actor(ct);var source=await TaskSourceAsync(task,ct);var access=await TaskAccess(source,actor,behalf,true,ct,"TASK_READ");
        var principal=access.Principal==actor.UserId?actor:await PostgreSqlPermissionReader.ReadAsync(db,access.Principal,ct);
        var allowed=new List<string>();var reader=new TaskDelegationReaderV2(db,fence.EnvironmentId);
        foreach(var action in new[]{"sendback","transfer","addsign"})
        {
            if(!actor.ActionKeys.Contains("oa-inbox:"+action)||!principal.ActionKeys.Contains("oa-inbox:"+action))continue;
            if(access.OnBehalfOf!=null&&(action=="addsign"||await reader.Match(actor.UserId,access.Principal,action=="sendback"?"TASK_SENDBACK":"TASK_TRANSFER",access.FlowKey,access.BusinessType,ct)==null))continue;
            await Action(actor,action,ct,"oa-inbox");allowed.Add(action);
        }
        var candidates=new List<NativeTaskCandidateV2>();
        if(allowed.Contains("transfer")||allowed.Contains("addsign"))
            foreach(var user in await db.Sys_Users.AsNoTracking().Where(u=>u.Enable&&u.Id!=access.Principal&&(u.LockedUntil==null||u.LockedUntil<=DateTime.Now)).OrderBy(u=>u.UserName).Select(u=>new{u.Id,u.UserName}).ToArrayAsync(ct))
            {
                try{await TargetAuthority(source,user.Id,ct);candidates.Add(new(user.Id,user.UserName));}
                catch(ScopeAuthorizationExceptionV1 e)when(e.Status is 403 or 404 or 422){/* Not a qualified candidate. */}
            }
        var targets=allowed.Contains("sendback")?await new FlowEngine(db,new ApproverResolver(db,exactConditions:true)).SendBackOptionsV2(task,Parse<FlowPublicationV2>(source.Publication),ct):[];
        var result=new NativeTaskOperationContextV2(await TaskView(task,ct),allowed.ToArray(),candidates.ToArray(),targets);await FinalTask(access,lease,ct);return result;
    }
}

public sealed partial class ApprovalTaskDecisionOwnerV2
{
    public async Task<NativeTaskOperationContextV2> OperationContextAsync(Guid task,Guid? behalf=null,CancellationToken ct=default)
    {
        Entry();await using var tx=await db.Database.BeginTransactionAsync(ct);await Ready(ct);var lease=await authority.AcquireAsync(task,behalf,ct,"TASK_READ");
        var source=await ReadSource(task,ct);var original=Parse<ApprovalSubmitCommandV2>(System.Text.Encoding.UTF8.GetString(source.Payload));await Business.Decision(original,lease.Evidence.PrincipalId,false,ct);
        var actor=await PostgreSqlPermissionReader.ReadAsync(db,lease.Evidence.ActualHandlerId,ct);var principal=actor.UserId==lease.Evidence.PrincipalId?actor:await PostgreSqlPermissionReader.ReadAsync(db,lease.Evidence.PrincipalId,ct);
        var allowed=new List<string>();var reader=new TaskDelegationReaderV2(db,scope.EnvironmentId);
        var instance=await db.Wf_FlowInstances.AsNoTracking().SingleAsync(i=>i.Id==lease.Evidence.Assignment.InstanceId,ct);
        foreach(var action in new[]{"sendback","transfer","addsign"})
        {
            if(!actor.ActionKeys.Contains("oa-inbox:"+action)||!principal.ActionKeys.Contains("oa-inbox:"+action))continue;
            if(behalf!=null&&(action=="addsign"||await reader.Match(actor.UserId,principal.UserId,action=="sendback"?"TASK_SENDBACK":"TASK_TRANSFER",instance.FlowKey,original.Subject.BizType,ct)==null))continue;
            await authority.RequireAction(actor.UserId,action,ct);allowed.Add(action);
        }
        var candidates=new List<NativeTaskCandidateV2>();
        if(allowed.Contains("transfer")||allowed.Contains("addsign"))
            foreach(var user in await db.Sys_Users.AsNoTracking().Where(u=>u.Enable&&u.Id!=principal.UserId&&(u.LockedUntil==null||u.LockedUntil<=DateTime.Now)).OrderBy(u=>u.UserName).Select(u=>new{u.Id,u.UserName}).ToArrayAsync(ct))
            {
                try{await AppTarget(original,user.Id,ct);candidates.Add(new(user.Id,user.UserName));}
                catch(ScopeAuthorizationExceptionV1 e)when(e.Status is 403 or 404 or 422){/* Not a qualified candidate. */}
            }
        var targets=allowed.Contains("sendback")?await new FlowEngine(db,new ApproverResolver(db,exactConditions:true)).SendBackOptionsV2(task,Parse<FlowPublicationV2>(source.Publication),ct):[];
        var result=new NativeTaskOperationContextV2(await View(task,ct),allowed.ToArray(),candidates.ToArray(),targets);await authority.ValidateOperationAsync(lease,ct);return result;
    }
}
