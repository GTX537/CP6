using CP6.Entity.DomainModels.Crm;

namespace CP6.Core.Services.Crm;

/// <summary>Single source of truth for CRM lead and opportunity transitions.</summary>
public static class CrmStateMachine
{
    private static readonly IReadOnlyDictionary<CrmLeadStatus, CrmLeadStatus[]> LeadTransitions =
        new Dictionary<CrmLeadStatus, CrmLeadStatus[]>
        {
            [CrmLeadStatus.New] = [CrmLeadStatus.Assigned, CrmLeadStatus.Contacted, CrmLeadStatus.Disqualified, CrmLeadStatus.Merged],
            [CrmLeadStatus.Assigned] = [CrmLeadStatus.Contacted, CrmLeadStatus.Disqualified, CrmLeadStatus.Merged],
            [CrmLeadStatus.Contacted] = [CrmLeadStatus.Qualified, CrmLeadStatus.Disqualified, CrmLeadStatus.Merged],
            [CrmLeadStatus.Qualified] = [CrmLeadStatus.Converted, CrmLeadStatus.Disqualified, CrmLeadStatus.Merged],
            [CrmLeadStatus.Converted] = [],
            [CrmLeadStatus.Disqualified] = [],
            [CrmLeadStatus.Merged] = [],
        };

    private static readonly IReadOnlyDictionary<CrmOpportunityStage, CrmOpportunityStage[]> OpportunityTransitions =
        new Dictionary<CrmOpportunityStage, CrmOpportunityStage[]>
        {
            [CrmOpportunityStage.Qualification] = [CrmOpportunityStage.NeedsAnalysis, CrmOpportunityStage.Lost],
            [CrmOpportunityStage.NeedsAnalysis] = [CrmOpportunityStage.Proposal, CrmOpportunityStage.Lost],
            [CrmOpportunityStage.Proposal] = [CrmOpportunityStage.Negotiation, CrmOpportunityStage.Accepted, CrmOpportunityStage.Lost],
            [CrmOpportunityStage.Negotiation] = [CrmOpportunityStage.Proposal, CrmOpportunityStage.Accepted, CrmOpportunityStage.Lost],
            [CrmOpportunityStage.Accepted] = [CrmOpportunityStage.Negotiation, CrmOpportunityStage.Won, CrmOpportunityStage.Lost],
            [CrmOpportunityStage.Won] = [],
            [CrmOpportunityStage.Lost] = [],
        };

    public static bool CanTransition(CrmLeadStatus from, CrmLeadStatus to) =>
        LeadTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public static bool CanTransition(CrmOpportunityStage from, CrmOpportunityStage to) =>
        OpportunityTransitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public static void EnsureTransition(CrmLeadStatus from, CrmLeadStatus to)
    {
        if (!CanTransition(from, to))
            throw new InvalidOperationException($"CRM_LEAD_TRANSITION_INVALID:{from}->{to}");
    }

    public static void EnsureTransition(CrmOpportunityStage from, CrmOpportunityStage to, bool hasAcceptedQuotation = false, bool hasCreatedOrder = false)
    {
        if (!CanTransition(from, to))
            throw new InvalidOperationException($"CRM_OPPORTUNITY_TRANSITION_INVALID:{from}->{to}");
        if (to == CrmOpportunityStage.Accepted && !hasAcceptedQuotation)
            throw new InvalidOperationException("CRM_ACCEPTED_QUOTATION_REQUIRED");
        if (to == CrmOpportunityStage.Won && !hasCreatedOrder)
            throw new InvalidOperationException("CRM_ORDER_REQUIRED_FOR_WON");
    }

    public static int Probability(CrmOpportunityStage stage) => stage switch
    {
        CrmOpportunityStage.Qualification => 10,
        CrmOpportunityStage.NeedsAnalysis => 25,
        CrmOpportunityStage.Proposal => 50,
        CrmOpportunityStage.Negotiation => 75,
        CrmOpportunityStage.Accepted => 90,
        CrmOpportunityStage.Won => 100,
        CrmOpportunityStage.Lost => 0,
        _ => 0,
    };
}
