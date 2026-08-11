using CP6.Core.Services.Crm;
using CP6.Entity.DomainModels.Crm;

namespace CP6.Tests.Crm;

public class CrmStateMachineTests
{
    [Theory]
    [InlineData(CrmLeadStatus.New, CrmLeadStatus.Assigned)]
    [InlineData(CrmLeadStatus.Assigned, CrmLeadStatus.Contacted)]
    [InlineData(CrmLeadStatus.Contacted, CrmLeadStatus.Qualified)]
    [InlineData(CrmLeadStatus.Qualified, CrmLeadStatus.Converted)]
    public void LeadTransition_AllowsDefinedPath(CrmLeadStatus from, CrmLeadStatus to)
        => Assert.True(CrmStateMachine.CanTransition(from, to));

    [Theory]
    [InlineData(CrmLeadStatus.New, CrmLeadStatus.Converted)]
    [InlineData(CrmLeadStatus.Converted, CrmLeadStatus.Contacted)]
    [InlineData(CrmLeadStatus.Disqualified, CrmLeadStatus.Assigned)]
    [InlineData(CrmLeadStatus.Merged, CrmLeadStatus.Assigned)]
    public void LeadTransition_RejectsSkippedOrTerminalPath(CrmLeadStatus from, CrmLeadStatus to)
        => Assert.Throws<InvalidOperationException>(() => CrmStateMachine.EnsureTransition(from, to));

    [Fact]
    public void Opportunity_AcceptedRequiresAcceptedQuotation()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrmStateMachine.EnsureTransition(CrmOpportunityStage.Proposal, CrmOpportunityStage.Accepted));
        Assert.Equal("CRM_ACCEPTED_QUOTATION_REQUIRED", ex.Message);
    }

    [Fact]
    public void Opportunity_WonRequiresCreatedOrder()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CrmStateMachine.EnsureTransition(
                CrmOpportunityStage.Accepted,
                CrmOpportunityStage.Won,
                hasAcceptedQuotation: true));
        Assert.Equal("CRM_ORDER_REQUIRED_FOR_WON", ex.Message);
    }

    [Fact]
    public void Opportunity_WonAfterOrder_IsAllowed()
    {
        CrmStateMachine.EnsureTransition(
            CrmOpportunityStage.Accepted,
            CrmOpportunityStage.Won,
            hasAcceptedQuotation: true,
            hasCreatedOrder: true);
        Assert.Equal(100, CrmStateMachine.Probability(CrmOpportunityStage.Won));
        Assert.Equal(0, CrmStateMachine.Probability(CrmOpportunityStage.Lost));
    }

    [Fact]
    public void Opportunity_LostIsTerminal()
        => Assert.Throws<InvalidOperationException>(() =>
            CrmStateMachine.EnsureTransition(CrmOpportunityStage.Lost, CrmOpportunityStage.Qualification));
}
