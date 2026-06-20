using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
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

    // ── Bug 1 fix tests ──

    /// <summary>Bug 1 fix (Excel row-number): header skip uses physical row number, not iteration counter.
    /// With a leading blank row (row 1), a header row (row 2) and two data rows (rows 3-4),
    /// SkipHeaderRows=2 must skip rows 1 and 2 and parse rows 3-4 as data (2 rows, 0 errors).</summary>
    [Fact]
    public void Excel_ParseWithHeaderSkip_PhysicalRowNumber()
    {
        // Arrange: build in-memory .xlsx — row1=blank, row2=header, row3/4=data
        var profile = new BankImportProfile
        {
            Name = "XLSX-Test", FileFormat = BankFileFormat.Excel,
            SkipHeaderRows = 2,            // skip physical rows 1 (blank) and 2 (header)
            DateField = "0", DateFormat = "yyyy/MM/dd",
            AmountMode = BankAmountMode.DepositWithdrawalColumns,
            DepositAmountField = "1", WithdrawalAmountField = "2",
            RefNoField = "3",
            DecimalSeparator = ".", ThousandsSeparator = ","
        };

        using var ms = new MemoryStream();
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            // Row 1: blank (leave empty — RowsUsed() will skip it, but row number must still be 1)
            ws.Cell(1, 1).Value = "";
            // Row 2: header
            ws.Cell(2, 1).Value = "Date";
            ws.Cell(2, 2).Value = "Deposit";
            ws.Cell(2, 3).Value = "Withdrawal";
            ws.Cell(2, 4).Value = "Ref";
            // Row 3: data 1 (deposit 100)
            ws.Cell(3, 1).Value = "2026/06/10";
            ws.Cell(3, 2).Value = "100";
            ws.Cell(3, 3).Value = "";
            ws.Cell(3, 4).Value = "R1";
            // Row 4: data 2 (withdrawal 50)
            ws.Cell(4, 1).Value = "2026/06/11";
            ws.Cell(4, 2).Value = "";
            ws.Cell(4, 3).Value = "50";
            ws.Cell(4, 4).Value = "R2";
            wb.SaveAs(ms);
        }
        ms.Position = 0;

        var importer = new BankStatementImporter();
        var result = importer.Parse(profile, ms, "test.xlsx");

        // Assert: exactly 2 data rows, no errors
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(new DateTime(2026, 6, 10), result.Rows[0].TxnDate);
        Assert.Equal(100m, result.Rows[0].Amount);
        Assert.Equal(1, result.Rows[0].Direction);          // Deposit
        Assert.Equal(new DateTime(2026, 6, 11), result.Rows[1].TxnDate);
        Assert.Equal(50m, result.Rows[1].Amount);
        Assert.Equal(2, result.Rows[1].Direction);          // Withdrawal
        // SourceLineNo must reflect physical Excel row numbers (3 and 4)
        Assert.Equal(3, result.Rows[0].SourceLineNo);
        Assert.Equal(4, result.Rows[1].SourceLineNo);
    }

    // ── Bug 2 fix tests ──

    /// <summary>Bug 2 fix (Preview cross-session Fingerprint dedup): a row already in DB with the same
    /// Fingerprint but a different RawRowHash must be flagged as StrongDup in Preview (not just in Confirm).
    /// Before the fix Preview would miss this and over-report SuccessCount.</summary>
    [Fact]
    public async Task Preview_CrossSession_FingerprintDup_Detected()
    {
        var db = TestHelper.CreateInMemoryContext();
        var (svc, stmtId, profId) = await Seed(db);

        // The CSV profile in Seed: date col0 fmt yyyy/MM/dd, deposit col1, withdrawal col2, ref col3
        // Row: date=2026/06/15, deposit=500, ref=FP-TEST → Fingerprint = Sha256("20260615|1|500|FP-TEST|||")
        var txnDate = new DateTime(2026, 6, 15);
        const decimal amount = 500m;
        const int direction = 1;       // Deposit
        const string refNo = "FP-TEST";

        // Compute expected Fingerprint (mirrors BankStatementImporter.Sha256 logic)
        static string Sha256Hex(string s)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
            return Convert.ToHexString(bytes);
        }
        var expectedFp = Sha256Hex($"{txnDate:yyyyMMdd}|{direction}|{amount}|{refNo}|||");

        // Seed a DB line with that Fingerprint but an unrelated RawRowHash (simulates prior import with different raw text)
        var existingLine = new BankStatementLine
        {
            Id = Guid.NewGuid(), StatementId = stmtId, LineNo = 1,
            TxnDate = txnDate, Direction = BankLineDirection.Deposit, Amount = amount,
            CurrencyCd = "CNY",
            RefNo = refNo, Description = null, CounterpartyName = null, BalanceAfter = null,
            Source = BankLineSource.Imported, MatchStatus = BankLineMatchStatus.Unmatched,
            ImportBatchNo = "BKRIMP-SEED",
            RawRowHash = Sha256Hex("different-raw-text-from-prior-export"),
            Fingerprint = expectedFp,
        };
        existingLine.RecomputeSigned();
        db.BankStatementLines.Add(existingLine);
        await db.SaveChangesAsync();

        // CSV row has same semantic fields → same Fingerprint, but different raw text → different RawRowHash
        var csv = $"date,deposit,withdrawal,ref\n2026/06/15,500,,{refNo}\n";
        var preview = await svc.PreviewAsync(stmtId, profId, Csv(csv), "reexport.csv");

        // Bug 2 fix: Preview must flag this as StrongDup (cross-session Fingerprint dup)
        Assert.Equal(1, preview.StrongDupCount);
        Assert.Equal(0, preview.SuccessCount);
    }

    // ── Pre-existing test ──

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
