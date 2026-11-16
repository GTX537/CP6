using CP6.Core.Services.Sys;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

public partial class OrderService
{
    private Task<CreditCheckResultDto> ReadCreditAsync(string customer, decimal amount) =>
        ReadOwner.ReadAsync("erp-order", FieldPermissionUseV1.Read, false, async (access, ct) =>
        {
            // 余额、额度判断和说明均披露 Amount，并依赖 UnitPrice。
            RequireFullFields(access);
            // 单条语句读取完整客户集合及当前 Owner 范围，不能将可见行小计当成完整余额。
            var rows = await _db.OrderDetails.AsNoTracking()
                .Where(d => !d.IsDeleted && d.HaibaiNo2 == customer)
                .Select(d => new { d.Amount, Allowed = access.Rows.Any(allowed => allowed.Id == d.Id) })
                .ToArrayAsync(ct);
            if (rows.Any(row => !row.Allowed)) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
            return CreditResult(rows.Sum(row => row.Amount ?? 0m), amount);
        });

    private Task<bool> ReadConsignedCheckAsync(string number, int detail, decimal quantity) =>
        ReadOwner.ReadAsync("erp-order", FieldPermissionUseV1.Read, false, async (access, ct) =>
        {
            var row = await _db.OrderDetails.AsNoTracking()
                .Where(d => !d.IsDeleted && d.WebOrderNo == number && d.WebOrderDetailNo == detail)
                .Select(d => new { d.Quantity, Allowed = access.Rows.Any(allowed => allowed.Id == d.Id) })
                .FirstOrDefaultAsync(ct);
            // 核对当前查询权后，真实不存在的新行保留原结果。
            if (row == null) return true;
            if (!row.Allowed) throw new OrderPriceCorrectionException("OBJECT_FORBIDDEN", 403);
            return quantity <= (row.Quantity ?? 0m);
        });
}
