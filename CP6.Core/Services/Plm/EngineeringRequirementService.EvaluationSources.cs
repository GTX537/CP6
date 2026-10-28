using System.Text;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Plm;
public sealed partial class EngineeringRequirementService
{
    // Current native permission and original inputs, on the caller's actual transaction.
    // A draft remains saveable without these evaluation qualifications.
    internal async Task<RequirementEvaluationInputsV1> EvaluationInputs(OperationsAccessV1 root,
        EngineeringSourceRefV1 reference,CancellationToken ct)
    {
        EngineeringRequirementValuesV1.Ref(reference);
        var a=await root.SourceAsync(OperationsOwnerV1.EngineeringRequirement,ct);
        a.RequireEngineeringRequirementSourceAccess(db);
        var s=new EngineeringRequirementStoreV1(db);await s.Fence(false,ct);
        var requirement=await Exact(a,s,reference,"REQUIREMENT",ct);
        requirement=await Observe(a,s,requirement,ct);
        var missing=requirement.Missing.Where(x=>x!="REQUIREMENT_APPROVAL_PENDING"
            && !x.StartsWith("CRITERION_REVIEW_PENDING:",StringComparison.Ordinal)
            && !x.StartsWith("RELATION_REVIEW_PENDING:",StringComparison.Ordinal)
            && !x.StartsWith("UNCOVERED:",StringComparison.Ordinal)).ToList();
        var originals=new Dictionary<EngineeringSourceRefV1,EngineeringRequirementSnapshotV1>();
        var seen=new HashSet<EngineeringSourceRefV1>();long size=Encoding.UTF8.GetByteCount(requirement.OriginalJson);
        async Task Add(EngineeringRequirementSnapshotV1 v)
        {
            if(v.Reference==reference || originals.ContainsKey(v.Reference))return;
            size+=Encoding.UTF8.GetByteCount(v.OriginalJson);
            if(size>16777216)throw Error("REQUIREMENT_EVALUATION_INPUT_SIZE_LIMIT",400);
            originals.Add(v.Reference,v);await a.RequireEngineeringRequirement(v.Reference.Id,ct);
            a.AtCommit(t=>a.RequireEngineeringRequirement(v.Reference.Id,t));
        }
        async Task Walk(EngineeringRequirementSnapshotV1 v,int depth)
        {
            if(!seen.Add(v.Reference))return;
            if(depth>128 || seen.Count>4096)throw Error("REQUIREMENT_GRAPH_LIMIT",422);
            if(v.Body.Requirement is not {} definition)return;
            var source=await Exact(a,s,definition.Source,"SOURCE",ct);await Add(source);
            if(v.Reference==reference)
            {
                missing.AddRange(EngineeringRequirementValuesV1.Missing(source.Body).Select(x=>"ORIGIN:"+x));
                if(source.Body.Source!.Kind!="INTERNAL_PROPOSAL")missing.Add("ORIGIN_CONFIRMATION_REQUIRED");
                if(!source.IsCurrent)missing.Add("ORIGIN_VERSION_CHANGED");
            }
            foreach(var link in definition.Relations)
            {var target=await Exact(a,s,link.Target,"REQUIREMENT",ct);await Add(target);await Walk(target,depth+1);}
        }
        await Walk(requirement,0);
        var units=new Dictionary<UomProfileRefV1,UomProfileVersionV1>();
        async Task<UomProfileVersionV1> Unit(UomProfileRefV1 r)
        {
            if(units.TryGetValue(r,out var prior))return prior;
            var ua=await a.SourceAsync(OperationsOwnerV1.UomProfile,ct);await ua.RequireUomProfile(r.Id,ct);ua.AtCommit(t=>ua.RequireUomProfile(r.Id,t));
            var us=new UomProfileStoreV1(db);await us.Fence(false,ct);var value=await us.Read(r.Id,Revision(r.Version),ct);
            if(value.Reference!=r)throw Error("REQUIREMENT_UNIT_REFERENCE_CHANGED",412);
            if(!value.IsCurrent || value.Profile.Enabled!=true)missing.Add("UNIT_SOURCE_CHANGED:"+r.Id);
            size+=Encoding.UTF8.GetByteCount(value.OriginalJson);if(size>16777216)throw Error("REQUIREMENT_EVALUATION_INPUT_SIZE_LIMIT",400);
            units.Add(r,value);return value;
        }
        foreach(var c in requirement.Body.Requirement!.Criteria)
        {
            if(c.Kind!="QUANTITATIVE")continue;
            if(c.Evaluation is not {} e){missing.Add("EVALUATION_UNIT_AND_BOUNDARY_REQUIRED:"+c.Id);continue;}
            if(c.Lower==null && !e.LowerUnbounded)missing.Add("LOWER_BOUND_INTENT_REQUIRED:"+c.Id);
            if(c.Upper==null && !e.UpperUnbounded)missing.Add("UPPER_BOUND_INTENT_REQUIRED:"+c.Id);
            var unit=await Unit(e.Unit);
            if(c.ProposedUnit!=unit.Profile.Code)missing.Add("UNIT_LABEL_MISMATCH:"+c.Id);
            if(unit.Profile.Mode!="BASE")
            {
                if(unit.Profile.BaseRef==null)missing.Add("UNIT_BASE_REQUIRED:"+c.Id);
                else
                {
                    var baseline=await Unit(unit.Profile.BaseRef);
                    if(baseline.Profile.Mode!="BASE" || baseline.Profile.DimensionKey!=unit.Profile.DimensionKey)missing.Add("UNIT_BASE_CHANGED:"+c.Id);
                }
            }
        }
        // Related definitions are fixed originals too. Preserve their selected unit/base originals
        // without pretending that their own criteria or relationship coverage have been approved.
        foreach(var unitRef in originals.Values.Where(x=>x.Body.Requirement!=null)
            .SelectMany(x=>x.Body.Requirement!.Criteria).Select(x=>x.Evaluation?.Unit).OfType<UomProfileRefV1>().Distinct())
        {
            var unit=await Unit(unitRef);
            if(unit.Profile.BaseRef is {} baseRef)await Unit(baseRef);
        }
        return new(requirement,originals.Values.OrderBy(x=>x.Reference.Id).ThenBy(x=>x.Reference.Version,StringComparer.Ordinal).ToArray(),
            units.Values.OrderBy(x=>x.Reference.Id).ThenBy(x=>x.Reference.Version,StringComparer.Ordinal).ToArray(),missing.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }
}
