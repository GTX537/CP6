using CP6.Core.EFDbContext;
using CP6.Core.Services.Plan.Contracts;
using CP6.Entity.DomainModels.Plan;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Plan;

/// <inheritdoc/>
public class PlanConvertService : IPlanConvertService
{
    private readonly CP6Context _db;
    private readonly IPlanToPrService _prService;
    private readonly IPlanToWorkOrderService _woService;

    public PlanConvertService(CP6Context db, IPlanToPrService prService, IPlanToWorkOrderService woService)
    {
        _db = db;
        _prService = prService;
        _woService = woService;
    }

    public async Task ConfirmAsync(Guid plannedOrderId, string? user)
    {
        var po = await LoadAsync(plannedOrderId);
        if (po.Status != (int)PlannedOrderStatus.Suggested)
            throw new InvalidOperationException("E-PLAN-状态非法: 仅建议态计划订单可确认");

        po.Status = (int)PlannedOrderStatus.Confirmed;
        Stamp(po, user);
        await _db.SaveChangesAsync();
    }

    public async Task<string> ConvertAsync(Guid plannedOrderId, string? user)
    {
        var po = await LoadAsync(plannedOrderId);
        if (po.Status == (int)PlannedOrderStatus.Converted)
            throw new InvalidOperationException("E-PLAN-已转单: 计划订单已转单");
        if (po.Status == (int)PlannedOrderStatus.Ignored)
            throw new InvalidOperationException("E-PLAN-已忽略: 计划订单已忽略，不可转单");

        var peggings = await _db.Peggings.AsNoTracking()
            .Where(p => p.PlannedOrderId == po.Id && !p.IsDeleted)
            .ToListAsync();

        var docNo = po.Type == (int)PlannedOrderType.Purchase
            ? await _prService.CreatePrFromPlannedOrderAsync(po, peggings)
            : await _woService.CreateWorkOrderFromPlannedOrderAsync(po, peggings);

        po.Status = (int)PlannedOrderStatus.Converted;
        po.ConvertedDocNo = docNo;
        Stamp(po, user);
        await _db.SaveChangesAsync();
        return docNo;
    }

    public async Task IgnoreAsync(Guid plannedOrderId, string? user)
    {
        var po = await LoadAsync(plannedOrderId);
        if (po.Status == (int)PlannedOrderStatus.Converted)
            throw new InvalidOperationException("E-PLAN-已转单: 已转单计划订单不可忽略");

        po.Status = (int)PlannedOrderStatus.Ignored;
        Stamp(po, user);
        await _db.SaveChangesAsync();
    }

    private async Task<Plan_PlannedOrder> LoadAsync(Guid id)
        => await _db.PlannedOrders.FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted)
           ?? throw new InvalidOperationException("E-PLAN-计划订单不存在");

    private static void Stamp(Plan_PlannedOrder po, string? user)
    {
        po.Modifier = user;
        po.ModifyDate = DateTime.Now;
    }
}
