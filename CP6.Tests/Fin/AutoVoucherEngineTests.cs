using CP6.Core.EFDbContext;
using CP6.Core.Services.Fin;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Fin;

/// <summary>
/// 财务章05 A-2：★自动凭证引擎核心。规则即数据 → 拼凭证 → AutoPost 直过。
/// FixedRole 按 Role 锚点取科目 / DocumentLines 透传炸开 / 幂等跳重 / 借贷不平被 AutoPost 拦下（双保险）。
/// </summary>
public class AutoVoucherEngineTests
{
    private static CP6Context NewDb() => new(new DbContextOptionsBuilder<CP6Context>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static AutoVoucherEngine Engine(CP6Context db) =>
        new(db, new JournalEntryService(db, new FiscalPeriodService(db, 1), new FinSequenceService(db)));

    private static readonly DateTime Biz = new(2026, 6, 15);

    /// <summary>种一个末级启用科目（带 Role 锚点）。</summary>
    private static GlAccount Acc(CP6Context db, string code, string? role = null, bool requirePartner = false)
    {
        var a = new GlAccount
        {
            Id = Guid.NewGuid(), Code = code, Name = code, IsLeaf = true, IsActive = true,
            Role = role, RequirePartner = requirePartner,
        };
        db.GlAccounts.Add(a);
        return a;
    }

    private static PostingRuleLine FixedRole(PostingSide side, string role, string amountField, bool carryPartner = false) =>
        new() { Side = side, Source = RuleLineSource.FixedRole, AccountRole = role, AmountField = amountField, CarryPartner = carryPartner };

    private static PostingRuleLine DocLines(PostingSide side, string accField, string amtField) =>
        new() { Side = side, Source = RuleLineSource.DocumentLines, LineAccountField = accField, LineAmountField = amtField };

    // ───────────────────────────────────────────

    [Fact]
    public async Task Generate_FixedRole_ResolvesAccountByRole()
    {
        using var db = NewDb();
        var exp = Acc(db, "5001", role: "MATERIAL");
        var ap = Acc(db, "2202", role: "AP_CONTROL", requirePartner: true);
        db.PostingRules.Add(new PostingRule
        {
            EventType = "AP.InvoicePosted", Name = "AP发票", IsActive = true,
            Lines =
            {
                FixedRole(PostingSide.Debit, "MATERIAL", "GrossAmount"),
                FixedRole(PostingSide.Credit, "AP_CONTROL", "GrossAmount", carryPartner: true),
            },
        });
        await db.SaveChangesAsync();

        var evt = new FinBizEvent
        {
            EventType = "AP.InvoicePosted", Source = VoucherSource.AP, SourceDocNo = "AP-001",
            BizDate = Biz, PartnerId = "SUP001", Description = "买料",
        };
        evt.HeaderAmounts["GrossAmount"] = 1000m;

        var r = await Engine(db).GenerateAsync(evt);
        Assert.True(r.Ok, r.Code);

        var entry = await db.JournalEntries.Include(x => x.Lines).SingleAsync();
        Assert.Equal(JournalStatus.Posted, entry.Status);
        var debit = entry.Lines.Single(l => l.Debit > 0);
        var credit = entry.Lines.Single(l => l.Credit > 0);
        Assert.Equal(exp.Id, debit.AccountId);          // ★ Role→科目 锚点解析
        Assert.Equal(ap.Id, credit.AccountId);
        Assert.Equal(1000m, debit.Debit);
        Assert.Equal(1000m, credit.Credit);
        Assert.Equal("SUP001", credit.PartnerId);        // CarryPartner 盖章
    }

    [Fact]
    public async Task Generate_DocumentLines_ExplodesPerAccount()
    {
        using var db = NewDb();
        var mat = Acc(db, "5001", role: "MATERIAL");
        var freight = Acc(db, "5002");
        var ap = Acc(db, "2202", role: "AP_CONTROL", requirePartner: true);
        db.PostingRules.Add(new PostingRule
        {
            EventType = "AP.InvoicePosted", Name = "AP混行发票", IsActive = true,
            Lines =
            {
                DocLines(PostingSide.Debit, "ExpenseAccountId", "Amount"),
                FixedRole(PostingSide.Credit, "AP_CONTROL", "GrossAmount", carryPartner: true),
            },
        });
        await db.SaveChangesAsync();

        var evt = new FinBizEvent
        {
            EventType = "AP.InvoicePosted", Source = VoucherSource.AP, SourceDocNo = "AP-002",
            BizDate = Biz, PartnerId = "SUP001", Description = "混行",
        };
        evt.HeaderAmounts["GrossAmount"] = 350m;
        foreach (var (acc, amt) in new[] { (mat.Id, 100m), (mat.Id, 200m), (freight.Id, 50m) })
        {
            var dl = new FinBizEventLine();
            dl.Guids["ExpenseAccountId"] = acc;
            dl.Amounts["Amount"] = amt;
            evt.DocLines.Add(dl);
        }

        var r = await Engine(db).GenerateAsync(evt);
        Assert.True(r.Ok, r.Code);

        var entry = await db.JournalEntries.Include(x => x.Lines).SingleAsync();
        var debits = entry.Lines.Where(l => l.Debit > 0).ToList();
        Assert.Equal(2, debits.Count);                                   // 同科目两行合并→2 条借方
        Assert.Equal(300m, debits.Single(l => l.AccountId == mat.Id).Debit);   // 100+200 合并
        Assert.Equal(50m, debits.Single(l => l.AccountId == freight.Id).Debit);
        Assert.Equal(350m, entry.Lines.Single(l => l.Credit > 0).Credit);
    }

    [Fact]
    public async Task Generate_Idempotent_SkipsDuplicateSourceDoc()
    {
        using var db = NewDb();
        Acc(db, "5001", role: "MATERIAL");
        Acc(db, "2202", role: "AP_CONTROL", requirePartner: true);
        db.PostingRules.Add(new PostingRule
        {
            EventType = "AP.InvoicePosted", Name = "AP发票", IsActive = true,
            Lines =
            {
                FixedRole(PostingSide.Debit, "MATERIAL", "GrossAmount"),
                FixedRole(PostingSide.Credit, "AP_CONTROL", "GrossAmount", carryPartner: true),
            },
        });
        await db.SaveChangesAsync();

        FinBizEvent Make()
        {
            var e = new FinBizEvent
            {
                EventType = "AP.InvoicePosted", Source = VoucherSource.AP, SourceDocNo = "AP-DUP",
                BizDate = Biz, PartnerId = "SUP001",
            };
            e.HeaderAmounts["GrossAmount"] = 1000m;
            return e;
        }

        var eng = Engine(db);
        await eng.GenerateAsync(Make());
        var n1 = await db.JournalEntries.CountAsync(x => x.SourceDocNo == "AP-DUP");
        await eng.GenerateAsync(Make());                                  // 重放
        var n2 = await db.JournalEntries.CountAsync(x => x.SourceDocNo == "AP-DUP");

        Assert.Equal(1, n1);
        Assert.Equal(n1, n2);                                            // ★ 幂等：不重复生成
    }

    [Fact]
    public async Task Generate_Unbalanced_RejectedByAutoPost()
    {
        using var db = NewDb();
        Acc(db, "5001", role: "MATERIAL");
        Acc(db, "2202", role: "AP_CONTROL", requirePartner: true);
        // 规则配错：借取 NetAmount(900)、贷取 GrossAmount(1000) → 借贷不平
        db.PostingRules.Add(new PostingRule
        {
            EventType = "AP.Bad", Name = "错配规则", IsActive = true,
            Lines =
            {
                FixedRole(PostingSide.Debit, "MATERIAL", "NetAmount"),
                FixedRole(PostingSide.Credit, "AP_CONTROL", "GrossAmount", carryPartner: true),
            },
        });
        await db.SaveChangesAsync();

        var evt = new FinBizEvent
        {
            EventType = "AP.Bad", Source = VoucherSource.AP, SourceDocNo = "AP-BAD",
            BizDate = Biz, PartnerId = "SUP001",
        };
        evt.HeaderAmounts["NetAmount"] = 900m;
        evt.HeaderAmounts["GrossAmount"] = 1000m;

        var r = await Engine(db).GenerateAsync(evt);
        Assert.False(r.Ok);
        Assert.Equal("E-FIN-107", r.Code);                               // ★ AutoPost 借贷恒等拦下
        Assert.Equal(0, await db.JournalEntries.CountAsync());           // 不落库
    }
}
