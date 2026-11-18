using CP6.Core.EFDbContext;

namespace CP6.Core.Services.Pub;

/// <summary>由原业务归属方提供，绑定实际原分配器、批准记录及冻结时的完整清单。</summary>
public sealed record NumberCutoverSnapshotV1(NumberAllocationScopeV1 Scope, NumberAllocationRuleV1 Rule,
    string ApprovalRef, string OriginalAllocator, IReadOnlyList<NumberPriorPeriodV1> Periods,
    IReadOnlyList<NumberPriorSlotV1> Slots, IReadOnlyList<NumberPriorOperationV1> Operations);

public sealed record NumberPriorPeriodV1(string PeriodKey, int LastIssued);
public sealed record NumberPriorSlotV1(string PeriodKey, int SequenceValue, string? ExposedNumber,
    string? GapReason, string? OperationRef);
public sealed record NumberPriorOperationV1(Guid ActorId, NumberAllocationCommandV1 Command,
    NumberAllocationReceiptV1? OriginalReceipt);

/// <summary>原归属方独立于恢复库保存的写入批准，以及开放该候选库前必须找回的已分配原件。</summary>
public sealed record NumberWriterApprovalV1(NumberAllocationScopeV1 Scope, string CutoverApprovalRef,
    string WriterApprovalRef, IReadOnlyList<NumberAllocationReceiptV1> RequiredAllocations);

/// <summary>宿主绑定原归属方。读取清单时已锁住实际旧规则行，原分配器与切换共用该锁。
/// 原操作查询只能读取原回执，不得新发号。普通遗留规则缺少原始清单时由原分配器继续持有。</summary>
public interface INumberCutoverSourceV1
{
    Task<NumberCutoverSnapshotV1?> ReadApprovedAsync(CP6Context db, NumberAllocationScopeV1 scope, CancellationToken ct);
    /// <summary>仅为该实际数据库及恢复代次取得当前唯一写入资格，持有至本事务结束。
    /// 恢复开放须由归属方从候选库以外的完整分配与外露记录生成清单；未确认、退役或只读库返回空。</summary>
    Task<NumberWriterApprovalV1?> AcquireWriterAsync(CP6Context db, NumberAllocationScopeV1 scope,
        string cutoverApprovalRef, CancellationToken ct);
    Task<NumberAllocationReceiptV1?> ReadOriginalAsync(CP6Context db, NumberAllocationScopeV1 scope,
        string approvalRef, string operationRef, CancellationToken ct);
}
