using CP6.Core.EFDbContext;
using CP6.Core.Services;
using CP6.Entity.DomainModels;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests;

public class CreditNoteServiceTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static CreditNoteService NewService(CP6Context db) => new(db);

    private static void SeedCreditNote(
        CP6Context db,
        string creditNoteNo,
        string customerCd,
        DateTime issueDate,
        string type = CreditNoteType.Refund,
        string? webOrderNo = null,
        string? rmaNo = null)
    {
        db.CreditNotes.Add(new CreditNote
        {
            CreditNoteNo = creditNoteNo,
            CustomerCd = customerCd,
            Type = type,
            WebOrderNo = webOrderNo ?? $"WEB-{creditNoteNo}",
            RmaNo = rmaNo ?? $"RMA-{creditNoteNo}",
            ProductCd = $"P-{creditNoteNo}",
            Qty = 2m,
            Amount = 1200m,
            Reason = "returned goods",
            IssueDate = issueDate,
        });
    }

    [Fact]
    public async Task Search_FilterByCustomer()
    {
        using var db = NewDb();
        SeedCreditNote(db, "CN-001", "C001", new DateTime(2026, 6, 1));
        SeedCreditNote(db, "CN-002", "C002", new DateTime(2026, 6, 2));
        SeedCreditNote(db, "CN-003", "C003", new DateTime(2026, 6, 3));
        await db.SaveChangesAsync();

        var result = await NewService(db).SearchAsync(new CreditNoteQuery { CustomerCd = "C002" });

        var item = Assert.Single(result.Items);
        Assert.Equal(1, result.Total);
        Assert.Equal("CN-002", item.CreditNoteNo);
        Assert.Equal("C002", item.CustomerCd);
    }

    [Fact]
    public async Task Search_FilterByDateRange()
    {
        using var db = NewDb();
        SeedCreditNote(db, "CN-001", "C001", new DateTime(2026, 6, 1));
        SeedCreditNote(db, "CN-002", "C001", new DateTime(2026, 6, 2));
        SeedCreditNote(db, "CN-003", "C001", new DateTime(2026, 6, 3));
        SeedCreditNote(db, "CN-004", "C001", new DateTime(2026, 6, 4));
        SeedCreditNote(db, "CN-005", "C001", new DateTime(2026, 6, 5));
        await db.SaveChangesAsync();

        var result = await NewService(db).SearchAsync(new CreditNoteQuery
        {
            DateFrom = new DateTime(2026, 6, 2),
            DateTo = new DateTime(2026, 6, 4),
        });

        Assert.Equal(3, result.Total);
        Assert.Equal(new[] { "CN-004", "CN-003", "CN-002" }, result.Items.Select(x => x.CreditNoteNo));
    }

    [Fact]
    public async Task Search_JoinsCustomerName_FromBp()
    {
        using var db = NewDb();
        SeedCreditNote(db, "CN-001", "C001", new DateTime(2026, 6, 1));
        db.BusinessPartners.Add(new BusinessPartner
        {
            BpCd = "C001",
            BpName = "Customer One",
            BaseCd = "B01",
            CustomerFlg = true,
        });
        await db.SaveChangesAsync();

        var result = await NewService(db).SearchAsync(new CreditNoteQuery());

        var item = Assert.Single(result.Items);
        Assert.Equal("Customer One", item.CustomerName);
    }

    [Fact]
    public async Task Search_NoMatchingBp_FallsBackToCustomerCd()
    {
        using var db = NewDb();
        SeedCreditNote(db, "CN-001", "C404", new DateTime(2026, 6, 1));
        await db.SaveChangesAsync();

        var result = await NewService(db).SearchAsync(new CreditNoteQuery());

        var item = Assert.Single(result.Items);
        Assert.Equal("C404", item.CustomerName);
    }
}
