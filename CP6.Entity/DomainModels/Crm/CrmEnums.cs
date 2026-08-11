namespace CP6.Entity.DomainModels.Crm;

public enum CrmLeadStatus
{
    New = 0,
    Assigned = 10,
    Contacted = 20,
    Qualified = 30,
    Converted = 40,
    Disqualified = 90,
    Merged = 99,
}

public enum CrmOpportunityStage
{
    Qualification = 10,
    NeedsAnalysis = 20,
    Proposal = 30,
    Negotiation = 40,
    Accepted = 50,
    Won = 90,
    Lost = 99,
}

public enum CrmActivityType
{
    Call = 10,
    Email = 20,
    Meeting = 30,
    CustomerMessage = 40,
    Note = 50,
    System = 90,
}

public enum CrmSourceChannel
{
    Website = 10,
    Manual = 20,
}

public enum CrmPublicSubmissionStatus
{
    Accepted = 10,
    Quarantined = 20,
    ConvertedToLead = 30,
}

public enum CrmSiteStatus
{
    Draft = 0,
    Published = 10,
    Disabled = 90,
}

public enum CrmPageType
{
    Home = 10,
    Company = 20,
    Product = 30,
    Service = 40,
    News = 50,
    Contact = 60,
    Privacy = 70,
}

public enum CrmPublicationStatus
{
    Draft = 0,
    Published = 10,
    Superseded = 20,
}

public static class CrmEntityTypes
{
    public const string Lead = "lead";
    public const string Opportunity = "opportunity";
}

public static class CrmErpEntityTypes
{
    public const string BusinessPartner = "business-partner";
    public const string Quotation = "quotation";
    public const string Order = "order";
}
