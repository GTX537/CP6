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

    /// <summary>Bug 1 fix: UPDATE branch must not overwrite TenantId/Creator/CreateDate from dto.</summary>
    [Fact]
    public async Task Profile_Update_PreservesTenantAndCreateAudit()
    {
        var svc = Create(out var db);

        // Insert (first upsert — StampTenant stamps DefaultTenant, Creator=admin)
        var insertDto = new BankImportProfile
        {
            Name = "Test-Profile", FileFormat = BankFileFormat.Csv,
            DateField = "0", AmountMode = BankAmountMode.DepositWithdrawalColumns,
            DepositAmountField = "2", WithdrawalAmountField = "3", IsActive = true
        };
        await svc.UpsertProfileAsync(insertDto, "admin");

        var inserted = (await svc.ListProfilesAsync()).Single();
        var originalId       = inserted.Id;
        var originalTenantId = inserted.TenantId;
        var originalCreator  = inserted.Creator;
        var originalCreated  = inserted.CreateDate;

        // Simulate API binding: dto has TenantId == Guid.Empty and null create-audit
        var updateDto = new BankImportProfile
        {
            Id        = originalId,
            Name      = "Test-Profile-Updated",
            TenantId  = Guid.Empty,     // ← the bad value that would be copied without the fix
            Creator   = null,           // ← similarly wiped
            CreateDate = default,       // ← similarly wiped
            FileFormat = BankFileFormat.Csv,
            DateField = "0", AmountMode = BankAmountMode.DepositWithdrawalColumns,
            DepositAmountField = "2", WithdrawalAmountField = "3", IsActive = true
        };
        await svc.UpsertProfileAsync(updateDto, "editor");

        // After update, the row must still be visible through the tenant-scoped ListProfilesAsync
        var all = await svc.ListProfilesAsync();
        Assert.Single(all);                                        // tenant intact → row visible
        Assert.Equal("Test-Profile-Updated", all[0].Name);        // name was updated
        Assert.Equal(originalTenantId, all[0].TenantId);          // TenantId preserved
        Assert.Equal(originalCreator,  all[0].Creator);           // Creator preserved
        Assert.Equal(originalCreated,  all[0].CreateDate);        // CreateDate preserved
        Assert.Equal("editor", all[0].Modifier);                  // Modifier stamped
        Assert.NotNull(all[0].ModifyDate);                        // ModifyDate stamped
    }

    /// <summary>Bug 2 fix: SplitCsv must handle RFC-4180 doubled-quote escape ("") as a literal ".</summary>
    [Fact]
    public void Csv_EscapedQuote_ParsesCorrectly()
    {
        var profile = new BankImportProfile
        {
            Name = "EscQ-Profile", FileFormat = BankFileFormat.Csv,
            DateField = "0", DateFormat = "yyyyMMdd",
            AmountMode = BankAmountMode.DepositWithdrawalColumns,
            DepositAmountField = "2", WithdrawalAmountField = "3",
            DescriptionField = "1",
            SkipHeaderRows = 0, Delimiter = ",", Encoding = "UTF-8",
            DecimalSeparator = ".", ThousandsSeparator = ","
        };

        // RFC-4180: "ACME ""Q3"" payment" → ACME "Q3" payment
        var csvLine = "20240115,\"ACME \"\"Q3\"\" payment\",1234.56,";
        using var ms = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(csvLine));

        var importer = new BankStatementImporter();
        var result = importer.Parse(profile, ms, "test.csv");

        Assert.Empty(result.Errors);
        Assert.Single(result.Rows);
        Assert.Equal("ACME \"Q3\" payment", result.Rows[0].Description);
    }
}
