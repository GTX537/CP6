using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>应付主数据实现（章03）。银行账户/税码 列表+新建（编码唯一，重复抛）。</summary>
public class ApMasterService : IApMasterService
{
    private readonly CP6Context _db;
    public ApMasterService(CP6Context db) => _db = db;

    public async Task<List<BankAccount>> ListBankAccountsAsync(bool includeInactive = false)
    {
        var q = _db.BankAccounts.AsQueryable();
        if (!includeInactive) q = q.Where(b => b.IsActive);
        return await q.OrderBy(b => b.Code).ToListAsync();
    }

    public async Task<Guid> CreateBankAccountAsync(BankAccount bank, string user)
    {
        if (await _db.BankAccounts.AnyAsync(b => b.Code == bank.Code))
            throw new InvalidOperationException("E-FIN-230");   // 银行账户编码重复
        bank.Id = bank.Id == Guid.Empty ? Guid.NewGuid() : bank.Id;
        bank.Creator = user;
        bank.CreateDate = DateTime.Now;
        _db.BankAccounts.Add(bank);
        await _db.SaveChangesAsync();
        return bank.Id;
    }

    public async Task<List<TaxCode>> ListTaxCodesAsync(bool includeInactive = false)
    {
        var q = _db.TaxCodes.AsQueryable();
        if (!includeInactive) q = q.Where(t => t.IsActive);
        return await q.OrderBy(t => t.Code).ToListAsync();
    }

    public async Task<Guid> CreateTaxCodeAsync(TaxCode tax, string user)
    {
        if (await _db.TaxCodes.AnyAsync(t => t.Code == tax.Code))
            throw new InvalidOperationException("E-FIN-231");   // 税码重复
        tax.Id = tax.Id == Guid.Empty ? Guid.NewGuid() : tax.Id;
        tax.Creator = user;
        tax.CreateDate = DateTime.Now;
        _db.TaxCodes.Add(tax);
        await _db.SaveChangesAsync();
        return tax.Id;
    }
}
