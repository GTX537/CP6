namespace CP6.Entity.DTOs.Wms;

public sealed class SlottingQueryV1
{
    public string? WarehouseCd { get; set; }
    public int? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
public sealed class SlottingPageV1
{
    public string Version => "SLOTTING-OWNER/1";
    public string Coverage => "AUTHORIZED_LEGACY_OBSERVATION";
    public bool WritesAvailable => false;
    public string WriteBlocker => "SLOTTING_OWNER_ADOPTION_UNPROVEN";
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public List<SlottingObservationV1> Items { get; init; } = [];
}
public sealed class SlottingObservationV1
{
    public string Version => "SLOTTING-OWNER/1";
    public string Knowledge => "UNKNOWN";
    public string StatisticsMethod => "LEGACY_ROW_COUNT";
    public Guid Id { get; init; }
    public string SlottingPlanNo { get; init; } = "";
    public string WarehouseCd { get; init; } = "";
    public int AnalysisDays { get; init; }
    public int LegacyStatus { get; init; }
    public DateTime? AnalyzedAt { get; init; }
    public DateTime? CreateDate { get; init; }
    public bool SnapshotReadable { get; init; }
    public string SnapshotState { get; init; } = "SOURCE_DISCLOSURE_UNAVAILABLE";
    public int? TxnSampleCount { get; init; }
    public int? RecommendationCount { get; init; }
    public string? RecommendationsJson { get; init; }
    public string JsonState { get; init; } = "NOT_DISCLOSED";
    public bool ApproverCdReadable { get; init; }
    public bool RemarksReadable { get; init; }
    public string? ApproverCd { get; init; }
    public string? Remarks { get; init; }
}
