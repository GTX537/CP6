using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task RequirementExclusion_mixed_optional_and_mandatory_clauses_preserve_every_source_obligation()
    {
        await using var h=await Seed();var f=await ExclusionSetup(h);var drafts=EvaluationDrafts(h);var clause=f.Member.Source.Body.Source!.Clauses[0];
        var mandatory=clause with{Id="mandatory",OriginalText="Independent mandatory clause remains required",Terms=clause.Terms! with{Necessity="MANDATORY"}};
        await drafts.CommandAsync(ReviseRequirement(f.Member.Source,f.Member.Source.Body with{Source=f.Member.Source.Body.Source with{Clauses=[clause,mandatory]}}));var source=await drafts.ReadAsync(f.Member.Source.Reference.Id);
        await drafts.CommandAsync(ReviseRequirement(f.Member.Definition,f.Member.Definition.Body with{Requirement=f.Member.Definition.Body.Requirement! with{Source=source.Reference,ClauseIds=["strength","mandatory"]}}));
        var member=f.Member with{Source=source,Definition=await drafts.ReadAsync(f.Member.Definition.Reference.Id)};var approval=await EvaluationApprove(h,member);
        var owner=Sets(h);await owner.CommandAsync(SetRevise(f.Set,f.Set.Body with{Members=[new(member.Definition.Reference,approval.ControlReference,approval.CurrentApproval!.Reference,"INCLUDED","Both exact clauses retained")]}));
        var current=await owner.ReadAsync(f.Set.Reference.Id);var command=SetRevise(current,ExcludedBody(new(member,current)));
        Assert.Equal(422,(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(command))).Status);
        Assert.Equal("UNKNOWN",(await owner.OperationAsync(command.OperationId,false)).State);Assert.Equal(current.Reference,(await owner.ReadAsync(current.Reference.Id)).Reference);
    }
    [CurrentPermissionPostgresFact]
    public async Task RequirementExclusion_actual_FLOW_and_Owner_approval_make_the_exact_preserved_set_available()
    {
        await using var h=await Seed();var flow=await SetDecisionSetup(h,optionalExclusion:true);var f=flow.Source;var sets=Sets(h);var id=f.Set.Reference.Id;var reviews=SetReviews(h);
        Assert.Null((await SetVote(DecisionActor(h,f.Member.First))).Result.DecisionId);Assert.NotNull((await SetVote(DecisionActor(h,f.Member.Second))).Result.DecisionId);
        Assert.False((await reviews.SubmissionAsync(id,flow.Submission)).Submission.IsApproved);
        var(receiver,bytes,_)=await SetReceiver(h,flow);FactAck(await receiver.ReceiveAsync(bytes));var ready=await reviews.SubmissionAsync(id,flow.Submission);
        var command=new RequirementSetApplicationCommandV1(Guid.NewGuid(),flow.Submission,null,"Apply exact independently reviewed optional source decision");
        var result=await reviews.ApplyAsync(id,ready.Application.Etag,command);Assert.Equal("REQUIREMENT_SET_APPROVED",result.Receipt.OutcomeCode);
        Assert.Equal(result,await reviews.ApplyAsync(id,ready.Application.Etag,command));var current=await sets.ReadAsync(id);Assert.Equal("APPROVED",current.State);
        Assert.Equal("ACCEPTED",Assert.Single(current.ExclusionStatus!).State);Assert.Equal("EXCLUDED",Assert.Single(current.Body.Members).Disposition);
        Assert.Equal("AVAILABLE",(await sets.SourceAsync(current.Reference)).Availability);Assert.Equal(f.Member.Source.OriginalJson,Assert.Single(current.MemberStatus!).Source.OriginalJson);
    }
}
