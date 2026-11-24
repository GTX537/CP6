using CP6.Core.Services.Erp;
using CP6.Core.Services.Plan;
using CP6.Entity.DomainModels.Erp;

namespace CP6.Tests.Erp;

public sealed class PlanningDemandSourceDisclosureTests
{

    [Theory]
    [InlineData(true, false, true, false, DemandSourceIntake.ReceiveCapability, false)]
    [InlineData(true, true, true, false, DemandSourceIntake.ReceiveCapability, true)]
    [InlineData(true, true, true, true, DemandSourceIntake.ResultReadCapability, true)]
    [InlineData(false, true, true, true, DemandSourceIntake.ResultReadCapability, true)]
    [InlineData(false, true, true, false, null, true)]
    [InlineData(true, true, false, false, null, false)]
    [InlineData(false, false, false, false, null, false)]
    public async Task Planning_source_history_runs_only_after_current_capabilities_and_visible_origin(
        bool canReceive, bool canRecover, bool visible, bool receiptKnown, string? operation, bool seeHistory)
    {
        var calls = new List<string>();
        var actor = Guid.NewGuid();
        var access = new OrderReadOwnerV1.Access(Array.Empty<OrderDetail>().AsQueryable(),
            Array.Empty<Order>().AsQueryable(), true, true, actor, "fixture");
        var query = new DemandSourceIntake.SourceResultQuery("release-a", "lineage-a", "1", new string('0', 64));
        var origin = new DemandSourceIntake.SalesSource
        {
            ActorId = actor, LineageId = query.ReleaseLineageId, SourceVersion = 1, SourceDigest = query.SourceDigest
        };

        Task<DemandSourceIntake.ReceiveSlot> Inspect() => DemandSourceIntake.InspectAuthorizedReceive(query, 1,
            (action, _) =>
            {
                calls.Add(action);
                return Task.FromResult<OrderReadOwnerV1.Access?>(
                    (action == DemandSourceIntake.ReceiveCapability ? canReceive : canRecover) ? access : null);
            },
            (scope, _) =>
            {
                calls.Add("visible-origin");
                Assert.Same(access, scope);
                return Task.FromResult(visible ? origin : null);
            },
            _ =>
            {
                calls.Add("planning-history");
                return Task.FromResult(receiptKnown ? new DemandSourceIntake.Inbox { ActorId = actor } : null);
            },
            CancellationToken.None);

        if (operation is null)
        {
            var denied = await Assert.ThrowsAsync<DemandSourceIntakeException>(Inspect);
            Assert.Equal(("ACCESS_DENIED", 403), (denied.Message, denied.Status));
        }
        else
        {
            var slot = await Inspect();
            Assert.Equal(operation, slot.Action);
            Assert.Equal(canRecover, slot.CanReadResult);
        }

        var expected = new List<string> { DemandSourceIntake.ReceiveCapability, DemandSourceIntake.ResultReadCapability };
        if (canReceive || canRecover) expected.Add("visible-origin");
        if (seeHistory) expected.Add("planning-history");
        Assert.Equal(expected, calls);
    }

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
