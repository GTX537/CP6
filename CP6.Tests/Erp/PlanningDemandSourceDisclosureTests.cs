using CP6.Core.Services.Erp;
using CP6.Core.Services.Plan;
using CP6.Entity.DomainModels.Erp;

namespace CP6.Tests.Erp;

public sealed class PlanningDemandSourceDisclosureTests
{

    [Theory]
    [InlineData(DemandSourceIntake.ReceiveCapability, DemandSourceIntake.ReceiveCapability, DemandSourceIntake.ReceiveCapability, true)]
    [InlineData(DemandSourceIntake.ResultReadCapability, DemandSourceIntake.ResultReadCapability, DemandSourceIntake.ResultReadCapability, true)]
    [InlineData(DemandSourceIntake.ResultReadCapability, DemandSourceIntake.ReceiveCapability, DemandSourceIntake.ResultReadCapability, false)]
    [InlineData(DemandSourceIntake.ReceiveCapability, DemandSourceIntake.ResultReadCapability, DemandSourceIntake.ReceiveCapability, false)]
    [InlineData(DemandSourceIntake.ResultReadCapability, DemandSourceIntake.ResultReadCapability, null, false)]
    [InlineData(DemandSourceIntake.ReceiveCapability, null, DemandSourceIntake.ReceiveCapability, false)]
    public void Planning_source_capability_requires_the_matching_current_grant_and_declaration(
        string operation, string? granted, string? declared, bool allowed)
    {
        var actions = new HashSet<string>(StringComparer.Ordinal);
        if (granted is not null) actions.Add("service:" + granted);
        Assert.Equal(allowed, OrderReadOwnerV1.HasDeclaredAction(actions, "service", operation,
            declared is null ? [] : [declared]));
    }
    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    public void Planning_source_disclosure_requires_current_origin_subject_and_full_native_fields(
        bool sameSubject, bool fullPriceRead, bool fullAmountRead, bool knownOrigin, bool allowed)
    {
        var subject = Guid.NewGuid();
        var originActor = knownOrigin ? (sameSubject ? subject : Guid.NewGuid()) : Guid.Empty;
        var access = new OrderReadOwnerV1.Access(Array.Empty<OrderDetail>().AsQueryable(),
            Array.Empty<Order>().AsQueryable(), fullPriceRead, fullAmountRead, subject, "fixture");

        Assert.Equal(allowed, DemandSourceIntake.AllowsOriginalDisclosure(access, originActor));
    }
}
