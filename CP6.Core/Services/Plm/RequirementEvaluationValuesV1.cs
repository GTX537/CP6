using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
namespace CP6.Core.Services.Plm;
internal static class RequirementEvaluationValuesV1
{
    internal const string Journal="Main.Plm.RequirementEvaluation.operations.v1", Contract="Main.Plm.RequirementEvaluation.commands.v1",
        ControlOwner="Main.Plm.RequirementEvaluationControl.v1";
    internal static void Policy(RequirementEvaluationPolicyInputV1? p)
    {
        if(p==null || p.State is not("ENABLED" or "DISABLED") || p.Roles==null || p.Roles.Length is <1 or >100
            || p.Roles.Any(x=>x==null) || p.EffectiveTo<=p.EffectiveFrom
            || p.Roles.Select(x=>x.Code).Distinct(StringComparer.Ordinal).Count()!=p.Roles.Length
            || p.Roles.Select(x=>x.RoleId).Distinct().Count()!=p.Roles.Length)throw Error("REQUIREMENT_REVIEW_POLICY_INVALID",400);
        EngineeringRequirementValuesV1.Text(p.Basis,true);
        foreach(var r in p.Roles){EngineeringRequirementValuesV1.Text(r.Code,true,256);if(r.RoleId<=0 || r.MinimumApprovals is <1 or >100)throw Error("REQUIREMENT_REVIEW_POLICY_INVALID",400);}
    }
    internal static string InputDigest(RequirementEvaluationInputsV1 v)=>RequirementEvaluationStoreV1.Hash(RequirementEvaluationStoreV1.Bytes(new{
        requirement=v.Requirement.OriginalJson,related=v.RelatedOriginals.Select(x=>x.OriginalJson).ToArray(),units=v.Units.Select(x=>x.OriginalJson).ToArray()}));
}
