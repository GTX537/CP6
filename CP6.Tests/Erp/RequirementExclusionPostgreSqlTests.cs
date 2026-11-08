using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private async Task<SetFixture> ExclusionSetup(Harness h,string necessity="OPTIONAL",string waivability="SOURCE_DECISION_REQUIRED",bool registerScope=true)
    {
        var f=await EvaluationSetup(h,registerScope);await EngineeringAction(h,"plm-engineering-requirement","source-confirm");
        foreach(var action in new[]{"query","add","edit","review-policy","review-submit","review-decide","review-apply","exclusion-decide"})await EngineeringAction(h,"plm-requirement-set",action);
        var drafts=EvaluationDrafts(h);var body=JsonSerializer.Deserialize<EngineeringRequirementBodyV1>(QualifiedSourceJson)!;
        var clause=body.Source!.Clauses[0];body=body with{Source=body.Source with{Clauses=[clause with{Terms=clause.Terms! with{Necessity=necessity,Waivability=waivability}}]}};
        await drafts.CommandAsync(ReviseRequirement(f.Source,body));var source=await drafts.ReadAsync(f.Source.Reference.Id);
        await drafts.CommandAsync(ReviseRequirement(f.Definition,f.Definition.Body with{Requirement=f.Definition.Body.Requirement! with{Source=source.Reference,ClauseIds=["strength"]}}));
        f=f with{Source=source,Definition=await drafts.ReadAsync(f.Definition.Reference.Id)};var approved=await EvaluationApprove(h,f);
        var sets=Sets(h);var command=new RequirementSetCommandV1{OperationId=Guid.NewGuid(),ObjectId=Guid.NewGuid(),Action="CREATE",ExpectedAuthorityVersion=(await sets.CreationContextAsync()).AuthorityVersion,Reason="Preserve explicitly optional original",
            Body=new("SET","Optional source requirement","Explicit source decision for this exact set","Every original retained; optional requirement may be omitted only with source authority",
                [new(f.Definition.Reference,approved.ControlReference,approved.CurrentApproval!.Reference,"INCLUDED","Source optional; free text does not authorize exclusion")],[])};
        await sets.CommandAsync(command);return new(f,await sets.ReadAsync(command.ObjectId));
    }
    private static RequirementSetBodyV1 ExcludedBody(SetFixture f,EngineeringSourceRefV1? source=null)
    {
        var body=JsonSerializer.SerializeToNode(f.Set.Body)!.AsObject();var member=body["Members"]![0]!.AsObject();member["Disposition"]="EXCLUDED";
        member["Exclusion"]=new JsonObject{["Schema"]="requirement-member-exclusion/1",["Source"]=JsonSerializer.SerializeToNode(source??f.Member.Source.Reference),["Basis"]="Current source authority accepts omission of the optional exact original; no conflict or mandatory requirement waived"};
        return body.Deserialize<RequirementSetBodyV1>()!;
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementExclusion_actual_optional_source_decision_allows_review_without_erasing_any_original()
    {
        await using var h=await Seed();var f=await ExclusionSetup(h);var owner=Sets(h);var command=SetRevise(f.Set,ExcludedBody(f));var result=await owner.CommandAsync(command);
        var saved=await owner.ReadAsync(f.Set.Reference.Id);Assert.DoesNotContain(saved.Missing,x=>x.StartsWith("EXCLUSION_"));
        Assert.Equal("EXCLUDED",Assert.Single(saved.Body.Members).Disposition);Assert.Equal(f.Set.OriginalJson,(await owner.ReadAsync(f.Set.Reference.Id,"1")).OriginalJson);
        Assert.Equal(f.Member.Source.OriginalJson,Assert.Single(saved.MemberStatus!).Source.OriginalJson);Assert.Equal(f.Member.Definition.OriginalJson,Assert.Single(saved.MemberStatus!).Requirement.OriginalJson);
        var ready=await SetReady(h,saved.Reference.Id);Assert.NotNull((await SetReviews(h).SubmitAsync(saved.Reference.Id,ready.Etag,SetSubmit(ready))).Submission);
        Assert.Equal(result,await owner.CommandAsync(command));
    }
}
