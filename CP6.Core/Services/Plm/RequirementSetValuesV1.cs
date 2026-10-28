using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
internal static class RequirementSetValuesV1
{
    internal const string Owner="Main.Plm.RequirementSet.v1",Contract="Main.Plm.RequirementSet.commands.v1";
    internal static void Text(string? s,bool required=false,int max=8000)=>EngineeringRequirementValuesV1.Text(s,required,max);
    internal static void Ref(EngineeringSourceRefV1? r)
    {if(r==null || r.Owner!=Owner || r.Id==Guid.Empty || !ValidDigest(r.Digest))throw Error("REQUIREMENT_SET_REFERENCE_INVALID",400);Revision(r.Version);}
    internal static void Body(RequirementSetBodyV1 b)
    {
        if(b==null || b.Kind!="SET" || b.Members==null || b.Members.Length>256 || b.Conflicts==null || b.Conflicts.Length>256)throw Error("REQUIREMENT_SET_BODY_INVALID",400);
        Text(b.Name,true,512);Text(b.Scope);Text(b.CompletenessBasis);
        var ids=new HashSet<Guid>();
        foreach(var m in b.Members)
        {
            if(m==null || m.Requirement==null || !ids.Add(m.Requirement.Id) || m.Disposition is not("INCLUDED" or "EXCLUDED"))throw Error("REQUIREMENT_SET_MEMBER_INVALID",400);
            EngineeringRequirementValuesV1.Ref(m.Requirement);Text(m.Basis);
            if((m.EvaluationControl==null)!=(m.Approval==null))throw Error("REQUIREMENT_SET_APPROVAL_INVALID",400);
            if(m.EvaluationControl is {} c && (c.Owner!="Main.Plm.RequirementEvaluationControl.v1" || c.Id!=m.Requirement.Id || !ValidDigest(c.Digest)))throw Error("REQUIREMENT_SET_APPROVAL_INVALID",400);
            if(m.EvaluationControl!=null)Revision(m.EvaluationControl.Version);
            if(m.Approval is {} a && (a.Owner!="Main.Plm.RequirementApproval.v1" || a.Id==Guid.Empty || a.Version!="1" || !ValidDigest(a.Digest)))throw Error("REQUIREMENT_SET_APPROVAL_INVALID",400);
        }
        var conflicts=new HashSet<string>(StringComparer.Ordinal);
        foreach(var c in b.Conflicts)
        {
            if(c==null || c.Left==null || c.Right==null || !conflicts.Add(c.Id) || c.Left.Id==c.Right.Id || c.Proposal is not("UNRESOLVED" or "CLARIFY" or "SCOPE_SEPARATION" or "ECM" or "DEV"))throw Error("REQUIREMENT_SET_CONFLICT_INVALID",400);
            Text(c.Id,true,128);Text(c.Intersection);Text(c.Basis);EngineeringRequirementValuesV1.Ref(c.Left);EngineeringRequirementValuesV1.Ref(c.Right);
            if(!b.Members.Any(m=>m.Requirement==c.Left)||!b.Members.Any(m=>m.Requirement==c.Right))throw Error("REQUIREMENT_SET_CONFLICT_MEMBER_REQUIRED",400);
        }
    }
}
