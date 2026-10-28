using CP6.Core.EFDbContext;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
public sealed partial class IterationCandidateService
{
    internal bool Owns(CP6Context context)=>ReferenceEquals(db,context) && owner.Owns(db) && iterations.Owns(db) && checks.Owns(db);
    internal async Task<IterationCandidateSnapshotV1[]> ReviewCandidates(OperationsAccessV1 a,Guid iteration,
        EngineeringSourceRefV1[]? references,bool hold,bool complete,CancellationToken ct)
    {
        await iterations.ReviewSource(a,iteration,hold,ct);var store=new IterationCandidateStoreV1(db);await store.Fence(false,ct);
        var ids=await store.Heads().Where(x=>x.IterationId==iteration).Select(x=>x.Id).ToArrayAsync(ct);
        var selected=references??ids.Select(id=>new EngineeringSourceRefV1(IterationCandidateValuesV1.Owner,id,"","")).ToArray();
        if(references!=null)
        {
            foreach(var r in references)IterationCandidateValuesV1.Reference(r,IterationCandidateValuesV1.Owner);
            if(references.Select(r=>r.Id).Distinct().Count()!=references.Length)throw Error("ITERATION_REVIEW_CANDIDATE_DUPLICATE",400);
            if(complete && !ids.Order().SequenceEqual(references.Select(r=>r.Id).Order()))throw Error("ITERATION_REVIEW_COMPLETE_CANDIDATE_SET_REQUIRED",412);
        }
        var values=new List<IterationCandidateSnapshotV1>();
        foreach(var r in selected.OrderBy(x=>x.Id.ToString("D"),StringComparer.Ordinal))
        {
            var value=await Observe(a,await store.Read(iteration,r.Id,references==null?null:Revision(r.Version),ct),ct);
            if(references!=null && value.Reference!=r)throw Error("ITERATION_CANDIDATE_REFERENCE_CHANGED",412);
            if(hold && (!value.IsCurrent || value.SourceDrift!="UNCHANGED" || value.Findings.Length>0
                || value.Original.Definition.TechnicalSource.State!="PASS" || value.Original.Definition.TechnicalSource.Manifest==null))throw Error("ITERATION_REVIEW_CANDIDATE_NOT_READY",422);
            values.Add(value);
        }
        if(hold && values.Count==0)throw Error("ITERATION_REVIEW_CANDIDATES_REQUIRED",422);
        return values.ToArray();
    }
}
