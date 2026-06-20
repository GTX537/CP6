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

    // ── B-2 tests ──

    private static async Task<(BankStatementService svc, Guid stmtId, Guid profId)> Seed(CP6.Core.EFDbContext.CP6Context db)
    {
        var svc = new BankStatementService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db), new BankStatementImporter());
        var period = await new FiscalPeriodService(db, 1).EnsureOpenAsync(new DateTime(2026, 6, 1), "admin");
        var acct = new BankAccount { Id = Guid.NewGuid(), Code = "B1", Name = "工行", GlAccountId = Guid.NewGuid(), IsActive = true };
        db.BankAccounts.Add(acct); await db.SaveChangesAsync();
        var prof = new BankImportProfile { Id = Guid.NewGuid(), Name = "CSV", FileFormat = BankFileFormat.Csv,
            SkipHeaderRows = 1, DateField = "0", DateFormat = "yyyy/MM/dd",
            AmountMode = BankAmountMode.DepositWithdrawalColumns, DepositAmountField = "1", WithdrawalAmountField = "2",
            RefNoField = "3", IsActive = true };
        db.BankImportProfiles.Add(prof);
        var r = await svc.CreateAsync(new BankStatement { BankAccountId = acct.Id, FiscalPeriodId = period.Id,
            OpeningBalance = 0, ClosingBalance = 100 }, "admin");
        await db.SaveChangesAsync();
        var stmt = await db.BankStatements.FirstAsync();
        return (svc, stmt.Id, prof.Id);
    }

    private static Stream Csv(string body) => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));

    [Fact]
    public async Task Preview_ParsesRows_NoPersist()
    {
        var db = TestHelper.CreateInMemoryContext();
        var (svc, stmtId, profId) = await Seed(db);
        var csv = "date,deposit,withdrawal,ref\n2026/06/05,100,,R1\n2026/06/06,,30,R2\n";
        var prev = await svc.PreviewAsync(stmtId, profId, Csv(csv), "a.csv");
        Assert.Equal(2, prev.SuccessCount);
        Assert.Empty(await db.BankStatementLines.ToListAsync());   // 不落库
    }

    [Fact]
    public async Task Confirm_PersistsLines_WithSignedAmount()
    {
        var db = TestHelper.CreateInMemoryContext();
        var (svc, stmtId, profId) = await Seed(db);
        var csv = "date,deposit,withdrawal,ref\n2026/06/05,100,,R1\n2026/06/06,,30,R2\n";
        var r = await svc.ConfirmImportAsync(stmtId, profId, Csv(csv), "a.csv", "admin");
        Assert.True(r.Ok);
        var lines = await db.BankStatementLines.OrderBy(x => x.LineNo).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Equal(100m, lines[0].SignedAmount);     // Deposit +
        Assert.Equal(-30m, lines[1].SignedAmount);     // Withdrawal −
        Assert.All(lines, l => Assert.Equal(BankLineSource.Imported, l.Source));
    }

    [Fact]
    public async Task Confirm_FatalParseError_RejectsWholeBatch()
    {
        var db = TestHelper.CreateInMemoryContext();
        var (svc, stmtId, profId) = await Seed(db);
        var csv = "date,deposit,withdrawal,ref\n2026/06/05,100,,R1\nBADDATE,,30,R2\n";  // 第2行日期坏
        var r = await svc.ConfirmImportAsync(stmtId, profId, Csv(csv), "a.csv", "admin");
        Assert.False(r.Ok);
        Assert.Equal("E-A4-IMPORT-001", r.Code);
        Assert.Empty(await db.BankStatementLines.ToListAsync());   // 整批不落库
    }

    [Fact]
    public async Task Confirm_StrongDup_Skipped()
    {
        var db = TestHelper.CreateInMemoryContext();
        var (svc, stmtId, profId) = await Seed(db);
        var csv = "date,deposit,withdrawal,ref\n2026/06/05,100,,R1\n";
        await svc.ConfirmImportAsync(stmtId, profId, Csv(csv), "a.csv", "admin");
        await svc.ConfirmImportAsync(stmtId, profId, Csv(csv), "a.csv", "admin");  // 同行再导
        Assert.Single(await db.BankStatementLines.ToListAsync());  // 强重复跳过
    }

    [Fact]
    public async Task Confirm_NonOpen_Rejected()
    {
        var db = TestHelper.CreateInMemoryContext();
        var (svc, stmtId, profId) = await Seed(db);
        var stmt = await db.BankStatements.FirstAsync(); stmt.Status = BankStatementStatus.Locked;
        await db.SaveChangesAsync();
        var r = await svc.ConfirmImportAsync(stmtId, profId, Csv("date,deposit,withdrawal,ref\n2026/06/05,1,,R\n"), "a.csv", "admin");
        Assert.False(r.Ok);
        Assert.Equal("E-A4-STATEMENT-LOCKED", r.Code);
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
