using CP6.Core.Services.Erp;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task RequirementExclusion_direct_creation_binds_its_first_source_decision_and_rejects_unspecified_scope_atomically()
    {
        await using var h=await Seed();var f=await ExclusionSetup(h);var owner=Sets(h);var context=await owner.CreationContextAsync();
        var invalid=new RequirementSetCommandV1{OperationId=Guid.NewGuid(),ObjectId=Guid.NewGuid(),Action="CREATE",ExpectedAuthorityVersion=context.AuthorityVersion,Reason="Source decision requires an actual set scope",Body=ExcludedBody(f) with{Scope=""}};
        Assert.Equal("REQUIREMENT_EXCLUSION_SCOPE_REQUIRED",(await Assert.ThrowsAsync<OrderPriceCorrectionException>(()=>owner.CommandAsync(invalid))).Message);
        Assert.Equal("UNKNOWN",(await owner.OperationAsync(invalid.OperationId,false)).State);
        var command=invalid with{OperationId=Guid.NewGuid(),ObjectId=Guid.NewGuid(),Body=ExcludedBody(f)};var result=await owner.CommandAsync(command);var created=await owner.ReadAsync(result.ObjectId);
        var decision=Assert.Single(created.ExclusionStatus!);Assert.Equal("ACCEPTED",decision.State);Assert.Equal(created.Reference,decision.DecisionVersion);Assert.Equal(h.User,decision.DecisionActor);
        Assert.Equal(f.Member.Source.OriginalJson,Assert.Single(created.MemberStatus!).Source.OriginalJson);Assert.Equal(result,await owner.CommandAsync(command));
    }
}
