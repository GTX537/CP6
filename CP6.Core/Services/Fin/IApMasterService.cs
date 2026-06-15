using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 应付主数据（章03）：银行账户 + 税码的列表/新建。供 AP 发票/付款界面的下拉选择。
/// </summary>
public interface IApMasterService
{
    Task<List<BankAccount>> ListBankAccountsAsync(bool includeInactive = false);
    Task<Guid> CreateBankAccountAsync(BankAccount bank, string user);

    Task<List<TaxCode>> ListTaxCodesAsync(bool includeInactive = false);
    Task<Guid> CreateTaxCodeAsync(TaxCode tax, string user);
}
