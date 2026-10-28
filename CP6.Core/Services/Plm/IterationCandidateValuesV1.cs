using System.Text.Json;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
internal static class IterationCandidateValuesV1
{
    internal const string Owner="Main.Plm.IterationCandidate.v1",Contract="Main.Plm.IterationCandidate.commands.v1";
    internal static void Reference(EngineeringSourceRefV1? r,string owner)
    {
        if(r==null || r.Owner!=owner || r.Id==Guid.Empty || !ValidDigest(r.Digest))throw Error("ITERATION_CANDIDATE_REFERENCE_INVALID",400);
        Revision(r.Version);
    }
    internal static void Command(IterationCandidateCommandV1 c)
    {
        if(c==null || c.OperationId==Guid.Empty || c.IterationId==Guid.Empty || c.CandidateId==Guid.Empty
            || c.ExpectedIterationEtag==Guid.Empty || c.Action is not("CREATE" or "REVISE")
            || (c.Action=="CREATE"?(c.ExpectedVersion!=null || c.ExpectedEtag!=null):(c.ExpectedVersion==null || c.ExpectedEtag==null || c.ExpectedEtag==Guid.Empty))
            || c.SourceDecision is not("ADOPT_CURRENT" or "KEEP_ORIGINAL"))throw Error("ITERATION_CANDIDATE_COMMAND_INVALID",400);
        Reference(c.ExpectedIteration,EngineeringIterationValuesV1.Owner);if(c.ExpectedIteration.Id!=c.IterationId)throw Error("ITERATION_CANDIDATE_COMMAND_INVALID",400);
        Reference(c.SourceCheck,EngineeringSourceCheckValuesV1.Owner);if(c.SourceCheck.Version!="1")throw Error("ITERATION_CANDIDATE_REFERENCE_INVALID",400);
        if(c.ExpectedVersion!=null)Revision(c.ExpectedVersion);
        if(c.ParentCandidate!=null){Reference(c.ParentCandidate,Owner);if(c.ParentCandidate.Id==c.CandidateId)throw Error("ITERATION_CANDIDATE_PARENT_INVALID",400);}
        EngineeringIterationValuesV1.Text(c.Title,true,512);EngineeringIterationValuesV1.Text(c.ChangeSummary,false);
        EngineeringIterationValuesV1.Text(c.Assumptions,false);EngineeringIterationValuesV1.Text(c.Reason);
        EngineeringIterationValuesV1.Text(c.DriftReason,c.SourceDecision=="KEEP_ORIGINAL");
        if(c.SourceDecision=="ADOPT_CURRENT" && c.DriftReason.Length!=0)throw Error("ITERATION_CANDIDATE_COMMAND_INVALID",400);
    }
    // Semantic diff preserves occurrence identity and order; exact original bytes remain separately addressable.
    internal static IterationCandidateChangeV1[] Differences(IterationCandidateDefinitionV1? before,IterationCandidateDefinitionV1 after)
    {
        Dictionary<string,JsonElement> Parts(IterationCandidateDefinitionV1? x)
        {
            var result=new Dictionary<string,JsonElement>(StringComparer.Ordinal);if(x==null)return result;
            void Add(string key,object? value)=>result.Add(key,JsonSerializer.SerializeToElement(value,EngineeringProductStoreV1.Json));
            Add("title",x.Title);Add("changeSummary",x.ChangeSummary);Add("assumptions",x.Assumptions);
            Add("inputVersion",x.InputVersion);Add("requirementSet",x.RequirementSet);Add("product",x.Product);Add("site",x.Site);
            Add("engineeringRevision",x.EngineeringRevision);Add("engineeringCandidate",x.EngineeringCandidate);
            Add("configuration",x.TechnicalSource.Input.Configuration);Add("sourceCheck",x.SourceCheck);
            foreach(var c in x.TechnicalSource.Input.Components)Add("occurrences/"+c.OccurrenceId,c);
            return result;
        }
        var a=Parts(before);var b=Parts(after);var changes=new List<IterationCandidateChangeV1>();
        foreach(var key in a.Keys.Concat(b.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var hasA=a.TryGetValue(key,out var left);var hasB=b.TryGetValue(key,out var right);
            if(hasA && hasB && left.GetRawText()==right.GetRawText())continue;
            changes.Add(new(key,hasA?left:null,hasB?right:null));
        }
        return changes.ToArray();
    }
}
