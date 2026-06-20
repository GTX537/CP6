using Microsoft.EntityFrameworkCore;
using CP6.Entity.DomainModels.Fin;
using CP6.Core.Services.Fin;

namespace CP6.Tests.Fin;

public class BankStatementImportTests
{
    private static BankStatementService Create(out CP6.Core.EFDbContext.CP6Context db)
    {
        db = TestHelper.CreateInMemoryContext();
        return new BankStatementService(db,
            new FiscalPeriodService(db, 1),
            new FinSequenceService(db),
            new BankStatementImporter());
    }

    [Fact]
    public async Task Profile_Upsert_Then_List()
    {
        var svc = Create(out var db);
        await svc.UpsertProfileAsync(new BankImportProfile { Name = "MUFG-CSV", FileFormat = BankFileFormat.Csv,
            DateField = "0", AmountMode = BankAmountMode.DepositWithdrawalColumns,
            DepositAmountField = "2", WithdrawalAmountField = "3", IsActive = true }, "admin");
        var all = await svc.ListProfilesAsync();
        Assert.Single(all);
        Assert.Equal("MUFG-CSV", all[0].Name);
    }

    [Fact]
    public async Task Profile_Upsert_BlankName_Throws()
    {
        var svc = Create(out _);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpsertProfileAsync(new BankImportProfile { Name = "" }, "admin"));
    }
}
