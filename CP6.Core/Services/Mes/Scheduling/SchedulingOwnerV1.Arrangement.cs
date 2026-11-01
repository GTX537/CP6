using System.Globalization;
using System.Text.RegularExpressions;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;
public sealed partial class SchedulingOwnerV1
{
    public Task<SchedRefV1> PublishPlanningSimulationAsync(SchedPlanningInputV1 input,CancellationToken ct=default)
    {
        if(!simulation.Value.Enabled)throw Error("SCHED_SIMULATION_DISABLED",404);
        if(input?.DataKind!="SIMULATED"||input.RootId==Guid.Empty||input.PredecessorRootIds==null||input.Operations==null
            ||input.PredecessorRootIds.Contains(input.RootId)||input.PredecessorRootIds.Distinct().Count()!=input.PredecessorRootIds.Length
            ||input.Operations.Any(x=>x==null))throw Error("SCHED_PLANNING_INPUT_INVALID");
        Reference(input.Reference);
        foreach(var op in input.Operations)if(op.TechnicalDuration is {} d)
        {
            Reference(d.Reference);
            if(d.Microseconds==null||!Regex.IsMatch(d.Microseconds,@"\A[1-9][0-9]*\z")||!long.TryParse(d.Microseconds,CultureInfo.InvariantCulture,out var value)||value>long.MaxValue/10)
                throw Error("SCHED_DURATION_INVALID");
        }
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"simulate-source",true,async(a,t)=>
        {
            await Lease(a,true,t);a.RequireSchedulingSimulation();await Visible(a,input.RootId,t);var root=await Store.Root(input.RootId,t);
            if(!root.Operations.Select(x=>x.OperationId).Order().SequenceEqual(input.Operations.Select(x=>x.OperationId).Order()))throw Error("SCHED_PLANNING_MANIFEST_INVALID");
            var siteRoots=await Store.RootIds(root.SiteRef.Id).ToArrayAsync(t);
            if(input.PredecessorRootIds.Any(id=>!siteRoots.Contains(id)))throw Error("SCHED_ROOT_DEPENDENCY_INVALID");
            var current=await Store.Planning(input.RootId,t);
            if(current!=null)
            {
                if(current.Reference==input.Reference){if(Digest(current)!=Digest(input))throw Error("SCHED_SOURCE_IDENTITY_CONFLICT",409);return current.Reference;}
                if(current.Reference.Id!=input.Reference.Id||current.Reference.Owner!=input.Reference.Owner||Next(current.Reference.Version)!=input.Reference.Version)throw Error("SCHED_SOURCE_PREDECESSOR_CHANGED",409);
            }
            await Store.SetPlanning(input,t);return input.Reference;
        },ct);
    }
    public Task<ArrangementCaptureV1> ArrangementCaptureAsync(Guid siteId,CancellationToken ct=default)
    {
        if(siteId==Guid.Empty)throw Error("SCHED_SITE_REQUIRED");
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"query",true,async(a,t)=>
        {
            await Lease(a,true,t);var visible=await (await a.PlanningOrders(t)).Select(x=>x.Id).ToArrayAsync(t);var all=await Store.Roots(siteId,t);
            if(!all.Any(r=>visible.Contains(r.RootId)))throw Error("SCHED_NOT_VISIBLE",404);
            var visibleResources=all.Where(r=>visible.Contains(r.RootId)).SelectMany(r=>r.Operations).Where(o=>o.ResourceRef!=null).Select(o=>o.ResourceRef!).Distinct().ToArray();
            var roots=new List<ArrangementRootV1>();var unavailable=new List<SchedBusyV1>();var now=Now();
            foreach(var root in all.OrderBy(x=>x.RootId))
            {
                var observation=await Store.Observation(root.RootId,t);
                if(!visible.Contains(root.RootId))
                {
                    foreach(var op in root.Operations.Where(o=>visibleResources.Any(r=>SameResource(r,o.ResourceRef))))
                    {var guard=observation.Guards.Single(g=>g.OperationId==op.OperationId);var interval=guard.ExecutionState is "STARTED" or "COMPLETED"?guard.ActualInterval:op.Interval;
                     if(interval!=null||guard.ExecutionState!="NOT_STARTED")unavailable.Add(new(visibleResources.First(r=>SameResource(r,op.ResourceRef)),interval));}
                    continue;
                }
                var capture=new SchedCaptureV1(Guid.NewGuid(),now,now.AddMinutes(30),"SIMULATED",root,observation,OperationSet(root),Policy,true);
                var legacy=await db.WorkOrderProcesses.AsNoTracking().Where(x=>x.WorkOrderNo==root.WorkOrderNo&&!x.IsDeleted).Select(x=>new{x.Id,x.LeadTime}).ToArrayAsync(t);
                roots.Add(new(capture,await Store.Planning(root.RootId,t),legacy.ToDictionary(x=>x.Id,x=>x.LeadTime?.ToString(CultureInfo.InvariantCulture))));
            }
            var result=new ArrangementCaptureV1(Guid.NewGuid(),siteId,now,now.AddMinutes(30),roots.ToArray(),unavailable.ToArray(),roots.Count==all.Length?"COMPLETE":"PARTIAL",Digest(new{siteId,roots,unavailable}));
            await Store.SaveArrangement(result.CaptureId,siteId,"capture",a.ActorId,result,t);return result;
        },ct);
    }
    private static async Task VisibleArrangement(OperationsAccessV1 a,ArrangementPreviewV1 preview,CancellationToken ct)
    {foreach(var id in preview.Items.Select(i=>i.RootId).Distinct())await Visible(a,id,ct);}
    public Task<ArrangementPreviewV1> PrepareArrangementAsync(ArrangementRequestV1 input,CancellationToken ct=default)
    {
        if(input==null||input.SiteId==Guid.Empty||input.CaptureId==Guid.Empty||input.Algorithm!=ArrangementAlgorithmV1.Algorithm||!input.KeepLocked
            ||input.OperationIds is not{Length:>0 and <=500}||input.OperationIds.Distinct().Count()!=input.OperationIds.Length||input.ResourceScopeRefs==null
            ||input.DefaultDurationMinutes is <1 or >10080||input.BaseTime.Ticks%10!=0)throw Error("SCHED_ARRANGEMENT_INPUT_INVALID");
        Reason(input.Reason);foreach(var r in input.ResourceScopeRefs)Reference(r);
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"arrange.preview",true,async(a,t)=>
        {
            await Lease(a,true,t);var d=await Store.Arrangement(input.CaptureId,"capture",t);if(d.ActorId!=a.ActorId)throw Error("SCHED_CAPTURE_ACTOR_MISMATCH",403);
            var capture=Decode<ArrangementCaptureV1>(d.Body);if(capture.SiteId!=input.SiteId)throw Error("SCHED_CAPTURE_SITE_MISMATCH");
            if(capture.ExpiresAt<=DateTimeOffset.UtcNow)throw Error("SCHED_CAPTURE_EXPIRED",409);
            var operations=capture.Roots.SelectMany(r=>r.Capture.Root.Operations).ToArray();
            if(input.OperationIds.Any(id=>!operations.Any(o=>o.OperationId==id))||input.ResourceScopeRefs.Any(r=>!operations.Any(o=>o.ResourceRef==r)))throw Error("SCHED_CANDIDATE_NOT_CAPTURED");
            foreach(var r in capture.Roots.Where(r=>r.Capture.Root.Operations.Any(o=>input.OperationIds.Contains(o.OperationId))))
            {
                await Visible(a,r.Capture.Root.RootId,t);var current=await Store.Root(r.Capture.Root.RootId,t);
                if(current.Expected!=r.Capture.Root.Expected||OperationSet(current)!=r.Capture.OperationSetRef
                    ||(await Store.Observation(current.RootId,t)).Reference!=r.Capture.Observation.Reference
                    ||(await Store.Planning(current.RootId,t))?.Reference!=r.Planning?.Reference)throw Error("SCHED_CAPTURE_STALE",409);
            }
            var result=ArrangementAlgorithmV1.Prepare(capture,input);
            await Store.SaveArrangement(result.PreviewId,input.SiteId,"preview",a.ActorId,result,t);
            foreach(var group in result.AtomicGroups)await Store.Save(group.Group.GroupId,group.Group.RootWoId,"arrange-plan",a.ActorId,group,t);
            return result;
        },ct);
    }
    public Task<ArrangementPageV1> ArrangementPreviewAsync(Guid id,int cursor=0,int pageSize=20,CancellationToken ct=default)
    {
        if(cursor<0||pageSize is not(20 or 50 or 100))throw Error("SCHED_PAGE_INVALID");
        return Authority.ExecuteAsync<ArrangementPageV1>(OperationsOwnerV1.Planning,"preview.read",false,async(a,t)=>
        {
            await Lease(a,false,t);var d=await Store.Arrangement(id,"preview",t);var p=Decode<ArrangementPreviewV1>(d.Body);await VisibleArrangement(a,p,t);
            return new(p.PreviewId,p.Revision,p.Digest,p.ExpiresAt,p.Input,p.AlgorithmVersion,p.Assumptions,p.Items.Skip(cursor).Take(pageSize).ToArray(),p.ManifestCount,p.ManifestDigest,p.AtomicGroups,p.Coverage,cursor+pageSize<p.ManifestCount?cursor+pageSize:null);
        },ct);
    }
}
