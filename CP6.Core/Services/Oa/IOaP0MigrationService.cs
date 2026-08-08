namespace CP6.Core.Services.Oa;

public sealed record OaP0PreflightReport(
    int FlowDefs,
    int FormDefs,
    int Running,
    int Suspended,
    int Terminal,
    int LegacyDraftInstances,
    int OrphanFlowKeys,
    int OrphanFormKeys,
    int UnpinnableActiveInstances,
    int UnpinnableFormData,
    int InvalidSubFlowRefs,
    int DuplicateActiveBusinessKeys,
    int InvalidLegacyDrafts)
{
    public bool SafeToBackfill =>
        UnpinnableActiveInstances == 0 &&
        InvalidSubFlowRefs == 0 &&
        DuplicateActiveBusinessKeys == 0 &&
        InvalidLegacyDrafts == 0;
}

public sealed record OaP0BackfillCount(int Expected, int Inserted, int Skipped, int Errors);

public sealed record OaP0BackfillReport(
    OaP0BackfillCount FlowVersions,
    OaP0BackfillCount FormVersions,
    OaP0BackfillCount FlowPins,
    OaP0BackfillCount FormDataPins,
    OaP0BackfillCount Bindings,
    OaP0BackfillCount Dependencies,
    OaP0BackfillCount Drafts);

public interface IOaP0MigrationService
{
    Task<OaP0PreflightReport> PreflightAsync(CancellationToken ct = default);
    Task<OaP0BackfillReport> BackfillAsync(CancellationToken ct = default);
}
