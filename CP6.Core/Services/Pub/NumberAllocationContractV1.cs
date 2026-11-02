namespace CP6.Core.Services.Pub;

/// <summary>Trusted Owner registration for a NEW Main tenant/BizKey domain. Company/warehouse
/// describe that domain; they do not silently split the existing tenant/BizKey unique key.</summary>
public sealed record NumberAllocationScopeV1(Guid RuleId, Guid TenantId, string SourceOwner, string BizKey,
    string BusinessScope, string? Company, string? Warehouse, string CutoverBasis)
{
    public string ScopeKind => "Tenant";
    public string UniqueDomain => "Main.Pub_DocSequence(TenantId,BizKey)";
}
public sealed record NumberAllocationRuleV1(string Version, string Prefix, string? DateFormat, int MinimumWidth,
    int ResetCycle, string TimeZone, string Culture, int FirstValue = 1);
public sealed record NumberAllocationCommandV1(string OperationRef, string ExpectedRuleVersion,
    DateTimeOffset BusinessInstant, string BusinessInputIdentity);
public sealed record NumberAllocationReceiptV1(Guid AllocationId, Guid TenantId, Guid RuleId, string SourceOwner,
    string OperationRef, string RuleVersion, string PeriodKey, int SequenceValue, string Number,
    DateTimeOffset AllocatedAtUtc, string AllocationState = "ALLOCATED", string BusinessState = "UNKNOWN");
public sealed record NumberAllocationPreviewV1(string Sample, string RuleVersion, string PeriodKey,
    string TimeZone, bool WouldStartPeriod, bool IsReservation = false);
/// <summary>Changes format only. Domain, period, timezone, culture and initial value stay fixed.</summary>
public sealed record NumberRuleChangeCommandV1(string OperationRef, string ExpectedRuleVersion,
    string NewVersion, string Prefix, string? DateFormat, int MinimumWidth);
public sealed record NumberRuleChangeReceiptV1(Guid ChangeId, Guid TenantId, Guid RuleId, string SourceOwner,
    string OperationRef, string PreviousVersion, NumberAllocationRuleV1 Rule, DateTimeOffset ChangedAtUtc);
public sealed class NumberAllocationExceptionV1(string code, int status = 409, Exception? inner = null)
    : InvalidOperationException(code, inner)
{ public int Status { get; } = status; }
