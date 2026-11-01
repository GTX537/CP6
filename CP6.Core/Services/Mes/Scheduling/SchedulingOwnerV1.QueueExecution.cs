using System.Numerics;
using CP6.Core.Services.Integration;
using static CP6.Core.Services.Mes.Scheduling.SchedulingValuesV1;
namespace CP6.Core.Services.Mes.Scheduling;
public sealed partial class SchedulingOwnerV1
{
    public Task<QueueExecutionResultV1> PublishQueueExecutionSimulationAsync(QueueExecutionEventV1 input,CancellationToken ct=default)
    {
        if(!simulation.Value.Enabled)throw Error("SCHED_SIMULATION_DISABLED",404);
        if(input?.DataKind!="SIMULATED"||input.RootId==Guid.Empty||input.OperationId==Guid.Empty||input.State is not("STARTED" or "COMPLETED"))throw Error("QUEUE_EXECUTION_INPUT_INVALID");
        Reference(input.FactRef);if(input.ActualInterval!=null)Interval(input.ActualInterval);
        return Authority.ExecuteAsync(OperationsOwnerV1.Planning,"simulate-source",true,async(a,t)=>
        {
            await Lease(a,true,t);a.RequireSchedulingSimulation();await Visible(a,input.RootId,t);var root=await Store.Root(input.RootId,t);
            var op=root.Operations.SingleOrDefault(o=>o.OperationId==input.OperationId)??throw Error("SCHED_OPERATION_NOT_IN_CAPTURE");
            var current=await Store.Execution(root.RootId,op.OperationId,t);var seen=await Store.ExecutionDigest(input.FactRef,t);
            var stream=await Store.ExecutionStream(input.FactRef,t);
            if(stream!=null&&(stream.RootId!=input.RootId||stream.OperationId!=input.OperationId))throw Error("QUEUE_EXECUTION_STREAM_CHANGED",409);
            if(seen!=null){if(seen!=Digest(input))throw Error("QUEUE_EXECUTION_IDENTITY_CONFLICT",409);return new QueueExecutionResultV1(input.FactRef,"DUPLICATE",false);}
            if(current!=null&&(current.FactRef.Owner!=input.FactRef.Owner||current.FactRef.Id!=input.FactRef.Id))throw Error("QUEUE_EXECUTION_STREAM_CHANGED",409);
            if(current!=null&&BigInteger.Parse(input.FactRef.Version)<BigInteger.Parse(current.FactRef.Version))
            {await Store.SaveExecution(input,false,t);return new QueueExecutionResultV1(input.FactRef,"OLDER_RECORDED_NOT_PROJECTED",false);}
            var observation=await Store.Observation(root.RootId,t);var guard=observation.Guards.Single(g=>g.OperationId==op.OperationId);
            if(guard.ExecutionState=="COMPLETED"&&input.State!="COMPLETED")throw Error("QUEUE_EXECUTION_REGRESSION",409);
            await Store.SaveExecution(input,true,t);
            var guards=observation.Guards.Select(g=>g.OperationId==op.OperationId?g with{ExecutionState=input.State,ActualInterval=input.ActualInterval}:g).ToArray();
            await Store.Publish(observation with{Reference=Ref(observation.Reference.Owner,observation.Reference.Id,Next(observation.Reference.Version),new{observation.Reference,input.FactRef,guards}),Guards=guards},t);
            var queue=op.ResourceRef==null?null:await Store.Queue(root.SiteRef.Id,op.ResourceRef.Id,t);var reassess=false;
            if(queue!=null)
            {
                // EXEC facts remain authoritative even when queue membership needs reconciliation.
                // Reading the queue must never become a gate for accepting a late execution fact.
                var position=Array.FindIndex(queue.Members,m=>m.OperationId==op.OperationId);reassess=position<0;
                if(input.State=="STARTED"&&position>0)
                foreach(var member in queue.Members.Take(position))
                    if((await Store.Observation(member.RootId,t)).Guards.Single(g=>g.OperationId==member.OperationId).ExecutionState=="NOT_STARTED")reassess=true;
                if(input.State=="COMPLETED"&&queue.Members.Any(m=>m.OperationId==op.OperationId))
                    await Store.SaveQueue(queue with{Revision=Next(queue.Revision),Members=queue.Members.Where(m=>m.OperationId!=op.OperationId).ToArray()},new{kind="EXEC_COMPLETED",input.FactRef,a.ActorId,at=Now()},t);
            }
            return new QueueExecutionResultV1(input.FactRef,"PROJECTED",reassess);
        },ct);
    }
}
