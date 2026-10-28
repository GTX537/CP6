using CP6.Core.Services.Integration;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
public sealed partial class EngineeringIterationService
{
    internal bool Owns(CP6.Core.EFDbContext.CP6Context context)=>ReferenceEquals(db,context) && owner.Owns(db) && sets.Owns(db) && documents.Owns(db);
    internal async Task<EngineeringIterationSnapshotV1> CandidateSource(OperationsAccessV1 access,Guid id,bool write,CancellationToken ct)
    {
        var store=new EngineeringIterationStoreV1(db);await store.Fence(false,ct);
        access.RequireEngineeringIterationFields(write);await access.RequireEngineeringIteration(id,ct);
        var value=await Observe(access,store,await store.Read(id,null,ct),ct);
        var work=value.Body.Iteration??throw Error("ITERATION_REQUIRED",422);
        if(write)
        {
            var project=await Project(access,store,work.ProjectId,ct);
            if(work.Stage!="IN_DESIGN" || project.State!="ACTIVE")throw Error("ITERATION_CANDIDATE_STAGE_CHANGED",409);
            if(work.HoldState!="NORMAL")throw Error("ITERATION_HELD",409);
            if(access.ActorId!=project.Definition.OwnerId && access.ActorId!=work.Plan.OwnerId)throw Error("ITERATION_MANAGER_REQUIRED",403);
            await ProjectSources(access,project.Definition,true,ct);await Inputs(access,work.Input,true,ct);
            if(await ProjectDrift(store,work,project,ct))throw Error("ITERATION_PROJECT_INPUT_CHANGED",412);
            await Staff([work.Plan.OwnerId],ct);
        }
        return value;
    }
    internal async Task<IterationProjectInputV1> CandidateProject(IterationWorkV1 work,CancellationToken ct)
    {
        var store=new EngineeringIterationStoreV1(db);await store.Fence(false,ct);return await CapturedProject(store,work,ct);
    }
    internal async Task CandidateHistory(OperationsAccessV1 access,EngineeringSourceRefV1 reference,CancellationToken ct)
    {
        var store=new EngineeringIterationStoreV1(db);await store.Fence(false,ct);
        var v=await Observe(access,store,await store.Read(reference.Id,Revision(reference.Version),ct),ct);
        if(v.Reference!=reference)throw Error("ITERATION_CANDIDATE_REFERENCE_CHANGED",412);
    }
}
