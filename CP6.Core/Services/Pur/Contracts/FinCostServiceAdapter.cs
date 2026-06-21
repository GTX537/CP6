using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur.Contracts;

/// <summary>
/// 财务成本入账适配器（P2-D2 真实实现）。外注收成品后，把成品成本（加工费 + 支給材成本并入）
/// 经自动凭证引擎 <see cref="IAutoVoucherEngine"/> 按 <c>Subcontract.CostPosted</c> 规则生成成本凭证
/// （借 库存商品 FG / 贷 原材料 INVENTORY = FinishedCost）——成本唯一真相在财务，采购不双写。
/// 加工费的应付由三单匹配（<see cref="FinApServiceAdapter"/>）独立记，本适配器不碰应付（零重复）。
/// </summary>
public class FinCostServiceAdapter : IFinCostService
{
    private readonly IAutoVoucherEngine _engine;
    private readonly CP6Context _db;

    public FinCostServiceAdapter(IAutoVoucherEngine engine, CP6Context db)
    {
        _engine = engine;
        _db = db;
    }

    private const string SubcontractCostEvent = "Subcontract.CostPosted";   // 对应 PostingRuleSeed 同名规则

    /// <inheritdoc />
    public async Task<FinCostResult> PostSubcontractCostAsync(SubcontractCostDto dto, string? operatorId)
    {
        var sourceDocNo = $"SC-{dto.PoNo}-{dto.LineNo}";   // 幂等键 (Cost, SC-{Po}-{Line})

        var evt = new FinBizEvent
        {
            EventType = SubcontractCostEvent,
            Source = VoucherSource.Cost,
            SourceDocNo = sourceDocNo,
            BizDate = DateTime.Now,
            Description = $"外注成品成本入账 {dto.PoNo}-{dto.LineNo}",
        };
        evt.HeaderAmounts["Amount"] = dto.FinishedCost;    // 借 FG / 贷 INVENTORY = 成品成本（加工费+支給材并入）

        var r = await _engine.GenerateAsync(evt);
        if (!r.Ok) return FinCostResult.Fail(r.Code ?? "E-FIN-UNKNOWN");

        // 引擎只返回 FinResult（无凭证号），按幂等键回查凭证号（含幂等跳过时已存在的那张）
        var voucherNo = await _db.JournalEntries
            .Where(j => j.Source == VoucherSource.Cost && j.SourceDocNo == sourceDocNo && j.Status == JournalStatus.Posted)
            .Select(j => j.No)
            .FirstOrDefaultAsync();

        return FinCostResult.Pass(voucherNo);
    }
}
