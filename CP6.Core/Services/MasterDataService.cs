using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services;

public class MasterDataService : IMasterDataService
{
    private readonly CP6Context _db;
    public MasterDataService(CP6Context db) => _db = db;

    public Task<List<MasterBase>> GetBasesAsync() =>
        _db.MasterBases.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.BaseCd)
            .ToListAsync();

    public Task<List<MasterStaff>> GetStaffsAsync(string? baseCd)
    {
        var q = _db.MasterStaffs.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(baseCd))
            q = q.Where(x => x.BaseCd == baseCd);
        return q.OrderBy(x => x.SortOrder).ThenBy(x => x.StaffCd).ToListAsync();
    }

    public Task<List<MasterGenericCode>> GetGenericCodesAsync(string groupCode) =>
        _db.MasterGenericCodes.AsNoTracking()
            .Where(x => !x.IsDeleted && x.GroupCode == groupCode)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .ToListAsync();

    /// <summary>
    /// 得意先ルックアップ — Quotation / ProductMaster の使用済 CustomerCd を集約。
    /// 専用 取引先マスタ未配備につき distinct 集約方式。
    /// </summary>
    public async Task<MasterLookupResult<CustomerLookupDto>> SearchCustomersAsync(string? keyword, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 20;

        // Quotation 由来
        var qFromQuot = _db.Quotations.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CustomerCd != null && x.CustomerCd != "")
            .Select(x => new { x.CustomerCd, x.CustomerName });

        // ProductMaster 由来（CustomerName 列なし → null 扱い）
        var qFromProd = _db.ProductMasters.AsNoTracking()
            .Where(x => !x.IsDeleted && x.CustomerCd != null && x.CustomerCd != "")
            .Select(x => new { x.CustomerCd, CustomerName = (string?)null });

        var union = qFromQuot.Concat(qFromProd);

        // グループ化（コード単位、最後に出現した名称を採用）
        var grouped = union
            .GroupBy(x => x.CustomerCd)
            .Select(g => new CustomerLookupDto
            {
                CustomerCd = g.Key,
                CustomerName = g.Where(y => y.CustomerName != null).Select(y => y.CustomerName).FirstOrDefault(),
                UsageCount = g.Count(),
            });

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            grouped = grouped.Where(x =>
                x.CustomerCd.Contains(k) ||
                (x.CustomerName != null && x.CustomerName.Contains(k)));
        }

        var total = await grouped.CountAsync();
        var rows = await grouped
            .OrderBy(x => x.CustomerCd)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new MasterLookupResult<CustomerLookupDto> { Rows = rows, Total = total };
    }

    /// <summary>
    /// 製品マスタルックアップ — セット品検索 / 既存製品参照 共通。
    /// </summary>
    public async Task<MasterLookupResult<ProductLookupDto>> SearchProductsAsync(
        string? keyword, string? customerCd, bool onlyApproved, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 20;

        var q = _db.ProductMasters.AsNoTracking().Where(x => !x.IsDeleted);

        if (onlyApproved) q = q.Where(x => x.Status == 9);
        if (!string.IsNullOrWhiteSpace(customerCd)) q = q.Where(x => x.CustomerCd == customerCd);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            q = q.Where(x =>
                x.ProductCd.Contains(k) ||
                (x.SetProductName != null && x.SetProductName.Contains(k)) ||
                (x.CustomerItemName1 != null && x.CustomerItemName1.Contains(k)) ||
                (x.CustomerItemName2 != null && x.CustomerItemName2.Contains(k)));
        }

        var total = await q.CountAsync();
        var rows = await q
            .OrderBy(x => x.ProductCd)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ProductLookupDto
            {
                ProductCd = x.ProductCd,
                SetProductName = x.SetProductName,
                CustomerCd = x.CustomerCd,
                CustomerName = null, // 専用顧客マスタなし、フロントで再解決可
                CustomerItemName1 = x.CustomerItemName1,
                CustomerItemName2 = x.CustomerItemName2,
                Status = x.Status,
                ModifyDate = x.ModifyDate,
            })
            .ToListAsync();

        return new MasterLookupResult<ProductLookupDto> { Rows = rows, Total = total };
    }
}
