using System.Net;
using System.Text.Json;
using CP6.Core.Services.Fin;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Erp;
using CP6.Entity.DomainModels.Fin;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private const string ArInvoicePath = "/api/fin/ar/invoice";
    private const string ArReceiptPath = "/api/fin/ar/receipt";
    private const string ArInvoiceMenu = "fin-ar-invoice";
    private const string ArReceiptMenu = "fin-ar-receipt";
    private static readonly DateTime ArDate = new(2026, 6, 15);
    private const decimal ArCost = 91.23m;
    private const decimal ArApplied = 12.34m;
    private const decimal ArFxDifference = 3.08m;

    [TenantManagementPostgresTheory]
    [InlineData("invoice-create", false)]
    [InlineData("invoice-create", true)]
    [InlineData("invoice-post", false)]
    [InlineData("invoice-post", true)]
    [InlineData("invoice-credit", false)]
    [InlineData("invoice-credit", true)]
    [InlineData("invoice-reverse", false)]
    [InlineData("invoice-reverse", true)]
    [InlineData("receipt-receive", false)]
    [InlineData("receipt-receive", true)]
    [InlineData("receipt-settle", false)]
    [InlineData("receipt-settle", true)]
    [InlineData("receipt-reverse", false)]
    [InlineData("receipt-reverse", true)]
    public async Task PGT017_ar_HTTP_write_and_pause_share_commit_order(string operation, bool pauseFirst)
    {
        await using var h = await Seed();
        await using var admin = await TenantManagementSeed();
        var f = await SeedAr(h, operation);
        var command = ArCommand(operation, f);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        var manager = await Login(host, admin);
        using var read = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(h.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(read);
        var suspend = new TenantManagementCommandV1("ar-pause", active.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(h, admin.User);
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        var before = await ReadAr(observer, h.Tenant);

        var race = await ObserveAssetRace(connection, observer, command.Table, command.Verb, "Sys_Tenants", "UPDATE", pauseFirst,
            Write, () => host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), suspend)),
            async () =>
            {
                Assert.Equal(before, await ReadAr(observer, h.Tenant));
                Assert.Equal(authority, await ReadPauseAuthority(h, admin.User));
            });
        using var paused = race.Authority;
        using var business = race.Business;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        CheckPause(authority, await ReadPauseAuthority(h, admin.User), receipt, suspend);
        var after = await ReadAr(observer, h.Tenant);
        if (pauseFirst)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, business.StatusCode);
            Assert.Equal(before, after);
        }
        else
        {
            await CheckArSuccess(business, operation, f, before, after);
        }
        using var stale = await Write();
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(after, await ReadAr(observer, h.Tenant));
        if (pauseFirst)
        {
            var resume = new TenantManagementCommandV1("ar-resume", receipt.Snapshot.Revision, "reactivate", null);
            using var resumed = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(h.Tenant, "/commands"), resume));
            Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
            actor = await Login(host, h);
            using var current = await Write();
            await CheckArSuccess(current, operation, f, before, await ReadAr(observer, h.Tenant));
        }
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Operation = operation, PauseFirst = pauseFirst, race.FirstWait, race.RootWait,
            Status = (int)business.StatusCode, WriteSets = ArCounts(after),
        }));

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(actor, HttpMethod.Post, command.Path, command.Body));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_ar_credit_memo_and_role_removal_share_commit_order(bool revokeFirst)
    {
        const string operation = "invoice-credit";
        await using var h = await Seed();
        await SeedFenceAdministrator(h);
        var f = await SeedAr(h, operation);
        var command = ArCommand(operation, f);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        var manager = await Login(host, "fence-manager", "Fence1!Password", h.Tenant.ToString("N"));
        var revoke = await RevokeActions(host, manager, command.Action);
        var authority = await ReadPolicyWrites(h, revoke.OperationId);
        var connection = h.Db.Database.GetConnectionString()!;
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        var before = await ReadAr(observer, h.Tenant);

        var race = await ObserveAssetRace(connection, observer, command.Table, command.Verb, "Sys_RoleAction", "DELETE", revokeFirst,
            Write, () => host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke)),
            async () =>
            {
                Assert.Equal(before, await ReadAr(observer, h.Tenant));
                Assert.Equal(authority, await ReadPolicyWrites(h, revoke.OperationId));
            });
        using var changed = race.Authority;
        using var business = race.Business;
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var policy = await ReadPolicyWrites(h, revoke.OperationId);
        Assert.NotEqual(authority.RoleRevision, policy.RoleRevision);
        Assert.Equal((authority.Generation + 1, authority.Snapshot + 1, authority.Outbox + 1, 1L, 1L, 1L),
            (policy.Generation, policy.Snapshot, policy.Outbox, policy.CurrentOutbox, policy.Audits, policy.Receipts));
        Assert.DoesNotContain(command.Action, policy.Actions.Split(','));
        var after = await ReadAr(observer, h.Tenant);
        if (revokeFirst)
        {
            await CheckApDenied(business, "IAM_FORBIDDEN");
            Assert.Equal(before, after);
        }
        else
        {
            await CheckArSuccess(business, operation, f, before, after);
        }
        using var stale = await Write();
        await CheckApDenied(stale, $"无权限：{command.Menu}:{command.Action}");
        Assert.Equal(after, await ReadAr(observer, h.Tenant));
        output.WriteLine(JsonSerializer.Serialize(new { RevokeFirst = revokeFirst, race.FirstWait, race.RootWait, Status = (int)business.StatusCode }));

        Task<HttpResponseMessage> Write() => host.Send(OperationsRequest(actor, HttpMethod.Post, command.Path, command.Body));
    }

    [TenantManagementPostgresTheory]
    [InlineData("invoice-post", "missing-rule", "E-FIN-150")]
    [InlineData("invoice-credit", "missing-rule", "E-FIN-150")]
    [InlineData("invoice-reverse", "cost-voucher", "E-FIN-120")]
    [InlineData("receipt-receive", "non-leaf", "E-FIN-104")]
    [InlineData("receipt-settle", "second-invoice", "E-FIN-302")]
    [InlineData("receipt-reverse", "primary-voucher", "E-FIN-120")]
    [InlineData("receipt-reverse", "diff-voucher", "E-FIN-120")]
    public async Task PGT017_ar_late_fin_error_rolls_back_every_write(string operation, string fault, string code)
    {
        await using var h = await Seed();
        var f = await SeedAr(h, operation, fault);
        var command = ArCommand(operation, f, fault);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAr(observer, h.Tenant);

        using var result = await host.Send(OperationsRequest(actor, HttpMethod.Post, command.Path, command.Body));
        var after = await ReadAr(observer, h.Tenant);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Operation = operation, Fault = fault, Status = (int)result.StatusCode, Before = ArCounts(before), After = ArCounts(after),
            SequencesBefore = ApSequences(before), SequencesAfter = ApSequences(after),
            InvoiceBefore = before.Rows("invoices").Select(x => AssetDecimal(x, "SettledAmount")),
            InvoiceAfter = after.Rows("invoices").Select(x => AssetDecimal(x, "SettledAmount")),
            OriginalJournals = before.Rows("journals").Select(x => new { Id = AssetId(x), Status = AssetInt(x, "Status") }),
            FinalJournals = after.Rows("journals").Select(x => new { Id = AssetId(x), Status = AssetInt(x, "Status") }),
        }));
        string[] args = fault switch
        {
            "missing-rule" => [operation == "invoice-credit" ? "AR.CogsReversal" : "AR.Cogs"],
            "non-leaf" => [f.NonLeafCode],
            _ => [],
        };
        await CheckApError(result, code, args);
        Assert.Equal(before, after);
    }

    [TenantManagementPostgresTheory]
    [InlineData("invoice-create")]
    [InlineData("receipt-receive")]
    public async Task PGT017_ar_body_uses_signed_tenant_and_generated_links(string operation)
    {
        await using var h = await Seed();
        await using var foreign = await Seed();
        var f = await SeedAr(h, operation);
        var other = await SeedAr(foreign, "invoice-create");
        var foreignVoucher = await SeedArVoucher(foreign, other, VoucherSource.AR, "W610-FOREIGN-VOUCHER");
        var command = ArCommand(operation, f);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        var draft = ArDraft(f);
        draft.TenantId = foreign.Tenant;
        draft.Lines[0].TenantId = foreign.Tenant;
        draft.JournalEntryId = foreignVoucher;
        draft.CostJournalEntryId = foreignVoucher;
        var receive = ArReceive(f);
        receive.TenantId = foreign.Tenant;
        receive.JournalEntryId = foreignVoucher;
        var body = operation == "invoice-create" ? (object)draft : receive;
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAr(observer, h.Tenant);
        var foreignBefore = await ReadAr(observer, foreign.Tenant);

        using var result = await host.Send(OperationsRequest(actor, HttpMethod.Post, command.Path, body));
        var after = await ReadAr(observer, h.Tenant);
        var foreignAfter = await ReadAr(observer, foreign.Tenant);
        output.WriteLine(JsonSerializer.Serialize(new { Operation = operation, Status = (int)result.StatusCode,
            OwnBefore = ArCounts(before), OwnAfter = ArCounts(after), ForeignBefore = ArCounts(foreignBefore), ForeignAfter = ArCounts(foreignAfter) }));
        await CheckArSuccess(result, operation, f, before, after);
        Assert.Equal(foreignBefore, foreignAfter);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_ar_credit_memo_recovers_prior_draft_and_replays_original_identity(bool costAlreadyCommitted)
    {
        await using var h = await Seed();
        var f = await SeedAr(h, "invoice-credit", "missing-rule");
        var seq = new FinSequenceService(h.Db);
        var journal = new JournalEntryService(h.Db, new FiscalPeriodService(h.Db, 1), seq);
        var engine = new AutoVoucherEngine(h.Db, journal);
        var invoices = new ArInvoiceService(h.Db, engine, journal, seq);
        var command = ArCommand("invoice-credit", f);
        var legacy = await invoices.CreateCreditMemoAsync(ArCredit(f), "historical");
        Assert.Equal("E-FIN-150", legacy.Result.Code);
        var originalId = legacy.InvoiceId!.Value;
        h.Db.ChangeTracker.Clear();
        await h.Db.PostingRules.Where(r => r.EventType == "AR.CogsReversal").ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, true));
        if (costAlreadyCommitted)
        {
            var cost = new FinBizEvent
            {
                EventType = "AR.CogsReversal", Source = VoucherSource.Cost, SourceDocNo = legacy.No, BizDate = ArDate,
            };
            cost.HeaderAmounts["Amount"] = ArCost;
            var generated = await engine.GenerateAsync(cost);
            Assert.True(generated.Ok, generated.Code);
        }
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAr(observer, h.Tenant);
        var draft = before.Row("invoices", originalId);
        Assert.Equal((int)ArInvoiceStatus.Draft, AssetInt(draft, "Status"));
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("JournalEntryId").ValueKind);
        Assert.Equal(costAlreadyCommitted ? 2 : 1, before.Rows("journals").Length);
        var priorRevenue = Assert.Single(before.Rows("journals"), row => AssetInt(row, "Source") == (int)VoucherSource.AR);
        var retry = ArCredit(f);
        retry.CustomerId = "CHANGED-RETRY";
        retry.EstimatedCost = 9.99m;
        retry.Lines.Clear();

        using var result = await host.Send(OperationsRequest(actor, HttpMethod.Post, command.Path, retry));
        var receipt = await ReadApSuccessData(result);
        Assert.Equal((originalId, legacy.No), (receipt.GetProperty("id").GetGuid(), receipt.GetProperty("no").GetString()));
        var committed = await ReadAr(observer, h.Tenant);
        var posted = committed.Row("invoices", originalId);
        output.WriteLine(JsonSerializer.Serialize(new { OriginalId = originalId, legacy.No, OriginalRevenue = AssetId(priorRevenue),
            Status = AssetInt(posted, "Status"), Before = ArCounts(before), After = ArCounts(committed) }));
        Assert.Equal((int)ArInvoiceStatus.Posted, AssetInt(posted, "Status"));
        Assert.Equal(f.CreditNote, AssetId(posted, "CreditNoteId"));
        Assert.Equal("CUST-AR", posted.GetProperty("CustomerId").GetString());
        Assert.Equal(ArCost, AssetDecimal(posted, "CostAmount"));
        Assert.Equal(AssetId(priorRevenue), AssetId(posted, "JournalEntryId"));
        var revenueId = AssetId(priorRevenue);
        Assert.Equal(priorRevenue.GetRawText(), committed.Row("journals", revenueId).GetRawText());
        Assert.Equal(ApJournalLines(before, revenueId).Select(row => row.GetRawText()),
            ApJournalLines(committed, revenueId).Select(row => row.GetRawText()));
        Assert.Equal(Assert.Single(before.Rows("invoiceLines")).GetRawText(), Assert.Single(committed.Rows("invoiceLines")).GetRawText());
        Assert.Equal(2, committed.Rows("journals").Length);
        AssertArInvoiceVouchers(committed, posted, f, true);
        if (costAlreadyCommitted)
        {
            var priorCost = Assert.Single(before.Rows("journals"), row => AssetInt(row, "Source") == (int)VoucherSource.Cost);
            var costId = AssetId(priorCost);
            Assert.Equal(costId, AssetId(posted, "CostJournalEntryId"));
            Assert.Equal(priorCost.GetRawText(), committed.Row("journals", costId).GetRawText());
            Assert.Equal(ApJournalLines(before, costId).Select(row => row.GetRawText()),
                ApJournalLines(committed, costId).Select(row => row.GetRawText()));
        }
        Assert.Equal((1, 2), (ApSequences(committed)["AR"], ApSequences(committed)["GL"]));
        using var replay = await host.Send(OperationsRequest(actor, HttpMethod.Post, command.Path, retry));
        Assert.Equal(receipt.GetRawText(), (await ReadApSuccessData(replay)).GetRawText());
        Assert.Equal(committed, await ReadAr(observer, h.Tenant));
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_ar_zero_cost_draft_resolves_authoritative_cost_voucher(bool priorCost)
    {
        await using var h = await Seed();
        var f = await SeedAr(h, "invoice-post");
        var unrelated = await SeedArVoucher(h, f, VoucherSource.Cost, "W610-UNRELATED-COST");
        var draft = await h.Db.ArInvoices.SingleAsync();
        var authoritative = priorCost ? await SeedArVoucher(h, f, VoucherSource.Cost, draft.No) : (Guid?)null;
        draft.CostAmount = 0m;
        draft.CostJournalEntryId = unrelated;
        await h.Db.SaveChangesAsync();
        await GrantLegacyAuthorityAction(h, ArInvoiceMenu, "post");
        await GrantLegacyAuthorityAction(h, ArInvoiceMenu, "reverse");
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAr(observer, h.Tenant);

        using var post = await host.Send(OperationsRequest(actor, HttpMethod.Post, $"{ArInvoicePath}/{f.Invoice}/post"));
        Assert.Equal(JsonValueKind.Null, (await ReadApSuccessData(post)).ValueKind);
        var posted = (await ReadAr(observer, h.Tenant)).Row("invoices", f.Invoice);
        using var reverse = await host.Send(OperationsRequest(actor, HttpMethod.Post, $"{ArInvoicePath}/{f.Invoice}/reverse", new { Reason = "Fixture cancellation" }));
        Assert.Equal(JsonValueKind.Null, (await ReadApSuccessData(reverse)).ValueKind);
        var after = await ReadAr(observer, h.Tenant);
        var retainedCost = posted.GetProperty("CostJournalEntryId");
        output.WriteLine(JsonSerializer.Serialize(new { PriorCost = authoritative, PostedCostLink = retainedCost, Unrelated = unrelated,
            UnrelatedBefore = AssetInt(before.Row("journals", unrelated), "Status"), UnrelatedAfter = AssetInt(after.Row("journals", unrelated), "Status") }));
        Assert.Equal(authoritative, retainedCost.ValueKind == JsonValueKind.Null ? null : retainedCost.GetGuid());
        Assert.Equal(before.Row("journals", unrelated).GetRawText(), after.Row("journals", unrelated).GetRawText());
        Assert.Equal((int)ArInvoiceStatus.Reversed, AssetInt(after.Row("invoices", f.Invoice), "Status"));
        Assert.Equal(priorCost ? 2 : 1, after.Rows("journals").Count(j => AssetInt(j, "Source") == (int)VoucherSource.Reversal));
        if (authoritative is Guid id) Assert.Equal((int)JournalStatus.Reversed, AssetInt(after.Row("journals", id), "Status"));
    }

    [TenantManagementPostgresTheory]
    [InlineData("tax-code", "E-FIN-205")]
    [InlineData("revenue-account", "E-FIN-108")]
    [InlineData("cost-center", "E-FIN-206")]
    [InlineData("original-invoice", "E-FIN-302")]
    [InlineData("draft-credit-note", "E-FIN-305")]
    [InlineData("memo-credit-note", "E-FIN-305")]
    [InlineData("bank-account", "E-FIN-310")]
    [InlineData("unused-discount-account", "E-FIN-003")]
    public async Task PGT017_ar_new_store_links_require_current_tenant(string link, string code)
    {
        await using var h = await Seed();
        await using var foreign = await Seed();
        var operation = link switch
        {
            "memo-credit-note" => "invoice-credit",
            "bank-account" => "receipt-receive",
            "unused-discount-account" => "receipt-settle",
            _ => "invoice-create",
        };
        var f = await SeedAr(h, operation);
        var other = await SeedAr(foreign, "invoice-post");
        var command = ArCommand(operation, f);
        await GrantLegacyAuthorityAction(h, command.Menu, command.Action);
        var draft = ArDraft(f);
        object body = draft;
        string[] args = [];
        switch (link)
        {
            case "tax-code":
                draft.Lines[0].TaxCodeId = other.Tax;
                break;
            case "revenue-account":
                draft.Lines[0].RevenueAccountId = other.Revenue;
                args = ["1"];
                break;
            case "cost-center":
                draft.Lines[0].CostCenterId = other.Center;
                break;
            case "original-invoice":
                draft.OriginInvoiceId = other.Invoice;
                break;
            case "draft-credit-note":
                draft.IsCreditMemo = true;
                draft.CreditNoteId = await SeedArCreditNote(foreign);
                break;
            case "memo-credit-note":
                var memo = ArCredit(f);
                memo.CreditNoteId = await SeedArCreditNote(foreign);
                body = memo;
                break;
            case "bank-account":
                var receipt = ArReceive(f);
                receipt.BankAccountId = other.Bank;
                body = receipt;
                break;
            case "unused-discount-account":
                var apply = ArApply(f);
                apply.DiscountAccountId = other.Revenue;
                body = new { Applies = new[] { apply } };
                break;
        }
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();
        var before = await ReadAr(observer, h.Tenant);
        var foreignBefore = await ReadAr(observer, foreign.Tenant);

        using var result = await host.Send(OperationsRequest(actor, HttpMethod.Post, command.Path, body));
        var after = await ReadAr(observer, h.Tenant);
        var foreignAfter = await ReadAr(observer, foreign.Tenant);
        output.WriteLine(JsonSerializer.Serialize(new { Link = link, Status = (int)result.StatusCode,
            OwnBefore = ArCounts(before), OwnAfter = ArCounts(after), ForeignUnchanged = foreignBefore == foreignAfter }));
        await CheckApError(result, code, args);
        Assert.Equal(before, after);
        Assert.Equal(foreignBefore, foreignAfter);
    }

    [TenantManagementPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PGT017_ar_credit_memo_accepts_owned_note_and_legacy_empty_key(bool emptyKey)
    {
        await using var h = await Seed();
        var f = await SeedAr(h, "invoice-post");
        var note = emptyKey ? Guid.Empty : await SeedArCreditNote(h);
        var body = ArCredit(f);
        body.CreditNoteId = note;
        body.OriginInvoiceId = f.Invoice;
        body.EstimatedCost = 0m;
        body.Lines[0].TaxCodeId = Guid.Empty;
        body.Lines[0].CostCenterId = Guid.Empty;
        body.Lines[0].RevenueAccountId = null;
        await GrantLegacyAuthorityAction(h, ArInvoiceMenu, "credit-memo");
        await using var host = await StartAssetApi(h);
        var actor = await Login(host, h);
        await using var observer = new NpgsqlConnection(h.Db.Database.GetConnectionString());
        await observer.OpenAsync();

        using var result = await host.Send(OperationsRequest(actor, HttpMethod.Post, $"{ArInvoicePath}/credit-memo", body));
        var payload = await ReadApSuccessData(result);
        var id = payload.GetProperty("id").GetGuid();
        var committed = await ReadAr(observer, h.Tenant);
        var memo = committed.Row("invoices", id);
        Assert.Equal((f.Invoice, note), (AssetId(memo, "OriginInvoiceId"), AssetId(memo, "CreditNoteId")));
        Assert.Equal((h.Tenant, (int)ArInvoiceStatus.Posted), (AssetId(memo, "TenantId"), AssetInt(memo, "Status")));
        Assert.Equal(JsonValueKind.Null, memo.GetProperty("CostJournalEntryId").ValueKind);
        Assert.Equal((1234.72m, 0m), (AssetDecimal(memo, "GrossAmount"), AssetDecimal(memo, "TaxAmount")));
        var line = Assert.Single(committed.Rows("invoiceLines"), r => AssetId(r, "InvoiceId") == id);
        Assert.Equal((Guid.Empty, Guid.Empty), (AssetId(line, "TaxCodeId"), AssetId(line, "CostCenterId")));
        Assert.Equal(JsonValueKind.Null, line.GetProperty("RevenueAccountId").ValueKind);
        using var replay = await host.Send(OperationsRequest(actor, HttpMethod.Post, $"{ArInvoicePath}/credit-memo", body));
        Assert.Equal(payload.GetRawText(), (await ReadApSuccessData(replay)).GetRawText());
        Assert.Equal(committed, await ReadAr(observer, h.Tenant));
    }

    private static async Task<Guid> SeedArCreditNote(Harness h)
    {
        var note = new CreditNote { Id = Guid.NewGuid(), CreditNoteNo = "W610-NOTE", CustomerCd = "CUST-AR",
            IssueDate = ArDate, Qty = 12.34567891m, Amount = 1234.7197m };
        h.Db.CreditNotes.Add(note);
        await h.Db.SaveChangesAsync();
        return note.Id;
    }

    private sealed record ArFixture(Guid Tenant, Guid Invoice, Guid Receipt, Guid Line, Guid CreditNote,
        Guid Tax, Guid Center, Guid Bank, Guid BankGl, Guid ArGl, Guid Revenue, Guid OutputTax, Guid Cogs, Guid Goods, Guid Loss,
        string NonLeafCode, Guid RevenueJournal, Guid CostJournal, Guid ReceiptJournal, Guid DiffJournal);

    private static async Task<ArFixture> SeedAr(Harness h, string operation, string? fault = null)
    {
        var db = h.Db;
        await new GlAccountService(db).ImportTemplateAsync(FinCoaTemplate.CnGaap, "fixture");
        await PostingRuleSeed.EnsureSeededAsync(db);
        var accounts = await db.GlAccounts.AsNoTracking().ToListAsync();
        Guid Role(string role) => accounts.Single(account => account.Role == role).Id;
        var bankGl = accounts.Single(a => a.Code == "1002");
        var nonLeaf = accounts.Where(a => !a.IsLeaf).OrderBy(a => a.Code).First();
        var bank = new BankAccount { Id = Guid.NewGuid(), Code = "W610", Name = "AR fixture bank",
            GlAccountId = fault == "non-leaf" ? nonLeaf.Id : bankGl.Id };
        var tax = new TaxCode { Id = Guid.NewGuid(), Code = "AR12", Name = "Output tax", Rate = 0.125m,
            Direction = TaxDirection.Output };
        var center = new CostCenter { Id = Guid.NewGuid(), Code = "AR-CC", Name = "AR cost center" };
        db.AddRange(bank, tax, center);
        await db.SaveChangesAsync();
        var f = new ArFixture(h.Tenant, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            tax.Id, center.Id, bank.Id, bankGl.Id, Role("AR_CONTROL"), Role("REVENUE"), Role("TAX_OUTPUT"),
            Role("COGS"), Role("FG"), Role("FX_LOSS"), nonLeaf.Code, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty);
        if (fault == "missing-rule")
        {
            var eventType = operation == "invoice-credit" ? "AR.CogsReversal" : "AR.Cogs";
            await db.PostingRules.Where(r => r.EventType == eventType).ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, false));
        }
        var seq = new FinSequenceService(db);
        var journal = new JournalEntryService(db, new FiscalPeriodService(db, 1), seq);
        var engine = new AutoVoucherEngine(db, journal);
        var invoices = new ArInvoiceService(db, engine, journal, seq);
        var receipts = new ReceiptService(db, engine, journal, seq);
        var settlement = new ArSettlementService(db, journal);
        if (operation is "invoice-post" or "invoice-reverse" or "receipt-settle" or "receipt-reverse")
        {
            var invoice = ArDraft(f);
            invoice.FxRate = 1.25m;
            Assert.True((await invoices.CreateAsync(invoice, "fixture")).Ok);
            if (operation != "invoice-post")
            {
                var posted = await invoices.PostAsync(invoice.Id, "fixture");
                Assert.True(posted.Ok, posted.Code);
                f = f with { RevenueJournal = invoice.JournalEntryId!.Value, CostJournal = invoice.CostJournalEntryId!.Value };
                if (operation is "receipt-settle" or "receipt-reverse")
                {
                    var receipt = ArReceive(f);
                    var received = await receipts.ReceiveAsync(receipt, "fixture");
                    Assert.True(received.Ok, received.Code);
                    f = f with { ReceiptJournal = receipt.JournalEntryId!.Value };
                    if (operation == "receipt-reverse")
                    {
                        var applied = await settlement.SettleAsync(receipt.Id, [ArApply(f)], "fixture");
                        Assert.True(applied.Ok, applied.Code);
                        f = f with { DiffJournal = (await db.ArSettlements.SingleAsync()).DiffJournalEntryId!.Value };
                    }
                }
            }
        }
        var wrongJournal = fault switch
        {
            "cost-voucher" => f.CostJournal,
            "primary-voucher" => f.ReceiptJournal,
            "diff-voucher" => f.DiffJournal,
            _ => Guid.Empty,
        };
        if (wrongJournal != Guid.Empty)
        {
            var malformed = await db.JournalEntries.SingleAsync(j => j.Id == wrongJournal);
            malformed.Status = JournalStatus.PendingReview;
            await db.SaveChangesAsync();
        }
        return f;
    }

    private static async Task<Guid> SeedArVoucher(Harness h, ArFixture f, VoucherSource source, string sourceNo)
    {
        var journal = new JournalEntryService(h.Db, new FiscalPeriodService(h.Db, 1), new FinSequenceService(h.Db));
        var entry = new JournalEntry
        {
            VoucherDate = ArDate, Source = source, SourceDocNo = sourceNo,
            Lines = [new JournalLine { AccountId = f.Cogs, Debit = ArCost }, new JournalLine { AccountId = f.Goods, Credit = ArCost }],
        };
        Assert.True((await journal.AutoPostAsync(entry)).Ok);
        return entry.Id;
    }

    private static ArInvoice ArDraft(ArFixture f) => new()
    {
        Id = f.Invoice, CustomerId = "CUST-AR", InvoiceDate = ArDate, DueDate = ArDate.AddDays(30),
        CurrencyCd = "USD", FxRate = 1.234567m, CostAmount = ArCost,
        ShipmentId = "EXTERNAL-SHIPMENT", OrderId = "EXTERNAL-ORDER",
        Lines =
        [
            new ArInvoiceLine { Id = f.Line, InvoiceId = Guid.NewGuid(), ItemId = "RAW-SKU", Qty = 12.34567891m,
                UnitPrice = 100.0123m, Amount = 1234.56m, TaxCodeId = f.Tax, RevenueAccountId = f.Revenue, CostCenterId = f.Center },
        ],
    };

    private static FinCreditMemoRequest ArCredit(ArFixture f) => new()
    {
        CreditNoteId = f.CreditNote, CustomerId = "CUST-AR", InvoiceDate = ArDate,
        DueDate = ArDate.AddDays(30), CurrencyCd = "USD", FxRate = 1.25m, EstimatedCost = ArCost,
        Lines =
        [
            new FinShipmentInvoiceLine { ItemId = "RAW-CREDIT-SKU", Qty = 12.34567891m, UnitPrice = 100.0123m,
                TaxCodeId = f.Tax, RevenueAccountId = f.Revenue, CostCenterId = f.Center },
        ],
    };

    private static Receipt ArReceive(ArFixture f) => new()
    {
        Id = f.Receipt, CustomerId = "CUST-AR", ReceiptDate = ArDate, CurrencyCd = "USD",
        FxRate = 1.000001m, Amount = 78.90m, BankAccountId = f.Bank, Method = PaymentMethod.Check,
    };

    private static SettlementApply ArApply(ArFixture f) => new() { InvoiceId = f.Invoice, AppliedAmount = ArApplied };

    private sealed record ArWrite(string Path, string Menu, string Action, string Table, string Verb, object? Body);

    private static ArWrite ArCommand(string operation, ArFixture f, string? fault = null) => operation switch
    {
        "invoice-create" => new(ArInvoicePath, ArInvoiceMenu, "add", "Fin_ArInvoice", "INSERT", ArDraft(f)),
        "invoice-post" => new($"{ArInvoicePath}/{f.Invoice}/post", ArInvoiceMenu, "post", "Fin_ArInvoice", "UPDATE", null),
        "invoice-credit" => new($"{ArInvoicePath}/credit-memo", ArInvoiceMenu, "credit-memo", "Fin_ArInvoice", "INSERT", ArCredit(f)),
        "invoice-reverse" => new($"{ArInvoicePath}/{f.Invoice}/reverse", ArInvoiceMenu, "reverse", "Fin_ArInvoice", "UPDATE",
            new { Reason = "Invoice cancellation" }),
        "receipt-receive" => new(ArReceiptPath, ArReceiptMenu, "add", "Fin_Receipt", "INSERT", ArReceive(f)),
        "receipt-settle" => new($"{ArReceiptPath}/{f.Receipt}/settle", ArReceiptMenu, "settle", "Fin_ArSettlement", "INSERT",
            new { Applies = fault == "second-invoice"
                ? new[] { ArApply(f), new SettlementApply { InvoiceId = Guid.NewGuid(), AppliedAmount = 1.23m } }
                : new[] { ArApply(f) } }),
        "receipt-reverse" => new($"{ArReceiptPath}/{f.Receipt}/reverse", ArReceiptMenu, "reverse", "Fin_Receipt", "UPDATE",
            new { Reason = "Duplicate bank receipt" }),
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static async Task<AssetRows> ReadAr(NpgsqlConnection observer, Guid tenant)
    {
        await using var command = new NpgsqlCommand("""
            SELECT jsonb_build_object(
              'invoices', ARRAY(SELECT to_jsonb(r) FROM public."Fin_ArInvoice" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'invoiceLines', ARRAY(SELECT to_jsonb(r) FROM public."Fin_ArInvoiceLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'receipts', ARRAY(SELECT to_jsonb(r) FROM public."Fin_Receipt" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'settlements', ARRAY(SELECT to_jsonb(r) FROM public."Fin_ArSettlement" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'journals', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalEntry" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'lines', ARRAY(SELECT to_jsonb(r) FROM public."Fin_JournalLine" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'periods', ARRAY(SELECT to_jsonb(r) FROM public."Fin_FiscalPeriod" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'numbers', ARRAY(SELECT to_jsonb(r) FROM public."Fin_Sequence" r WHERE r."TenantId"=@tenant ORDER BY r."Id"),
              'audit', ARRAY(SELECT to_jsonb(r) FROM public."Sys_FieldAuditLogs" r WHERE r."TenantId"=@tenant
                AND r."EntityName" IN ('ArInvoice','ArInvoiceLine','Receipt','ArSettlement','JournalEntry','JournalLine','FiscalPeriod','FinSequence')
                ORDER BY r."Id")
            )::text
            """, observer);
        command.Parameters.AddWithValue("tenant", tenant);
        return new((string)(await command.ExecuteScalarAsync())!);
    }

    private static readonly string[] ArTables = ["invoices", "invoiceLines", "receipts", "settlements", "journals", "lines", "periods", "numbers", "audit"];
    private static Dictionary<string, int> ArCounts(AssetRows rows) => ArTables.ToDictionary(t => t, t => rows.Rows(t).Length);

    private static async Task CheckArSuccess(HttpResponseMessage result, string operation, ArFixture f, AssetRows before, AssetRows after)
    {
        var payload = await ReadApSuccessData(result);
        if (operation is not ("invoice-create" or "invoice-credit" or "receipt-receive"))
            Assert.Equal(JsonValueKind.Null, payload.ValueKind);
        var expectedChanges = operation switch
        {
            "invoice-create" => new[] { ("invoices", 1), ("invoiceLines", 1), ("numbers", 1) },
            "invoice-post" => new[] { ("journals", 2), ("lines", 5), ("periods", 1), ("numbers", 1) },
            "invoice-credit" => new[] { ("invoices", 1), ("invoiceLines", 1), ("journals", 2), ("lines", 5), ("periods", 1), ("numbers", 2) },
            "invoice-reverse" => new[] { ("journals", 2), ("lines", 5) },
            "receipt-receive" => new[] { ("receipts", 1), ("journals", 1), ("lines", 2), ("periods", 1), ("numbers", 2) },
            "receipt-settle" => new[] { ("settlements", 1), ("journals", 1), ("lines", 2) },
            "receipt-reverse" => new[] { ("settlements", -1), ("journals", 2), ("lines", 4) },
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        var deltas = expectedChanges.ToDictionary(x => x.Item1, x => x.Item2);
        foreach (var table in ArTables)
        {
            Assert.Equal(before.Rows(table).Length + deltas.GetValueOrDefault(table), after.Rows(table).Length);
            Assert.All(after.Rows(table), row => Assert.Equal(f.Tenant, AssetId(row, "TenantId")));
            var prior = before.Rows(table).Where(row => !ArRowCanChange(operation, f, table, row));
            var current = after.Rows(table).ToDictionary(row => AssetId(row), row => row.GetRawText());
            foreach (var row in prior) Assert.Equal(row.GetRawText(), current[AssetId(row)]);
        }
        var numbers = ApSequences(after);
        switch (operation)
        {
            case "invoice-create":
                var draft = after.Row("invoices", f.Invoice);
                CheckArHttpIdentity(payload, draft, "AR-2026-06-00001");
                Assert.Equal((int)ArInvoiceStatus.Draft, AssetInt(draft, "Status"));
                Assert.Equal(JsonValueKind.Null, draft.GetProperty("JournalEntryId").ValueKind);
                Assert.Equal(JsonValueKind.Null, draft.GetProperty("CostJournalEntryId").ValueKind);
                Assert.Equal(("CUST-AR", "USD", "EXTERNAL-SHIPMENT", "EXTERNAL-ORDER"),
                    (draft.GetProperty("CustomerId").GetString(), draft.GetProperty("CurrencyCd").GetString(),
                    draft.GetProperty("ShipmentId").GetString(), draft.GetProperty("OrderId").GetString()));
                Assert.Equal((1234.56m, 154.32m, 1388.88m, 1.234567m, ArCost),
                    (AssetDecimal(draft, "NetAmount"), AssetDecimal(draft, "TaxAmount"), AssetDecimal(draft, "GrossAmount"),
                    AssetDecimal(draft, "FxRate"), AssetDecimal(draft, "CostAmount")));
                CheckArLine(after.Row("invoiceLines", f.Line), f, f.Invoice, 1234.56m, 154.32m);
                Assert.Equal(1, numbers["AR"]);
                break;
            case "invoice-post":
                AssertArInvoiceVouchers(after, after.Row("invoices", f.Invoice), f, false);
                Assert.Equal((1, 2), (numbers["AR"], numbers["GL"]));
                break;
            case "invoice-credit":
                var credit = after.Row("invoices", payload.GetProperty("id").GetGuid());
                CheckArHttpIdentity(payload, credit, "AR-2026-06-00001");
                Assert.True(credit.GetProperty("IsCreditMemo").GetBoolean());
                Assert.Equal(f.CreditNote, AssetId(credit, "CreditNoteId"));
                CheckArLine(Assert.Single(after.Rows("invoiceLines")), f, AssetId(credit), 1234.72m, 154.34m);
                AssertArInvoiceVouchers(after, credit, f, true);
                Assert.Equal((1, 2), (numbers["AR"], numbers["GL"]));
                break;
            case "invoice-reverse":
                var reversedInvoice = after.Row("invoices", f.Invoice);
                Assert.Equal((int)ArInvoiceStatus.Reversed, AssetInt(reversedInvoice, "Status"));
                Assert.Equal((f.RevenueJournal, f.CostJournal),
                    (AssetId(reversedInvoice, "JournalEntryId"), AssetId(reversedInvoice, "CostJournalEntryId")));
                AssertArReversal(before, after, f.RevenueJournal, 1736.10m);
                AssertArReversal(before, after, f.CostJournal, ArCost);
                Assert.Equal(ApSequences(before)["GL"] + 2, numbers["GL"]);
                break;
            case "receipt-receive":
                var receipt = after.Row("receipts", f.Receipt);
                CheckArHttpIdentity(payload, receipt, "RCP-2026-06-00001");
                Assert.Equal(f.Bank, AssetId(receipt, "BankAccountId"));
                Assert.Equal((int)ReceiptStatus.Posted, AssetInt(receipt, "Status"));
                Assert.Equal((int)PaymentMethod.Check, AssetInt(receipt, "Method"));
                Assert.Equal((78.90m, 1.000001m, "USD"),
                    (AssetDecimal(receipt, "Amount"), AssetDecimal(receipt, "FxRate"), receipt.GetProperty("CurrencyCd").GetString()));
                var received = CheckApJournal(after, AssetId(receipt, "JournalEntryId"), receipt.GetProperty("No").GetString()!, 78.90m, VoucherSource.AR);
                Assert.Equal(78.90m, ArDebit(received, f.BankGl));
                Assert.Equal(78.90m, ArCreditAmount(received, f.ArGl));
                Assert.Equal((1, 1), (numbers["RCP"], numbers["GL"]));
                break;
            case "receipt-settle":
                var matched = Assert.Single(after.Rows("settlements"));
                Assert.Equal((f.Receipt, f.Invoice), (AssetId(matched, "ReceiptId"), AssetId(matched, "ArInvoiceId")));
                Assert.Equal((ArApplied, -ArFxDifference, (int)SettlementDiffType.FxDiff),
                    (AssetDecimal(matched, "SettledAmount"), AssetDecimal(matched, "DiffAmount"), AssetInt(matched, "DiffType")));
                var difference = CheckApJournal(after, AssetId(matched, "DiffJournalEntryId"), "RCP-2026-06-00001#AR-2026-06-00001#DIFF", ArFxDifference, VoucherSource.AR);
                Assert.Equal(ArFxDifference, ArDebit(difference, f.Loss));
                Assert.Equal(ArFxDifference, ArCreditAmount(difference, f.ArGl));
                CheckArBalance(after, f, ArApplied, ArInvoiceStatus.PartiallySettled, ReceiptStatus.Posted);
                Assert.Equal(ApSequences(before)["GL"] + 1, numbers["GL"]);
                break;
            case "receipt-reverse":
                CheckArBalance(after, f, 0m, ArInvoiceStatus.Posted, ReceiptStatus.Reversed);
                Assert.Empty(after.Rows("settlements"));
                AssertArReversal(before, after, f.ReceiptJournal, 78.90m);
                AssertArReversal(before, after, f.DiffJournal, ArFxDifference);
                Assert.Equal(ApSequences(before)["GL"] + 2, numbers["GL"]);
                break;
        }
        if (operation != "invoice-create")
        {
            var period = Assert.Single(after.Rows("periods"));
            Assert.Equal((2026, 6, (int)PeriodStatus.Open), (AssetInt(period, "Year"), AssetInt(period, "Month"), AssetInt(period, "Status")));
        }
    }

    private static bool ArRowCanChange(string operation, ArFixture f, string table, JsonElement row) =>
        (operation, table) switch
        {
            ("invoice-post" or "invoice-reverse" or "receipt-settle" or "receipt-reverse", "invoices") => AssetId(row) == f.Invoice,
            ("receipt-settle" or "receipt-reverse", "receipts") => AssetId(row) == f.Receipt,
            ("receipt-reverse", "settlements") => AssetId(row, "ReceiptId") == f.Receipt && AssetId(row, "ArInvoiceId") == f.Invoice,
            ("invoice-reverse", "journals") => AssetId(row) == f.RevenueJournal || AssetId(row) == f.CostJournal,
            ("receipt-reverse", "journals") => AssetId(row) == f.ReceiptJournal || AssetId(row) == f.DiffJournal,
            ("invoice-reverse" or "receipt-settle" or "receipt-reverse", "numbers") => row.GetProperty("SeqKey").GetString() == "GL",
            _ => false,
        };

    private static void CheckArHttpIdentity(JsonElement payload, JsonElement row, string number)
    {
        Assert.Equal((AssetId(row), number), (payload.GetProperty("id").GetGuid(), payload.GetProperty("no").GetString()));
        Assert.Equal(number, row.GetProperty("No").GetString());
        Assert.Equal("price-owner", row.GetProperty("Creator").GetString());
    }

    private static void CheckArLine(JsonElement line, ArFixture f, Guid invoice, decimal amount, decimal tax)
    {
        Assert.Equal((invoice, f.Revenue, f.Tax, f.Center),
            (AssetId(line, "InvoiceId"), AssetId(line, "RevenueAccountId"), AssetId(line, "TaxCodeId"), AssetId(line, "CostCenterId")));
        Assert.Equal((12.34567891m, 100.0123m, amount, tax),
            (AssetDecimal(line, "Qty"), AssetDecimal(line, "UnitPrice"), AssetDecimal(line, "Amount"), AssetDecimal(line, "TaxAmount")));
    }

    private static void AssertArInvoiceVouchers(AssetRows rows, JsonElement invoice, ArFixture f, bool creditMemo)
    {
        Assert.Equal((int)ArInvoiceStatus.Posted, AssetInt(invoice, "Status"));
        var number = invoice.GetProperty("No").GetString()!;
        var revenue = CheckApJournal(rows, AssetId(invoice, "JournalEntryId"), number, creditMemo ? 1736.33m : 1736.10m, VoucherSource.AR);
        var cost = CheckApJournal(rows, AssetId(invoice, "CostJournalEntryId"), number, ArCost, VoucherSource.Cost);
        Assert.Equal("CUST-AR", Assert.Single(revenue, row => AssetId(row, "AccountId") == f.ArGl).GetProperty("PartnerId").GetString());
        Assert.Equal(creditMemo ? 1234.72m : 1234.56m,
            AssetDecimal(Assert.Single(revenue, row => AssetId(row, "AccountId") == f.Revenue), "OrigAmount"));
        if (creditMemo)
        {
            Assert.Equal(1543.40m, ArDebit(revenue, f.Revenue));
            Assert.Equal(192.93m, ArDebit(revenue, f.OutputTax));
            Assert.Equal(1736.33m, ArCreditAmount(revenue, f.ArGl));
            Assert.Equal(ArCost, ArDebit(cost, f.Goods));
            Assert.Equal(ArCost, ArCreditAmount(cost, f.Cogs));
        }
        else
        {
            Assert.Equal(1736.10m, ArDebit(revenue, f.ArGl));
            Assert.Equal(1543.20m, ArCreditAmount(revenue, f.Revenue));
            Assert.Equal(192.90m, ArCreditAmount(revenue, f.OutputTax));
            Assert.Equal(ArCost, ArDebit(cost, f.Cogs));
            Assert.Equal(ArCost, ArCreditAmount(cost, f.Goods));
        }
        Assert.All(cost, line => Assert.Equal(JsonValueKind.Null, line.GetProperty("CurrencyCd").ValueKind));
    }

    private static decimal ArDebit(JsonElement[] lines, Guid account) =>
        AssetDecimal(Assert.Single(lines, row => AssetId(row, "AccountId") == account), "Debit");

    private static decimal ArCreditAmount(JsonElement[] lines, Guid account) =>
        AssetDecimal(Assert.Single(lines, row => AssetId(row, "AccountId") == account), "Credit");

    private static void CheckArBalance(AssetRows rows, ArFixture f, decimal applied, ArInvoiceStatus invoiceStatus, ReceiptStatus receiptStatus)
    {
        var invoice = rows.Row("invoices", f.Invoice);
        var receipt = rows.Row("receipts", f.Receipt);
        Assert.Equal((applied, (int)invoiceStatus), (AssetDecimal(invoice, "SettledAmount"), AssetInt(invoice, "Status")));
        Assert.Equal((applied, (int)receiptStatus), (AssetDecimal(receipt, "SettledAmount"), AssetInt(receipt, "Status")));
        Assert.Equal(f.ReceiptJournal, AssetId(receipt, "JournalEntryId"));
    }

    private static void AssertArReversal(AssetRows before, AssetRows after, Guid id, decimal amount)
    {
        var origin = after.Row("journals", id);
        Assert.Equal((int)JournalStatus.Reversed, AssetInt(origin, "Status"));
        var reversal = after.Row("journals", AssetId(origin, "ReversedById"));
        Assert.Equal(id, AssetId(reversal, "ReverseOfId"));
        var reverseLines = CheckApJournal(after, AssetId(reversal), origin.GetProperty("No").GetString()!, amount, VoucherSource.Reversal);
        Assert.Equal(ApJournalLines(before, id).Select(row => (AssetId(row, "AccountId"), AssetDecimal(row, "Debit"), AssetDecimal(row, "Credit"))).Order(),
            reverseLines.Select(row => (AssetId(row, "AccountId"), AssetDecimal(row, "Credit"), AssetDecimal(row, "Debit"))).Order());
    }
}
