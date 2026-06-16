using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur.Contracts;

/// <summary>
/// 财务建应付适配器（P-D1 真实实现）。把采购端口 <see cref="PurApInvoiceDto"/> 映射到财务 <see cref="FinApInvoiceRequest"/>，
/// 委托财务 <see cref="IFinAp"/>（ApInvoiceService）建并过账——应付唯一真相在财务，采购不双写。
/// 借方科目按 GL 角色锚点 <c>INVENTORY</c>（原材料）解析（纸器采购入原材料）。
/// </summary>
public class FinApServiceAdapter : IFinApService
{
    private const string PurchaseDebitRole = "INVENTORY";   // 采购入账借方角色锚点（原材料）

    private readonly CP6Context _db;
    private readonly IFinAp _finAp;

    public FinApServiceAdapter(CP6Context db, IFinAp finAp)
    {
        _db = db;
        _finAp = finAp;
    }

    /// <inheritdoc />
    public async Task<PurApResult> CreateApInvoiceAsync(PurApInvoiceDto dto, string? operatorId)
    {
        // 借方进项科目：按角色锚点取（换模板包零改动）；缺则回退首个末级有效资产科目
        var debit = await _db.GlAccounts.FirstOrDefaultAsync(a => a.Role == PurchaseDebitRole && a.IsActive && a.IsLeaf)
                    ?? await _db.GlAccounts.FirstOrDefaultAsync(a => a.IsLeaf && a.IsActive
                        && a.Type == CP6.Entity.DomainModels.Fin.AccountType.Asset);
        if (debit == null) return PurApResult.Fail("E-PUR-044");   // 无可用进项科目（财务未初始化科目表）

        var req = new FinApInvoiceRequest
        {
            SupplierId = dto.SupplierId,
            SupplierInvoiceNo = dto.SupplierInvoiceNo,
            InvoiceDate = dto.InvoiceDate,
            DueDate = dto.DueDate,
            CurrencyCd = dto.CurrencyCd,
            FxRate = dto.FxRate,
            PurchaseOrderId = dto.PurchaseOrderId,
            Description = dto.Description,
            Lines = dto.Lines.Select(l => new FinApInvoiceRequestLine
            {
                ItemId = l.ItemId,
                Qty = l.Qty,
                UnitPrice = l.UnitPrice,
                TaxCodeId = l.TaxCodeId,
                ExpenseAccountId = debit.Id,
            }).ToList(),
        };

        var r = await _finAp.CreateInvoiceFromPurchaseAsync(req, operatorId ?? "system");
        return r.Result.Ok
            ? PurApResult.Pass(r.InvoiceId, r.InvoiceNo)
            : PurApResult.Fail(r.Result.Code ?? "E-PUR-046");
    }
}
