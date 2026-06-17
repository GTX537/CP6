using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Pur;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Pur;

/// <summary>采购价表服务实现（采购 章01 §3/§4）。</summary>
public class SupplierPriceService : ISupplierPriceService
{
    private readonly CP6Context _db;

    public SupplierPriceService(CP6Context db) => _db = db;

    /// <inheritdoc />
    public async Task<decimal?> ResolvePriceAsync(string supplierId, string itemId, decimal qty, DateTime onDate)
    {
        // 满足 MinQty≤qty ∧ 当时有效（ValidFrom≤onDate ∧ (ValidTo==null ∨ ValidTo≥onDate)），取 MinQty 最大一档。
        var price = await _db.SupplierPrices
            .Where(p => !p.IsDeleted
                        && p.SupplierId == supplierId
                        && p.ItemId == itemId
                        && p.MinQty <= qty
                        && p.ValidFrom <= onDate
                        && (p.ValidTo == null || p.ValidTo >= onDate))
            .OrderByDescending(p => p.MinQty)
            .ThenByDescending(p => p.ValidFrom)
            .Select(p => (decimal?)p.Price)
            .FirstOrDefaultAsync();

        return price;
    }

    /// <inheritdoc />
    public async Task<List<SupplierPrice>> ListAsync(string supplierId, string? itemId = null)
    {
        var q = _db.SupplierPrices.Where(p => !p.IsDeleted && p.SupplierId == supplierId);
        if (!string.IsNullOrWhiteSpace(itemId))
            q = q.Where(p => p.ItemId == itemId);
        return await q.OrderBy(p => p.ItemId).ThenBy(p => p.MinQty).ToListAsync();
    }

    /// <inheritdoc />
    public async Task<SupplierPrice> SaveAsync(SupplierPrice price, string? userName)
    {
        if (string.IsNullOrWhiteSpace(price.SupplierId)) throw new InvalidOperationException("E-PUR-011"); // 供应商必填
        if (string.IsNullOrWhiteSpace(price.ItemId)) throw new InvalidOperationException("E-PUR-012");     // 物料必填
        if (price.Price < 0) throw new InvalidOperationException("E-PUR-013");                              // 单价不可为负
        if (price.MinQty < 0) throw new InvalidOperationException("E-PUR-014");                             // 阶梯量不可为负

        if (price.Id == Guid.Empty)
        {
            price.Creator = userName;
            price.CreateDate = DateTime.Now;
            _db.SupplierPrices.Add(price);
        }
        else
        {
            var exist = await _db.SupplierPrices.FirstOrDefaultAsync(p => p.Id == price.Id && !p.IsDeleted)
                        ?? throw new InvalidOperationException("E-PUR-015"); // 价档不存在
            exist.SupplierId = price.SupplierId;
            exist.ItemId = price.ItemId;
            exist.Price = price.Price;
            exist.CurrencyCd = price.CurrencyCd;
            exist.MinQty = price.MinQty;
            exist.ValidFrom = price.ValidFrom;
            exist.ValidTo = price.ValidTo;
            exist.Source = price.Source;
            exist.Modifier = userName;
            exist.ModifyDate = DateTime.Now;
            price = exist;
        }

        await _db.SaveChangesAsync();
        return price;
    }

    /// <inheritdoc />
    public async Task<SupplierPrice> UpsertAsync(SupplierPrice price, string? userName)
    {
        if (string.IsNullOrWhiteSpace(price.SupplierId)) throw new InvalidOperationException("E-PUR-011"); // 供应商必填
        if (string.IsNullOrWhiteSpace(price.ItemId)) throw new InvalidOperationException("E-PUR-012");     // 物料必填
        if (price.Price < 0) throw new InvalidOperationException("E-PUR-013");                              // 单价不可为负
        if (price.MinQty < 0) throw new InvalidOperationException("E-PUR-014");                             // 阶梯量不可为负

        // 业务键命中 (SupplierId, ItemId, MinQty, ValidFrom)（与唯一索引对齐）→ 原地更新；否则新增
        var exist = await _db.SupplierPrices.FirstOrDefaultAsync(p => !p.IsDeleted
            && p.SupplierId == price.SupplierId
            && p.ItemId == price.ItemId
            && p.MinQty == price.MinQty
            && p.ValidFrom == price.ValidFrom);

        if (exist == null)
        {
            price.Creator = userName;
            price.CreateDate = DateTime.Now;
            _db.SupplierPrices.Add(price);
            await _db.SaveChangesAsync();
            return price;
        }

        exist.Price = price.Price;
        exist.CurrencyCd = price.CurrencyCd;
        exist.ValidTo = price.ValidTo;
        exist.Source = price.Source;
        exist.Modifier = userName;
        exist.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return exist;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, string? userName)
    {
        var exist = await _db.SupplierPrices.FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted)
                    ?? throw new InvalidOperationException("E-PUR-015");
        exist.IsDeleted = true;
        exist.Modifier = userName;
        exist.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
